using System;

namespace Northwind;

/// <summary>The memory of the effect pool: the records of 22h bytes from <see cref="Base"/>, the high-water mark [19E0], the script address [19C3], the limits [18E6] and [18EA], and the effect tables of the armies.</summary>
public sealed class EffectMemory
{
    public byte[] Pool = new byte[0x8100];
    public uint Base = 0x404000, HighWater, ScriptAddress, LimitX, LimitY;
    public uint[] TablePointer = new uint[3], WrkBase = new uint[3], ScriptOffset = new uint[3];
    public uint Offset(uint abs) => abs - Base;
    public uint U32(uint abs) => BitConverter.ToUInt32(Pool, (int)(abs - Base));
    public void Put(uint abs, uint v) => BitConverter.GetBytes(v).CopyTo(Pool, (int)(abs - Base));
}

/// <summary>The effect pool of LE:1: 9727 (FS:19, creates an effect) and 96CD (updates every live effect). Verified against the original x86 by tools/verify_effect_pool.py.</summary>
public static class OriginalEffectPool
{
    /// <summary>9727: the effect of the type BL (bit 80h clears the army AL) of the army AL at the point (ECX, EDX), the layer BH. The three stack arguments go to the script of the type with AH = 0.
    /// <paramref name="script"/> receives (EAX, EBX, ECX, EDX, EDI, ESI) of the call. AH = FFh and the flags of the OR on failure.</summary>
    public static void Create(EffectMemory m, ActionRegs g, uint[] args, Action<uint, uint, uint, uint, uint, uint> script)
    {
        g.Cmp(g.Ecx, m.LimitX, 32);
        if (g.Jae) { Fail(g); return; }
        g.Cmp(g.Edx, m.LimitY, 32);
        if (g.Jae) { Fail(g); return; }
        g.Test(g.Bl, 0x80, 8);
        if (!g.Zf) { g.Al = 0; g.Logic(0, 8); }
        g.Bl = (byte)(g.Bl & 0x7f);
        uint eax = g.Eax, edi = m.Base, limit = edi + 0x7fde;
        for (; ; )
        {
            g.Cmp(m.Pool[edi - m.Base], 0xff, 8);
            if (g.Zf) break;
            edi += 0x22; g.Cmp(edi, limit, 32);
            if (g.Cf || g.Zf) continue;
            g.Edi = edi; g.Eax = eax; Fail(g); return;
        }
        int o = (int)(edi - m.Base);
        m.Pool[o] = g.Bl; m.Pool[o + 1] = g.Al; m.Put(edi + 7, g.Ecx); m.Put(edi + 0xc, g.Edx); m.Pool[o + 5] = g.Bh; m.Pool[o + 0x11] = 0;
        byte army = g.Al, type = g.Bl;
        g.Eax &= 0xff; g.Ah = 0x56; g.Eax = (g.Eax & 0xffff0000) | (ushort)(g.Al * 0x56); g.Cf = g.Of = (g.Eax & 0xff00) != 0;
        g.Edx = (g.Eax & 0xffff) + 0x6ca;
        g.Al = 7; g.Eax = (g.Eax & 0xffff0000) | (ushort)(7 * type); g.Cf = g.Of = (g.Eax & 0xff00) != 0;
        g.Ebx = m.TablePointer[army] + (g.Eax & 0xffff); g.Eax = m.ScriptOffset[army] + m.WrkBase[army]; m.ScriptAddress = g.Eax;
        g.Ebx = args[0]; g.Ecx = args[1]; g.Edx = args[2]; g.Ah = 0; g.Edi = edi;
        script(g.Eax, g.Ebx, g.Ecx, g.Edx, edi, g.Esi);
        g.Cmp(edi, m.HighWater, 32);
        if (!g.Jb) { g.Eax = 0x22 + edi; m.HighWater = g.Eax; }
        g.Al = 0; g.Logic(g.Al, 8);
    }

    private static void Fail(ActionRegs g) { g.Ah = 0xff; g.Logic(0xff, 8); }

    /// <summary>96CD: AH = 1 update of every live effect (type ≠ FFh) below the high-water mark; afterwards the mark is the end of the last live one.</summary>
    public static void Update(EffectMemory m, ActionRegs g, bool active, Action<uint, uint, uint, uint, uint, uint> script)
    {
        g.Cmp(active ? 1u : 0u, 0, 8);
        if (!active) return;
        uint esi = m.Base; uint eax = esi;
        for (; ; )
        {
            g.Cmp(esi, m.HighWater, 32);
            if (!g.Jb) break;
            if (m.Pool[esi - m.Base] != 0xff)
            {
                byte type = m.Pool[esi - m.Base], army = m.Pool[esi - m.Base + 1];
                uint ebx = (uint)(0x56 * army) + 0x6ca;
                uint edi = m.TablePointer[army] + (uint)(7 * type);
                uint address = m.WrkBase[army] + m.ScriptOffset[army];
                m.ScriptAddress = address;
                g.Esi = esi; g.Ebx = ebx; g.Edi = edi;
                script((address & 0xffff00ff) | 0x100, ebx, g.Ecx, g.Edx, edi, esi);
                eax = esi;
            }
            esi += 0x22;
        }
        g.Eax = eax + 0x22; m.HighWater = g.Eax; g.Add(eax, 0x22);
        g.Esi = esi;
    }
}
