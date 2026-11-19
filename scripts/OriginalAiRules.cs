using System.Buffers.Binary;
namespace Northwind;

public readonly record struct CensusBounds(uint MaxX, uint MaxY, uint MinX, uint MinY);

/// <summary>Settlement AI kernels from LE:1: census 1E87A and territory threat 23B10. See docs/ai.md.</summary>
public static class OriginalAiRules
{
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
