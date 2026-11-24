using System;

namespace Northwind;

/// <summary>The registers a building script keeps between its calls of the game services.</summary>
public sealed class BuildingRegs
{
    public uint Eax, Ebx, Ecx, Edx;
    public bool Zf;
}

/// <summary>The services of the building scripts of ARMY__01.WRK (recorded stubs in tools/verify_building_scripts.py).</summary>
public interface IBuildingScriptWorld
{
    /// <summary>A service by its FS slot: 85, 121, 197, 3D, 51, 49, 129, 99, 12D, CD (results in EAX, EBX and ZF) and 19, 1D (the registers stay).</summary>
    void Service(int slot, BuildingRegs g);
    /// <summary>FS:6D: the participant record of the owner (ECX, the pointer is an opaque token) and the template of the building (EDI).</summary>
    (byte[] Participant, byte[] Template, uint Pointer) Participant6D();
    /// <summary>The template of the building the dispatcher reads (the pointer [1C49]).</summary>
    byte[] DispatchTemplate();
    /// <summary>FS:71 on the object in EDI: the dword at +40h of its template.</summary>
    uint TemplateFlags71();
    /// <summary>FS:79 (EDI the template): ZF, the object found (EDI) and its template flags (EDX +40h).</summary>
    (bool Zf, byte[] Object, uint Flags) Service79();
    /// <summary>FS:1B7 (AL the owner): a record of the owner whose dword at +1A10h the script raises.</summary>
    byte[] Service1B7(byte al);
    /// <summary>FS:109 (random byte), FS:4D (appearance of the army), FS:D1 (voice), FS:DD (vitals), FS:115, FS:E1 (the unit step): recorded stubs.</summary>
    byte Random();
    void Appearance(byte army);
    void Voice();
    void Vitals();
    void Service115();
    /// <summary>FS:C5 (reads the dword at +48h of the record).</summary>
    void ServiceC5(byte[] r);
    void Actions();
    /// <summary>The address of the record (esi) as the script stores it.</summary>
    uint RecordAddress { get; }
    /// <summary>The dispatcher C88C before the handler: the state, EBX, the handler offset and ECX.</summary>
    void BeforeHandler(int state, uint ebx, uint handler, uint ecx);
    /// <summary>The dispatcher C88C after the handler: ZF of the handler in, ZF for the rest of the script out.</summary>
    bool AfterHandler(bool handlerZf);
}

/// <summary>The scripts of the buildings of ARMY__01.WRK: A (0FE), B (24D), C (354), D (3ED). AH = 0 initializes the order fields, AH = 1 steps (the redraw, the services, then the dispatcher C88C with the
/// inline table of six handlers by the state +14 and 7), AH = 2 is the voice, AH = 5 (A) tests a visitor, AH = 6 returns a table. Verified against the original x86 by tools/verify_building_scripts.py; the
/// services are recorded stubs, the dispatcher is run as far as its front half (state, EBX, the handler) and the inline table is the handler list below.</summary>
public static class OriginalBuildingScripts
{
    /// <summary>The offsets of the data tables returned by AH = 6 (A, B, none, D).</summary>
    public static readonly uint[] Tables = [0x21b, 0x336, 0, 0x4c6, 0, 0, 0, 0xa65, 0xdcd, 0x221, 0x336, 0, 0, 0, 0, 0, 0xf54, 0x130c, 0, 0, 0, 0, 0];
    private static readonly int[][] Handlers = [[0x1ef, 0x1b5, 0x1b2, 0x203, 0x23b, 0x247], [0x316, 0x2c6, 0x2c3, 0x2c3, 0x32a, 0x34e], [0x3c1, 0x3bb, 0x3b8, 0x3b8, 0x3b8, 0x3e7], [0x474, 0x466, 0x463, 0x4ae, 0x4de, 0x4ea], [0x771, 0x73b, 0x738, 0x738, 0x738, 0x79a], [], [0x909, 0x903, 0x900, 0x900, 0x900, 0x980], [0xa33, 0x9f9, 0x9f6, 0x9f6, 0xa59, 0xa71], [0xdad, 0xd73, 0xd70, 0xd70, 0xdc1, 0xde9], [0x1f5, 0x1bb, 0x1b8, 0x209, 0x251, 0x25d], [0x316, 0x2dc, 0x2d9, 0x2d9, 0x32a, 0x34e], [0x3c7, 0x3c1, 0x3be, 0x3be, 0x3be, 0x44e], [0x7c3, 0x7bd, 0x7ba, 0x7ba, 0x7ba, 0x7ef], [0x67d, 0x647, 0x644, 0x644, 0x644, 0x69b], [0xd56, 0xd20, 0xd1d, 0xd1d, 0xd1d, 0xd7f], [], [0xf02, 0xef4, 0xef1, 0xf3c, 0xf74, 0xf80], [0x12da, 0x12a0, 0x129d, 0x129d, 0x1300, 0x1318], [0x135c, 0x136e, 0x1359, 0x1359, 0x1359, 0x1374], [0x13b8, 0x13ca, 0x13b5, 0x13b5, 0x13b5, 0x13d0], [], [], []];

