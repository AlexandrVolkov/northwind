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
