using System.Buffers.Binary;
namespace Northwind;

/// <summary>Catapult stone, ARMY__00.WRK effect type 8 (85A), its explosion type 10 (986) and the LE:1 helpers D6E6 (flight)
/// and BD0F (hit in an area). Effect records are 22h bytes. CFAC (the crater left in the terrain) is the caller's.
/// See docs/combat.md, «Камни катапульт».</summary>
public static class OriginalCatapultRules
{
    public const byte Stone = 8, Explosion = 10, Attack = 0x0a;
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);
    private static ushort U16(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(raw[offset..]);
    private static void W16(Span<byte> raw, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(raw[offset..], value);

    /// <summary>WRK 85A AH=0: owner, frame BBh, aim with accuracy 1 (9A1B), speed 9 (996F). Returns the sound (6).</summary>
    public static int Create(Span<byte> effect, ReadOnlySpan<byte> shooter, uint targetX, uint targetY, uint widthPixels, uint heightPixels,
        Func<byte> random, Func<byte> secondary)
    {
        effect[2] = shooter[3]; effect[0] = Stone; W16(effect, 3, 0xbb);
        var (x, y) = OriginalArrowRules.Scatter(shooter, targetX, targetY, 1, widthPixels, heightPixels, random, secondary);
        OriginalArrowRules.Velocity(effect, x, y, 9);
        return 6;
    }

    /// <summary>D6E6: one flight step. False (landed) when the remaining distance +12 is not above zero. Otherwise the
    /// 24.8 position (+6/+0B) moves by the velocity; the distance shrinks by the larger step, where a step whose
    /// addition carried counts negated (the original's abs); the height +10 rises by +20 (not below zero), which falls by
    /// B9h; outside the map (pixel sizes) the effect is freed. True while flying.</summary>
    public static bool Fly(Span<byte> effect, uint widthPixels, uint heightPixels)
    {
        if ((int)U32(effect, 0x12) <= 0) return false;
        uint vx = U32(effect, 0x18), x = U32(effect, 6), nx = unchecked(x + vx); W32(effect, 6, nx);
        uint ax = nx < x ? unchecked(0u - vx) : vx;
        uint vy = U32(effect, 0x1c), y = U32(effect, 0x0b), ny = unchecked(y + vy); W32(effect, 0x0b, ny);
        uint ay = ny < y ? unchecked(0u - vy) : vy;
        W32(effect, 0x12, unchecked(U32(effect, 0x12) - (Math.Max(ax, ay) >> 8)));
        ushort height = unchecked((ushort)(U16(effect, 0x10) + U16(effect, 0x20)));
        W16(effect, 0x10, (short)height < 0 ? (ushort)0 : height);
        W16(effect, 0x20, unchecked((ushort)(U16(effect, 0x20) - 0xb9)));
        if (U32(effect, 7) >= widthPixels || U32(effect, 0x0c) >= heightPixels) effect[0] = 0xff;
        return true;
    }

    /// <summary>WRK 88E: a flight step; on landing returns the cell (the caller hits it with <see cref="Hit"/>, leaves the
    /// crater CFAC and turns the effect into <see cref="Explode"/>).</summary>
    public static (byte X, byte Y)? Step(Span<byte> effect, uint widthPixels, uint heightPixels)
    {
        if (Fly(effect, widthPixels, heightPixels)) return null;
        return ((byte)(U32(effect, 7) >> 5), (byte)(U32(effect, 0x0c) >> 5));
    }

    /// <summary>BD0F with the area of 219C3 around the cell: every distinct object of the planes in <paramref name="planes"/>
    /// (bit n — plane n), cell by cell, row by row. A living one (signed +19 above 0) loses attack − DAT[44] when positive
    /// (not below 0); every one gets the hit flags in the low five bits of +17: (kind &lt;&lt; 3) | 1, with bit 1 for a
    /// wound.</summary>
    public static void Hit(byte attack, byte kind, byte planes, byte x, byte y, byte width, byte height, byte mapWidth, byte mapHeight,
        Func<int, int, uint> occupant, Func<uint, (byte[] Record, byte[] Template)> resolve)
    {
        var (x0, y0, x1, y1) = OriginalTargetRules.Clip(x, y, width, height, mapWidth, mapHeight);
        byte flags = (byte)(kind << 3 | 1);
        var seen = new List<uint>();
        byte rows = (byte)(y1 - y0 + 1), columns = (byte)(x1 - x0 + 1);
        int cy = y0;
        do
        {
            int cx = x0; byte left = columns;
            do
            {
                for (int plane = 0; plane < 3; plane++)
                {
                    if ((planes >> plane & 1) == 0) continue;
                    uint reference = occupant(plane, cy * mapWidth + cx);
                    if (reference == 0 || seen.Contains(reference)) continue;
                    seen.Add(reference);
                    var (o, t) = resolve(reference);
                    byte mark = flags;
                    if ((sbyte)o[0x19] > 0 && attack > t[0x44])
                    {
                        byte damage = (byte)(attack - t[0x44]);
                        mark |= 2;
                        o[0x19] = o[0x19] >= damage ? (byte)(o[0x19] - damage) : (byte)0;
                    }
                    o[0x17] = (byte)(o[0x17] & 0xe0 | mark);
                }
                cx++;
            } while (--left != 0);
            cy++;
        } while (--rows != 0);
    }

    /// <summary>WRK 986 AH=0: the effect becomes an explosion (type 10, frame BCh, 18 frames of 2 ticks, height 20h).
    /// Returns the sound (7).</summary>
    public static int Explode(Span<byte> effect)
    {
        W16(effect, 3, 0xbc); effect[0] = Explosion; effect[0x16] = 2; effect[0x17] = 0x12; effect[0x11] = 0x20;
        return 7;
    }

    /// <summary>WRK 9AE: the explosion drifts by the global vector ([1C8E]/[1C92] through FS:[0C]) and plays its frames;
    /// after the last one the effect is freed.</summary>
    public static void Smoke(Span<byte> effect, int driftX, int driftY)
    {
        W32(effect, 6, unchecked(U32(effect, 6) + (uint)driftX)); W32(effect, 0x0b, unchecked(U32(effect, 0x0b) + (uint)driftY));
        if (--effect[0x16] != 0) return;
        effect[0x16] = 2; W16(effect, 3, unchecked((ushort)(U16(effect, 3) + 1)));
        if (--effect[0x17] == 0) effect[0] = 0xff;
    }
}