    private static uint Dword(byte[] r, int i) => BitConverter.ToUInt32(r, i);
    private static void SetBl(BuildingRegs g, uint v) => g.Ebx = (g.Ebx & 0xffffff00) | (v & 0xff);

    /// <summary>Runs the script k (0..3) for the record r; AH = 5 takes the other object, AH = 5 returns AL, AH = 6 EDX.</summary>
    public static uint Run(IBuildingScriptWorld w, byte[] r, int ah, byte bl, int k, byte[]? other, out bool zf)
    {
        zf = false;
        var g = new BuildingRegs { Eax = (uint)ah << 8, Ebx = bl };
        int kd = k; k = Base(k);
        if (k == 20) return Unit20(w, r, g, ah, bl, kd == 21, out zf);
        if (k == 22) return Script1078(w, r, g, ah, out zf);
        if (k == 5) return Script7A0(w, r, g, ah, kd == 15);
        if (k == 13 && ah != 5) return Script524(w, r, g, ah, out zf);
        if (ah == 0)
        {
            if (k is 6 or 11) { r[0x23] = 0x64; return 0; }
            if (k is not (2 or 4 or 12 or 13 or 18 or 19)) { r[0x27] = 0xff; for (int i = 0; i < 4; i++) r[0x22 + i] = 0xff; r[0x26] = 0xff; }
            return 0;
        }
        if (ah == 2) { if (k is 0 or 1 or 3 or 7 or 8 or 9 or 10) w.Service(0xcd, g); return 0; }
        if (ah == 6) return Tables[kd];
        if (ah == 5)
        {
            if (k is 9) k = 0;
            if (k is 11 or 13) k = 6;
            if (k is 4) { uint e = 0x1000; if (r[0x19] <= 0x23) e = ~w.TemplateFlags71(); zf = (e & 0x1000) == 0; return e & 0xff; }
            if (k is 6) { uint f = w.TemplateFlags71(); if ((f & 0x1000) != 0) { zf = true; return 0; } f &= 0xf00; zf = f == 0x300; return f & 0xff; }
            if (k != 0 || other == null) return 0;
            if (other[3] != r[3]) { zf = false; return 0xff; }
            if ((w.TemplateFlags71() & 0x20000) != 0) { zf = true; return 0; }
            zf = false; return 0xff;
        }
        if (ah != 1) return 0;
        if ((r[0x14] & 0x80) != 0)
        {
            r[0x14] &= 0x7f; w.Service(0x1d, g);
            if (k is 4 or 6 or 7 or 11 or 18 or 19) { }
            else if (k is 2 or 12) { var (p, t, ptr) = w.Participant6D(); g.Ecx = ptr; BitConverter.GetBytes(Dword(p, 0x2c) + Dword(t, 0x78)).CopyTo(p, 0x2c); }
            else { w.Service(0x85, g); if (k == 9) w.Service(0x19f, g); }
        }
        if (k is 0 or 9) w.Service(0x121, g);
        if (k is 4 or 6 or 11) w.Service(0xbd, g);
        Dispatch(w, r, g, kd);
        if (!g.Zf) return 0;
        Continue(w, r, g, k);
        return 0;
    }

