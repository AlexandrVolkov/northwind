using System;

namespace Northwind;

/// <summary>The services of the effect scripts of ARMY__01.WRK 10ED..1357 (recorded stubs in tools/verify_army01_effects.py).</summary>
public interface IArmy01EffectWorld
{
    /// <summary>FS:A5 (ZF), FS:A9 (AL), FS:AD: results in EAX, EBX and ZF.</summary>
    void Service(int slot, BuildingRegs g);
    /// <summary>The object behind a pointer relative to the pool (+12h).</summary>
    byte[] ObjectAt(uint rel);
    /// <summary>FS:71 for the object behind the pointer: its template.</summary>
    byte[] Template71(uint rel);
    /// <summary>The byte at +8 of the structure fs:[0Ch].</summary>
    byte StateByte();
    /// <summary>FS:35: a sound (EAX).</summary>
    void Sound35(uint eax);
}

/// <summary>The effect scripts of ARMY__01.WRK from 10ED: the dust and the fragments (kinds 9, 0Ah, 16h..1Ah), two frame animations (1Bh, 1Ch), the mark that follows the target (1Dh), which turns into the
/// spark (1Eh). AH = 0 creates the effect from the record of its creator, AH = 1 steps. Verified against the original x86 by tools/verify_army01_effects.py.</summary>
public static class OriginalArmy01Effects
{
    /// <summary>The entries 10ED, 114C, 117B, 11A2, 11AC, 11B6, 11C0, 11CA, 120E, 1258, 12E5 are the scripts 0..10.</summary>
    public static void Run(IArmy01EffectWorld w, byte[] r, int ah, int k, byte[] parent, uint parentRel)
    {
        var g = new BuildingRegs { Eax = (uint)ah << 8, Ebx = ah == 1 ? 0u : parentRel };
        switch (k)
        {
            case 0:
                if (ah != 1)
                {
                    Word(r, 3, Word(parent, 1)); r[2] = parent[3]; int e = (parent[0x25] & 7) >> 1; Put(r, 0x1c, (uint)(e * 4 + 0x531)); Put(r, 0x18, 0x258); r[0] = 9; r[0x17] = 5; return;
                }
                Put(r, 0x18, U32(r, 0x18) - 1); if (U32(r, 0x18) != 0) return;
                Put(r, 0x18, 0x258); if (r[0x17] == 5) Word(r, 3, (ushort)U32(r, 0x1c));
                Word(r, 3, (ushort)(Word(r, 3) + 1)); r[0x17]--; if (r[0x17] == 0) r[0] = 0xff; return;
            case 1:
                if (ah != 1) { Word(r, 3, 0x60e); r[0] = 0xa; Put(r, 0x12, parentRel); r[0x11] = 0x18; return; }
                if ((sbyte)w.ObjectAt(U32(r, 0x12))[0x19] <= 0) r[0] = 0xff; return;
            case >= 2 and <= 6:
                if (ah != 1) { r[0] = (byte)(0x14 + k); Word(r, 3, Word(parent, 1)); r[2] = parent[3]; Put(r, 0x18, 0x1388); return; }
                Put(r, 0x18, U32(r, 0x18) - 1); if (U32(r, 0x18) == 0) r[0] = 0xff; return;
            case 7:
                if (ah != 1) { Word(r, 3, (ushort)((byte)((parent[0x25] & 7) << 3) + 0x80b)); r[0] = 0x1b; r[0x16] = 2; r[0x17] = 7; return; }
                Animate(r); return;
            case 8:
                if (ah != 1) { int e = parent[0x25] & 7; Word(r, 3, (ushort)(e * 2 + e * 8 + 0x84b)); r[0] = 0x1c; r[0x16] = 2; r[0x17] = 9; return; }
                Animate(r); return;
            case 9: Mark(w, r, g, ah, parent, parentRel); return;
            default:
                if (ah != 1) { Spark(w, r, g); return; }
                SparkStep(w, r, g); return;
        }
    }

    internal static ushort Word(byte[] a, int i) => BitConverter.ToUInt16(a, i);
    internal static void Word(byte[] a, int i, ushort v) => BitConverter.GetBytes(v).CopyTo(a, i);
    internal static uint U32(byte[] a, int i) => BitConverter.ToUInt32(a, i);
    internal static void Put(byte[] a, int i, uint v) => BitConverter.GetBytes(v).CopyTo(a, i);

    internal static void Animate(byte[] r)
    {
        if (r[0x17] != 0)
        {
            r[0x16]--; if (r[0x16] != 0) return;
            r[0x16] = 2; Word(r, 3, (ushort)(Word(r, 3) + 1)); r[0x17]--; return;
        }
        r[0x16]++; if (r[0x16] == 0) r[0] = 0xff;
    }

    private static void Mark(IArmy01EffectWorld w, byte[] r, BuildingRegs g, int ah, byte[] parent, uint parentRel)
    {
        if (ah != 1) { r[2] = parent[3]; Put(r, 0x12, parentRel); r[0] = 0x1d; r[0x16] = 0; }
        else
        {
            r[0x17]--;
            if ((r[0x17] & 1) == 0) { r[0x16]++; if (r[0x16] >= 0x0a) r[0x16] = 0; }
        }
        uint al = (uint)(byte)(w.StateByte() + 8) & 0xe;
        Word(r, 3, (ushort)((byte)(al * 5 + r[0x16]) + 0x89b));
        var obj = w.ObjectAt(U32(r, 0x12));
        if ((Word(obj, 0x17) & 0x80) == 0)
        {
            if ((sbyte)obj[0x19] <= 0) { r[0] = 0xff; return; }
            var t = w.Template71(U32(r, 0x12));
            uint a = (uint)(byte)(t[0x4a] + t[0x4b] + 1) + Word(t, 0x48);
            if (Word(obj, 1) <= (ushort)a) return;
        }
        Word(obj, 0x17, (ushort)(Word(obj, 0x17) & 0xfeff));
        g.Eax &= 0xffff00ff;
        Spark(w, r, g);
    }

    private static void Spark(IArmy01EffectWorld w, byte[] r, BuildingRegs g)
    {
        Word(r, 3, 0x8eb); r[0] = 0x1e; r[0x16] = 0;
        g.Eax = (g.Eax & 0xffffff00) | 4; g.Ebx = (g.Ebx & 0xffffff00) | 4; w.Service(0xad, g);
    }

    private static void SparkStep(IArmy01EffectWorld w, byte[] r, BuildingRegs g)
    {
        r[0x17]++;
        if (r[0x16] != 0) { Put(r, 0x12, U32(r, 0x12) - 1); if (U32(r, 0x12) == 0) r[0] = 0xff; return; }
        if ((r[0x17] & 1) == 0) { Word(r, 3, (ushort)(Word(r, 3) + 1)); if (Word(r, 3) == 0x8f5) Word(r, 3, 0x8eb); }
        w.Service(0xa5, g); if (!g.Zf) return;
        g.Eax = 2; w.Service(0xa9, g);
        byte al = (byte)g.Eax; al--; if (al == 0) { r[0] = 0xff; return; }
        al--; if (al == 0) { r[0] = 0xff; return; }
        r[0x16]--; r[5] = 0; Put(r, 0x12, 0x200);
    }
}
