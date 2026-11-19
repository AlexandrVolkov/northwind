using System.Buffers.Binary;
namespace Northwind;

public sealed class OriginalAiCharacter
{
    public int Index { get; set; }
    public string RawHex { get; set; } = "";
}
/// <summary>data/original_ai.json from tools/ai_source.py: character records of LE:3 ED43.</summary>
public sealed class OriginalAiData
{
    public int FormatVersion { get; set; }
    public List<OriginalAiCharacter> Characters { get; set; } = [];
    public byte[] Table() => Characters.OrderBy(c => c.Index).SelectMany(c => Convert.FromHexString(c.RawHex)).ToArray();
}

public readonly record struct CensusBounds(uint MaxX, uint MaxY, uint MinX, uint MinY);

/// <summary>Settlement AI kernels from LE:1: block initialisation 1E53A, census 1E87A and territory threat 23B10. See docs/ai.md.</summary>
public static class OriginalAiRules
{
    public const int BlockSize = 0x1a35, CharacterSize = 0x53, CharacterCount = 9;
    public static readonly int[] PlanLists = [0x1ab, 0x34f, 0x4f3, 0x697, 0x83b];

    /// <summary>1E53A body for one participant: clear the block, empty the five plan lists (1EB33: 20 records of 15h, type FF),
    /// home cell and territory from the first object with DAT[40] &amp; 38h = 0 (1ECA0, 219C3 with 2Fh x 2Fh), half map size,
    /// then copy character record [participant +16] (LE:3 ED43, 53h bytes) to +5..+57 and spread its bytes +2B..+30.
    /// The player's own participant ([1972]) gets character 0, and character 0 takes +29 = [1972] &amp; 3.</summary>
    public static void Initialize(Span<byte> ai, Span<byte> participant, byte index, byte player, ReadOnlySpan<byte> characters,
        IEnumerable<(byte[] Record, byte[] Template)> chain, uint mapWidth, uint mapHeight)
    {
        if (index == player) participant[0x16] = 0;
        ai[..BlockSize].Clear();
        ai[0] = index; W32(ai, 1, index);
        foreach (int list in PlanLists) for (int k = 0; k < 20; k++) ai[list + k * 0x15] = 0xff;
        foreach (var (record, template) in chain)
        {
            if ((sbyte)record[0x19] <= 0) continue;
            if (record[0x14] == 0 && (sbyte)record[0x19] <= (sbyte)template[0x46]) continue;
            if ((U32(template, 0x40) & 0x38) != 0) continue;
            byte cx = (byte)(U32(record, 4) >> 5), cy = (byte)(U32(record, 8) >> 5);
            ai[0x61] = cx; ai[0x62] = cy;
            var (left, top, right, bottom) = OriginalTargetRules.Clip(cx, cy, 0x2f, 0x2f, (byte)mapWidth, (byte)mapHeight);
            ai[0x63] = left; ai[0x64] = top; ai[0x65] = right; ai[0x66] = bottom;
            break;
        }
        ai[0x19e5] = (byte)(mapWidth >> 1); ai[0x19e6] = (byte)(mapHeight >> 1);
        byte character = participant[0x16];
        characters.Slice(CharacterSize * character, CharacterSize).CopyTo(ai[5..]);
        if (character == 0) ai[0x29] = (byte)(player & 3);
        ai[0x19e4] = ai[0x2b]; ai[0x19ff] = ai[0x2c]; ai[0x1a14] = ai[0x2d]; ai[0x1a15] = ai[0x2e]; ai[0x1a27] = ai[0x2f]; ai[0x1a34] = ai[0x30];
    }

    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    /// <summary>1E87A..1EAC1: strength, value, building counts, territory and affordability. <paramref name="ai"/> is the
    /// 1A35h-byte AI/statistics block, <paramref name="participant"/> the F5h-byte settlement record.</summary>
    public static CensusBounds Census(Span<byte> ai, ReadOnlySpan<byte> participant, IEnumerable<(byte[] Record, byte[] Template)> chain,
        byte mapWidth, byte mapHeight)
    {
        ai[0x1a00] = ai[0x1a02] = ai[0x1a03] = ai[0x5c] = 0;
        W32(ai, 0x19e9, 0); W32(ai, 0x19ed, 0); W32(ai, 0x19f1, 0); ai[0x1a26] = 0; W32(ai, 0x77, 0);
        uint maxX = 0, maxY = 0, minX = uint.MaxValue, minY = uint.MaxValue;
        W32(ai, 0x58, U32(participant, 0x2c)); W32(ai, 0x5d, U32(participant, 0x28));
        if ((U32(ai, 1) & 0xff) == 0)
        {
            W32(ai, 0x6f, 0);
            uint previous = U32(ai, 0x6b), gold = U32(participant, 0x20);
            W32(ai, 0x6b, gold);
            if (previous > gold) W32(ai, 0x6f, previous - gold);
        }
        uint afford = U32(participant, 0x20);
        afford = Math.Min(afford, U32(participant, 0x18) >> 1);
        afford = Math.Min(afford, U32(participant, 0x1c) >> 1);
        afford = Math.Min(afford, unchecked(U32(participant, 0x24) << 3));
        W32(ai, 0x1a04, afford);
        foreach (var (record, template) in chain)
        {
            if ((sbyte)record[0x19] <= 0) continue;
            if (record[0x14] == 0 && (sbyte)record[0x19] <= (sbyte)template[0x46]) continue;
            W32(ai, 0x19e9, U32(ai, 0x19e9) + template[0x76]);
            uint flags = U32(template, 0x40);
            if ((flags & 0x808) == 0x800) W32(ai, 0x19ed, U32(ai, 0x19ed) + 1);
            if ((flags & 0x28) == 8)
            {
                ai[0x5c]++;
                if ((flags & 0x40) == 0)
                {
                    uint x = U32(record, 4), y = U32(record, 8);
                    if (x > maxX) maxX = x;
                    if (x < minX) minX = x;
                    if (y > maxY) maxY = y;
                    if (y < minY) minY = y;
                }
            }
            if (ai[5] != 2) W32(ai, 0x19f1, U32(ai, 0x19f1) + U32(template, 0x9e));
            uint kind = flags & 0xf08;
            if (kind == 0x408 && U32(record, 0x24) != 0) { ai[0x19ff + (record[0x23] == 1 ? 3 : 4)]++; continue; }
            if (kind == 0x208) ai[0x1a00]++;
            if (kind == 0xb08) ai[0x1a26]++;
            if (kind == 0xc08) W32(ai, 0x77, U32(ai, 0x77) | 1);
        }
        if (U32(ai, 0x19f1) > 5000 && U32(ai, 0x19e9) <= 400) W32(ai, 0x19f1, 5000);
        if (maxX != 0)
        {
            byte cx = (byte)((maxX + minX) >> 6), cy = (byte)((maxY + minY) >> 6);
            ai[0x61] = cx; ai[0x62] = cy;
            uint size = ((maxX - minX) >> 5) & 0xffff00ff | (((maxY - minY) >> 5) & 0xff) << 8;
            size += 0x1414;
            var (left, top, right, bottom) = OriginalTargetRules.Clip(cx, cy, (byte)size, (byte)(size >> 8), mapWidth, mapHeight);
            ai[0x63] = left; ai[0x64] = top; ai[0x65] = right; ai[0x66] = bottom;
        }
        return new(maxX, maxY, minX, minY);
    }