    private static void Dispatch(IBuildingScriptWorld w, byte[] r, BuildingRegs g, int k)
    {
        int state = r[0x14] & 7; uint bh = 0;
        if (state == 0)
        {
            r[0x0f] |= 2; bh = r[0x19];
            if (bh == w.DispatchTemplate()[0x45]) r[0x0f] &= 0xfd;
        }
        g.Eax = (uint)state; g.Ebx = (g.Ebx & 0xffff0000) | (bh << 8) | (uint)(r[0x14] & 0x40);
        int handler = Handlers[k][state]; g.Edx = (uint)handler;
        w.BeforeHandler(state, g.Ebx, g.Edx, g.Ecx);
        Handler(w, r, g, Base(k), state, k >= 14);
        g.Zf = w.AfterHandler(g.Zf);
    }

    private static void Done(BuildingRegs g) { g.Eax &= 0xffffff00; g.Zf = true; }

    private static void Idle(byte[] r, BuildingRegs g)
    {
        r[0x0f] |= 0x10; if (r[0x22] == 0xff) r[0x0f] &= 0xef;
        SetBl(g, ~g.Ebx); g.Zf = (g.Ebx & 0x40) == 0;
    }

    private static void Effect(IBuildingScriptWorld w, byte[] r, BuildingRegs g, uint dx, uint dy, uint bl, bool second = false)
    {
        g.Eax = (g.Eax & 0xffffff00) | r[0]; g.Ebx = (g.Ebx & 0xffff0000) | 0x100 | bl;
        g.Ecx = Dword(r, 4) - dx; g.Edx = Dword(r, 8) - dy;
        w.Service(0x19, g);
    }

    private static int Base(int k) => k == 14 ? 4 : k == 15 ? 5 : k == 16 ? 3 : k == 17 ? 7 : k == 21 ? 20 : k;

    private static void Handler(IBuildingScriptWorld w, byte[] r, BuildingRegs g, int k, int state, bool second)
    {
        if (k >= 4) { Handler2(w, r, g, k, state, second); return; }
        switch (k, state)
        {
            case (0, 0) or (1, 0): Idle(r, g); return;
            case (3, 0):
                { uint saved = g.Ebx; g.Eax = second ? 0xcu : 6; g.Ebx = 0x11f; w.Service(0x129, g); g.Ebx = (g.Ebx & 0xffff0000) | 0x0f8b; g.Ecx = 0x20; g.Edx = second ? 0xffffffeeu : 0xffffffce; w.Service(0x99, g); g.Ebx = saved; Idle(r, g); return; }
            case (2, 0):
                { uint saved = g.Ebx; g.Eax = 5; g.Ebx = 0x31f; w.Service(0x129, g); g.Ebx = saved; SetBl(g, ~g.Ebx); g.Zf = (g.Ebx & 0x40) == 0; return; }
            case (0, 1):
                w.Service(0x3d, g); if (!g.Zf) return;
                r[0x0f] = 0x24;
                if ((g.Ebx & 0xff) == 0 && (BitConverter.ToUInt16(r, 0x17) & 0x100) == 0) { Effect(w, r, g, 5, 0x4a, 0x1d); BitConverter.GetBytes((ushort)(BitConverter.ToUInt16(r, 0x17) | 0x100)).CopyTo(r, 0x17); }
                Done(g); return;
            case (1, 1):
                w.Service(0x3d, g); if (!g.Zf) return;
                r[0x0f] = 0x24;
                if ((g.Ebx & 0xff) == 0 && (BitConverter.ToUInt16(r, 0x17) & 0x100) == 0)
                {
                    Effect(w, r, g, 3, 0x14, 0x1d);
                    g.Ecx += 0x23; g.Edx -= 0xb; w.Service(0x19, g);
                    BitConverter.GetBytes((ushort)(BitConverter.ToUInt16(r, 0x17) | 0x100)).CopyTo(r, 0x17);
                }
                Done(g); return;
            case (2, 1): w.Service(0x3d, g); return;
            case (3, 1):
                w.Service(0x3d, g); if (!g.Zf) return;
                r[0x0f] = 0x24; Done(g); return;
            case (0, 2) or (1, 2) or (1, 3) or (2, 2) or (2, 3) or (2, 4) or (3, 2): Done(g); return;
            case (0, 3) or (3, 3):
                g.Edx = 0xd; w.Service(0x51, g); for (int i = 0; i < 4; i++) r[0x22 + i] = 0xff; r[0x26] = 0xff; Done(g); return;
            case (0, 4): g.Edx = 0xffffffd5; w.Service(0x49, g); return;
            case (1, 4): g.Edx = 1; w.Service(0x49, g); return;
            case (3, 4): g.Edx = second ? 0xffffffd5u : 0xffffffddu; w.Service(0x49, g); return;
            default: w.Service(0x12d, g); return;
        }
    }

