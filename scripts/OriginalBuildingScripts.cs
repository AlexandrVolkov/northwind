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
    public static readonly uint[] Tables = [0x21b, 0x336, 0, 0x4c6];
    private static readonly int[][] Handlers = [[0x1ef, 0x1b5, 0x1b2, 0x203, 0x23b, 0x247], [0x316, 0x2c6, 0x2c3, 0x2c3, 0x32a, 0x34e], [0x3c1, 0x3bb, 0x3b8, 0x3b8, 0x3b8, 0x3e7], [0x474, 0x466, 0x463, 0x4ae, 0x4de, 0x4ea]];

    private static uint Dword(byte[] r, int i) => BitConverter.ToUInt32(r, i);
    private static void SetBl(BuildingRegs g, uint v) => g.Ebx = (g.Ebx & 0xffffff00) | (v & 0xff);

    /// <summary>Runs the script k (0..3) for the record r; AH = 5 takes the other object, AH = 5 returns AL, AH = 6 EDX.</summary>
    public static uint Run(IBuildingScriptWorld w, byte[] r, int ah, byte bl, int k, byte[]? other = null)
    {
        var g = new BuildingRegs { Eax = (uint)ah << 8, Ebx = bl };
        if (ah == 0)
        {
            if (k != 2) { r[0x27] = 0xff; for (int i = 0; i < 4; i++) r[0x22 + i] = 0xff; r[0x26] = 0xff; }
            return 0;
        }
        if (ah == 2) { if (k != 2) w.Service(0xcd, g); return 0; }
        if (ah == 6) return k == 2 ? 0 : Tables[k];
        if (ah == 5)
        {
            if (k != 0 || other == null) return 0;
            if (other[3] != r[3]) return 0xff;
            return (w.TemplateFlags71() & 0x20000) != 0 ? 0u : 0xff;
        }
        if (ah != 1) return 0;
        if ((r[0x14] & 0x80) != 0)
        {
            r[0x14] &= 0x7f; w.Service(0x1d, g);
            if (k == 2) { var (p, t, ptr) = w.Participant6D(); g.Ecx = ptr; BitConverter.GetBytes(Dword(p, 0x2c) + Dword(t, 0x78)).CopyTo(p, 0x2c); }
            else w.Service(0x85, g);
        }
        if (k == 0) w.Service(0x121, g);
        Dispatch(w, r, g, k);
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
        Handler(w, r, g, k, state);
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

    private static void Handler(IBuildingScriptWorld w, byte[] r, BuildingRegs g, int k, int state)
    {
        switch (k, state)
        {
            case (0, 0) or (1, 0): Idle(r, g); return;
            case (3, 0):
                { uint saved = g.Ebx; g.Eax = 6; g.Ebx = 0x11f; w.Service(0x129, g); g.Ebx = (g.Ebx & 0xffff0000) | 0x0f8b; g.Ecx = 0x20; g.Edx = 0xffffffce; w.Service(0x99, g); g.Ebx = saved; Idle(r, g); return; }
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
            case (3, 4): g.Edx = 0xffffffdd; w.Service(0x49, g); return;
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
        if (k == 2)
        {
            var (p, t, _) = w.Participant6D();
            if ((sbyte)r[0x1a] > (sbyte)t[0x46])
            {
                uint have = Dword(p, 0x2c), cost = Dword(t, 0x78);
                BitConverter.GetBytes(have >= cost ? have - cost : 0u).CopyTo(p, 0x2c);
            }
            return;
        }
        r[0x22] = 0xff; r[0x27] = 0xff;
    }
}
