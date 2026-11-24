using System.Buffers.Binary;
namespace Northwind;

/// <summary>The services of the move step (recorded stubs in tools/verify_unit_move.py).</summary>
public interface IMoveWorld
{
    uint Angle(uint goalX, uint x, uint goalY, uint y);   // 6996: direction byte (0..255) between two cells
    void Occupancy(uint reference);                        // 43B0: removes (0) or puts (reference) the footprint
    bool Free(byte army, byte template, byte layer, uint x, uint y);   // AC63: true (ZF) = the place is free
    void RevealFog();                                      // 813E
    void Finish();                                         // 12BF2: the end of the move
    uint LimitX { get; } uint LimitY { get; }              // 18E6 / 18EA: the map size in pixels
    sbyte Trig(int index);                                 // DS:4790: the 32 (cos, sin) pairs of the headings, scaled by 16
}

/// <summary>The move step (state 4 of <see cref="OriginalUnitMotion"/>), LE:1 12930..12AE1: the unit turns by one 32nd of the circle every fourth update toward the goal cell, moves by its speed
/// (+26 with the 64-fold carry) along the heading +31 on the table DS:4790, if the new place is free (AC63) and inside the map; otherwise the move ends (BL 80h blocked, 40h finished).
/// Verified against the original x86 by tests/fixtures/unit-move.json (tools/verify_unit_move.py).</summary>
public sealed class OriginalUnitMove(IMoveWorld world)
{
    public byte Flags { get; private set; }
    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static void Put(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);

    public byte Step(byte[] r, byte[] t, byte goalX, byte goalY, byte bl, uint reference)
    {
        Flags = bl;
        if ((r[0x24] & 7) != 1) { r[0x27] = 0; r[0x2c] = 0; Put(r, 0x28, 0xffffffff); r[0x24]++; r[0x26] = (byte)((r[0x26] & 0xc0) | t[0x58]); }
        if ((Flags & 0x40) != 0) { Finish(r); return End(r, t); }
        uint cx = U32(r, 4) >> 5, cy = U32(r, 8) >> 5;
        if (cx == goalX && cy == goalY) { Finish(r); return End(r, t); }
        if ((r[0x2e] & 3) == 0)
        {
            byte turn = (byte)(world.Angle(goalX, cx, goalY, cy) >> 3), heading = r[0x31];
            turn = (byte)(turn - heading);
            if (turn != 0)
            {
                turn &= 0x1f; heading++; if (turn > 0x10) heading -= 2; heading &= 0x1f; r[0x31] = heading;
                byte sector = (byte)(heading >> 2); r[0x25] = (byte)((sector << 3) | sector); r[0x27] = 0;
            }
        }
        byte speed = r[0x26]; int sum = r[0x26] + (byte)(speed << 6); r[0x26] = (byte)sum;
        if (sum > 0xff) speed += 4;
        int s = speed & 0x3f; int h = r[0x31] * 2;
        int dy = (s * world.Trig(h + 1)) >> 4, dx = (s * world.Trig(h)) >> 4;
        uint nx = (uint)(dx + (int)U32(r, 4)), ny = (uint)(dy + (int)U32(r, 8));
        if (nx >= world.LimitX || ny >= world.LimitY) { Flags |= 0x80; Finish(r); return End(r, t); }
        world.Occupancy(0);
        if (!world.Free(r[0], r[0x0e], r[0x0d], nx, ny)) { world.Occupancy(reference); Flags |= 0x80; Finish(r); return End(r, t); }
        Put(r, 4, nx); Put(r, 8, ny); world.Occupancy(reference);
        if ((r[0x2e] & 1) == 0) { r[0x27]++; if (r[0x27] >= t[0x4a]) r[0x27] = 0; }
        if ((r[0x2e] & 3) == 0) world.RevealFog();
        return End(r, t);
    }

    private void Finish(byte[] r) { Flags |= 0x40; r[0x27] = 0; world.Finish(); }

    private byte End(byte[] r, byte[] t)   // 12AA6
    {
        uint eax = r[0x31]; 
        if ((eax & 3) == 0)
        {
            int product = (int)(eax >> 2) * t[0x4a]; byte low = (byte)((byte)product + r[0x27]);
            eax = (uint)((product & 0xff00) | low);
        }
        else
        {
            uint ebx = (byte)(t[0x4a] + t[0x4b]); eax += ebx * 8; ebx = t[0x4f]; eax += ebx * 4;
        }
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(1), (ushort)(eax + BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(0x48))));
        return Flags;
    }
}