    private static void Continue(IBuildingScriptWorld w, byte[] r, BuildingRegs g, int k)
    {
        if (k == 0)
        {
            g.Eax = Dword(r, 8) >> 5; g.Ecx = Dword(r, 4) >> 5;
            g.Ecx = (g.Ecx & 0xffff00ff) | ((g.Eax & 0xff) << 8); g.Eax = (g.Eax & 0xffff00ff) | ((uint)r[3] << 8);
            w.Service(0x197, g); r[0x22] = 0xff; r[0x27] = 0xff; return;
        }
        if (k is 2 or 12)
        {
            var (p, t, _) = w.Participant6D();
            if ((sbyte)r[0x1a] > (sbyte)t[0x46])
            {
                uint have = Dword(p, 0x2c), cost = Dword(t, 0x78);
                BitConverter.GetBytes(have >= cost ? have - cost : 0u).CopyTo(p, 0x2c);
            }
            return;
        }
        if (k is 4 or 6 or 11 or 13 or 18 or 19) return;
        r[0x22] = 0xff; r[0x27] = 0xff;
        if (k is 8 or 9)
        {
            g.Eax = Dword(r, 8) >> 5; g.Ecx = Dword(r, 4) >> 5;
            g.Ecx = (g.Ecx & 0xffff00ff) | ((g.Eax & 0xff) << 8); g.Eax = (g.Eax & 0xffff00ff) | ((uint)r[3] << 8);
            w.Service(0x197, g);
        }
    }

    private static ushort Word(byte[] a, int i) => BitConverter.ToUInt16(a, i);
    private static void SetWord(byte[] a, int i, int v) => BitConverter.GetBytes((ushort)v).CopyTo(a, i);

