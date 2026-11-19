using System.Buffers.Binary;
namespace Northwind;

/// <summary>Outcome of one arrow update (ARMY__00.WRK 5DA): still flying, freed, a visual hit effect, or a miss handler.</summary>
public enum ArrowState : byte { Flying, Freed, Spark, HardSpark, Dropped, Stuck }

/// <summary>Arrow effect, ARMY__00.WRK type 2, with LE:1 9A1B/996F/A5B8. Effect records are 22h bytes. See docs/combat.md.</summary>
public static class OriginalArrowRules
{
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);
    private static uint Abs(uint value) => (value & 0x80000000) != 0 ? unchecked(0u - value) : value;

    /// <summary>9A1B: scatter the aim point by distance and the shooter's experience; two RNG streams.</summary>
    public static (uint X, uint Y) Scatter(ReadOnlySpan<byte> shooter, uint x, uint y, byte accuracy, uint widthPixels, uint heightPixels,
        Func<byte> random, Func<byte> secondary)
    {
        uint dx = Abs(unchecked(U32(shooter, 4) - x)), dy = Abs(unchecked(U32(shooter, 8) - y));
        uint distance = (dx > dy ? dx : dy) >> 1;
        byte half = distance >= 0x100 ? (byte)0xff : (byte)distance;
        byte factor = (byte)(accuracy + (shooter[0x16] >> 4));
        byte spread = (byte)(half - (byte)((half * factor) >> 4));
        uint Offset(byte r)
        {
            uint value = (uint)((r * spread) >> 8);
            bool negative = (value & 1) != 0; value >>= 1;
            return negative ? unchecked(0u - value) : value;
        }
        uint oy = Offset(random()); uint ny = unchecked(y + oy); if (ny >= heightPixels) ny = unchecked(ny - oy);
        uint ox = Offset(secondary()); uint nx = unchecked(x + ox); if (nx >= widthPixels) nx = unchecked(nx - ox);
        return (nx, ny);
    }

    /// <summary>996F: distance in +12, 24.8 velocity in +18/+1C, arc speed in +20; clears fractions and height.</summary>
    public static void Velocity(Span<byte> effect, uint x, uint y, byte speed)
    {
        byte signs = 0;
        uint dx = unchecked(x - U32(effect, 7)); if ((dx & 0x80000000) != 0) { dx = unchecked(0u - dx); signs |= 1; }
        uint dy = unchecked(y - U32(effect, 0xc)); if ((dy & 0x80000000) != 0) { dy = unchecked(0u - dy); signs |= 2; }
        W32(effect, 0x12, dx < dy ? dy : dx);
        effect[6] = 0; effect[0xb] = 0; effect[0x10] = 0; BinaryPrimitives.WriteUInt16LittleEndian(effect[0x20..], 0);
        uint full = (uint)speed << 8;
        uint vx, vy;
        uint qy = dy >> 2, qx = dx >> 2;
        if (qy == 0) { vy = 0; vx = full; }
        else if (qx == 0) { vx = 0; vy = full; }
        else
        {
            byte by = (byte)qy, bx = (byte)qx;
            if (by == bx) { vx = full; vy = full; }
            else if (by > bx) { vx = (uint)((byte)((bx << 8) / by) * speed); vy = full; }
            else { vy = (uint)((byte)((by << 8) / bx) * speed); vx = full; }
        }
        if ((signs & 1) != 0) vx = unchecked(0u - vx);
        if ((signs & 2) != 0) vy = unchecked(0u - vy);
        W32(effect, 0x18, vx); W32(effect, 0x1c, vy);
        ushort distance = (ushort)U32(effect, 0x12);
        byte steps = (byte)(distance / speed);
        if (steps <= 1) return;
        byte count = (byte)(steps - 1);
        ushort lift = (ushort)((effect[0x11] << 8) / count);
        uint quotient = (uint)((byte)lift | ((effect[0x11] << 8) % count) << 8);
        uint arc = (uint)(0x5c * count);
        if (arc > quotient) BinaryPrimitives.WriteUInt16LittleEndian(effect[0x20..], (ushort)(arc - quotient));
    }

    /// <summary>A5B8: sixteen-way direction of the velocity, also added to the frame word +3.</summary>
    public static byte Direction(Span<byte> effect)
    {
        int ax = (int)U32(effect, 0x18), bx = -(int)U32(effect, 0x1c);
        uint magnitudeY = Abs((uint)bx), magnitudeX = Abs((uint)ax);
        sbyte bias = 0;
        if (magnitudeX >= magnitudeY) { (ax, bx) = (bx, -ax); bias = 4; }
        if (bx < 0) bias -= 8;
        short dividend = (short)(ax >> 2);
        sbyte divisor = (sbyte)(bx >> 8);
        byte quotient = (byte)(sbyte)(dividend / divisor);
        byte direction = (byte)((((byte)(quotient + 0x40) >> 5) - bias - 2) & 0xf);
        BinaryPrimitives.WriteUInt16LittleEndian(effect[3..], (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(effect[3..]) + direction));
        return direction;
    }

    /// <summary>WRK 593..5D9 (AH=0). Returns the sound passed to A47E.</summary>
    public static int Create(Span<byte> effect, ReadOnlySpan<byte> shooter, uint targetX, uint targetY, uint widthPixels, uint heightPixels,
        Func<byte> random, Func<byte> secondary)
    {
        effect[2] = shooter[3]; effect[0] = 2; effect[0x16] = 0;
        var (x, y) = Scatter(shooter, targetX, targetY, 8, widthPixels, heightPixels, random, secondary);
        Velocity(effect, x, y, 0x0f);
        BinaryPrimitives.WriteUInt16LittleEndian(effect[3..], 0x19);
        byte direction = Direction(effect);
        BinaryPrimitives.WriteUInt16LittleEndian(effect[3..], (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(effect[3..]) + direction));
        effect[0x17] = (byte)(direction * 2);
        return 2;
    }

    /// <summary>WRK 5DA..6C6 (AH=1). <paramref name="struck"/> is 982B at the arrow point (null when empty).
    /// Returns the new state, the sounds and, for 7FE/AEB, their BL argument.</summary>
    public static (ArrowState State, int? Sound, byte Argument) Step(Span<byte> effect, Span<byte> target, ReadOnlySpan<byte> targetTemplate,
        bool struck, uint widthPixels, uint heightPixels, Func<byte> random)
    {
        if ((int)U32(effect, 0x12) > 0x10)
        {
            if (struck && (U32(targetTemplate, 0x40) & 8) != 0 && effect[2] != target[3] && (random() & 1) != 0)
                return Hit(effect, target, targetTemplate, random);
            return Fly(effect, widthPixels, heightPixels);
        }
        if (!struck) return Miss(effect, 8);
        return Hit(effect, target, targetTemplate, random);
    }
    private static (ArrowState, int?, byte) Hit(Span<byte> effect, Span<byte> target, ReadOnlySpan<byte> targetTemplate, Func<byte> random)
    {
        uint flags = U32(targetTemplate, 0x40);
        if ((sbyte)target[0x19] > 0)
        {
            byte marks = 9; byte health = target[0x19];
            if (4 > targetTemplate[0x44])
            {
                marks |= 2; byte damage = (byte)(4 - targetTemplate[0x44]);
                sbyte left = (sbyte)(byte)(health - damage);
                target[0x19] = left < 0 ? (byte)0 : (byte)left;
            }
            target[0x17] = (byte)((target[0x17] & 0xe0) | marks);
            if ((marks & 2) == 0) return Miss(effect, (byte)flags);
        }
        return Visual(effect, (flags & 0x10) != 0, random);
    }
    private static (ArrowState, int?, byte) Visual(Span<byte> effect, bool hard, Func<byte> random)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(effect[3..], hard ? (ushort)0x7d : (ushort)((random() & 8) + 0x62));
        effect[0] = (byte)(hard ? 5 : 4); effect[0x16] = 2; effect[0x17] = 8;
        return (hard ? ArrowState.HardSpark : ArrowState.Spark, 3, 0);
    }
    private static (ArrowState, int?, byte) Miss(Span<byte> effect, byte flags)
        => ((flags & 8) == 0 ? ArrowState.Dropped : ArrowState.Stuck, null, (byte)(effect[0x17] >> 1));
    private static (ArrowState, int?, byte) Fly(Span<byte> effect, uint widthPixels, uint heightPixels)
    {
        uint vx = U32(effect, 0x18), x = U32(effect, 6), sumX = unchecked(x + vx);
        W32(effect, 6, sumX); if (sumX < x) vx = unchecked(0u - vx);
        uint vy = U32(effect, 0x1c), y = U32(effect, 0xb), sumY = unchecked(y + vy);
        W32(effect, 0xb, sumY); if (sumY < y) vy = unchecked(0u - vy);
        uint step = (vx >= vy ? vx : vy) >> 8;
        W32(effect, 0x12, unchecked(U32(effect, 0x12) - step));
        if (U32(effect, 7) >= widthPixels || U32(effect, 0xc) >= heightPixels) { effect[0] = 0xff; return (ArrowState.Freed, null, 0); }
        return (ArrowState.Flying, null, 0);
    }
}