    /// <summary>23B10: value DAT[76] of living objects (above DAT[46]) inside the territory +63..+66, per owner (16 dwords at F149).</summary>
    public static uint[] Threat(ReadOnlySpan<byte> ai, IEnemySearchWorld world)
    {
        var threat = new uint[16];
        ushort from = BinaryPrimitives.ReadUInt16LittleEndian(ai[0x63..]), to = BinaryPrimitives.ReadUInt16LittleEndian(ai[0x65..]);
        ushort size = unchecked((ushort)(to - from + 0x101));
        foreach (uint reference in OriginalTargetRules.Collect(world, ai[0x63], ai[0x64], (byte)size, (byte)(size >> 8)))
        {
            var (record, template) = world.Resolve(reference);
            if ((sbyte)record[0x19] <= (sbyte)template[0x46]) continue;
            threat[record[3] & 15] += template[0x76];
        }
        return threat;
    }
}

/// <summary>Participants seen by one AI turn: raw AI blocks (1A35h), participant records (F5h) and the message kernel.</summary>
public interface IAiTurnWorld : IEnemySearchWorld
{
    int Count { get; }
    Span<byte> Block(int participant);
    Span<byte> Participant(int participant);
    /// <summary>EDD5: AL = type, AH = other, BL = queue owner, BH, EBP = parameter.</summary>
    void Send(byte type, byte other, byte owner, byte bh, uint parameter);
}