    private static void Handler2(IBuildingScriptWorld w, byte[] r, BuildingRegs g, int k, int state, bool second)
    {
        switch (k, state)
        {
            case (4, 0):
                if ((g.Ebx & 0x40) != 0) { Done(g); return; }
                w.Service(0x9d, g); g.Eax |= 0xff; g.Zf = false; return;
            case (4, 1):
                w.Service(0x3d, g); if (!g.Zf) return;
                if ((g.Ebx & 0xff) == 0 && (Word(r, 0x17) & 0x100) == 0) { Effect(w, r, g, 6, 0x2f, second ? 0x1cu : 0x1d); SetWord(r, 0x17, Word(r, 0x17) | 0x100); }
                Done(g); return;
            case (6, 0): Barracks(w, r, g, false); return;
            case (11, 0): Barracks(w, r, g, true); return;
            case (6, 1) or (11, 1): w.Service(0x3d, g); return;
            case (7, 0):
                { uint saved = g.Ebx; g.Eax = second ? 0xdu : 7; g.Ebx = second ? 0x71fu : 0x11f; w.Service(0x129, g); g.Ebx = saved; Idle(r, g); return; }
            case (7, 1):
                w.Service(0x3d, g); if (!g.Zf) return;
                r[0x0f] = 0x24;
                if ((g.Ebx & 0xff) == 0 && (Word(r, 0x17) & 0x100) == 0) { Effect(w, r, g, 0xfffffffe, 10, second ? 0x1cu : 0x1d); SetWord(r, 0x17, Word(r, 0x17) | 0x100); }
                Done(g); return;
            case (7, 4): g.Edx = 1; w.Service(0x49, g); return;
            case (8, 0) or (9, 0) or (10, 0): Idle(r, g); return;
            case (8, 1):
                w.Service(0x3d, g); if (!g.Zf) return;
                r[0x0f] = 0x24;
                if ((g.Ebx & 0xff) == 0 && (Word(r, 0x17) & 0x100) == 0) { Effect(w, r, g, 2, 0x43, 0x1d); SetWord(r, 0x17, Word(r, 0x17) | 0x100); }
                Done(g); return;
            case (9, 1) or (10, 1):
                w.Service(0x3d, g); if (!g.Zf) return;
                r[0x0f] = 0x24;
                if ((g.Ebx & 0xff) == 0 && (Word(r, 0x17) & 0x100) == 0) { if (k == 9) Effect(w, r, g, 0x28, 0x33, 0x1c); else Effect(w, r, g, 0xc, 0xd, 0x1c); SetWord(r, 0x17, Word(r, 0x17) | 0x100); }
                Done(g); return;
            case (9, 3):
                g.Edx = 0xd; w.Service(0x51, g); for (int i = 0; i < 4; i++) r[0x22 + i] = 0xff; r[0x26] = 0xff; Done(g); return;
            case (9, 4): g.Edx = 0xffffffc5; w.Service(0x49, g); return;
            case (10, 4): g.Edx = 1; w.Service(0x49, g); return;
            case (8, 4): g.Edx = 1; w.Service(0x49, g); return;
            case (18, 0) or (19, 0): g.Eax = k == 18 ? 4u : 0xe; g.Ebx = k == 18 ? 0xfu : 0xff; w.Service(0x129, g); Done(g); return;
            case (18, 1) or (19, 1): w.Service(0x3d, g); return;
            case (12, 0):
                { uint saved = g.Ebx; g.Eax = 0xb; g.Ebx = 0x31f; w.Service(0x129, g); g.Ebx = (g.Ebx & 0xffff0000) | 0x0f8b; g.Ecx = 3; g.Edx = 0xffffffe4; w.Service(0x99, g); g.Ebx = saved; SetBl(g, ~g.Ebx); g.Zf = (g.Ebx & 0x40) == 0; return; }
            case (12, 1): w.Service(0x3d, g); return;
            case (13, 0):
                if ((g.Ebx & 0x40) != 0) { Done(g); return; }
                w.Service(0x65, g); g.Eax = 9; g.Ebx = 0xf; w.Service(0x129, g); g.Eax |= 0xff; g.Zf = false; return;
            case (13, 1):
                w.Service(0x3d, g); if (!g.Zf) return;
                if ((g.Ebx & 0xff) == 0 && (Word(r, 0x17) & 0x100) == 0) { Effect(w, r, g, 0x28, 0x2c, 0x1c); SetWord(r, 0x17, Word(r, 0x17) | 0x100); }
                Done(g); return;
            case (4, 5) or (6, 5) or (7, 5) or (8, 5) or (9, 5) or (10, 5) or (11, 5) or (12, 5) or (13, 5) or (18, 5) or (19, 5): w.Service(0x12d, g); return;
            default: Done(g); return;
        }
    }

    /// <summary>The state 0 handler of the script 896 (909): the waiting building spends time on the cargo, and looks for an object in reach (FS:79).</summary>
    private static void Barracks(IBuildingScriptWorld w, byte[] r, BuildingRegs g, bool second)
    {
        if ((g.Ebx & 0x40) != 0) { Done(g); return; }
        var t = w.DispatchTemplate();
        if ((sbyte)r[0x19] > (sbyte)t[0x46])
        {
            g.Eax = second ? 0xau : 0; g.Ebx = 0xf; w.Service(0x129, g);
            if (r[0x24] != 0 && --r[0x23] == 0)
            {
                r[0x23] = t[0x79]; r[0x24]--;
                var (p, _, ptr) = w.Participant6D(); g.Ecx = ptr; BitConverter.GetBytes(Dword(p, 0x1c) + 0xa).CopyTo(p, 0x1c);
                if (second) { g.Eax = (g.Eax & 0xffffff00) | r[3]; var e = w.Service1B7(r[3]); BitConverter.GetBytes(Dword(e, 0x1a10) + 0xa).CopyTo(e, 0x1a10); }
            }
            var (zf, obj, flags) = w.Service79();
            if (!zf)
            {
                if ((Word(r, 0x17) & 0x40) != 0 && (flags & 0x20000) != 0 && obj[0x31] == 3)
                { SetWord(obj, 0x17, Word(obj, 0x17) & 0xffbf); obj[0x31] = 0; r[0x24]++; }
                else SetWord(obj, 0x17, Word(obj, 0x17) | 0x20);
                SetWord(r, 0x17, Word(r, 0x17) & 0xffbf);
            }
        }
        g.Eax |= 0xff; g.Zf = false;
    }

