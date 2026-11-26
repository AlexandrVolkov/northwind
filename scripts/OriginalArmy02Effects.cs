using System;
using static Northwind.OriginalArmy01Effects;

namespace Northwind;

/// <summary>The effect scripts of ARMY__02.WRK from 16DD (kinds 7, 9, 16h..18h, 1Ah, 1Ch, 1Dh, 20h..28h): the 17 scripts follow the shapes of ARMY__01.WRK 10ED..1357 with other frames, plus the sound 35h of the
/// two burning flames. AH = 0 creates the effect from the record of its creator, AH = 1 steps. Verified against the original x86 by tools/verify_army01_effects.py.</summary>
public static class OriginalArmy02Effects
{
    /// <summary>The entries 16DD, 170C, 1775, 1795, 1828, 18B8, 18FC, 1989, 1A06, 1A65, 1A8C, 1A96, 1AA0, 1AAA, 1AB4, 1ABE, 1AC8 are the scripts 0..16.</summary>
    public static void Run(IArmy01EffectWorld w, byte[] r, int ah, int k, byte[] parent, uint parentRel)
    {
        var g = new BuildingRegs { Eax = (uint)ah << 8, Ebx = ah == 1 ? 0u : parentRel };
        switch (k)
        {
            case 0:
                if (ah != 1) { Word(r, 3, 0x663); r[0] = 7; Put(r, 0x12, parentRel); r[0x11] = 0x18; return; }
                if ((sbyte)w.ObjectAt(U32(r, 0x12))[0x19] <= 0) r[0] = 0xff; return;
            case 1:
                if (ah != 1)
                {
                    Word(r, 3, Word(parent, 1)); r[2] = parent[3];
                    uint e = (uint)((parent[0x17] >> 1) & 0xc); if (e != 0) e -= 4;
                    e = (byte)(e + ((parent[0x25] & 7) >> 1)); Put(r, 0x1c, e * 3 + 0x7cd); Put(r, 0x18, 0x258); r[0] = 9; r[0x17] = 4; return;
                }
                Fragments(r, 4); return;
            case 8:
                if (ah != 1)
                {
                    Word(r, 3, Word(parent, 1)); r[2] = parent[3];
                    Put(r, 0x1c, (uint)(((parent[0x25] & 7) >> 1) * 4 + 0xa24)); Put(r, 0x18, 0x258); r[0] = 0x20; r[0x17] = 5; return;
                }
                Fragments(r, 5); return;
            case 2:
                if (ah != 1) { Word(r, 3, 0x806); r[0] = 0x16; r[0x17] = 0x14; return; }
                Word(r, 3, (ushort)(Word(r, 3) + 1)); r[0x17]--; if (r[0x17] == 0) r[0] = 0xff; return;
            case 3: Flame(w, r, g, ah, 0x7f2, 0x7fc, 0x17, 5, 5, true); return;
            case 4: Flame(w, r, g, ah, 0x7fc, 0x806, 0x18, 4, 6, false); return;
            case 5:
                if (ah != 1) { Word(r, 3, (ushort)((byte)((parent[0x25] & 7) << 3) + 0x608)); r[0] = 0x1a; r[0x16] = 2; r[0x17] = 7; return; }
                Animate(r); return;
            case 16:
                if (ah != 1) { int e = parent[0x25] & 7; Word(r, 3, (ushort)(e * 2 + e * 8 + 0xa8d)); r[0] = 0x26; r[0x16] = 2; r[0x17] = 9; return; }
                Animate(r); return;
            case 6: Mark(w, r, g, ah, parent, parentRel); return;
            case 7:
                if (ah != 1) { Spark(w, r, g); return; }
                SparkStep(w, r, g); return;
            default:
                {
                    byte[] kinds = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0x21, 0x22, 0x23, 0x24, 0x25, 0x27, 0x28];
                    if (ah != 1) { r[0] = kinds[k]; Word(r, 3, Word(parent, 1)); r[2] = parent[3]; Put(r, 0x18, 0x1388); return; }
                    Put(r, 0x18, U32(r, 0x18) - 1); if (U32(r, 0x18) == 0) r[0] = 0xff; return;
                }
        }
    }

    private static void Fragments(byte[] r, byte first)
    {
        Put(r, 0x18, U32(r, 0x18) - 1); if (U32(r, 0x18) != 0) return;
        Put(r, 0x18, 0x258); if (r[0x17] == first) Word(r, 3, (ushort)U32(r, 0x1c));
        Word(r, 3, (ushort)(Word(r, 3) + 1)); r[0x17]--; if (r[0x17] == 0) r[0] = 0xff;
    }

    /// <summary>The burning flames 1795 (kind 17h) and 1828 (18h): two frame sets with life +16; the first (5Bh frames, FS:A9 asks 5) first counts its life down, then sounds 8.</summary>
    private static void Flame(IArmy01EffectWorld w, byte[] r, BuildingRegs g, int ah, ushort first, ushort last, byte kind, uint ad, uint ask, bool countFirst)
    {
        if (ah != 1)
        {
            Word(r, 3, first); r[0] = kind; r[0x16] = 2;
            g.Eax = ad; g.Ebx = (g.Ebx & 0xffffff00) | 4; w.Service(0xad, g); return;
        }
        if (r[5] == 0) { r[0x16]--; if (r[0x16] == 0) r[0] = 0xff; return; }
        r[0x17]++;
        if ((r[0x17] & 1) == 0) { Word(r, 3, (ushort)(Word(r, 3) + 1)); if (Word(r, 3) == last) Word(r, 3, first); }
        w.Service(0xa5, g); if (!g.Zf) return;
        if (countFirst) r[0x16]--;
        g.Eax = 8; w.Sound35(8);
        g.Eax = ask; w.Service(0xa9, g);
        byte al = (byte)g.Eax; al--; if (al == 0) { r[0] = 0xff; return; }
        al--; if (al == 0) { w.Sound35(7); r[0] = 0xff; return; }
        g.Eax = (g.Eax & 0xffffff00) | al;
        r[0x16]--; if (r[0x16] == 0) { r[5] = 0; return; }
        g.Eax = (g.Eax & 0xffffff00) | 1; g.Ebx = (g.Ebx & 0xffffff00) | 2; w.Service(0xad, g);
    }

    private static void Mark(IArmy01EffectWorld w, byte[] r, BuildingRegs g, int ah, byte[] parent, uint parentRel)
    {
        if (ah != 1) { r[2] = parent[3]; Put(r, 0x12, parentRel); r[0] = 0x1c; r[0x16] = 0; }
        else
        {
            r[0x17]--;
            if ((r[0x17] & 1) == 0) { r[0x16]++; if (r[0x16] >= 0x0a) r[0x16] = 0; }
        }
        uint al = (uint)(byte)(w.StateByte() + 8) & 0xe;
        Word(r, 3, (ushort)((byte)(al * 5 + r[0x16]) + 0x81a));
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
        Word(r, 3, 0x86a); r[0] = 0x1d; r[0x16] = 0;
        g.Eax = (g.Eax & 0xffffff00) | 4; g.Ebx = (g.Ebx & 0xffffff00) | 4; w.Service(0xad, g);
    }

    private static void SparkStep(IArmy01EffectWorld w, byte[] r, BuildingRegs g)
    {
        r[0x17]++;
        if (r[0x16] != 0) { Put(r, 0x12, U32(r, 0x12) - 1); if (U32(r, 0x12) == 0) r[0] = 0xff; return; }
        if ((r[0x17] & 1) == 0) { Word(r, 3, (ushort)(Word(r, 3) + 1)); if (Word(r, 3) == 0x874) Word(r, 3, 0x86a); }
        w.Service(0xa5, g); if (!g.Zf) return;
        g.Eax = 2; w.Service(0xa9, g);
        byte al = (byte)g.Eax; al--; if (al == 0) { r[0] = 0xff; return; }
        al--; if (al == 0) { w.Sound35(7); r[0] = 0xff; return; }
        r[0x16]--; r[5] = 0; Put(r, 0x12, 0x200);
    }
}
