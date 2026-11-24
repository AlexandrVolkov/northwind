using System;

namespace Northwind;

/// <summary>The memory the object allocator works on: the pool of the objects (at the address <see cref="PoolBase"/>), the participants (+5 the first object, +9 the end of the chain, +0Eh the search hint, the last two
/// as offsets into the pool), the template tables of the armies and the globals of the allocator (the script address [19C3], the health [1F44], the frame [1F2F]).</summary>
public sealed class AllocMemory
{
    public byte[] Pool = new byte[0x20000];
    public uint PoolBase = 0x400000;
    public uint[] First = new uint[2], End = new uint[2], Hint = new uint[2];
    public byte[][] Size = new byte[3][], Hp = new byte[3][];
    public uint[][] Script = new uint[3][];
    public ushort[][] Frame = new ushort[3][];
    public uint[] WrkBase = new uint[3];
    public uint ScriptAddress; public byte HpGlobal; public ushort FrameGlobal;
    public uint U32(uint abs) => BitConverter.ToUInt32(Pool, (int)(abs - PoolBase));
    public void Put(uint abs, uint v) => BitConverter.GetBytes(v).CopyTo(Pool, (int)(abs - PoolBase));
}

/// <summary>FS:45 = LE:1 A758..A870: allocates an object record for the owner. The chain of the objects of the owner (from the first object to the end) is searched first-fit from the hint: free space is the
/// gap before the hint, then the slack of every object behind its template size; behind the last object the chain grows (up to 10000h bytes from the first). The record is cleared, filled (army, owner, template,
/// position, layer, state 80h, health, frame, +20 = FFh) and the initialization script of the template runs with AH = 0 and the three stack arguments. Verified against the original x86 by
/// tools/verify_object_alloc.py.</summary>
public static class OriginalObjectAllocator
{
    public static (bool Ok, uint Record) Allocate(AllocMemory m, byte army, byte owner, byte tmpl, byte layer, uint x, uint y, uint eaxHigh, uint[] args, Action<uint, uint, uint, uint, uint> script)
    {
        uint size = m.Size[army][tmpl];
        m.ScriptAddress = m.WrkBase[army] + m.Script[army][tmpl];
        m.HpGlobal = m.Hp[army][tmpl]; m.FrameGlobal = m.Frame[army][tmpl];
        uint first = m.First[owner], esi = m.PoolBase + m.Hint[owner], ebp = m.PoolBase + m.End[owner], edi = first;
        uint free = esi - first;
        if (free >= size)
        {
            m.Put(edi + 0x10, free); m.Hint[owner] = edi - m.PoolBase;
        }
        else
        {
            bool found = false;
            while (esi != ebp)
            {
                byte a = m.Pool[esi - m.PoolBase], t = m.Pool[esi - m.PoolBase + 0xe];
                uint tsize = m.Size[a][t], slack = m.U32(esi + 0x10) - tsize;
                edi = tsize + esi;
                if (slack >= size) { m.Put(edi + 0x10, slack); m.Put(esi + 0x10, m.U32(esi + 0x10) - slack); found = true; break; }
                esi += m.U32(esi + 0x10);
            }
            if (!found)
            {
                edi = esi; uint end = esi + size;
                if (end - first >= 0x10000) return (false, edi - m.PoolBase);
                m.Put(edi + 0x10, size); m.End[owner] = end - m.PoolBase;
            }
        }
        uint keep = m.U32(edi + 0x10);
        Array.Clear(m.Pool, (int)(edi - m.PoolBase), (int)size);
        m.Put(edi + 0x10, keep);
        int o = (int)(edi - m.PoolBase);
        m.Pool[o] = army; m.Pool[o + 3] = owner; m.Pool[o + 0xe] = tmpl; m.Put(edi + 4, x); m.Put(edi + 8, y); m.Pool[o + 0xd] = layer; m.Pool[o + 0x14] = 0x80;
        m.Pool[o + 0x19] = m.HpGlobal; m.Pool[o + 0x1a] = m.HpGlobal; BitConverter.GetBytes(m.FrameGlobal).CopyTo(m.Pool, o + 1); m.Pool[o + 0x20] = 0xff;
        script((eaxHigh & 0xffff0000) | (uint)(m.FrameGlobal & 0xff), args[0], args[1], args[2], edi - m.PoolBase);
        return (true, edi - m.PoolBase);
    }
}