    /// <summary>The script 7A0: a countdown (+19), the redraw and two effects when +14 has bit 80h, otherwise the state machine on +14 (services 8D, 91, 95, C1).</summary>
    private static uint Script7A0(IBuildingScriptWorld w, byte[] r, BuildingRegs g, int ah, bool second)
    {
        if (ah == 0) { r[0x0f] = 4; r[0x0c] = 0xa; r[0x14] = second ? (byte)0x84 : (byte)4; return 0; }
        if (ah != 1) return 0;
        if ((sbyte)r[0x19] <= 0)
        {
            r[0x19]--; if (r[0x19] > 0xfa) return 0;
            w.Service(0x15, g); return 0;
        }
        if ((r[0x14] & 0x80) != 0)
        {
            r[0x14] = 4; r[0x19] = 0x32; w.Service(0x1d, g);
            g.Ebx = (g.Ebx & 0xffff0000) | (second ? 0x107u : 0x10au); g.Eax = (g.Eax & 0xffffff00) | r[0]; g.Ecx = Dword(r, 4) - 0x23; g.Edx = Dword(r, 8) + 8;
            w.Service(0x19, g); g.Ecx += 0x42; w.Service(0x19, g);
            return 0;
        }
        uint al;
        bool set = false; al = 0;
        if ((sbyte)r[0x19] <= 0x19 && r[0x0f] != 0) { r[0x0f] = 0; al = 3; set = true; }
        for (; ; )
        {
            if (set)
            {
                r[0x14] = (byte)al; r[0x15] = 0; r[0x23] = 0; g.Eax = (g.Eax & 0xffff0000) | al;
            }
            set = false;
            w.Service(0x8d, g);
            al = r[0x14]; g.Eax = (g.Eax & 0xffffff00) | al;
            if ((al & 0x40) != 0) { al = (al >> 3) & 7; set = true; continue; }
            al &= 7; g.Eax = (g.Eax & 0xffffff00) | al;
            switch (al)
            {
                case 2:
                    r[0x0f] = 0x18; w.Service(0x91, g); al = 0; g.Eax &= 0xffffff00;
                    if ((g.Ebx & 0x40) != 0) { set = true; continue; }
                    return 0;
                case 3:
                    if (r[0x0f] != 0) r[0x0f] = 0x14;
                    w.Service(0x95, g); al = 0; g.Eax &= 0xffffff00;
                    if ((g.Ebx & 0x40) != 0) { set = true; continue; }
                    return 0;
                case 4: w.Service(0xc1, g); return 0;
                default: return 0;
            }
        }
    }

    /// <summary>The script 524 of ARMY__02.WRK: AH = 2 sets up the object (FS:D9, FS:5D), AH = 4 counts with FS:59, AH = 1 steps (the counts are sampled on the redraw, then FS:BD and the dispatcher).</summary>
    private static uint Script524(IBuildingScriptWorld w, byte[] r, BuildingRegs g, int ah, out bool zf)
    {
        zf = false;
        if (ah == 2)
        {
            w.Service(0xd9, g); Put(r, 4, 0x244); Put(r, 8, 0x100); r[0x14] = 4; Put(r, 0x15, w.RecordAddress + 0x24);
            g.Eax = w.RecordAddress + 0x24; g.Edx = r[0x23] == 1 ? 1u : 0xf; w.Service(0x5d, g); return 0;
        }
        if (ah == 4)
        {
            g.Eax = 0x101; w.Service(0x59, g); if ((g.Eax) != 0) { zf = false; return 0; }
            g.Eax = 0x102; w.Service(0x59, g); zf = g.Eax == 0; return 0;
        }
        if (ah != 1) return 0;
        if ((r[0x14] & 0x80) != 0)
        {
            r[0x14] &= 0x7f; w.Service(0x1d, g);
            g.Ecx = Dword(r, 4); g.Edx = Dword(r, 8); g.Eax = 0x101; r[0x23] = 1; w.Service(0x59, g);
            g.Eax <<= 11; Put(r, 0x24, g.Eax);
            g.Ecx = Dword(r, 4); g.Edx = Dword(r, 8); g.Eax = (g.Eax & 0xffff0000) | 0x102; w.Service(0x59, g);
            g.Eax <<= 11; uint v = g.Eax;
            if (Dword(r, 0x24) < v) { Put(r, 0x24, v); r[0x23] = 2; }
        }
        w.Service(0xbd, g);
        Dispatch(w, r, g, 13);
        return 0;
    }

