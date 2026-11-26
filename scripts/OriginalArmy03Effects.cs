using System;
using static Northwind.OriginalArmy01Effects;

namespace Northwind;

/// <summary>The mage explosion of ARMY__03.WRK 324C (kind 10h, frames 32Bh..33Eh): AH = 0 creates it; AH = 1 steps every second update; on the frame 330h it throws ten fragments (effect 12h) around and
/// strikes the squares of a 6×6 field around the point (effect 93h): the first hit object that is not already an effect of kind 13h is turned into one with 1 in 4 and loses up to 1Fh health. Verified against
/// the original x86 by tools/verify_army01_effects.py.</summary>
public static class OriginalArmy03Effects
{
    /// <summary>The wrappers 2B63 and 2B74: the first sets +31 = 22h and +19 = 64h at AH = 0 and hands every other call to the jug script E8; the second, at AH = 1, takes the quest field +12h of the participant
    /// (FS:6D) as +22 unless the strength +19F1 of the AI record of the owner (FS:1B7) is 7CFh or less, then hands over to the pit trap 7F0 with AH = 1.</summary>
    public static void Wrapper(IArmy01EffectWorld w, byte[] r, int ah, int which, uint ebx)
    {
        var g = new BuildingRegs { Eax = (uint)ah << 8, Ebx = ah == 1 ? 0u : ebx };
        if (which == 0)
        {
            if (ah != 0) { w.Jump(0xe8, g.Eax, g.Ebx); return; }
            r[0x31] = 0x22; r[0x19] = 0x64; return;
        }
        if (ah != 1) { w.Jump(0x7f0, g.Eax, g.Ebx); return; }
        var (part, ptr) = w.Part6D(); g.Ecx = ptr;
        g.Eax = (g.Eax & 0xffffff00) | r[3]; var ai = w.Service1B7(r[3]);
        uint eax = U32(part, 0x12); if (U32(ai, 0x19f1) <= 0x7cf) eax = 0;
        Put(r, 0x22, eax); g.Eax = (eax & 0xffff00ff) | 0x100;
        w.Jump(0x7f0, g.Eax, g.Ebx);
    }

    public static void Run(IArmy01EffectWorld w, byte[] r, int ah)
    {
        var g = new BuildingRegs { Eax = (uint)ah << 8, Ebx = 0 };
        if (ah != 1) { r[0] = 0x10; Word(r, 3, 0x32b); return; }
        if ((w.Tick & 1) != 0) return;
        if (Word(r, 3) == 0x330)
        {
            g.Eax = (g.Eax & 0xffffff00) | 0x0a; uint ecx = 0, edx = 0;
            do
            {
                uint counter = g.Eax;
                ecx = U32(r, 7) + 0xc8; edx = U32(r, 0xc) - 0xc8;
                g.Eax = w.Random(); g.Eax += 0x80; ecx -= g.Eax;
                g.Eax = (g.Eax & 0xffffff00) | w.Random10D(); g.Eax += 0x80; edx += g.Eax;
                uint pa = g.Eax, pc = ecx, pd = edx;
                g.Eax = (g.Eax & 0xffffff00) | r[1]; g.Ebx = (g.Ebx & 0xffff0000) | 0x112;
                ecx = U32(r, 7) + 0x20; edx = U32(r, 0xc);
                w.Effect19(g.Eax, g.Ebx, ecx, edx, pa, pc, pd);
                ecx = pc; edx = pd; g.Eax = counter;
                byte al = (byte)g.Eax; al--; g.Eax = (g.Eax & 0xffffff00) | al;
                if (al == 0) break;
            } while (true);
            g.Eax = (g.Eax & 0xffff00ff) | (6u << 8);
            edx = U32(r, 0xc) - 0x32;
            do
            {
                g.Eax = (g.Eax & 0xffffff00) | 6; ecx = U32(r, 7) - 0x3c;
                do
                {
                    uint ea = g.Eax, ec = ecx, ed = edx;
                    bool skip = ((g.Eax >> 8) & 0xff) == 6 && (byte)g.Eax <= 4;
                    if (!skip)
                    {
                        g.Ebx = (g.Ebx & 0xffff0000) | 0x193;
                        bool zf = w.Effect19(g.Eax, g.Ebx, ecx, edx, ed, ec, ea);
                        var o = w.Found;
                        if (zf && o[0] != 0x13)
                        {
                            g.Eax = w.Random();
                            if ((g.Eax & 3) == 0)
                            {
                                g.Eax = (g.Eax & 0xffffff00) | w.Random10D(); g.Eax &= 0x1f;
                                o[6] = (byte)(o[6] - (byte)g.Eax); o[0] = 0x13; o[0x17] = 0xff; o[0x1e] = 0xff; Put(o, 0x18, 0);
                            }
                        }
                    }
                    g.Eax = ea; ecx = ec + 0x20; edx = ed;
                    byte al = (byte)g.Eax; al--; g.Eax = (g.Eax & 0xffffff00) | al;
                    if (al == 0) break;
                } while (true);
                edx += 0x20;
                byte ah2 = (byte)(g.Eax >> 8); ah2--; g.Eax = (g.Eax & 0xffff00ff) | ((uint)ah2 << 8);
                if (ah2 == 0) break;
            } while (true);
        }
        Word(r, 3, (ushort)(Word(r, 3) + 1));
        if (Word(r, 3) == 0x33f) r[0] = 0xff;
    }
}