/// <summary>AI turn LE:1 2323A..23550 (docs/ai.md): vassal tribute, collapse, relation decisions every 128 turns.</summary>
public static class OriginalAiTurn
{
    public const int Plans = 0x697, PlanSize = 0x15, PlanCount = 20;
    public const uint WeakStrength = 0x7cf, ThreatLimit = 200, LossLimit = 200;
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    /// <summary>2323A..23550 for participant <paramref name="me"/>; <paramref name="ebp"/> is the register at entry (message parameter
    /// of the collapse notices). Returns true when the turn reaches the construction stage 23551, which then still owes the
    /// final <c>+1 −= 42h</c> of 2365C; otherwise the turn is complete.</summary>
    public static bool Decide(IAiTurnWorld world, byte me, uint ebp, uint mapWidth, uint[] threat)
    {
        var ai = world.Block(me);
        W32(ai, 1, U32(ai, 1) + 0x42);
        Tribute(world, me);
        if ((ai[0x1a27] & 1) == 0) return Finish(ai);
        if ((U32(ai, 1) & 0xff) == 0 && U32(ai, 0x19f1) <= WeakStrength) Collapse(world, me, ebp);
        if ((ai[0x1a27] & 1) == 0) return Finish(ai);
        if ((U32(ai, 1) & 0x7f) != 0) return true;
        var values = OriginalAiRules.Threat(ai, world);
        Array.Copy(values, threat, 16);
        uint parameter = unchecked((mapWidth - (byte)(ai[0x65] - ai[0x63] + 1)) * 4);   // EBP left by D7B1
        for (int target = 0; target < world.Count; target++)
        {
            if (target == me || HasPlanFor(ai, (byte)target)) continue;
            var other = world.Block(target);
            if (U32(other, 0x19f1) <= WeakStrength) continue;
            uint strength = U32(ai, 0x19f1);
            bool vassalFlag = (ai[0x1a27] & 2) != 0;
            byte t = (byte)target;
            switch (world.Participant(me)[0xb9 + target])
            {
                case 1:
                    if (strength < U32(ai, 0x3d)) break;
                    if (threat[target] > ThreatLimit) { AddPlan(ai, vassalFlag ? (byte)2 : (byte)1, t, parameter); break; }
                    if (Overlaps(ai, other)) { AddPlan(ai, vassalFlag ? (byte)2 : (byte)3, t, parameter); break; }
                    if (U32(other, 0x6f) >= LossLimit) { AddPlan(ai, 2, t, parameter); break; }
                    if (Queued(world.Participant(target), 0xc, me)) { AddPlan(ai, vassalFlag ? (byte)2 : (byte)3, t, parameter); break; }
                    if (Queued(world.Participant(target), 0xd, me)) AddPlan(ai, 2, t, parameter);
                    break;
                case 3:
                    if (strength >= U32(ai, 0x3d) && U32(other, 0x6f) >= LossLimit) AddPlan(ai, 2, t, parameter);
                    break;
                case 5:
                    if (strength < U32(ai, 0x3d) || vassalFlag) break;
                    if (threat[target] > ThreatLimit) world.Send(5, t, me, 0, parameter);
                    else if (Overlaps(ai, other)) AddPlan(ai, 1, t, parameter);
                    break;
                case 4:
                    if (strength > U32(ai, 0x35)) world.Send(0xd, t, me, 0, parameter);
                    break;
                case 2:
                    AddPlan(ai, 3, t, parameter);
                    break;
            }
        }
        return true;
    }

    private static bool Finish(Span<byte> ai) { W32(ai, 1, U32(ai, 1) - 0x42); return false; }