    private static void Put(byte[] a, int i, uint v) => BitConverter.GetBytes(v).CopyTo(a, i);

    /// <summary>The unit-like script F86 of ARMY__02.WRK: AH = 0 +0F = 7, appearance, +2E random, +32 = its template; AH = 2 the voice; AH = 5 tests a visitor (+31 = FFh passes the template flag 1000h of the other);
    /// AH = 1 on death (+19 = 0 and +17 & 18h = 10h) throws the effects 16h, 17h, 18h, then redraws, the vitals, FS:115 and (+31 ≠ FFh) the unit step with +0F = 7.</summary>
    private static uint Unit20(IBuildingScriptWorld w, byte[] r, BuildingRegs g, int ah, byte bl, bool noMiddle, out bool zf)
    {
        zf = false;
        if (ah == 2) { w.Voice(); return 0; }
        if (ah == 5)
        {
            if (r[0x31] != 0xff) return g.Eax & 0xff;
            uint e = ~w.TemplateFlags71(); zf = (e & 0x1000) == 0; return e & 0xff;
        }
        if (ah == 0) { r[0x0f] = 7; w.Appearance(bl); r[0x2e] = w.Random(); r[0x32] = r[0]; return 0; }
        if (ah != 1) return 0;
        if (r[0x19] == 0) g.Eax = (g.Eax & 0xffff0000) | (uint)(BitConverter.ToUInt16(r, 0x17) & 0xff00) | (uint)(r[0x17] & 0x18);
        if (r[0x19] == 0 && (r[0x17] & 0x18) == 0x10)
        {
            foreach (uint kind in new uint[] { 0x16, 0x17, 0x18 })
            {
                g.Eax = (g.Eax & 0xffffff00) | r[0]; g.Ebx = (g.Ebx & 0xffff0000) | 0x100 | kind; g.Ecx = Dword(r, 4); g.Edx = Dword(r, 8);
                if (noMiddle && kind == 0x17) continue;   // 1462: the call of the second effect is missing in the original
                w.Service(0x19, g);
            }
        }
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Service(0x1d, g); }
        w.Vitals(); w.Service115();
        if (r[0x31] == 0xff) return 0;
        r[0x0f] = 7; w.Actions(); return 0;
    }

    /// <summary>The script 1078 of ARMY__02.WRK: AH = 0 +20 = FFh; AH = 4 tail-calls FS:D5 with EBX = 404h; AH = 1 on the redraw takes +23 (20h for the state 3, 10h for 4, else 0) from the redraw, otherwise
    /// lets FS:C5 see +48 raised by +23 and, with +15 = 4 and +19 ≤ 0, counts +19 down and removes the object.</summary>
    private static uint Script1078(IBuildingScriptWorld w, byte[] r, BuildingRegs g, int ah, out bool zf)
    {
        zf = false;
        if (ah == 0) { r[0x20] = 0xff; return 0; }
        if (ah == 4) { g.Ebx = 0x404; w.Service(0xd5, g); zf = g.Zf; return 0; }
        if (ah != 1) return 0;
        if ((r[0x14] & 0x80) != 0)
        {
            r[0x14] &= 0x7f; w.Service(0x1d, g);
            uint al = g.Eax & 7; r[0x23] = 0x20; if (al == 3) return 0;
            r[0x23] = 0x10; if (al == 4) return 0;
            r[0x23] = 0; return 0;
        }
        uint saved = Dword(r, 0x48); g.Eax = r[0x23];
        BitConverter.GetBytes((ushort)(BitConverter.ToUInt16(r, 0x48) + r[0x23])).CopyTo(r, 0x48);
        w.ServiceC5(r);
        BitConverter.GetBytes(saved).CopyTo(r, 0x48);
        if (r[0x15] != 4 || (sbyte)r[0x19] > 0) return 0;
        r[0x19]--; if (r[0x19] > 0xfa) return 0;
        w.Service(0x15, g); return 0;
    }
}
