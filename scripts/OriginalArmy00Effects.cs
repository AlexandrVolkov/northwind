using System;
using static Northwind.OriginalArmy01Effects;

namespace Northwind;

/// <summary>The effect scripts of ARMY__00.WRK 1267..151B: the guardian of a target (1Bh), the impacts that pick between the spark 6FE and 735 (129D, 1481, 14CE), the fountain of fragments 12EC (kind 1Dh),
/// and the three frame animations 1Eh..20h (13A3, 13F9, 143D). The jumps into 6FE, 735 and A44 are handed to the world. Verified against the original x86 by tools/verify_army01_effects.py.</summary>
public static class OriginalArmy00Effects
{
    /// <summary>The scripts 0..7 are 1267, 129D, 12EC, 13A3, 13F9, 143D, 1481, 14CE.</summary>
    public static void Run(IArmy01EffectWorld w, byte[] r, int ah, int k, byte[] parent, uint parentRel, uint ebxSmall)
    {
        var g = new BuildingRegs { Eax = (uint)ah << 8, Ebx = ah == 1 ? 0u : (k is 3 or 4 or 5 ? ebxSmall : parentRel) };
        switch (k)
        {
            case 0:
                if (ah != 1) { r[0] = 0x1b; Put(r, 0x12, parentRel); }
                var obj = w.ObjectAt(U32(r, 0x12));
                if ((sbyte)obj[0x19] > 0) { obj[0x19] = w.Template71(U32(r, 0x12))[0x45]; return; }
                r[0] = 0xff; return;
            case 1: Impact(w, r, g, ah, true, 0xa, 0, 1); return;
            case 6: Impact(w, r, g, ah, false, 5, 0x16, 0x16); return;
            case 7: Impact(w, r, g, ah, false, 5, 0x15, 0x15); return;
            case 2: Fountain(w, r, g, ah, parent, parentRel); return;
            default:
                {
                    int[] bases = [0, 0, 0, 0x1c6, 0x1e6, 0x206]; byte[] kinds = [0, 0, 0, 0x1e, 0x1f, 0x20];
                    if (ah != 1)
                    {
                        g.Ebx = ((ebxSmall & 7) << 2) + (uint)bases[k]; Word(r, 3, (ushort)g.Ebx); r[0] = kinds[k]; r[0x16] = 2; r[0x17] = 4;
                        w.Sound35(0x14);
                        if (k == 3)
                        {
                            g.Eax = 0x14; w.Service(0xa1, g); if (g.Zf) return;
                            if ((sbyte)w.Found[0x19] > 0x0a) w.Found[0x19] = 0x0a;
                        }
                        return;
                    }
                    r[0x16]--; if (r[0x16] != 0) return;
                    r[0x16] = 2; Word(r, 3, (ushort)(Word(r, 3) + 1)); r[0x17]--; if (r[0x17] == 0) r[0] = 0xff; return;
                }
        }
    }

    /// <summary>129D, 1481, 14CE: FS:A1 finds the target; FS:119 (asked about kind AL) returns its template; the template flag 10h chooses the hard spark (735) over the soft one (6FE); the sounds are
    /// 0 / 1 (129D) or 16h / 15h (1481, 14CE) for both; nothing found removes the effect. 129D keeps AL = 0Ah over the call of FS:A1.</summary>
    private static void Impact(IArmy01EffectWorld w, byte[] r, BuildingRegs g, int ah, bool keepAl, uint ask, uint softSound, uint hardSound)
    {
        if (keepAl)
        {
            g.Eax = (g.Eax & 0xffffff00) | ask; uint saved = g.Eax; w.Service(0xa1, g); g.Eax = saved;
            if (g.Zf) { r[0] = 0xff; return; }
            g.Eax &= 0xffff00ff;
        }
        else
        {
            w.Service(0xa1, g);
            if (g.Zf) { r[0] = 0xff; return; }
            g.Eax = (g.Eax & 0xffff0000) | ask;
        }
        w.Service(0x119, g);
        if (g.Zf) { r[0] = 0xff; return; }
        bool hard = (U32(w.TemplateRaw, 0x40) & 0x10) != 0;
        g.Eax = hard ? hardSound : softSound; w.Sound35(g.Eax);
        w.Jump(hard ? 0x735u : 0x6feu, g.Eax, g.Ebx);
    }

    private static void Fountain(IArmy01EffectWorld w, byte[] r, BuildingRegs g, int ah, byte[] parent, uint parentRel)
    {
        if (ah != 1)
        {
            g.Eax = (g.Eax & 0xffffff00) | parent[3]; r[2] = parent[3]; r[0] = 0x1d; Word(r, 3, 0x226);
            g.Esi = parentRel; g.Ebx &= 0xffffff00; w.Service(0x25, g);
            g.Ebx = (g.Ebx & 0xffffff00) | 0xc; w.Service(0x29, g); w.Service(0x39, g); return;
        }
        w.Service(0xa5, g); if (!g.Zf) return;
        g.Eax = (g.Eax & 0xffffff00) | 5;
        for (; ; )
        {
            uint counter = g.Eax;
            g.Eax = (g.Eax & 0xffffff00) | w.Random10D();
            g.Eax = (uint)(sbyte)(byte)g.Eax;
            uint ey = (uint)((int)g.Eax >> 1) + U32(r, 0xc); g.Eax = ey;
            if (ey <= w.Limit0)
            {
                g.Ebx = ey; g.Eax = w.Random(); g.Eax = (uint)(sbyte)(byte)g.Eax;
                uint ex = (uint)((int)g.Eax >> 1) + U32(r, 7); g.Eax = ex;
                if (ex <= w.Limit4)
                {
                    g.Ecx = U32(r, 7); g.Edx = U32(r, 0xc);
                    bool zf = w.Effect19(ex, 0x191, g.Ecx, g.Edx, 0, ex, ey);
                    g.Ebx = 0x191; g.Eax = ey; g.Zf = zf;
                    if (!zf) w.Found[2] = r[2];
                }
            }
            g.Eax = counter;
            byte al = (byte)g.Eax; al--; g.Eax = (g.Eax & 0xffffff00) | al;
            if (al == 0) break;
        }
        uint ecx = U32(r, 7) >> 5, eax = U32(r, 0xc) >> 5;
        ecx = (ecx & 0xffff00ff) | ((eax & 0xff) << 8);
        g.Ecx = ecx; g.Edx = (g.Edx & 0xffff0000) | 0x101; g.Eax = (eax & 0xffff0000) | 0x30a; g.Ebx = (g.Ebx & 0xffffff00) | 3;
        w.Service(0x69, g);
        g.Eax &= 0xffff00ff; w.Jump(0xa44, g.Eax, g.Ebx);
    }
}