    /// <summary>23244: half of each resource gain since the last turn (gold +20, metal +18, wood +1C) goes to every overlord (relation 4).</summary>
    private static void Tribute(IAiTurnWorld world, byte me)
    {
        var ai = world.Block(me);
        for (int lord = world.Count - 1; lord >= 0; lord--)
        {
            if (world.Participant(me)[0xb9 + lord] != 4) continue;
            var mine = world.Participant(me); var theirs = world.Participant(lord);
            foreach (var (resource, last) in new[] { (0x20, 0x1a28), (0x18, 0x1a2c), (0x1c, 0x1a30) })
            {
                uint now = U32(mine, resource), previous = U32(ai, last);
                if (now >= previous)
                {
                    uint half = (now - previous) >> 1;
                    W32(theirs, resource, U32(theirs, resource) + half); W32(mine, resource, U32(mine, resource) - half);
                }
                W32(ai, last, U32(mine, resource));
            }
        }
    }

    /// <summary>232D4..23383: at strength ≤ 1999 every 256 turns the settlement collapses: behaviour bytes reset, own queue cleared,
    /// enemies at war are credited (+19FD, or +19FE with flag 2) and every known participant except the last receives message 13h.</summary>
    private static void Collapse(IAiTurnWorld world, byte me, uint ebp)
    {
        var ai = world.Block(me);
        ai[0x19e4] |= 2; ai[0x19ff] = 0; ai[0x1a15] = 0; ai[0x1a27] &= 2; ai[0x1a34] = 0;
        var mine = world.Participant(me);
        for (int s = 0; s < 8; s++) mine[0x34 + s * 14] = 0xff;
        if (U32(ai, 0x19f1) == 0) return;
        for (int other = world.Count - 2; other >= 0; other--)
        {
            byte relation = world.Participant(me)[0xb9 + other];
            if (relation == 0) continue;
            if (relation == 2)
            {
                var block = world.Block(other);
                if ((ai[0x1a27] & 2) != 0) block[0x19fe]++; else block[0x19fd]++;
            }
            if (other != me) world.Send(0x13, (byte)other, me, 0, ebp);
        }
    }

    /// <summary>23A95: a plan of list +697 already aims at <paramref name="target"/> (+4).</summary>
    public static bool HasPlanFor(ReadOnlySpan<byte> ai, byte target)
    {
        for (int k = 0; k < PlanCount; k++)
            if (ai[Plans + k * PlanSize] != 0xff && ai[Plans + k * PlanSize + 4] == target) return true;
        return false;
    }

    /// <summary>23B6A as used by its JNE callers: territories intersect and the own bottom +66 differs from the other top +64.</summary>
    public static bool Overlaps(ReadOnlySpan<byte> mine, ReadOnlySpan<byte> other) =>
        mine[0x63] <= other[0x65] && mine[0x64] <= other[0x66] && mine[0x65] >= other[0x63] && mine[0x66] >= other[0x64] && mine[0x66] != other[0x64];

    /// <summary>EE69: queue of <paramref name="participant"/> holds a message of <paramref name="type"/> about <paramref name="other"/>.</summary>
    public static bool Queued(ReadOnlySpan<byte> participant, byte type, byte other)
    {
        for (int s = 0; s < 8; s++) if (participant[0x34 + s * 14] == type && participant[0x34 + s * 14 + 9] == other) return true;
        return false;
    }

    /// <summary>1EB5C in list +697: unless a plan with the same mode and +1 exists, take the first free record (1EAED).
    /// Bytes +5..+7 (CH, DL, DH in the original: address leftovers) are written as zero.</summary>
    public static void AddPlan(Span<byte> ai, byte mode, byte target, uint parameter)
    {
        for (int k = 0; k < PlanCount; k++)
            if (ai[Plans + k * PlanSize] == mode && ai[Plans + k * PlanSize + 1] == target) return;
        for (int k = 0; k < PlanCount; k++)
        {
            var plan = ai.Slice(Plans + k * PlanSize, PlanSize);
            if (plan[0] != 0xff) continue;
            plan.Clear(); plan[0] = mode; plan[1] = target; plan[4] = target; W32(plan, 8, parameter);
            return;
        }
    }
}
