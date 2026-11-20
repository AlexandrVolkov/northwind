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
    /// <summary>DS:287E arctangent table of 6996 (squad retreat direction).</summary>
    public string ArctangentHex { get; set; } = "";
    /// <summary>LE:3 F02E: squad message text ids, three per row; the row is AI block +29.</summary>
    public string SquadMessagesHex { get; set; } = "";
    /// <summary>LE:3 F0E6: class bytes of the armies gathered by military request 7.</summary>
    public string ArmyClassesHex { get; set; } = "";
    /// <summary>LE:3 F0F0: fortification patterns of order mode 7 (5-byte entries: class byte, dx0, dy0, dx1, dy1; FFh ends).</summary>
    public List<string> Fortifications { get; set; } = [];
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
        byte mapWidth, byte mapHeight, Func<int, byte>? fog = null)
    {
        var bounds = CensusBody(ai, participant, chain, mapWidth, mapHeight);
        // 1EAC1: every 256 turns the explored share (1EC3F) goes to +1A01.
        if (fog != null && (U32(ai, 1) & 0xff) == 0) ai[0x1a01] = Explored(ai[0], fog, mapWidth, mapHeight);
        return bounds;
    }

    /// <summary>1EC3F: (cells the participant has seen · 256 − 1) / cells, low byte (no seen cell gives FFh).</summary>
    public static byte Explored(byte participant, Func<int, byte> fog, byte mapWidth, byte mapHeight)
    {
        byte bit = (byte)(1 << (participant & 31));   // SHL CH, CL on a byte
        uint cells = (uint)(mapWidth * mapHeight), seen = 0;
        for (int c = 0; c < cells; c++) if ((fog(c) & bit) == 0) seen++;
        return (byte)(unchecked((seen << 8) - 1) / cells);
    }

    private static CensusBounds CensusBody(Span<byte> ai, ReadOnlySpan<byte> participant, IEnumerable<(byte[] Record, byte[] Template)> chain,
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

    /// <summary>226F1..227C6 (step counter + 63h during the call): with +1A14 bit 2 every 128 steps living objects with
    /// DAT[40] &amp; 4800Ch = 8 lose a point of condition; otherwise with bit 1 every 64 steps damaged buildings
    /// (DAT[40] &amp; 28h = 8, not an empty mine, DAT[45] − condition ≥ 5 as a byte, record +0F bit 2) get +14 = (+14 &amp; 7) | 48h.</summary>
    public static void Upkeep(ReadOnlySpan<byte> ai, IEnumerable<(byte[] Record, byte[] Template)> chain)
    {
        uint step = U32(ai, 1) + 0x63;
        if ((ai[0x1a14] & 2) != 0)
        {
            if ((step & 0x7f) != 0) return;
            foreach (var (record, template) in chain)
                if ((sbyte)record[0x19] > 0 && (U32(template, 0x40) & 0x4800c) == 8) record[0x19]--;
            return;
        }
        if ((ai[0x1a14] & 1) == 0 || (step & 0x3f) != 0) return;
        foreach (var (record, template) in chain)
        {
            uint flags = U32(template, 0x40);
            if ((flags & 0x28) != 8) continue;
            if (((flags >> 8) & 0xf) == 4 && U32(record, 0x24) == 0) continue;
            if ((byte)(template[0x45] - record[0x19]) < 5 || (record[0x0f] & 2) == 0) continue;
            record[0x14] = (byte)((record[0x14] & 7) | 0x48);
        }
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

/// <summary>AI plan executor LE:1 23667..23A6D over list +697 (docs/ai.md). Control flow follows the original labels.
/// Military requests go to list +1AB (1EB5C); bytes the original fills from register leftovers are written as zero
/// (BH of mode 7, +5..+0B), and message parameters are passed as zero.</summary>
public static class OriginalAiPlans
{
    public const int Military = 0x1ab, Plans = 0x697, Size = 0x15, Count = 20;
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    public static void Execute(IAiTurnWorld world, byte me, uint[] threat)
    {
        var ai = world.Block(me);
        if ((ai[0x1a27] & 1) == 0) return;
        for (int k = 0; k < Count; k++)
        {
            var plan = world.Block(me).Slice(Plans + k * Size, Size);
            byte target = plan[4];
            byte Relation() => world.Participant(me)[0xb9 + target];
            uint Strength() => U32(world.Block(me), 0x19f1);
            bool Queued(byte type) => OriginalAiTurn.Queued(world.Participant(target), type, me);
            void Send(byte type) => world.Send(type, target, me, 0, 0);
            void Survey() { var values = OriginalAiRules.Threat(world.Block(me), world); Array.Copy(values, threat, 16); }
            bool Strongest()
            {
                uint value = threat[target];
                for (int c = 0; c < world.Count; c++) if (threat[c] > value) return false;
                return true;
            }
            switch (plan[0])
            {
                case 1: goto Campaign;
                case 2: if (plan[2] != 2) goto Campaign; goto Surrendered;
                case 3: if (plan[2] != 2) goto Campaign; goto AllianceOffered;
                case 0xa: goto Truce;
                default: goto Drop;
            }
        Campaign:   // 23690
            if (plan[2] == 0)
            {
                plan[2]++;
                Request(ai, 5, target, 0x96);
                goto Next;
            }
            if (plan[2] != 1) goto AtWar;
            if (Relation() == 2) goto March;
            if ((U32(plan, 0x10) & 0x1f) == 0)
            {
                Survey();
                if (threat[target] >= OriginalAiTurn.ThreatLimit) goto March;
            }
            if (!Find(ai, 5, target)) goto March;
            if (Strength() > U32(ai, 0x41)) goto Next;
            goto Cancel;
        March:   // 23727
            plan[2]++;
            Request(ai, 7, target, 0);
            W32(plan, 8, unchecked((U32(ai, 0x49) * U32(ai, 0x5d) >> 5) + U32(ai, 0x19f9)));
            if (Relation() != 2) Send(5);
            goto Next;
        AtWar:   // 23778
            if (Relation() != 2) goto Cancel;
            if (Strength() <= U32(ai, 0x41)) goto ToTruce;
            if (U32(plan, 8) <= U32(ai, 0x19f9))
            {
                if (plan[0] == 3) goto ToTruce;
                plan[0]++; plan[2] = 0;
                world.Send(4, target, me, 0, 0);
                goto Next;
            }
            // 237B0..237F3: EE69 leaves EDI on the found message, so the FFh store consumes the target's message (the plan stays).
            if (Consume(world.Participant(target), 0, me)) Send(2);
            if (Consume(world.Participant(target), 6, me)) Send(8);
        EnemyAlive:   // 237F8
            if (U32(world.Block(target), 0x19f1) <= OriginalAiTurn.WeakStrength) goto Cancel;
            goto Next;
        ToTruce:   // 23826
            plan[0] = 0xa; plan[2] = 0;
            goto Next;
        Surrendered:   // 2386D
            if (!Queued(0)) goto AtWar;
            Send(1);
            goto Cancel;
        AllianceOffered:   // 238A6
            if (!Queued(6)) goto Surrendered;
            Send(7);
            goto Cancel;
        Truce:   // 238CC
            if (plan[2] == 0) { W32(plan, 8, 1); plan[2]++; }
            if (Relation() != 2) goto Cancel;
            if (plan[2] == 1)
            {
                if (Strength() <= U32(ai, 0x31)) { plan[2] = 2; W32(plan, 8, 1); goto Next; }
                W32(plan, 8, U32(plan, 8) - 1);
                if (U32(plan, 8) != 0) goto Answer;
                W32(plan, 8, 2000);
                Survey();
                if (threat[target] < 100 || !Strongest()) goto Answer;
                Send(6);
                W32(world.Block(me), 0x3d, Strength() + 3000);
                goto Answer;
            }
            if (Strength() > U32(ai, 0x31)) { plan[2] = 1; W32(plan, 8, 1); goto Next; }
            W32(plan, 8, U32(plan, 8) - 1);
            if (U32(plan, 8) != 0) goto Answer;
            W32(plan, 8, 2000);
            Survey();
            if (threat[target] < 100 || !Strongest()) goto Answer;
            Send(0);
        Answer:   // 23974
            if (Queued(6)) { Send(7); goto Cancel; }
            if (Queued(0)) { Send(1); goto Cancel; }
            goto EnemyAlive;
        Cancel:   // 23714
            CancelMilitary(world, ai, target);
        Drop:   // 23A40
            plan[0] = 0xff;
        Next:   // 23A49
            W32(plan, 0x10, U32(plan, 0x10) + 1);
        }
    }

    /// <summary>EE69 followed by <c>mov [edi], FFh</c>: mark the first queued message of <paramref name="type"/> about <paramref name="other"/> as read.</summary>
    public static bool Consume(Span<byte> participant, byte type, byte other)
    {
        for (int s = 0; s < 8; s++)
            if (participant[0x34 + s * 14] == type && participant[0x34 + s * 14 + 9] == other) { participant[0x34 + s * 14] = 0xff; return true; }
        return false;
    }

    /// <summary>1EB40 in list +1AB: a record with this mode and +1.</summary>
    public static bool Find(ReadOnlySpan<byte> ai, byte mode, byte first)
    {
        for (int k = 0; k < Count; k++)
            if (ai[Military + k * Size] == mode && ai[Military + k * Size + 1] == first) return true;
        return false;
    }

    /// <summary>1EB5C in list +1AB with BL = CL = target.</summary>
    public static void Request(Span<byte> ai, byte mode, byte target, byte bh)
    {
        if (Find(ai, mode, target)) return;
        for (int k = 0; k < Count; k++)
        {
            var record = ai.Slice(Military + k * Size, Size);
            if (record[0] != 0xff) continue;
            record.Clear(); record[0] = mode; record[1] = target; record[3] = bh; record[4] = target;
            return;
        }
    }

    /// <summary>23ABB: drop military records against <paramref name="target"/>: modes 5 and 7 by +1, 6 by +4, 9 by the owner of object +8.</summary>
    public static void CancelMilitary(IAiTurnWorld world, Span<byte> ai, byte target)
    {
        for (int k = 0; k < Count; k++)
        {
            var record = ai.Slice(Military + k * Size, Size);
            bool drop = record[0] switch
            {
                5 or 7 => record[1] == target,
                6 => record[4] == target,
                9 => world.Resolve(U32(record, 8)).Record[3] == target,
                _ => false,
            };
            if (drop) record[0] = 0xff;
        }
    }
}

/// <summary>AI development step LE:1 23C11..23DC1 (docs/ai.md): laboratory start/order and the request list +83B resolved
/// through the settlement's development tree. Bytes the original fills from register leftovers are written as zero
/// (+34F records +4..+7, +0A/+0B of building orders; +83B records +4..+7).</summary>
public static class OriginalAiResearch
{
    public const int Orders = 0x34f, Requests = 0x83b, Size = 0x15, Count = 20;
    public const uint LaboratoryMask = 0xf08, LaboratoryClass = 0x808;
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    /// <summary>1ECA0: first living object of the chain (under construction: above DAT[46]) with DAT[40] &amp; mask = value.</summary>
    public static byte[]? FirstObject(IEnumerable<(byte[] Record, byte[] Template)> chain, uint mask, uint value)
    {
        foreach (var (record, template) in chain)
        {
            if ((sbyte)record[0x19] <= 0) continue;
            if (record[0x14] == 0 && (sbyte)record[0x19] <= (sbyte)template[0x46]) continue;
            if ((U32(template, 0x40) & mask) == value) return record;
        }
        return null;
    }

    /// <summary>1EB5C: add a record unless one with this mode and +1 exists.</summary>
    public static void Add(Span<byte> ai, int list, byte mode, byte first, byte third, uint parameter)
    {
        for (int k = 0; k < Count; k++)
            if (ai[list + k * Size] == mode && ai[list + k * Size + 1] == first) return;
        for (int k = 0; k < Count; k++)
        {
            var record = ai.Slice(list + k * Size, Size);
            if (record[0] != 0xff) continue;
            record.Clear(); record[0] = mode; record[1] = first; record[3] = third; W32(record, 8, parameter);
            return;
        }
    }

    /// <summary>23C11 for the settlement <paramref name="me"/> of army <paramref name="army"/>; <paramref name="random"/> is 9E41.</summary>
    public static void Step(Span<byte> ai, byte me, byte army, Span<byte> tree, IReadOnlyList<byte[]> templates,
        IEnumerable<(byte[] Record, byte[] Template)> chain, Func<byte> random)
    {
        uint step = U32(ai, 1) + 0x85;
        if ((ai[0x1a34] & 1) == 0) return;
        if ((step & 0xff) == 0 && ai[0x53] != 0)
        {
            var laboratory = FirstObject(chain, LaboratoryMask, LaboratoryClass);
            if (laboratory != null) { laboratory[0x22] = 5; laboratory[0x23] = 5; }
            else
            {
                var open = OriginalTechRules.Buildable(templates, tree, true, LaboratoryMask, LaboratoryClass);   // 22E46
                if (open.Count > 0)
                {
                    byte template = open[(random() * open.Count) >> 8];
                    Add(ai, Orders, 5, 0x6a, 0x50, (uint)(template | army << 8));
                }
                else
                {
                    var known = OriginalTechRules.Buildable(templates, tree, false, LaboratoryMask, LaboratoryClass);
                    if (known.Count > 0) Add(ai, Requests, 2, 0x6a, 0x80, (uint)(0x80 | known[0] << 8 | army << 16));
                }
            }
        }
        for (int k = 0; k < Count; k++)
        {
            var request = ai.Slice(Requests + k * Size, Size);
        Again:   // 23CE3
            if (request[0] == 1)
            {
                if (request[8] == 1) Add(ai, Orders, 5, 0x61, 0x80, (uint)(request[9] | request[10] << 8));
            }
            else if (request[0] == 2 && Unlock(request, tree, random)) goto Again;
            request[0] = 0xff;
            W32(request, 0x10, U32(request, 0x10) + 1);
        }
    }

    /// <summary>23D12..23D88: find the wanted item (+8 kind, +9 id); if it still has unmet conditions, pick a random parent
    /// listing it as a child ((9E41 · unmet) >> 8 -th occurrence); a parent with no unmet conditions becomes the request (mode 1).</summary>
    private static bool Unlock(Span<byte> request, Span<byte> tree, Func<byte> random)
    {
        byte kind = request[8], id = request[9];
        int at = 0;
        while (true)
        {
            if (tree[at] == 0xff) return false;
            if (tree[at] == kind && tree[at + 1] == id) break;
            at += 0x10 + 2 * tree[at + 0xf];
        }
        if (tree[at + 2] == 0) return false;
        byte pick = (byte)((random() * tree[at + 2]) >> 8);
        at = 0;
        while (tree[at] != 0xff)
        {
            for (int c = 0; c < tree[at + 0xf]; c++)
                if (tree[at + 0x10 + 2 * c] == kind && tree[at + 0x11 + 2 * c] == id && (sbyte)--pick < 0)
                {
                    if (tree[at + 2] != 0) return false;
                    request[8] = tree[at]; request[9] = tree[at + 1]; request[0] = 1;
                    return true;
                }
            at += 0x10 + 2 * tree[at + 0xf];
        }
        return false;
    }
}
