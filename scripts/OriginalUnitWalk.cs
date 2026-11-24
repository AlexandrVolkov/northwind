using System.Buffers.Binary;
namespace Northwind;

/// <summary>The services of the walk step (recorded stubs in tools/verify_unit_walk.py).</summary>
public interface IWalkWorld
{
    (byte Heading, byte Code) NextStep();            // 7FA7 (AL heading, AH 0 none, 1 blocked, 2 turn only, else go)
    bool Turn(byte target);                          // 9653: true (ZF) = turned
    void Occupancy(uint step);                       // 9B0D at the middle of a step
    void Effect(byte army, byte type, uint x, uint y);   // 9727 (the footprint dust of DAT[A2])
    void RevealFog();                                // 813E
    void ReleaseRoute();                             // 8120
    void SearchEnemy(byte mode);                     // D895
    byte MapWidth { get; }
    uint OccupancyWord(uint cell);                   // the dword of the table 18CE of the cell
    byte TerrainType(uint cell);                     // byte of the terrain descriptor of the cell (18EE → 18FE)
    sbyte Delta(int index);                          // the 16 bytes at 19C7: x and y unit vector of each heading
}

/// <summary>One step of the walk (state 2 of <see cref="OriginalUnitMotion"/>), LE:1 70B0..72FD: takes the next step of the route, turns, computes the speed from the terrain and DAT[58+type],
/// moves the unit by its speed, advances the stride counter +27 (effects, DAT[57], DAT[4A]) and ends the step. Returns the flags (BL: 40h finished, 80h blocked). Verified against the original x86 by
/// tests/fixtures/unit-walk.json (tools/verify_unit_walk.py).</summary>
public sealed class OriginalUnitWalk(IWalkWorld world)
{
    public byte Flags { get; private set; }
    public byte Request0 { get; private set; }
    public byte Request1 { get; private set; }

    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static void Put(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);

    public byte Step(byte[] r, byte[] t, byte al, byte ah, byte bl)
    {
        Request0 = al; Request1 = ah; Flags = bl; bool resume = true;
        switch (r[0x24] & 7)
        {
            case 1: break;
            case 2: Wait(r, t); return End(r, t);
            case 3: Turn(r, t); return End(r, t);
            case 4: Move(r, t); return End(r, t);
            default: r[0x26] = 0; r[0x27] = 0; r[0x2c] = 0; Put(r, 0x28, 0xffffffff); r[0x24]++; break;
        }
        if (resume)
        {
            if ((Flags & 0x40) != 0) { Finish(r); return End(r, t); }
            var (heading, code) = world.NextStep();
            if (code == 0) { Finish(r); return End(r, t); }
            if (code == 1) { Flags |= 0x80; Finish(r); return End(r, t); }
            r[0x25] = (byte)((r[0x25] & 7) | (byte)(heading << 3)); r[0x24]++;
            if (code == 2) return End(r, t);
            r[0x24]++; r[0x2c] = 0x20; Turn(r, t);
        }
        return End(r, t);
    }

    private void Finish(byte[] r) { Flags |= 0x40; r[0x27] = 0; world.ReleaseRoute(); }

    private void Wait(byte[] r, byte[] t)   // 713B
    {
        if ((Flags & 0x40) != 0) { Finish(r); return; }
        r[0x27] = 0;
        if (!world.Turn((byte)(r[0x25] >> 3))) return;   // jne: not turned yet
        r[0x24]--;
    }

    private void Turn(byte[] r, byte[] t)   // 7165
    {
        Flags &= 0xbf;
        if (!world.Turn((byte)(r[0x25] >> 3))) return;
        r[0x24]++;
        uint x = U32(r, 4) >> 5, y = U32(r, 8) >> 5;
        uint cell = (x + (uint)((y & 0xff) * world.MapWidth & 0xffff)) & 0xffffffff;
        byte type; int slot;
        if ((r[0x0d] & 1) != 0 && world.OccupancyWord(cell) != 0) { type = 0; slot = 0; }
        else { type = world.TerrainType(cell); slot = type & 7; }
        byte speed = t[0x58 + slot];
        if ((speed & 0x80) == 0) speed = (byte)(speed - (byte)(type >> 4));
        speed &= 0x3f;
        r[0x26] = (byte)((r[0x26] & 0xc0) | speed);
        r[0x26] = (byte)(r[0x26] + (r[0x16] >> 5));
        Move(r, t);
    }

    private void Move(byte[] r, byte[] t)   // 71F5
    {
        Flags &= 0xbf;
        byte al = r[0x26], ah = (byte)(al << 6); al = (byte)(al >> 2);
        int sum = r[0x26] + ah; r[0x26] = (byte)sum; if (sum > 0xff) al++;
        uint cl = (uint)(al & 0xf);
        byte old = r[0x2c]; bool borrow = old < cl; r[0x2c] = (byte)(old - cl);
        if (borrow) { cl = old; r[0x2c] = 0; }
        if (old >= 0x10 && r[0x2c] < 0x10) world.Occupancy(cl);
        int h = (r[0x25] & 7) * 2;
        sbyte dx = world.Delta(h), dy = world.Delta(h + 1);
        if (dx != 0) Put(r, 4, dx < 0 ? U32(r, 4) - cl : U32(r, 4) + cl);
        if (dy != 0) Put(r, 8, dy < 0 ? U32(r, 8) - cl : U32(r, 8) + cl);
        r[0x27] = (byte)(r[0x27] + (byte)((r[0x26] << 3) & 0xf0));
        if ((r[0x27] >> 4) >= t[0x57]) goto stride;
        goto tail;
    stride:
        {
            byte next = (byte)((r[0x27] & 0xf) + 1);
            if (next >= t[0x4a])
            {
                if (t[0xa2] != 0xff) world.Effect(r[0], t[0xa2], U32(r, 4), U32(r, 8));
                next = 1;
            }
            r[0x27] = next;
        }
    tail:
        if (r[0x2c] != 0) return;
        r[0x24] = (byte)((r[0x24] & 0xf8) | 1);
        world.RevealFog(); world.SearchEnemy(0);
    }

    private byte End(byte[] r, byte[] t)   // 72D8
    {
        int product = (r[0x25] & 7) * t[0x4a];
        byte low = (byte)((byte)product + (byte)(r[0x27] & 0xf));
        ushort frame = (ushort)(((product & 0xff00) | low) + BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(0x48)));
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(1), frame);
        return Flags;
    }
}
