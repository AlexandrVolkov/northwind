using System;

namespace Northwind;

/// <summary>The registers and the flags of the unit action handlers (LE:1 8227..9210). The flag helpers follow the x86 semantics, the jumps of the handlers test them as the original does.</summary>
public sealed class ActionRegs
{
    public uint Eax, Ebx, Ecx, Edx, Edi, Esi = 0x400800, Ebp;
    public bool Zf, Cf, Sf, Of;
    public byte Al { get => (byte)Eax; set => Eax = (Eax & 0xffffff00) | value; }
    public byte Ah { get => (byte)(Eax >> 8); set => Eax = (Eax & 0xffff00ff) | ((uint)value << 8); }
    public byte Bl { get => (byte)Ebx; set => Ebx = (Ebx & 0xffffff00) | value; }
    public byte Bh { get => (byte)(Ebx >> 8); set => Ebx = (Ebx & 0xffff00ff) | ((uint)value << 8); }
    public byte Cl { get => (byte)Ecx; set => Ecx = (Ecx & 0xffffff00) | value; }
    public byte Ch { get => (byte)(Ecx >> 8); set => Ecx = (Ecx & 0xffff00ff) | ((uint)value << 8); }
    public byte Dl { get => (byte)Edx; set => Edx = (Edx & 0xffffff00) | value; }
    public byte Dh { get => (byte)(Edx >> 8); set => Edx = (Edx & 0xffff00ff) | ((uint)value << 8); }

    private static uint Mask(int bits) => bits == 32 ? 0xffffffff : (1u << bits) - 1;
    private static uint Sign(int bits) => 1u << (bits - 1);
    public void Cmp(uint a, uint b, int bits)
    {
        uint m = Mask(bits), x = a & m, y = b & m, r = (x - y) & m;
        Zf = r == 0; Cf = x < y; Sf = (r & Sign(bits)) != 0; Of = (((x ^ y) & (x ^ r)) & Sign(bits)) != 0;
    }
    public void Test(uint a, uint b, int bits) => Logic((a & b) & Mask(bits), bits);
    /// <summary>The flags after AND, OR, XOR, TEST: CF = OF = 0.</summary>
    public void Logic(uint result, int bits) { uint r = result & Mask(bits); Zf = r == 0; Sf = (r & Sign(bits)) != 0; Cf = false; Of = false; }
    /// <summary>The flags after SHR by one or more bits (CF = the last bit shifted out, the count is not zero).</summary>
    public uint Shr(uint value, int count, int bits)
    {
        uint r = (value & Mask(bits)) >> count; Cf = (((value & Mask(bits)) >> (count - 1)) & 1) != 0; Zf = r == 0; Sf = (r & Sign(bits)) != 0; Of = false; return r;
    }
    public void Add(uint a, uint b)
    {
        uint r = a + b; Zf = r == 0; Sf = (r & 0x80000000) != 0; Cf = ((ulong)a + b) > 0xffffffff; Of = (((a ^ r) & (b ^ r)) & 0x80000000) != 0;
    }
    public bool Jle => Zf || Sf != Of;
    public bool Jg => !Jle;
    public bool Jae => !Cf;
    public bool Jb => Cf;
}

/// <summary>The services and the memory of the unit action handlers.</summary>
public interface IUnitActionWorld
{
    /// <summary>A callee outside the checked function: the world records the call and sets the registers and the flags (68BD only sets EDX, the template).</summary>
    void Call(uint address, ActionRegs g);
    /// <summary>The object behind an absolute pointer (the pool base is <see cref="PoolBase"/>).</summary>
    byte[] ObjectAt(uint pointer);
    byte[] Template { get; }
    /// <summary>The statistics records of the participants ([6BA] + 1A35h * owner).</summary>
    byte[] Stats { get; }
    uint PoolBase { get; }
    uint G512 { get; set; }
    uint G51A { get; }
    uint G51E { get; }
    byte G1C9F { get; set; }
    byte G522 { get; }
    uint G50E { get; set; }
    /// <summary>The participant record of the owner (the bytes at +0CDh are read by 89F1).</summary>
    byte[] PartOf(byte owner);
    /// <summary>The data segment (the windows 19C0, 1C30, 4160, 9080 hold the globals of A10B and 9A8E) and fs:[13Dh].</summary>
    byte[] Ds { get; }
    /// <summary>The occupancy planes: the pointer of plane k ([18CE + 4k]) and a dword of one (a reference relative to the pool, 0 = empty).</summary>
    uint PlanePointer(uint k);
    uint PlaneRead(uint address);
    void PlaneWrite(uint address, uint value);
    /// <summary>The template table pointer [army entry + 4] of an army (0 for armies without a table).</summary>
    uint ArmyTable(byte army);
    /// <summary>A byte of the table that follows the call of BB8B (pairs value, key; FFh ends it).</summary>
    byte TableByte(uint address);
    byte Fs13D { get; set; }
    byte G525 { get; } byte G526 { get; }
    byte G1C61 { get; set; }
    uint G2730 { get; }
    /// <summary>The route bytes ([6B6] points to them) and the first dword of the descriptor of a map tile.</summary>
    byte[] Route { get; }
    uint TileDescriptor(uint tile);
    /// <summary>The byte [1CCB + 16h · owner] written by 8A54.</summary>
    void SetG1CCB(int index, byte value);
}

/// <summary>The unit action helpers 8E3C..902F of LE:1 (the target of the unit and the approach). Each method is one function of the original; the callees outside it go through
/// <see cref="IUnitActionWorld.Call"/>. Verified function by function against the original x86 by tools/verify_unit_actions.py (tests/fixtures/unit-actions.json).</summary>
public static class OriginalUnitActions
{
    private static uint U32(byte[] b, int o) => BitConverter.ToUInt32(b, o);
    private static ushort U16(byte[] b, int o) => BitConverter.ToUInt16(b, o);
    private static void Put(byte[] b, int o, uint v) => BitConverter.GetBytes(v).CopyTo(b, o);
    private static void PutW(byte[] b, int o, ushort v) => BitConverter.GetBytes(v).CopyTo(b, o);

    private static void Xor0(ActionRegs g) { g.Al = 0; g.Logic(0, 8); }                       // xor al, al
    private static void OrFF(ActionRegs g) { g.Al = 0xff; g.Logic(0xff, 8); }                // or al, 0FFh

    /// <summary>8E3C: the target of the unit becomes the object the player has selected ([51A]).</summary>
    public static void F8E3C(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        r[0x15] &= 0x0f; g.Al = (byte)(r[0x16] >> 5);
        g.Cmp(w.G51A, w.PoolBase, 32); g.Eax = w.G51A - w.PoolBase;
        Put(r, 0x1c, g.Eax);
    }

    /// <summary>8E56: marks the unit as engaged (+15 bit 10h) when its target is alive (health above DAT[46]).</summary>
    public static void F8E56(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        r[0x15] &= 0x0f; g.Edi = U32(r, 0x1c); g.Test(g.Edi, g.Edi, 32);
        if (g.Zf) return;
        g.Edi += w.PoolBase; w.Call(0x68bd, g);
        var o = w.ObjectAt(g.Edi); g.Al = o[0x19]; g.Cmp(g.Al, w.Template[0x46], 8);
        if (g.Jle) return;
        g.Al = (byte)(r[0x16] >> 5);
        r[0x15] |= 0x10; g.Logic(r[0x15], 8);
    }

    /// <summary>8E81: the idle search for an enemy of the standing guard.</summary>
    public static void F8E81(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Call(0x8d89, g); g.Cmp(g.Ah, 1, 8);
        if (g.Zf) { Xor0(g); return; }
        g.Cmp(g.Dl, 0, 8);
        if (!g.Zf) { OrFF(g); return; }
        w.Call(0x8abf, g); w.Call(0x8af4, g);
        if (!g.Zf) { OrFF(g); return; }
        g.Al = 1; w.Call(0xd895, g);
        if (g.Zf) { Xor0(g); return; }
        g.Edi -= w.PoolBase; Put(r, 0x1c, g.Edi);
        w.Call(0x8bea, g);
        if (!g.Zf) { OrFF(g); return; }
        w.Call(0x8b5e, g);
        if (!g.Zf) { Xor0(g); return; }
        w.Call(0x8c7c, g); OrFF(g);
    }

    /// <summary>8EC7: the reach test of the attack handler (state AH of 8D89: 0 = the unit has a target, 1 = it is on the way).</summary>
    public static void F8EC7(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Call(0x8d89, g); g.Cmp(g.Ah, 1, 8);
        if (g.Zf)
        {
            w.Call(0x8abf, g);
            if (g.Zf) { Xor0(g); return; }
            w.Call(0x8b5e, g);
            if (g.Zf) { Strike(w, g); return; }
            g.Cmp(g.Bl, 1, 8);
            if (!g.Zf) { Xor0(g); return; }
            g.Cmp(w.G522, 0, 8);
            if (!g.Zf) { OrFF(g); return; }
            w.Call(0x8cbb, g);
            if (g.Zf) { Xor0(g); return; }
            OrFF(g); return;
        }
        g.Cmp(U32(r, 0x1c), 0, 32);
        if (g.Zf) { Xor0(g); return; }
        w.Call(0x8b5e, g);
        if (g.Zf) { Strike(w, g); return; }
        g.Cmp(g.Bl, 1, 8);
        if (!g.Zf) { Xor0(g); return; }
        g.Cmp(w.G522, 0, 8);
        if (!g.Zf) { OrFF(g); return; }
        w.Call(0x8cbb, g);
        if (g.Zf) { Xor0(g); return; }
        OrFF(g);
    }

    /// <summary>The tail of 8EC7 when the target is within reach: the swing request unless the unit is told (+522) to hold.</summary>
    private static void Strike(IUnitActionWorld w, ActionRegs g)
    {
        g.Cmp(w.G522, 3, 8);
        if (g.Zf) { OrFF(g); return; }
        w.Call(0x8b37, g);
        if (!g.Zf) { OrFF(g); return; }
        w.Call(0x8af4, g);
        if (!g.Zf) { OrFF(g); return; }
        w.Call(0x8bea, g);
        if (!g.Zf) { OrFF(g); return; }
        w.Call(0x8c7c, g); OrFF(g);
    }

    /// <summary>8F70: the approach and hand-off state machine of the unit (+30 is its timer; CL of 8D89 the phase).</summary>
    public static void F8F70(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Call(0x8a7f, g);
        if (!g.Zf) { Stop(g, r); return; }
        w.Call(0x8d89, g); g.Test(g.Cl, g.Cl, 8);
        if (g.Zf)
        {
            g.Cmp(g.Dl, 2, 8);
            if (g.Zf) { w.Call(0x8c98, g); OrFF(g); return; }
            r[0x30] = 0x91; Retry(w, g, r); return;
        }
        g.Cmp(g.Cl, 1, 8);
        if (g.Zf)
        {
            g.Cmp(g.Dl, g.Dh, 8);
            if (!g.Zf) { OrFF(g); return; }
            g.Cmp(g.Dl, 0, 8);
            if (!g.Zf) { OrFF(g); return; }
            w.Call(0x8db0, g);
            if (!g.Zf) { Stop(g, r); return; }
            g.Test(g.Al, g.Al, 8);
            if (!g.Zf)
            {
                w.Call(0x9b86, g); g.Eax = g.Shr(g.Eax, 5, 32);
                if (g.Zf) { r[0x30] = 2; OrFF(g); return; }
            }
            else
            {
                g.Cmp(g.Ecx, U32(r, 4), 32);
                if (g.Zf)
                {
                    g.Cmp(g.Edx, U32(r, 8), 32);
                    if (g.Zf) { r[0x30] = 2; OrFF(g); return; }
                }
            }
            Back(w, g, r); return;
        }
        g.Cmp(g.Cl, 2, 8);
        if (g.Zf)
        {
            w.Call(0x8bea, g);
            if (!g.Zf) { OrFF(g); return; }
            var o = w.ObjectAt(g.Edi); PutW(o, 0x17, (ushort)(U16(o, 0x17) | 0x40)); PutW(r, 0x17, (ushort)(U16(r, 0x17) | 0x40)); r[0x30] = 0xf3;
            OrFF(g); return;
        }
        g.Cmp(g.Cl, 3, 8);
        if (!g.Zf) return;
        g.Test(g.Ch, g.Ch, 8);
        if (g.Zf) { Stop(g, r); return; }
        g.Cmp(r[0x30], 0x10, 8); r[0x30] -= 0x10;
        g.Test(U16(r, 0x17), 0x40, 16);
        if (!g.Zf) { OrFF(g); return; }
        PutW(r, 0x17, (ushort)(U16(r, 0x17) & 0xffbf)); g.Bl = 0; g.Logic(0, 8);
    }

    private static void Stop(ActionRegs g, byte[] r) { PutW(r, 0x17, (ushort)(U16(r, 0x17) & 0xffbf)); g.Bl = 2; Xor0(g); }

    /// <summary>8FEB: the timer +30 counts down by 10h; the phase asks the new target position (8DB0) and starts the move (8CA1) while the timer lasts.</summary>
    private static void Back(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        for (; ; )
        {
            g.Cmp(r[0x30], 0x10, 8); r[0x30] -= 0x10;
            if (g.Cf) { g.Bl = 1; Xor0(g); return; }
            Retry(w, g, r);
            return;
        }
    }

    /// <summary>8FA4: asks the target (8DB0), and starts the move to its cell (8CA1).</summary>
    private static void Retry(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Call(0x8db0, g);
        if (!g.Zf) { Stop(g, r); return; }
        g.Ecx = g.Shr(g.Ecx, 5, 32); g.Edx = g.Shr(g.Edx, 5, 32); g.Al = g.Cl; g.Ah = g.Dl; w.Call(0x8ca1, g); OrFF(g);
    }

    private static void NotTest40(IUnitActionWorld w, ActionRegs g) { g.Al = (byte)~w.G1C9F; g.Logic((uint)(g.Al & 0x40), 8); }   // mov al,[1C9F]; not al; test al,40h

    /// <summary>8227 (FS:E1): the unit step dispatcher. The table of six handlers follows the call in the script of the unit; the state +14 and 7 picks the handler, whose ZF decides whether the next state
    /// (+14 bits 3..5) takes over. <paramref name="handlers"/> are the addresses of the handlers of the table.</summary>
    public static void F8227(IUnitActionWorld w, ActionRegs g, byte[] r, uint table, uint[] handlers)
    {
        r[0x2e]++; g.Ebx = 0; g.Edx = table;
        g.Cmp(r[0x19], 0, 8);
        if (g.Jle) return;
        g.Test(U16(r, 0x17), 0x80, 16);
        if (!g.Zf) return;
        g.Al = r[0x14]; g.Bl = (byte)(r[0x14] & 0x40); g.Eax &= 7;
        g.Add(table, handlers[g.Eax] - table); w.G50E = handlers[g.Eax]; g.Edx = handlers[g.Eax]; w.Call(handlers[g.Eax], g);
        if (!g.Zf) return;
        g.Al = (byte)(r[0x14] >> 3); g.Eax = (uint)(g.Al & 7); g.Logic(g.Eax, 32);
        r[0x14] = g.Al; r[0x30] = g.Ah; r[0x15] = g.Ah;
    }

    /// <summary>8273: the action of a unit that walks to a point (the target +22/+23) and answers a strike.</summary>
    public static void F8273(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.G1C9F = g.Bl; w.Call(0x8d89, g);
        g.Cmp(g.Al, 1, 8);
        if (g.Zf)
        {
            g.Cmp(g.Dl, 0, 8);
            if (g.Zf) return;
            g.Test(U16(r, 0x17), 2, 16);
            if (!g.Zf) { w.Call(0x8c98, g); r[0x15] = 2; }
            else { NotTest40(w, g); return; }
        }
        else
        {
            g.Cmp(g.Al, 2, 8);
            if (!g.Zf) { g.Al = r[0x22]; g.Ah = r[0x23]; w.Call(0x8ca1, g); r[0x15]++; NotTest40(w, g); return; }
        }
        w.Call(0x8e81, g);
        if (!g.Zf) { NotTest40(w, g); return; }
    }

    /// <summary>82C3: the action of the idle unit: looks for an enemy (8D3E), follows the selected object or the target.</summary>
    public static void F82C3(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.G1C9F = g.Bl; w.Call(0x8d89, g);
        g.Test(g.Al, g.Al, 8);
        if (g.Zf)
        {
            g.Cmp(g.Dl, 0, 8);
            if (g.Zf)
            {
                w.Call(0x8af4, g); w.Call(0x8b37, g);
                if (!g.Zf) return;
                w.Call(0x8d3e, g); g.Cmp(w.G512, 0, 32);
                if (g.Zf)
                {
                    g.Edi = w.G51E; g.Test(g.Edi, g.Edi, 32);
                    if (!g.Zf) w.Call(0x902f, g);
                    else
                    {
                        w.Call(0x8b4c, g); g.Test(U16(r, 0x17), 2, 16);
                        if (!g.Zf) w.Call(0x8b0f, g);
                    }
                }
                else { w.Call(0x8e3c, g); r[0x15]++; }
                NotTest40(w, g); return;
            }
        }
        else
        {
            g.Cmp(g.Al, 1, 8);
            if (!g.Zf) return;
            w.Call(0x8ec7, g);
            if (!g.Zf) { NotTest40(w, g); return; }
        }
        r[0x15] &= 0xf0; w.Call(0x8c98, g);
        NotTest40(w, g);
    }

    /// <summary>The rectangle search of 833F and 83D9 (219C3, D7B1, DA45): true when somebody of the owner was found.</summary>
    private static bool Nearby(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Ecx = (uint)(U32(r, 4) >> 5); g.Eax = U32(r, 8) >> 5; g.Ecx = g.Shr(g.Ecx, 0, 32) is var _ ? g.Ecx : g.Ecx;
        g.Ch = (byte)g.Eax; g.Edx = 0x303; w.Call(0x219c3, g);
        g.Edx -= g.Ecx; g.Edx += 0x101;
        w.Call(0xd7b1, g); g.Al = r[3]; w.Call(0xda45, g); g.Test(g.Eax, g.Eax, 32);
        return !g.Zf;
    }

    /// <summary>833F: 82C3, and every eighth update a unit on its way (AL = 1) clears the target when nobody of the owner is near it.</summary>
    public static void F833F(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Call(0x82c3, g);
        if (g.Zf) return;
        g.Test(r[0x2e], 7, 8);
        if (g.Zf)
        {
            w.Call(0x8d89, g); g.Cmp(g.Al, 1, 8);
            if (g.Zf)
            {
                g.Edi = U32(r, 0x1c); g.Test(g.Edi, g.Edi, 32);
                if (!g.Zf)
                {
                    g.Edi += w.PoolBase;
                    if (Nearby(w, g, r)) Put(r, 0x1c, 0);
                }
            }
        }
        OrFF(g);
    }

    /// <summary>839F: the action of the unit that has a target: the engagement (8E56) or the reach test (8EC7).</summary>
    public static void F839F(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.G1C9F = g.Bl; w.Call(0x8d89, g);
        g.Test(g.Al, g.Al, 8);
        if (g.Zf) { w.Call(0x8e56, g); r[0x15]++; NotTest40(w, g); return; }
        g.Cmp(g.Al, 1, 8);
        if (!g.Zf) return;
        w.Call(0x8ec7, g);
        if (g.Zf) { Xor0(g); return; }
        g.Test(U16(r, 0x17), 2, 16);
        if (!g.Zf) { Xor0(g); return; }
        NotTest40(w, g);
    }

    /// <summary>83D9: 839F, and a unit on its way (DH = 3) whose target belongs to another owner gives it up when nobody of its own owner is near.</summary>
    public static void F83D9(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Call(0x839f, g);
        if (g.Zf) return;
        w.Call(0x8d89, g); g.Cmp(g.Dh, 3, 8);
        if (g.Zf)
        {
            g.Cmp(g.Dl, g.Dh, 8);
            if (!g.Zf)
            {
                g.Edi = U32(r, 0x1c); g.Test(g.Edi, g.Edi, 32);
                if (!g.Zf)
                {
                    g.Edi += w.PoolBase; var o = w.ObjectAt(g.Edi); g.Al = o[3]; g.Cmp(g.Al, r[3], 8);
                    if (!g.Zf && Nearby(w, g, r)) w.Call(0x8c98, g);
                }
            }
        }
        OrFF(g);
    }

    /// <summary>843E: the combat action of the unit: reach test (8ABF), the swing (8CF3), the engagement (8E56), 8EC7 and the approach 8F70.</summary>
    public static void F843E(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.G1C9F = g.Bl; w.Call(0x8abf, g);
        if (g.Zf) { Xor0(g); return; }
        w.Call(0x8d89, g); g.Test(g.Al, g.Al, 8);
        if (g.Zf)
        {
            w.Call(0x8cf3, g);
            if (g.Zf)
            {
                g.Test(g.Bl, g.Bl, 8);
                if (!g.Zf) { Xor0(g); return; }
                w.Call(0x8e56, g); r[0x15]++; r[0x30] = 0;
            }
            NotTest40(w, g); return;
        }
        g.Cmp(g.Al, 1, 8);
        if (g.Zf)
        {
            w.Call(0x8a7f, g);
            if (g.Zf) { r[0x15]++; r[0x30] = 0; NotTest40(w, g); return; }
            w.Call(0x8ec7, g);
            if (g.Zf) { Xor0(g); return; }
            NotTest40(w, g); return;
        }
        g.Cmp(g.Al, 2, 8);
        if (!g.Zf) { Xor0(g); return; }
        w.Call(0x8a7f, g);
        if (!g.Zf) { r[0x15]--; r[0x30] = 0; NotTest40(w, g); return; }
        w.Call(0x8f70, g);
        if (g.Zf) { Xor0(g); return; }
        NotTest40(w, g);
    }

    /// <summary>(DAT[4A] + DAT[4B]) · 8 and (DAT[4F] + DAT[50] + DAT[51] + DAT[52]) · 4 as bytes.</summary>
    private static void Frames(byte[] t, out byte first, out byte second)
    {
        first = (byte)((byte)(t[0x4a] + t[0x4b]) << 3); second = (byte)((byte)(t[0x4f] + t[0x50] + t[0x51] + t[0x52]) << 2);
    }

    /// <summary>877F: the action of the unit that is turned (C0F6 with AL = 13h).</summary>
    public static void F877F(IUnitActionWorld w, ActionRegs g, byte[] r) { g.Al = 0x13; w.Call(0xc0f6, g); g.Al = (byte)~g.Bl; g.Logic((uint)(g.Al & 0x40), 8); }

    /// <summary>878D: the standing frame request of the shooter (8D0B).</summary>
    public static void F878D(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Call(0x8d89, g); g.Test(g.Al, g.Al, 8);
        if (g.Zf)
        {
            g.Cmp(g.Dl, 0, 8); bool stand = false;
            if (g.Zf) { g.Cmp(g.Dh, g.Dl, 8); stand = g.Zf; }
            if (!stand) { w.Call(0x8c98, g); OrFF(g); return; }
            r[0x25] = 0x24; var t = w.Template; Frames(t, out byte f1, out byte f2);
            g.Al = f1; g.Bl = f2; g.Al = (byte)(g.Al + g.Bl); g.Ah = (byte)(t[0x78] - 1); g.Ah = (byte)(g.Ah + g.Al);
            g.Cmp(r[0x32], 0, 8);
            if (!g.Zf) { r[0x15] = 2; byte x = g.Ah; g.Ah = g.Al; g.Al = x; }
            else r[0x15] = 1;
            w.Call(0x8d0b, g); OrFF(g); return;
        }
        g.Cmp(g.Al, 1, 8);
        if (g.Zf)
        {
            g.Cmp(g.Dh, 5, 8);
            if (g.Zf) { OrFF(g); return; }
            r[0x32] = 0xff; PutW(r, 0x17, (ushort)(U16(r, 0x17) | 0x200)); Xor0(g); return;
        }
        g.Cmp(g.Al, 2, 8);
        if (!g.Zf) { Xor0(g); return; }
        g.Cmp(g.Dl, 5, 8);
        if (!g.Zf) { OrFF(g); return; }
        r[0x32] = 0; r[0x20] = 0xff; Xor0(g);
    }

    /// <summary>8854 of 881B: the standing frame request of the worker.</summary>
    private static void Tail8854(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        r[0x25] = 0x24; Frames(w.Template, out byte f1, out byte f2);
        g.Bl = f1; g.Ebx = (g.Ebx & 0xffff00ff) | ((uint)f2 << 8); g.Bl = (byte)(g.Bl + f2);
        g.Al = (byte)(g.Al + g.Bl); g.Ah = (byte)(g.Ah + g.Bl);
        w.Call(0x8d0b, g); OrFF(g);
    }

    /// <summary>881B: the action of the idler: the standing request, and (AL = 1) a wander to a random place near it (10AD2).</summary>
    public static void F881B(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Call(0x8d89, g); g.Test(g.Al, g.Al, 8);
        if (g.Zf)
        {
            g.Cmp(g.Dl, 0, 8); bool stand = false;
            if (g.Zf) { g.Cmp(g.Dh, g.Dl, 8); stand = g.Zf; }
            if (!stand) { w.Call(0x8c98, g); OrFF(g); return; }
            r[0x15] = 1; g.Al = 0; g.Ah = (byte)(w.Template[0x78] - 1);
            Tail8854(w, g, r); return;
        }
        g.Cmp(g.Al, 1, 8);
        if (g.Zf)
        {
            g.Cmp(g.Dh, 5, 8);
            if (g.Zf) { OrFF(g); return; }
            PutW(r, 0x17, (ushort)(U16(r, 0x17) | 0x200)); g.Eax = 0; w.Call(0x43b0, g);
            g.Ecx = U32(r, 4) >> 5; g.Eax = U32(r, 8) >> 5; g.Ch = (byte)g.Eax; g.Edx = 0xa0a; w.Call(0x219c3, g);
            g.Edx -= g.Ecx; g.Edx += 0x101;
            w.Call(0x9e41, g); g.Eax = (g.Eax & 0xffff0000) | (ushort)(g.Al * g.Dl); g.Cl = (byte)(g.Cl + g.Ah);
            w.Call(0x9e55, g); g.Eax = (g.Eax & 0xffff0000) | (ushort)(g.Al * g.Dh); g.Ch = (byte)(g.Ch + g.Ah);
            g.Al = r[0xe]; g.Ah = r[0]; g.Bl = (byte)(r[0xd] & 3); g.Edx = g.Ch; g.Ecx = g.Cl; g.Edx <<= 5; g.Ecx <<= 5;
            w.Call(0x10ad2, g);
            if (g.Zf) { Put(r, 4, g.Ecx); Put(r, 8, g.Edx); }
            g.Eax = g.Esi - w.PoolBase; w.Call(0x43b0, g); r[0x15] = 2; g.Al = (byte)(w.Template[0x78] - 1); g.Ah = 0;
            Tail8854(w, g, r); return;
        }
        g.Cmp(g.Al, 2, 8);
        if (!g.Zf) { Xor0(g); return; }
        g.Cmp(g.Dl, 5, 8);
        if (g.Zf) { OrFF(g); return; }
        Xor0(g);
    }

    /// <summary>8917: the action of the building that produces creatures (the herd of a lair): its health, the birth of the young, the take-over of a waiting worker.</summary>
    public static void F8917(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Cmp(r[0x19], 0, 8);
        if (g.Jle) return;
        var t = w.Template; g.Cmp(r[0x31], 0xff, 8);
        if (g.Zf)
        {
            r[0x14] = 0; g.Al = (byte)(r[0x2d] & 0xf); g.Cmp(g.Al, 0, 8);
            if (!g.Zf) { w.Call(0x8c98, g); return; }
            bool bit = (U32(r, 0x17) & 0x40) != 0; Put(r, 0x17, U32(r, 0x17) & ~0x40u); g.Cf = bit;
            if (!g.Cf) return;
            w.Call(0xc4cd, g);
            if (g.Zf) return;
            g.Test(U32(w.Template, 0x40), 0x1000, 32);
            if (g.Zf) return;
            uint saved = g.Edi; g.Edi -= w.PoolBase; Put(r, 0x1c, g.Edi); w.Call(0xcaee, g); g.Edi = saved;
            if (!g.Zf) return;
            g.Eax = 0; var o = w.ObjectAt(g.Edi); o[0x19] = 0; Put(r, 0x1c, 0); r[0x14] = 0; r[0x15] = 0; g.Logic(0, 32);
            g.Al = o[0]; r[0x32] = g.Al; g.Al = o[0xe]; r[0x31] = g.Al; g.Al = o[0x16]; r[0x16] = g.Al; g.Al = t[0x45]; r[0x19] = g.Al; g.Al = o[3];
            uint esi = g.Esi; w.Call(0xd1ba, g); g.Esi = esi;
            return;
        }
        g.Al = (byte)(t[0x45] >> 1); g.Cmp(r[0x19], g.Al, 8);
        if (g.Sf == g.Of) return;   // JGE
        r[0x19] = (byte)(r[0x19] + g.Al); g.Al = r[0x31]; g.Ah = r[0x32]; w.Call(0x9064, g);
        if (!g.Zf)
        {
            byte x = g.Bh; g.Bh = g.Bl; g.Bl = x; g.Bl = (byte)(g.Bl + 4); g.Bl = (byte)(g.Bl & 7); uint pushed = g.Ebx;
            g.Bl = g.Al; g.Al = g.Ah; g.Ah = r[3]; w.Call(0xa758, g); g.Eax = pushed;
            if (g.Zf) w.ObjectAt(g.Edi)[0x19] = 0;
        }
        r[0x31] = 0xff; g.Al = 0; r[0x16] = 0; r[0x0f] = 0; r[0x1b] = 0; r[0x14] = 0; r[0x15] = 0; g.Logic(0, 8);
    }

    /// <summary>89F1: every 32 updates the unit asks the army (1ECA0, 22E46, BE2E) and marks +0F bit 10h; bit 20h when the participant has any of its 20 slots (+0CDh) in use.</summary>
    public static void F89F1(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Test(r[0x2e], 0x1f, 8);
        if (!g.Zf) return;
        r[0x0f] &= 0xcf; g.Al = r[3]; g.Ecx = 0xf08; g.Edx = 0x508; w.Call(0x1eca0, g);
        if (g.Zf)
        {
            g.Al = r[3]; g.Ecx = 0xf08; g.Edx = 0x508; w.Call(0x22e46, g);
            if (!g.Zf) { w.Call(0xbe2e, g); if (g.Zf) r[0x0f] |= 0x10; }
        }
        g.Eax = 0xf5; g.Eax = (g.Eax & 0xffff0000) | (ushort)(0xf5 * r[3]); g.Ecx = g.Eax + 0x97a; g.Eax = 0;
        var part = w.PartOf(r[3]);
        for (; ; )
        {
            g.Cmp(part[0xcd + g.Eax], 0, 8);
            if (!g.Zf) { r[0x0f] |= 0x20; g.Logic(r[0x0f], 8); return; }
            g.Eax++; g.Cmp(g.Eax, 0x14, 32);
            if (g.Zf) return;
        }
    }

    /// <summary>8A54: the unit has left (12588), its slot [1CCB + 16h · owner] is cleared and 4473 runs.</summary>
    public static void F8A54(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Eax = U32(r, 8) >> 5; g.Ecx = U32(r, 4) >> 5; g.Ch = (byte)g.Eax; g.Ah = r[3]; w.Call(0x12588, g);
        g.Eax = 0x16; g.Eax = (g.Eax & 0xffff0000) | (ushort)(0x16 * r[3]); g.Cf = g.Of = (g.Eax & 0xff00) != 0; w.SetG1CCB((int)g.Eax, 0xff); w.Call(0x4473, g);
    }

    /// <summary>8A7F: the target of the unit asks D182 (AH = 5) whether it is in range of the unit; EDI the target.</summary>
    public static void F8A7F(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Eax = U32(r, 0x1c); g.Test(g.Eax, g.Eax, 32);
        if (g.Zf) { OrFF(g); return; }
        uint saved = g.Esi; g.Edx = g.Esi; g.Esi = w.PoolBase + g.Eax; g.Ah = 5; w.Call(0xd182, g);
        g.Edi = g.Esi; g.Esi = saved;
    }

    /// <summary>8A9F: the unit forgets its target (+1C = 0) when the target is no longer alive (health at most DAT[46]).</summary>
    public static void F8A9F(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Edi = U32(r, 0x1c); g.Test(g.Edi, g.Edi, 32);
        if (g.Zf) return;
        g.Edi += w.PoolBase; w.Call(0x68bd, g); g.Al = w.ObjectAt(g.Edi)[0x19]; g.Cmp(g.Al, w.Template[0x46], 8);
        if (g.Jg) return;
        g.Eax = 0; g.Logic(0, 32); Put(r, 0x1c, 0);
    }

    /// <summary>8ABF: as 8A9F, and a unit that loses its target gains experience +16 (DAT[76] / 4, up to 7F).</summary>
    public static void F8ABF(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Edi = U32(r, 0x1c); g.Test(g.Edi, g.Edi, 32);
        if (g.Zf) return;
        g.Edi += w.PoolBase; w.Call(0x68bd, g); g.Al = w.ObjectAt(g.Edi)[0x19]; g.Cmp(g.Al, w.Template[0x46], 8);
        if (g.Jg) return;
        g.Cmp(r[0x16], 0x40, 8);
        if (!g.Jae)
        {
            g.Al = (byte)(w.Template[0x76] >> 2); r[0x16] = (byte)(r[0x16] + g.Al); g.Logic(r[0x16], 8);
            if (g.Sf) r[0x16] = 0x7f;
        }
        g.Eax = 0; g.Logic(0, 32); Put(r, 0x1c, 0);
    }

    /// <summary>8AF4: the swing delay counter +2F counts up to DAT[75] minus the experience's share; ZF = ready.</summary>
    public static void F8AF4(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Eax = 0x400c00; g.Al = w.Template[0x75]; g.Bl = g.Al;
        g.Eax = (g.Eax & 0xffff0000) | (ushort)(g.Al * r[0x16]); g.Bl = (byte)(g.Bl - g.Ah);
        g.Cmp(r[0x2f], g.Bl, 8);
        if (!g.Jae) { r[0x2f]++; g.Zf = r[0x2f] == 0; g.Sf = (r[0x2f] & 0x80) != 0; g.Of = r[0x2f] == 0x80; return; }
        Xor0(g);
    }

    /// <summary>8B0F: asks A38A for a place when the unit rests (+2D low nibble 0) and requests the move to it.</summary>
    public static void F8B0F(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Al = (byte)(r[0x2d] & 0xf); g.Cmp(g.Al, 0, 8);
        if (!g.Zf) return;
        w.Call(0xa38a, g);
        if (!g.Zf) return;
        PutW(r, 0x22, (ushort)g.Eax); r[0x2d] &= 0xf; g.Cmp(r[0x2d], 2, 8);
        if (g.Zf) w.Call(0x8120, g);
        r[0x2d] |= 0x20; g.Logic(r[0x2d], 8);
    }

    /// <summary>8B37: a unit with +17 bit 20h (told to move) takes the place (8B0F).</summary>
    public static void F8B37(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Test(U16(r, 0x17), 0x20, 16);
        if (g.Zf) return;
        PutW(r, 0x17, (ushort)(U16(r, 0x17) & 0xffdf)); w.Call(0x8b0f, g); OrFF(g);
    }

    /// <summary>8B4C: every 128 updates B5AB with the template.</summary>
    public static void F8B4C(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Test(r[0x2e], 0x7f, 8);
        if (!g.Zf) return;
        w.Call(0xb5ab, g);
    }

    /// <summary>8BC7: the target is out of the reach of the unit (distance of 9B86 / 32 above DAT[47]) — ZF = 0 when it is.</summary>
    public static void F8BC7(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Edi = U32(r, 0x1c); g.Test(g.Edi, g.Edi, 32);
        if (g.Zf) return;
        g.Edi += w.PoolBase; w.Call(0x9b86, g);
        g.Eax = g.Shr(g.Eax, 5, 32); g.Cmp(g.Al, w.Template[0x47], 8);
        if (g.Cf || g.Zf) { Xor0(g); }
    }

    /// <summary>8BEA: the unit moves to its target (960B) when it rests.</summary>
    public static void F8BEA(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Al = (byte)(r[0x2d] & 0xf); g.Cmp(g.Al, 0, 8);
        if (!g.Zf) { w.Call(0x8c98, g); OrFF(g); return; }
        g.Edi = U32(r, 0x1c); g.Test(g.Edi, g.Edi, 32);
        if (g.Zf) return;
        g.Edi += w.PoolBase; var o = w.ObjectAt(g.Edi); g.Eax = U32(o, 4); g.Ebx = U32(o, 8); w.Call(0x960b, g);
        if (g.Zf) return;
        r[0x2d] &= 0xf; r[0x2d] |= 0x10; g.Logic(r[0x2d], 8);
    }

    /// <summary>8C47: the unit moves to the cell (AL, AH) when it rests.</summary>
    public static void F8C47(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Edx = g.Eax; g.Al = (byte)(r[0x2d] & 0xf); g.Cmp(g.Al, 0, 8);
        if (!g.Zf) { w.Call(0x8c98, g); OrFF(g); return; }
        g.Eax = g.Dl; g.Ebx = g.Dh; g.Eax <<= 5; g.Ebx <<= 5; g.Eax += 0x10; g.Ebx += 0x10; w.Call(0x960b, g);
        if (g.Zf) return;
        r[0x2d] &= 0xf; r[0x2d] |= 0x10; g.Logic(r[0x2d], 8);
    }

    /// <summary>8C89: the walk target +22/+23 = (AL, AH), request 3 of the motion nibbles.</summary>
    public static void F8C89(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        r[0x22] = g.Al; r[0x23] = g.Ah; r[0x2d] &= 0xf; r[0x2d] |= 0x30; g.Logic(r[0x2d], 8);
    }

    /// <summary>8CF3: 8BC7 and the follow request 8CBB.</summary>
    public static void F8CF3(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Call(0x8bc7, g); g.Bl = 0;
        if (g.Zf) return;
        w.Call(0x8cbb, g);
        if (!g.Zf) return;
        g.Al = (byte)(r[0x2d] & 0xf); g.Cmp(g.Al, 0, 8);
    }

    /// <summary>84AA: the action of the herdsman / escort: the cell (+32, +33) it keeps to, the distance to it (C331), the sub-state +34 (the counter and DAT[78]).</summary>
    public static void F84AA(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var t = w.Template;
        w.G1C9F = g.Bl; w.Call(0x8d89, g); g.Test(g.Al, g.Al, 8);
        if (g.Zf)
        {
            g.Cmp(g.Dl, 0, 8);
            if (g.Zf) { g.Al = r[0x32]; g.Ah = r[0x33]; w.Call(0x8ca1, g); r[0x15]++; }
            else w.Call(0x8c98, g);
            End(w, g); return;
        }
        g.Cmp(g.Al, 1, 8);
        if (g.Zf)
        {
            uint savedDx = g.Edx; g.Eax = U32(r, 4); g.Ebx = U32(r, 8); g.Ecx = r[0x32]; g.Edx = r[0x33]; g.Ecx <<= 5; g.Edx <<= 5; g.Ecx += 0x10; g.Edx += 0x10;
            w.Call(0xc331, g);
            g.Ebx = t[3]; g.Ebx <<= 4; g.Bl = (byte)(g.Bl + 0x10); g.Eax -= g.Ebx; g.Edx = savedDx;
            g.Cmp(g.Dl, g.Dh, 8);
            if (!g.Zf) { Distance(w, g, r); return; }
            g.Cmp(g.Dl, 0, 8);
            if (!g.Zf) { Distance(w, g, r); return; }
            g.Cmp(g.Eax, 0, 32);
            if (!g.Zf) { Wander(w, g, r); return; }
            g.Al = r[0x32]; g.Ah = r[0x33]; w.Call(0x8c47, g);
            if (!g.Zf) { End(w, g); return; }
            g.Al = t[0x78]; r[0x34] = (byte)((r[0x34] & 0xe0) | g.Al); r[0x15]++; End(w, g); return;
        }
        g.Cmp(g.Al, 2, 8);
        if (!g.Zf) { Xor0(g); g.Bl = 1; return; }
        g.Test(U16(r, 0x17), 1, 16);
        if (!g.Zf) { Xor0(g); g.Bl = 1; return; }
        g.Cmp(g.Dl, 3, 8);
        if (g.Zf) { End(w, g); return; }
        g.Cl = r[0x32]; g.Ch = r[0x33]; w.Call(0xd101, g); g.Cmp(g.Eax, 1, 32);
        if (!g.Zf) { Wander(w, g, r); return; }
        g.Test(r[0x34], 0x1f, 8);
        if (g.Zf)
        {
            r[0x34] += 0x20; g.Cmp(r[0x34], 0xe0, 8);
            if (!g.Jb)
            {
                r[0x34] = 0; g.Dl = r[0x32]; g.Dh = r[0x33]; g.Eax = 0x201; g.Ecx = 0x101; w.Call(0xcfac, g);
            }
            g.Edi = g.Esi; g.Al = 3; w.Call(0x123d3, g);
            r[0x16] = (byte)(r[0x16] + 5); g.Logic(r[0x16], 8);
            if (g.Sf) r[0x16] = 0x7f;
            g.Bl = 0; g.Logic(0, 8); return;
        }
        w.Call(0x8af4, g);
        if (!g.Zf) { End(w, g); return; }
        g.Al = r[0x32]; g.Ah = r[0x33]; w.Call(0x8c89, g); r[0x34]--; End(w, g);
    }

    private static void End(IUnitActionWorld w, ActionRegs g) { NotTest40(w, g); g.Bl = 1; }

    /// <summary>8554: the distance to the cell is up to 5: the unit stops (8C98).</summary>
    private static void Distance(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Cmp(g.Eax, 5, 32);
        if (g.Jg) { End(w, g); return; }
        w.Call(0x8c98, g); End(w, g);
    }

    /// <summary>85E2: looks for a place of the herd (C35F with DL = 1, then DAT[47]) and sets the cell +32/+33.</summary>
    private static void Wander(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var t = w.Template;
        g.Test(w.G1C9F, 0x40, 8);
        if (!g.Zf) { Xor0(g); g.Bl = 1; return; }
        g.Dl = 1; g.Al = 1; w.Call(0xc35f, g);
        if (!g.Zf)
        {
            r[0x32] = g.Al; r[0x33] = g.Bl; r[0x15] = 1;
            g.Eax = U32(r, 4) >> 5; g.Cmp(g.Al, r[0x32], 8);
            if (!g.Zf) { End(w, g); return; }
            g.Eax = U32(r, 8) >> 5; g.Cmp(g.Al, r[0x33], 8);
            if (!g.Zf) { End(w, g); return; }
            w.Call(0x8b0f, g); End(w, g); return;
        }
        g.Dl = t[0x47]; g.Al = 1; w.Call(0xc35f, g);
        if (g.Zf) { Xor0(g); g.Bl = 1; return; }
        g.Cmp(r[0x32], g.Al, 8);
        if (g.Zf) { g.Cmp(r[0x33], g.Bl, 8); if (g.Zf) { Xor0(g); g.Bl = 1; return; } }
        r[0x32] = g.Al; r[0x33] = g.Bl; r[0x15] = 0; End(w, g);
    }

    /// <summary>7FA7: reads the next step of the route (the nibbles at the cursor +28; bit 8 ends the path); at the end the route is released (8120) and the unit looks for a new one (73F5) within the
    /// allowance [1C61]; a step goes through the admission 7C63 (DL 0 = free, 2 = wait, 3 = contact — the occupant is hit —, FF = blocked).</summary>
    public static void F7FA7(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Ebx = U32(r, 0x28); g.Cmp(g.Ebx, 0xffffffff, 32);
        if (!g.Zf)
        {
            g.Al = g.Bl; bool odd = (g.Ebx & 1) != 0; g.Ebx >>= 1; g.Ebx += 0x401000;
            byte b = w.Route[g.Ebx - 0x401000]; g.Al = b; if (odd) g.Al = (byte)(b >> 4); g.Al = (byte)(g.Al & 0xf);
            g.Test(g.Al, 8, 8);
            if (g.Zf) { Step(w, g, r); return; }
            w.Call(0x8120, g);
            g.Eax = U32(r, 4) >> 5; g.Cmp(g.Al, w.G525, 8);
            if (g.Zf)
            {
                g.Eax = U32(r, 8) >> 5; g.Cmp(g.Al, w.G526, 8);
                if (g.Zf) { g.Ah = 0; g.Logic(g.Ah, 8); return; }
            }
        }
        Rebuild(w, g, r);
    }

    private static void Rebuild(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Call(0x8120, g); g.Cmp(w.G1C61, 0, 8);
        if (g.Zf) { Blocked(g, r); return; }
        w.G1C61--; g.Eax = 0; w.Call(0x43b0, g); w.Call(0x73f5, g); g.Eax = g.Esi - w.PoolBase; w.Call(0x43b0, g);
        g.Cmp(w.G2730, 0xffffffff, 32); g.Ah = 1;
        if (g.Zf) return;
        w.Call(0x802c, g);   // falls into the route storage (802C..809B), which has its own port
    }

    private static void Blocked(ActionRegs g, byte[] r) { g.Al = (byte)(r[0x25] & 7); g.Logic(g.Al, 8); g.Ah = 2; }

    private static void Step(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        uint eax = g.Eax; g.Eax = 0; w.Call(0x43b0, g); g.Eax = eax; w.Call(0x7c63, g);
        uint pushedEbx = g.Ebx, pushedEcx = g.Ecx; g.Eax = g.Esi - w.PoolBase; w.Call(0x43b0, g);
        g.Ebx = pushedEcx; g.Edx = pushedEbx; g.Eax = eax;
        g.Cmp(g.Dl, 0xff, 8);
        if (g.Zf) { Rebuild(w, g, r); return; }
        g.Cmp(g.Dl, 3, 8);
        if (g.Zf)
        {
            g.Edi = w.PoolBase + 0xc00; g.Bl = g.Al; w.Call(0xbb26, g);
            g.Eax = eax;
            if (g.Zf) { g.Ah = 2; return; }
            var o = w.ObjectAt(g.Ecx); g.Cmp(o[0x19], 0, 8);
            if (g.Jle) { g.Ah = 2; return; }
            o[0x19] = 0; o[0x17] &= 0xe0; o[0x17] |= 0x11; g.Ah = 2; return;
        }
        g.Cmp(g.Dl, 2, 8);
        if (g.Zf) { g.Ah = 2; return; }
        g.Test(g.Dl, g.Dl, 8);
        if (!g.Zf) { g.Ah = 2; return; }
        g.Edx = g.Esi - w.PoolBase; w.Call(0x43b7, g); g.Eax = eax;
        uint cur = U32(r, 0x28) + 1; Put(r, 0x28, cur); g.Zf = cur == 0; g.Sf = (cur & 0x80000000) != 0; g.Of = cur == 0x80000000; g.Ah = 0x20;
    }

    /// <summary>902F: the unit near its target (up to 8 cells: 9B86) tries the pick-up (12406) or the order (8C7C, 1243A) of the item.</summary>
    public static void F902F(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Call(0x9b86, g); g.Cmp(g.Eax, 8, 32);
        if (g.Cf == false && g.Zf == false) return;
        w.Call(0x8c1e, g);
        if (!g.Zf) return;
        w.Call(0x12406, g);
        if (g.Zf) { g.Ah = r[3]; w.Call(0x1243a, g); return; }
        g.Cmp(g.Al, 1, 8);
        if (!g.Zf) return;
        g.Edi -= w.PoolBase; Put(r, 0x1c, g.Edi); w.Call(0x8c7c, g);
    }

    /// <summary>81D9: the unit's fog and occupancy update (813E, 6885, 43E5) and the descriptor of its tile in EAX.</summary>
    public static void F81D9(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Call(0x813e, g); w.Call(0x6885, g);
        var o = w.ObjectAt(g.Edi); g.Test(U32(o, 0x40), 0x40000, 32);
        if (g.Zf) { g.Eax = g.Esi - w.PoolBase; w.Call(0x43e5, g); }
        g.Eax = U32(r, 8) >> 5; g.Ebx = U32(r, 4) >> 5; g.Eax = (g.Eax & 0xffff0000) | (ushort)(g.Al * 32); g.Add(g.Eax, g.Ebx); g.Eax += g.Ebx;
        g.Ebx = 0xf00000; g.Eax = w.TileDescriptor(g.Eax);   // EBX = [18FE], the descriptor table
    }

    private static ushort DsW(IUnitActionWorld w, uint a) => BitConverter.ToUInt16(w.Ds, (int)a);

    /// <summary>A10B: the game speed gate: the update counter [19E4] against the allowance ([908E] raised or lowered by the keys 4Eh / 4Ah) decides [1C4E] (whether the world steps), every 5 and every 2
    /// counts raise the bits of [19E7] that fs:[13Dh] mirrors.</summary>
    public static void FA10B(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds;
        g.Cmp(d[0x4169], 0, 8);
        if (!g.Zf) return;
        g.Test(d[0x19e7], 1, 8);
        if (!g.Zf)
        {
            g.Cmp(DsW(w, 0x1c35), 0x4a, 16);
            if (g.Zf) { g.Cmp(d[0x908e], 0xf0, 8); if (!g.Zf) d[0x908e]--; }
            g.Cmp(DsW(w, 0x1c35), 0x4e, 16);
            if (g.Zf) { g.Cmp(d[0x908e], 0xfe, 8); if (!g.Zf) d[0x908e]++; }
        }
        g.Ebx = (g.Ebx & 0xffff0000) | 0xff; g.Bl = (byte)(g.Bl - d[0x908e]);
        g.Al = d[0x19e4]; g.Ah = g.Al; g.Al = (byte)(g.Al - d[0x1c4d]); g.Cmp(g.Al, g.Bl, 8);
        if (!g.Jb) { g.Bh++; d[0x1c4d] = g.Ah; }
        d[0x1c4e] = g.Bh; d[0x19e7] &= 0xfe;
        g.Al = d[0x19e4]; g.Ah = g.Al; g.Al = (byte)(g.Al - d[0x19e5]); g.Cmp(g.Al, 5, 8);
        if (!g.Jb) { d[0x19e5] = g.Ah; d[0x19e7] |= 1; }
        d[0x19e7] &= 0xfd;
        g.Al = d[0x19e4]; g.Ah = g.Al; g.Al = (byte)(g.Al - d[0x19e6]); g.Cmp(g.Al, 2, 8);
        if (!g.Jb) { d[0x19e6] = g.Ah; d[0x19e7] |= 2; }
        g.Al = d[0x19e7]; w.Fs13D = g.Al;
    }

    /// <summary>9A8E: the end of the move of state 2: the unit is moved by the rest of its step (+2C of 20h) along its heading (the vectors at 19C7), the occupancy is carried over (4442, 43B7, 43B0) and the route released.</summary>
    public static void F9A8E(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Al = (byte)(r[0x2d] & 0xf); g.Cmp(g.Al, 2, 8);
        if (!g.Zf) return;
        uint ebp = r[0x2c]; g.Ecx = 0x20 - ebp; g.Cmp(0x20, ebp, 32);
        g.Dl = (byte)(r[0x25] >> 3); g.Edx = (uint)(g.Dl & 7); g.Eax = U32(r, 4);
        sbyte vx = (sbyte)w.Ds[0x19c7 + g.Edx * 2], vy = (sbyte)w.Ds[0x19c8 + g.Edx * 2];
        g.Cmp((byte)vx, g.Dh, 8);
        if (!g.Zf)
        {
            if (!(g.Sf != g.Of)) { Put(r, 4, U32(r, 4) + ebp); g.Eax -= g.Ecx; }
            else { Put(r, 4, U32(r, 4) - ebp); g.Eax += g.Ecx; }
        }
        g.Ebx = U32(r, 8); g.Cmp((byte)vy, g.Dh, 8);
        if (!g.Zf)
        {
            if (!(g.Sf != g.Of)) { Put(r, 8, U32(r, 8) + ebp); g.Ebx -= g.Ecx; }
            else { Put(r, 8, U32(r, 8) - ebp); g.Ebx += g.Ecx; }
        }
        g.Ebp = ebp; g.Cmp(ebp, 0x10, 32);
        if (!g.Jb)
        {
            g.Cl = (byte)(r[0xd] & 3); w.Call(0x4442, g); g.Edx = 0; w.Call(0x43b7, g);
            g.Eax = g.Esi - w.PoolBase; w.Call(0x43b0, g);
        }
        r[0x24] &= 0xf8; g.Logic(r[0x24], 8); w.Call(0x8120, g);
    }

    /// <summary>DA45: counts the objects of the list at 1F45 whose owner is in the mask +12h of the participant AL (EBX) and those whose owner is not (EAX).</summary>
    public static void FDA45(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Bl = g.Al; g.Eax = 0xf5; g.Eax = (g.Eax & 0xffff0000) | (ushort)(0xf5 * g.Bl);
        var part = w.PartOf(g.Bl); BitConverter.GetBytes(BitConverter.ToUInt32(part, 0x12)).CopyTo(d, 0x1ca2);
        g.Eax = 0; g.Ebx = 0; uint ebp = 0x1f45;
        for (; ; )
        {
            g.Edi = BitConverter.ToUInt32(d, (int)ebp); g.Test(g.Edi, g.Edi, 32);
            if (g.Zf) return;
            uint owner = w.ObjectAt(g.Edi)[3]; g.Ecx = owner;
            bool inMask = ((BitConverter.ToUInt32(d, (int)(0x1ca2 + (owner >> 5) * 4)) >> (int)(owner & 31)) & 1) != 0;
            if (inMask) g.Ebx++; else g.Eax++;
            ebp += 4;
        }
    }

    /// <summary>D740: a blow of the strength BL and the kind BH on the object found by 982B: its health +19 falls by the strength minus DAT[44] and +17 gets the flags of the blow (bit 2 = it hurt).</summary>
    public static void FD740(IUnitActionWorld w, ActionRegs g)
    {
        uint saved = g.Eax; w.Call(0x982b, g); g.Ebx = saved;
        if (g.Zf) return;
        var o = w.ObjectAt(g.Edi); g.Ah = o[0x19]; g.Cmp(g.Ah, 0, 8);
        if (g.Jle) { { g.Ah = 0xff; g.Logic(0xff, 8); }; return; }
        g.Bh = (byte)(g.Bh << 3); g.Ecx = 1; g.Cl = (byte)(g.Cl | g.Bh);
        byte before = g.Bl; g.Bl = (byte)(g.Bl - w.Template[0x44]); g.Cmp(before, w.Template[0x44], 8);
        if (!(g.Cf || g.Zf))
        {
            g.Cl = (byte)(g.Cl | 2); g.Ah = (byte)(g.Ah - g.Bl); g.Logic(g.Ah, 8);
            if (g.Sf) g.Ah = 0;
            o[0x19] = g.Ah;
        }
        PutW(o, 0x17, (ushort)((U16(o, 0x17) & 0xffe0) | (ushort)g.Ecx));
        { g.Ah = 0xff; g.Logic(0xff, 8); };
    }

    /// <summary>D77A: a random point around the object EDI (up to AL · 16 cells away in each axis, 9E41 / 9E55 pick the offsets and signs) for the effect 996F.</summary>
    public static void FD77A(IUnitActionWorld w, ActionRegs g)
    {
        g.Al = (byte)(g.Al << 4); g.Dl = g.Al;
        w.Call(0x9e41, g); g.Eax = (g.Eax & 0xffff0000) | (ushort)(g.Al * g.Dl); g.Ecx = g.Ah; g.Ecx += g.Ecx;
        w.Call(0x9e55, g); g.Eax = (g.Eax & 0xffff0000) | (ushort)(g.Al * g.Dl); g.Edx = g.Ah; g.Edx += g.Edx;
        g.Test(g.Ah, 1, 8); if (g.Zf) g.Ecx = (uint)-(int)g.Ecx;
        g.Test(g.Ah, 2, 8); if (g.Zf) g.Edx = (uint)-(int)g.Edx;
        var o = w.ObjectAt(g.Edi); g.Ecx += U32(o, 7); g.Add(g.Edx, U32(o, 0xc)); g.Edx += U32(o, 0xc);
        w.Call(0x996f, g);
    }

    /// <summary>8659: the action of the unit that looks for a target near it (C439) and approaches it (8F70).</summary>
    public static void F8659(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Bl = (byte)(g.Bl & 0x40); w.G1C9F = g.Bl; w.Call(0x8d89, g);
        g.Test(g.Al, g.Al, 8);
        if (g.Zf)
        {
            g.Ebx = 0xf00; g.Eax = 0x200; w.Call(0xc439, g);
            if (g.Zf) { Xor0(g); return; }
            g.Edi -= w.PoolBase; Put(r, 0x1c, g.Edi); r[0x15]++; r[0x30] = 0; g.Cmp(w.G1C9F, 0x40, 8); return;
        }
        g.Cmp(g.Al, 1, 8);
        if (!g.Zf) { Xor0(g); return; }
        w.Call(0x8f70, g);
        if (!g.Zf) { g.Cmp(w.G1C9F, 0x40, 8); return; }
        g.Test(g.Bl, g.Bl, 8);
        if (!g.Zf) { Xor0(g); return; }
        g.Cmp(r[0x31], 0, 8);
        if (!g.Zf) { Xor0(g); return; }
        r[0x14] = (byte)((r[0x14] & 7) | 0x58); Xor0(g);
    }

    /// <summary>BBE4: looks up the key AL &amp; [1F0A] in the table [1F31] (pairs of a frame offset and a key; the value FFh ends the table and the search starts again with AL = [1F43]), and moves the frame +1 of
    /// the record by the difference of the offsets times [1F44].</summary>
    private static void Bbe4(IUnitActionWorld w, ActionRegs g, byte[] rec)
    {
        var d = w.Ds; g.Al = (byte)(g.Al & d[0x1f0a]);
        for (int guard = 0; guard < 4096; guard++)
        {
            uint edi = BitConverter.ToUInt32(d, 0x1f31) - 2;
            for (; ; )
            {
                edi += 2;
                if (w.TableByte(edi) == 0xff) { g.Al = (byte)(d[0x1f43] & d[0x1f0a]); break; }
                if (g.Al == w.TableByte(edi + 1)) { Frame(w, g, rec, edi); return; }
            }
        }
        throw new InvalidOperationException("BBE4: no entry for the key");
    }

    private static void Frame(IUnitActionWorld w, ActionRegs g, byte[] rec, uint edi)
    {
        g.Edi = edi; byte cur = rec[0x22], off = w.TableByte(edi);
        g.Al = (byte)(cur - off); int product = (sbyte)g.Al * (sbyte)w.Ds[0x1f44]; g.Eax = (g.Eax & 0xffff0000) | (ushort)product;
        g.Cf = g.Of = product != (sbyte)product;
        ushort before = U16(rec, 1); PutW(rec, 1, (ushort)(before - (ushort)product)); g.Cmp(before, (ushort)product, 16);
        g.Al = off; rec[0x22] = g.Al;
    }

    /// <summary>BB8B (FS:55): the reshaping of a building line segment by its neighbours: the table behind the call (EDX bytes after the return address) maps the neighbour mask (AL of BADF masked by AH) to
    /// a frame offset; the segment and every neighbour BADF names are moved to it. The neighbour list is behind EDX of BADF.</summary>
    public static void FBB8B(IUnitActionWorld w, ActionRegs g, byte[] r, uint returnAddress)
    {
        var d = w.Ds; g.Edx += returnAddress;
        d[0x1f0a] = g.Ah; BitConverter.GetBytes(g.Edx).CopyTo(d, 0x1f31); d[0x1f43] = g.Al;
        w.Call(0x6885, g);
        var t = w.Template; g.Al = (byte)(t[0x4a] + t[0x4a]); g.Al = (byte)(g.Al + t[0x4b]); d[0x1f44] = g.Al;
        g.Al = 1; w.Call(0xbadf, g); g.Cl = g.Ah; Bbe4(w, g, r);
        g.Test(g.Cl, g.Cl, 8);
        if (g.Zf) return;
        uint esi = g.Esi;
        for (; ; )
        {
            g.Edx -= 4; uint ptr = ((g.Edx - 0x401900) / 4) % 2 == 0 ? 0x400900u : 0x400a00u; g.Esi = ptr;
            uint edx = g.Edx, ecx = g.Ecx; g.Al = 0; w.Call(0xbadf, g); Bbe4(w, g, w.ObjectAt(ptr));
            g.Ecx = ecx; g.Edx = edx; byte c = g.Cl; c--; g.Cl = c; g.Zf = c == 0; g.Sf = (c & 0x80) != 0;
            if (c == 0) break;
        }
        g.Esi = esi;
    }

    private static void Mul8(ActionRegs g, byte a, byte b) { g.Eax = (g.Eax & 0xffff0000) | (ushort)(a * b); g.Cf = g.Of = (g.Eax & 0xff00) != 0; }

    /// <summary>6877: ECX = the participant record of the owner +3 of the object ESI; then 6885.</summary>
    public static void F6877(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Eax = 0xf5; Mul8(g, g.Al, r[3]); g.Ecx = (g.Eax & 0xffff) + 0x97a; F6885(w, g, r);
    }

    /// <summary>6885: EBX = the army entry of the object ESI, EDI = its template ([army entry + 4] + 0ABh · +0E).</summary>
    public static void F6885(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Eax = 0; g.Al = r[0]; Mul8(g, g.Al, 0x56); g.Ebx = (g.Eax & 0xffff) + 0x6ca;
        g.Al = r[0xe]; Mul8(g, g.Al, 0xab); g.Edi = w.ArmyTable(r[0]) + (g.Eax & 0xffff); g.Add(w.ArmyTable(r[0]), g.Eax & 0xffff);
    }

    /// <summary>68A0: EDI = the template AL of the army AH.</summary>
    public static void F68A0(IUnitActionWorld w, ActionRegs g)
    {
        uint ecx = g.Ecx; byte army = g.Ah; g.Cl = g.Al; g.Eax &= 0xffff; g.Al = 0x56; Mul8(g, g.Al, army); g.Ebx = (g.Eax & 0xffff) + 0x6ca;
        g.Al = 0xab; Mul8(g, g.Al, g.Cl); uint table = w.ArmyTable(army); g.Add(table, g.Eax & 0xffff); g.Edi = table + (g.Eax & 0xffff); g.Ecx = ecx;
    }

    /// <summary>68BD: EDX = the template of the object EDI.</summary>
    public static void F68BD(IUnitActionWorld w, ActionRegs g)
    {
        var o = w.ObjectAt(g.Edi);
        g.Eax = 0; g.Al = o[0]; Mul8(g, g.Al, 0x56); g.Ebx = (g.Eax & 0xffff) + 0x6ca;
        g.Al = o[0xe]; Mul8(g, g.Al, 0xab); uint table = w.ArmyTable(o[0]); g.Add(table, g.Eax & 0xffff); g.Edx = table + (g.Eax & 0xffff);
    }

    /// <summary>C591: the object whose reference lies on the cell (DL, DH) of the plane (+0D &amp; 3) of the object ESI: EDI = the object (0 = none), ZF = 0 when its +17 has bit 40h.</summary>
    private static void C591(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Edi = 0; g.Cmp(g.Dl, 32, 8);
        if (g.Jae) { Xor0(g); return; }
        g.Cmp(g.Dh, 32, 8);
        if (g.Jae) { Xor0(g); return; }
        g.Eax = 32; Mul8(g, g.Al, g.Dh); g.Ebx = g.Dl; g.Eax += g.Ebx; g.Eax <<= 2;
        g.Bl = r[0xd]; g.Ebx &= 3; g.Eax += w.PlanePointer(g.Ebx);
        g.Edi = w.PlaneRead(g.Eax); g.Test(g.Edi, g.Edi, 32);
        if (g.Zf) { Xor0(g); return; }
        g.Edi += w.PoolBase; g.Test(U16(w.ObjectAt(g.Edi), 0x17), 0x40, 16);
    }

    /// <summary>C4CD: finds the waiting worker at the building: a building that blocks its entry (DAT[40] bit 8 with the offset DAT[53], DAT[54]) is asked at that cell, else the square ring around it (DAT[03] ×
    /// DAT[04]) is walked outwards (C591) for an object with +17 bit 40h; EDX = the template of the one found (68BD), AL = FFh.</summary>
    public static void FC4CD(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var t = w.Template; F6885(w, g, r); g.Test(U32(t, 0x40), 8, 32);
        if (!g.Zf)
        {
            g.Edx = (uint)(sbyte)t[0x53]; g.Eax = (uint)(sbyte)t[0x54]; g.Ebx = g.Edx; g.Ebx |= g.Eax; g.Logic(g.Ebx, 32);
            if (!g.Zf)
            {
                g.Edx += U32(r, 4); g.Eax += U32(r, 8); g.Edx >>= 5; g.Eax >>= 5; g.Dh = g.Al; C591(w, g, r);
                g.Test(g.Edi, g.Edi, 32);
                if (g.Zf) { Xor0(g); return; }
                Found(w, g); return;
            }
        }
        var d = w.Ds;
        g.Edx = U32(r, 4) >> 5; g.Eax = U32(r, 8) >> 5; g.Dh = g.Al; g.Cl = t[3]; g.Ch = t[4]; g.Ecx += 0x202;
        g.Al = (byte)(g.Cl >> 1); g.Dl = (byte)(g.Dl - g.Al); g.Al = (byte)(g.Ch >> 1); g.Dh = (byte)(g.Dh - g.Al); g.Ecx -= 0x101;
        d[0x1f41] = g.Cl; d[0x1f42] = g.Ch;
        for (int side = 0; side < 4; side++)
        {
            if (side > 0) g.Cl = d[side % 2 == 1 ? 0x1f42 : 0x1f41];
            for (; ; )
            {
                switch (side) { case 0: g.Dl++; break; case 1: g.Dh++; break; case 2: g.Dl--; break; default: g.Dh--; break; }
                C591(w, g, r);
                if (!g.Zf) { Found(w, g); return; }
                g.Cl--; g.Zf = g.Cl == 0;
                if (g.Cl == 0) break;
            }
        }
        Xor0(g);
    }

    private static void Found(IUnitActionWorld w, ActionRegs g) { F68BD(w, g); OrFF(g); }

    /// <summary>CAEE: the object of the target +1C, when it rests, is marked (+17 bit 80h, +1C = its reference) and its occupancy refreshed (43E5).</summary>
    public static void FCAEE(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Edi = U32(r, 0x1c) + w.PoolBase; var o = w.ObjectAt(g.Edi);
        g.Al = (byte)(o[0x2d] & 0xf); g.Cmp(g.Al, 0, 8);
        if (!g.Zf) return;
        PutW(o, 0x17, (ushort)(U16(o, 0x17) | 0x80)); g.Eax = g.Edi - w.PoolBase; Put(r, 0x1c, g.Eax);
        uint esi = g.Esi; g.Esi = g.Edi; g.Eax = 0; w.Call(0x43e5, g); g.Edi = g.Esi; g.Esi = esi; Xor0(g);
    }

    /// <summary>D12C: the terrain class (low 3 bits of the descriptor) of the cell of the unit.</summary>
    public static void FD12C(IUnitActionWorld w, ActionRegs g, byte[] r) => CellClass(w, g, r);

    /// <summary>D157: the terrain class of the cell of the object EDI.</summary>
    public static void FD157(IUnitActionWorld w, ActionRegs g) => CellClass(w, g, w.ObjectAt(g.Edi));

    private static void CellClass(IUnitActionWorld w, ActionRegs g, byte[] o)
    {
        g.Eax = U32(o, 8) >> 5; Mul8(g, g.Al, 32); g.Ebx = U32(o, 4) >> 5; g.Ebx += g.Eax; g.Ecx = 0xf00000;
        g.Eax = g.Ebx & 0xffff; g.Al = (byte)w.TileDescriptor(g.Eax); g.Eax &= 7; g.Logic(g.Eax, 32);
    }

    /// <summary>D182: the script of the template of the object ESI (script = [army entry + 9] + DAT[3B]) is called with the registers EAX and EBX kept (a tail jump).</summary>
    public static void FD182(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        uint eax = g.Eax, ebx = g.Ebx; F6885(w, g, r);
        g.Add(0, U32(w.Template, 0x3b)); g.Eax = U32(w.Template, 0x3b); Put(w.Ds, 0x19c3, g.Eax); g.Ebx = ebx; g.Eax = eax;
        w.Call(U32(w.Ds, 0x19c3), g);
    }

    /// <summary>D19C: as D182 for the template AL of the army AH given in EBX; ECX and EDX are kept.</summary>
    public static void FD19C(IUnitActionWorld w, ActionRegs g)
    {
        uint eax = g.Eax, ecx = g.Ecx, edx = g.Edx; g.Eax = g.Ebx; F68A0(w, g);
        g.Add(0, U32(w.Template, 0x3b)); g.Eax = U32(w.Template, 0x3b); Put(w.Ds, 0x19c3, g.Eax); g.Edx = edx; g.Ecx = ecx; g.Eax = eax;
        w.Call(U32(w.Ds, 0x19c3), g);
    }

    /// <summary>C331: EAX = the larger of the distances |EAX − ECX| and |EBX − EDX| (the flags of the last comparison).</summary>
    public static void FC331(ActionRegs g)
    {
        g.Eax = (uint)Math.Abs((int)(g.Eax - g.Ecx)); g.Ebx = (uint)Math.Abs((int)(g.Ebx - g.Edx)); g.Cmp(g.Eax, g.Ebx, 32);
        if (!(g.Cf || g.Zf)) return;
        g.Eax = g.Ebx;
    }

    /// <summary>C344: as C331 between the object ESI and the object EDI.</summary>
    public static void FC344(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var o = w.ObjectAt(g.Edi);
        g.Eax = (uint)Math.Abs((int)(U32(r, 4) - U32(o, 4))); g.Ebx = (uint)Math.Abs((int)(U32(r, 8) - U32(o, 8))); g.Cmp(g.Eax, g.Ebx, 32);
        if (!(g.Cf || g.Zf)) return;
        g.Eax = g.Ebx;
    }

    /// <summary>C35F: the first cell of the terrain class AL within DL cells around the unit (the window of DL·2 plus the size [273F] × [2740], clipped to the 32 × 32 map): AL, BL = the cell, ZF = 0 when found.</summary>
    public static void FC35F(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; byte target = g.Al, dl = g.Dl;
        byte cl = (byte)(dl + dl), ch = cl; cl = (byte)(cl + d[0x273f]); ch = (byte)(ch + d[0x2740]);
        byte al = (byte)(U32(r, 8) >> 5), dh = (byte)(d[0x2740] >> 1);
        al = (byte)(al - dh); bool borrow = al < dl; al = (byte)(al - dl);
        if (borrow) { ch = (byte)(ch + al); al = 0; }
        byte bl = (byte)(al + ch); borrow = bl < 32; bl = (byte)(bl - 32);
        if (!borrow) ch = (byte)(ch - bl);
        uint ebx = U32(r, 4) >> 5; bl = (byte)ebx; dh = (byte)(d[0x273f] >> 1);
        bl = (byte)(bl - dh); borrow = bl < dl; bl = (byte)(bl - dl);
        if (borrow) { cl = (byte)(cl + bl); bl = 0; }
        byte bh = (byte)(bl + cl); borrow = bh < 32; bh = (byte)(bh - 32);
        if (!borrow) cl = (byte)(cl - bh);
        dl = bl; dh = al; uint axy = (ushort)(al * 32);
        ebx = (ebx & 0xffff0000) | bl; ebx += axy; ebx = ebx * 4 + 0xe00000;
        uint eax = 32; eax = (eax & ~0xffu) | (byte)(32 - cl); uint edi = eax * 4; byte keepCl = cl, keepDl = dl;
        g.Edi = edi; g.Ecx = (g.Ecx & 0xffff0000) | ((uint)ch << 8) | cl; g.Edx = (g.Edx & 0xffff0000) | ((uint)dh << 8) | dl;
        for (; ; )
        {
            cl = keepCl; dl = keepDl;
            for (; ; )
            {
                uint tile = (ebx - 0xe00000) / 4; if (tile >= 1024) tile = 0; uint desc = w.TileDescriptor(tile); eax = desc & 7;
                if ((byte)eax == target)
                {
                    g.Eax = dl; g.Ebx = (ebx & ~0xffu) | dh; byte before = dh; dh++; g.Zf = dh == 0; g.Sf = (dh & 0x80) != 0; g.Cf = false;
                    g.Ecx = (g.Ecx & 0xffff0000) | ((uint)ch << 8) | cl; g.Edx = (g.Edx & 0xffff0000) | ((uint)dh << 8) | dl; _ = before; return;
                }
                ebx += 4; dl++; cl--;
                if (cl == 0) break;
            }
            ebx += edi; dh++; ch--;
            if (ch == 0) break;
        }
        g.Eax = eax; g.Ebx = ebx; g.Ecx = (g.Ecx & 0xffff0000) | ((uint)ch << 8) | cl; g.Edx = (g.Edx & 0xffff0000) | ((uint)dh << 8) | dl;
        g.Zf = true; g.Cf = false; g.Sf = false;
    }

    /// <summary>C439: the nearest object of the chain of the owner of ESI (from the hint to the end) whose template flags &amp; EBX equal [1F35] and whose health is above DAT[46]; EDI = it (0 = none), [1F31] its distance.</summary>
    public static void FC439(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; Put(d, 0x1f35, g.Eax); g.Eax = 0xf5; Mul8(g, 0xf5, r[3]); var part = w.PartOf(r[3]);
        uint end = U32(part, 9) + w.PoolBase, edx = U32(part, 0xe) + w.PoolBase; g.Ebp = end; g.Edx = edx;
        g.Edi = 0xffffffff; Put(d, 0x1f31, g.Edi); g.Edi = 0;
        g.Cmp(edx, end, 32);
        while (!g.Zf)
        {
            var o = w.ObjectAt(edx); g.Eax = 0x56; Mul8(g, 0x56, o[0]);
            g.Eax = 0xab; Mul8(g, 0xab, o[0xe]); uint ecx = w.ArmyTable(o[0]) + (g.Eax & 0xffff); g.Ecx = ecx;
            var t = w.ObjectAt(ecx); g.Eax = U32(t, 0x40) & g.Ebx; g.Ecx = ecx; g.Cmp(g.Eax, U32(d, 0x1f35), 32);
            if (g.Zf)
            {
                g.Al = t[0x46]; g.Cmp(o[0x19], g.Al, 8);
                if (!g.Jle)
                {
                    g.Eax = U32(o, 4); uint dx = g.Eax - U32(r, 4); g.Cmp(g.Eax, U32(r, 4), 32); if ((int)dx < 0) dx = (uint)-(int)dx; g.Eax = dx;
                    uint dy = U32(o, 8) - U32(r, 8); g.Ecx = U32(o, 8); g.Cmp(g.Ecx, U32(r, 8), 32); if ((int)dy < 0) dy = (uint)-(int)dy; g.Ecx = dy;
                    g.Cmp(g.Eax, g.Ecx, 32); if (g.Cf || g.Zf) g.Eax = g.Ecx;
                    g.Cmp(g.Eax, U32(d, 0x1f31), 32);
                    if (g.Cf || g.Zf) { Put(d, 0x1f31, g.Eax); g.Edi = edx; }
                }
            }
            edx += U32(o, 0x10); g.Edx = edx; g.Cmp(edx, end, 32);
        }
        g.Test(g.Edi, g.Edi, 32);
    }

    /// <summary>C0F6: the action of the unit that builds (the worker of the camp, +15: 0 order the walk, 1 walk to the cell, 2 pay and create the building, 3 build it, others wait); BL = the result (C0h finished, 40h failed, else the value of [1C9F]).</summary>
    public static void FC0F6(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; w.G1C9F = g.Bl; d[0x79a] = g.Al; var t = w.Template;
        void Done() => g.Bl = w.G1C9F;
        void Finish()
        {
            g.Eax = (uint)(r[0x25] & 7); g.Logic(g.Eax, 32); Mul8(g, (byte)g.Eax, t[0x4a]);
            uint lo = g.Eax & 0xffff, add = U16(t, 0x48); g.Add(lo << 16, add << 16); g.Eax = (g.Eax & 0xffff0000) | ((lo + add) & 0xffff);
            r[1] = (byte)g.Eax; r[2] = (byte)(g.Eax >> 8); Put(r, 0x1c, 0);
        }
        void Fail() { g.Bl = 0x40; Finish(); }
        void Dec2f()   // C156
        {
            r[0x2f]--; g.Zf = r[0x2f] == 0; g.Sf = (r[0x2f] & 0x80) != 0; g.Of = r[0x2f] == 0x7f;
            if (g.Zf) { Fail(); return; }
            r[0x27] = 0x32; g.Al = r[0x15]; r[0x34] = g.Al; r[0x15] = 0xa; Done();
        }
        g.Cmp(r[0x15], 0, 8);
        if (g.Zf) { g.Al = r[0x32]; g.Ah = r[0x33]; w.Call(0x8ca1, g); r[0x15] = 1; r[0x2f] = 3; Done(); return; }
        g.Cmp(r[0x15], 1, 8);
        if (g.Zf)
        {
            g.Al = (byte)(r[0x2d] & 0xf); g.Logic(g.Al, 8); g.Cmp(g.Al, 2, 8);
            if (g.Zf) { Done(); return; }
            g.Eax = g.Shr(U32(r, 4), 5, 32); g.Ebx = g.Shr(U32(r, 8), 5, 32);
            g.Cmp(g.Al, r[0x32], 8);
            if (g.Zf) { g.Cmp(g.Bl, r[0x33], 8); if (g.Zf) { r[0x15]++; g.Zf = r[0x15] == 0; g.Sf = (r[0x15] & 0x80) != 0; g.Of = r[0x15] == 0x80; Done(); return; } }
            Dec2f(); return;
        }
        g.Cmp(r[0x15], 2, 8);
        if (g.Zf)
        {
            g.Ah = 7; w.Call(0x9653, g);
            if (!g.Zf) { Done(); return; }
            g.Eax = 0; g.Logic(0, 32); w.Call(0x43b0, g);
            g.Al = d[0x79a]; g.Ah = r[0]; g.Bl = r[3]; g.Cl = r[0x32]; g.Ch = r[0x33];
            uint esi = g.Esi; w.Call(0xd363, g); Put(d, 0x79b, g.Ecx); Put(d, 0x79f, g.Edx); g.Esi = esi;
            bool zf = g.Zf, cf = g.Cf, sf = g.Sf, of = g.Of;
            g.Eax = g.Esi - w.PoolBase; w.Call(0x43b0, g);
            g.Zf = zf; g.Cf = cf; g.Sf = sf; g.Of = of;
            if (g.Jb) { var e = w.ObjectAt(g.Edi); Put(e, 0x17, (uint)(U16(e, 0x17) | 0x20) | (U32(e, 0x17) & 0xffff0000)); g.Logic(U16(e, 0x17), 16); Dec2f(); return; }
            if (!g.Zf) { Fail(); return; }
            g.Al = d[0x79a]; g.Ah = r[0]; F68A0(w, g);
            g.Eax = 0xf5; Mul8(g, 0xf5, r[3]); g.Ecx = 0x97a + (g.Eax & 0xffff); var p = w.PartOf(r[3]); var tt = w.ObjectAt(g.Edi);
            foreach (var (a, b) in new[] { (0x7c, 0x18), (0x80, 0x1c), (0x84, 0x20), (0x88, 0x24) })
            {
                g.Eax = U32(tt, a); g.Cmp(g.Eax, U32(p, b), 32);
                if (!g.Cf && !g.Zf) { Fail(); return; }
            }
            Put(p, 0x24, U32(p, 0x24) - U32(tt, 0x88)); g.Eax = U32(tt, 0x84); Put(p, 0x20, U32(p, 0x20) - g.Eax);
            g.Eax = U32(tt, 0x7c); Put(p, 0x18, U32(p, 0x18) - g.Eax); g.Eax = U32(tt, 0x80); Put(p, 0x1c, U32(p, 0x1c) - g.Eax);
            g.Ecx = U32(d, 0x79b); g.Edx = U32(d, 0x79f); g.Al = r[0]; g.Ah = r[3]; g.Bh = (byte)(tt[0x77] & 3); g.Logic(g.Bh, 8); g.Bl = d[0x79a];
            w.Call(0xa758, g);
            if (!g.Zf) { Fail(); return; }
            var o = w.ObjectAt(g.Edi); o[0x19] = 1; o[0x1a] = 1; g.Cmp(g.Edi, w.PoolBase, 32); g.Edi -= w.PoolBase; Put(r, 0x1c, g.Edi); r[0x15] = 3; r[0x2f] = 0;
            g.Eax = (uint)(r[0x25] & 7); g.Logic(g.Eax, 32); Mul8(g, (byte)g.Eax, t[0x4b]); g.Ebx = 0; g.Logic(0, 32); g.Bl = t[0x4a]; g.Eax += g.Ebx << 3;
            uint lo = g.Eax & 0xffff, add = U16(t, 0x48); g.Add(lo << 16, add << 16); g.Eax = (g.Eax & 0xffff0000) | ((lo + add) & 0xffff);
            r[1] = (byte)g.Eax; r[2] = (byte)(g.Eax >> 8); Done(); return;
        }
        g.Cmp(r[0x15], 3, 8);
        if (g.Zf)
        {
            g.Edi = 0; g.Logic(0, 32); g.Edi = U32(r, 0x1c); g.Add(g.Edi, w.PoolBase); g.Edi += w.PoolBase; var o = w.ObjectAt(g.Edi);
            g.Cmp(o[0x19], 0, 8); if (g.Jle) { Fail(); return; }
            g.Test(U16(r, 0x17), 1, 16); if (!g.Zf) { Fail(); return; }
            g.Test(r[0x2e], 1, 8); if (!g.Zf) { Done(); return; }
            ushort pos = (ushort)(U16(r, 1) + 1); r[1] = (byte)pos; r[2] = (byte)(pos >> 8); r[0x2f]++;
            g.Eax = t[0x4b]; g.Cmp(r[0x2f], g.Al, 8); if (g.Jb) { Done(); return; }
            pos = (ushort)(U16(r, 1) - g.Eax); r[1] = (byte)pos; r[2] = (byte)(pos >> 8); r[0x2f] = g.Ah;
            g.Al = t[0x79]; g.Add((uint)o[0x19] << 24, (uint)g.Al << 24); o[0x19] += g.Al; w.Call(0x68bd, g);
            g.Al = ObjectAtTemplate(w, g.Edx)[0x45]; g.Cmp(o[0x19], g.Al, 8); if (g.Jb) { Done(); return; }
            o[0x19] = g.Al; g.Bl = 0xc0; Finish(); return;
        }
        r[0x27]--; g.Zf = r[0x27] == 0; g.Sf = (r[0x27] & 0x80) != 0; g.Of = r[0x27] == 0x7f;
        if (g.Zf) { g.Al = r[0x34]; r[0x15] = g.Al; }
        Done();
    }
    private static byte[] ObjectAtTemplate(IUnitActionWorld w, uint p) => w.ObjectAt(p);

    /// <summary>B9EB: counts the objects of the chain of the owner AL (from the hint to the end) whose +1B equals BL; EAX = the count, CL = a checksum of their +0E / +0F.</summary>
    public static void FB9EB(IUnitActionWorld w, ActionRegs g)
    {
        var part = w.PartOf((byte)g.Eax); uint end = U32(part, 9) + w.PoolBase, esi = U32(part, 0xe) + w.PoolBase;
        g.Eax = 0; g.Ecx = 0; g.Cmp(esi, end, 32);
        while (!g.Zf)
        {
            var o = w.ObjectAt(esi);
            if (g.Bl == o[0x1b]) { g.Eax++; g.Cl ^= o[0xe]; g.Cl = (byte)(g.Cl + o[0xe]); g.Cl ^= o[0xf]; g.Cl = (byte)(g.Cl + o[0xf]); }
            esi += U32(o, 0x10); g.Cmp(esi, end, 32);
        }
    }

    /// <summary>BA2E: AL = FFh when an object of the chain of the owner AL has +1B equal to BL (else EAX = owner * F5h).</summary>
    public static void FBA2E(IUnitActionWorld w, ActionRegs g)
    {
        var part = w.PartOf((byte)g.Eax); g.Eax = (g.Eax & 0xff) * 0xf5; uint end = U32(part, 9) + w.PoolBase, esi = U32(part, 0xe) + w.PoolBase;
        g.Cmp(esi, end, 32);
        while (!g.Zf)
        {
            var o = w.ObjectAt(esi);
            if (g.Bl == o[0x1b]) { g.Al = 0xff; g.Logic(g.Al, 8); return; }
            esi += U32(o, 0x10); g.Cmp(esi, end, 32);
        }
    }

    /// <summary>BC15: the tail of the reshaping BB8B: the change message 43B0 for the record at EDX + [ESP], then BB8E and 98DB.</summary>
    public static void FBC15(IUnitActionWorld w, ActionRegs g, uint stackTop)
    {
        g.Edx += stackTop; uint saved = g.Edx; g.Eax = 0; g.Logic(0, 32); w.Call(0x43b0, g); g.Edx = saved;
        g.Al = 0; g.Logic(0, 8); w.Call(0xbb8e, g); w.Call(0x98db, g);
    }

    /// <summary>B5AB: the unit turns (9E41 gives a random number): the direction +25 moves by one step (0: back, 1: forth, 2, 3: unchanged apart from the cell), the position +1 = direction * DAT[4A] + DAT[48].</summary>
    public static void FB5AB(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var t = w.Template; w.Call(0x9e41, g);
        g.Ah = g.Al; g.Al = r[0x25]; g.Ah = (byte)(g.Ah & 3); g.Logic(g.Ah, 8);
        if (!g.Zf)
        {
            g.Cmp(g.Ah, 1, 8);
            if (!g.Zf) return;
            g.Al = (byte)(g.Al + 2);
        }
        g.Al = (byte)(g.Al - 1); g.Al = (byte)(g.Al & 7); r[0x25] = (byte)((r[0x25] & 0xf8) | g.Al);
        Mul8(g, g.Al, t[0x4a]); uint lo = g.Eax & 0xffff, add = U16(t, 0x48); g.Add(lo << 16, add << 16); g.Eax = (g.Eax & 0xffff0000) | ((lo + add) & 0xffff);
        r[1] = (byte)g.Eax; r[2] = (byte)(g.Eax >> 8);
    }

    /// <summary>B5D8: AL = the smallest number from 1 that no object of the chain of the owner AL has in +1B (0 when all 255 are taken); EBX = the first object.</summary>
    public static void FB5D8(IUnitActionWorld w, ActionRegs g)
    {
        var part = w.PartOf((byte)g.Eax); g.Eax = (g.Eax & 0xff) * 0xf5; uint end = U32(part, 9) + w.PoolBase, esi = U32(part, 0xe) + w.PoolBase;
        g.Al = 0; g.Ebx = esi; g.Cmp(esi, end, 32);
        if (!g.Zf)
        {
            for (; ; )
            {
                g.Al++; if (g.Al == 0) break;
                esi = g.Ebx; bool again = false;
                do
                {
                    var o = w.ObjectAt(esi);
                    if (g.Al == o[0x1b]) { again = true; break; }
                    esi += U32(o, 0x10);
                } while (esi != end);
                if (!again) break;
            }
        }
        g.Test(g.Al, g.Al, 8);
    }

    /// <summary>B616: the rectangle (CL, CH) .. (DL, DH) of the map is searched in the three planes of the cells for objects of the owner AL (+3) whose template has none of the flags C and +0F is not 0; +1B of those becomes BL.
    /// EAX = their number.</summary>
    public static void FB616(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds;
        g.Cmp(g.Cl, g.Dl, 8); if (!(g.Cf || g.Zf)) { byte x = g.Cl; g.Cl = g.Dl; g.Dl = x; }
        g.Cmp(g.Ch, g.Dh, 8); if (!(g.Cf || g.Zf)) { byte x = g.Ch; g.Ch = g.Dh; g.Dh = x; }
        g.Bh = g.Al; uint swap = g.Ecx; g.Ecx = g.Edx; g.Edx = swap;
        g.Cl = (byte)(g.Cl - g.Dl + 1); g.Ch = (byte)(g.Ch - g.Dh + 1);
        uint width = U32(d, 0x18de), height = U32(d, 0x18e2);
        g.Eax = width; Mul8(g, g.Al, g.Dh); g.Edx &= 0xff; g.Eax += g.Edx;
        uint esi = w.PlanePointer(0) + g.Eax * 4;
        g.Eax = width; Mul8(g, g.Al, (byte)height); g.Edx = g.Eax * 4;
        g.Eax = width; g.Al = (byte)(g.Al - g.Cl); uint ebp = g.Eax * 4;
        d[0x1f41] = g.Cl; Put(d, 0x1f31, 0);
        void Mark()   // B6B2
        {
            g.Eax += w.PoolBase; var o = w.ObjectAt(g.Eax); g.Cmp(o[3], g.Bh, 8);
            if (!g.Zf) return;
            g.Eax = 0x56; Mul8(g, 0x56, o[0]); g.Eax = 0xab; Mul8(g, 0xab, o[0xe]); g.Edi = w.ArmyTable(o[0]) + (g.Eax & 0xffff);
            var t = w.ObjectAt(g.Edi); g.Test(U32(t, 0x40), 0xc, 32);
            if (!g.Zf) return;
            g.Cmp(o[0xf], 0, 8);
            if (g.Zf) return;
            o[0x1b] = g.Bl; Put(d, 0x1f31, U32(d, 0x1f31) + 1);
        }
        do
        {
            g.Cl = d[0x1f41];
            do
            {
                foreach (uint plane in new[] { 2u, 1u, 0u })
                {
                    g.Eax = w.PlaneRead(esi + plane * g.Edx); g.Test(g.Eax, g.Eax, 32);
                    if (!g.Zf) Mark();
                }
                esi += 4; g.Cl--;
            } while (g.Cl != 0);
            g.Add(esi, ebp); esi += ebp; g.Ch--;
        } while (g.Ch != 0);
        g.Zf = true; g.Eax = U32(d, 0x1f31);
    }

    /// <summary>BE2E: can the participant of the unit pay the costs of template AL of its army (template +7C/+80/+84/+88 against participant +18/+1C/+20/+24)? AL = 0 when yes, else EAX = the cost that is too high.</summary>
    public static void FBE2E(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Ah = r[0]; F68A0(w, g); g.Eax = 0xf5; Mul8(g, 0xf5, r[3]); g.Ecx = 0x97a + (g.Eax & 0xffff);
        var p = w.PartOf(r[3]); var t = w.ObjectAt(g.Edi);
        foreach (var (a, b) in new[] { (0x7c, 0x18), (0x80, 0x1c), (0x84, 0x20), (0x88, 0x24) })
        {
            g.Eax = U32(t, a); g.Cmp(g.Eax, U32(p, b), 32);
            if (!g.Cf && !g.Zf) return;
        }
        g.Al = 0; g.Logic(0, 8);
    }

    /// <summary>BE6F: a random chance (9E41 AND BH = 0) to create the effect 9727 of the unit when its health fell by more than a quarter of the template health.</summary>
    public static void FBE6F(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Edi = U32(w.Ds, 0x1c49); w.Call(0x9e41, g); g.Al = (byte)(g.Al & g.Bh); g.Logic(g.Al, 8);
        if (!g.Zf) return;
        var t = w.ObjectAt(g.Edi); g.Al = t[0x45]; g.Ah = g.Al; g.Ah = (byte)g.Shr(g.Ah, 2, 8); g.Al = (byte)(g.Al - r[0x19]);
        g.Cmp(g.Al, g.Ah, 8);
        if (!g.Cf && !g.Zf) return;
        g.Al = r[0]; g.Bh = 2; g.Add(g.Ecx, U32(r, 4)); g.Ecx += U32(r, 4); g.Add(g.Edx, U32(r, 8)); g.Edx += U32(r, 8); w.Call(0x9727, g);
    }

    /// <summary>CB21: the unit ESI turns the object of its target (+1C) to the opposite side when AC63 allows it (marks it as moving, clears +14/+15, announces it with 43E5). Flags: ZF = done or no target.</summary>
    public static void FCB21(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        uint saved = g.Esi; uint target = U32(r, 0x1c); g.Esi = target; g.Test(g.Esi, g.Esi, 32);
        if (g.Zf) { g.Esi = saved; return; }
        g.Esi += w.PoolBase; var o = w.ObjectAt(g.Esi);
        g.Al = o[0xe]; g.Ah = o[0]; g.Bl = (byte)(o[0xd] & 3); g.Logic(g.Bl, 8); g.Ecx = U32(o, 4); g.Edx = U32(o, 8); w.Call(0xac63, g);
        if (!g.Zf) { g.Esi = saved; return; }
        g.Al = (byte)(o[0x25] + 4); g.Al &= 7; w.Call(0xae3a, g);
        Put16(o, 0x17, (ushort)((U16(o, 0x17) | 0x20) & 0xfd3f));
        g.Al = 0; o[0x14] = 0; o[0x15] = 0; g.Eax = g.Esi - w.PoolBase; w.Call(0x43e5, g); g.Al = 0; g.Logic(0, 8); g.Esi = saved;
    }
    private static void Put16(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }

    /// <summary>CC7B: the unit asks for a target (C4CD): with the template flag 20000h its owner gets the item +31 of the target (1243A) and the target loses +31; every 16th tick the target is marked (+17 bit 20h).</summary>
    public static void FCC7B(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        bool Skip = false;
        g.Test(U16(r, 0x17), 0x40, 16);
        if (!g.Zf)
        {
            w.Call(0xc4cd, g);
            if (!g.Zf)
            {
                g.Test(U32(w.ObjectAt(g.Edx), 0x40), 0x20000, 32);
                if (g.Zf) Skip = true;
                else
                {
                    var e = w.ObjectAt(g.Edi); Put16(e, 0x17, (ushort)(U16(e, 0x17) & 0xffbf)); g.Al = e[0x31]; g.Ah = r[3]; w.Call(0x1243a, g);
                    if (g.Zf) w.ObjectAt(g.Edi)[0x31] = 0;
                }
            }
            if (!Skip) Put16(r, 0x17, (ushort)(U16(r, 0x17) & 0xffbf));
        }
        g.Test(w.Ds[0x19ea], 0xf, 8);
        if (!g.Zf) return;
        w.Call(0xc4cd, g);
        if (g.Zf) return;
        var t = w.ObjectAt(g.Edi); Put16(t, 0x17, (ushort)(U16(t, 0x17) | 0x20));
    }

    /// <summary>CCC4: health 1, AL = 0.</summary>
    public static void FCCC4(ActionRegs g, byte[] r) { r[0x19] = 1; g.Al = 0; g.Logic(0, 8); }

    /// <summary>CB77: the unit converts the target (+22 counts the ticks: 1 at the start, 32h hands the object over (D1BA), 3Ch ends). BL = 1 when the target changed owner, 2 when it was lost, AL = FFh while it goes on.</summary>
    public static void FCB77(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        void Clear40() => Put16(r, 0x17, (ushort)(U16(r, 0x17) & 0xffbf));
        void Continue() { g.Bl = 0; g.Logic(g.Bl, 8); g.Al = 0xff; g.Logic(g.Al, 8); }       // CBAE
        void Lost() { g.Bl = 0; g.Logic(g.Bl, 8); }                                         // CBB8
        g.Cmp(r[0x22], 0, 8);
        if (g.Zf)
        {
            g.Test(U16(r, 0x17), 0x40, 16);
            if (g.Zf) { Lost(); return; }
            w.Call(0xc4cd, g);
            if (g.Zf) { Clear40(); Lost(); return; }
            g.Eax = U32(w.ObjectAt(g.Edx), 0x40); g.Test(g.Eax, 0x1000, 32);
            if (g.Zf) { Lost(); return; }
            g.Cmp(g.Edi, w.PoolBase, 32); g.Edi -= w.PoolBase; Put(r, 0x1c, g.Edi); w.Call(0xcaee, g);
            if (!g.Zf) { Clear40(); Lost(); return; }
            r[0x22]++; Clear40(); Continue(); return;
        }
        g.Cmp(r[0x19], 0, 8);
        if (g.Jle) { Handed(); return; }
        r[0x22]++; g.Cmp(r[0x22], 0x32, 8);
        if (g.Zf) { Convert(); return; }
        g.Cmp(r[0x22], 0x3c, 8);
        if (!g.Zf) { Continue(); return; }
        w.Call(0xcb21, g);
        if (g.Zf) { Finish(); return; }
        if (g.Jae) { Handed(); return; }
        r[0x22]--; var f = w.ObjectAt(g.Edi); Put16(f, 0x17, (ushort)(U16(f, 0x17) | 0x20)); Continue(); return;

        void Handed() { g.Eax = U32(r, 0x1c) + w.PoolBase; w.ObjectAt(g.Eax)[0x19] = 0; Finish(); }
        void Finish() { r[0x22] = 0; r[0x14] &= 7; r[0x14] |= 0x48; g.Bl = 2; Lost(); }
        void Convert()
        {
            Put16(r, 0x17, (ushort)(U16(r, 0x17) | 0x200)); Put16(r, 0x17, (ushort)(U16(r, 0x17) & 0xfeff));
            g.Eax = U32(r, 0x1c) + w.PoolBase; g.Al = w.ObjectAt(g.Eax)[3]; uint esi = g.Esi; w.Call(0xd1ba, g); g.Edi = g.Esi; g.Esi = esi;
            if (!g.Zf) { Continue(); return; }
            r[0x22] = 0; var n = w.ObjectAt(g.Edi); var st = w.Stats; int owner = n[3];
            st[owner * 0x1a35 + 0x17b + 3 * r[3]]++; g.Eax = (uint)(r[3] * 0x1a35); g.Ebx = 0x1a35; g.Edx = 0;
            uint at = U32(w.Ds, 0x6ba) + (uint)(r[3] * 0x1a35) + 0x1ab; g.Ebp = g.Edi - w.PoolBase; uint edi = g.Edi; g.Edi = at; g.Al = 9; g.Bl = 0; w.Call(0x1eaed, g); g.Edi = edi;
            g.Bl = 1; g.Al = 0xff; g.Logic(g.Al, 8);
        }
    }

    /// <summary>CCCB: the unit pays its upkeep (A716 gives the count, +15 = 1 once started): every tick the participant loses (10 * +23 * +22) of +20 (down to 0, then +23 = 1), the unit loses it from +24; when +24 runs out A6AE is called
    /// and the statistics value +73 of the owner falls by 271h (the original also writes +73 of the unit record itself when it is not positive).</summary>
    public static void FCCCB(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds;
        g.Cmp(r[0x15], 0, 8);
        if (g.Zf)
        {
            g.Al = r[3]; g.Ah = r[0]; w.Call(0xa716, g); g.Test(g.Al, g.Al, 8);
            if (g.Zf) return;
            g.Bl = g.Al; w.Call(0x9e41, g); Mul8(g, g.Al, g.Bl); g.Al = 0x10; Mul8(g, g.Al, g.Ah);
            g.Edi = g.Eax + 0x1f45; g.Al = d[g.Edi + 1]; r[0x28] = g.Al; g.Eax = U32(d, (int)g.Edi + 7); Put(r, 0x24, g.Eax); r[0x15]++;
        }
        g.Test(d[0x19ea], 0xff, 8);
        if (!g.Zf) return;
        g.Eax = 0xf5; Mul8(g, 0xf5, r[3]); g.Ecx = 0x97a + (g.Eax & 0xffff); var p = w.PartOf(r[3]);
        g.Eax = 0xa; Mul8(g, g.Al, r[0x23]); Mul8(g, g.Al, r[0x22]);
        uint a = U32(p, 0x20); g.Cmp(a, g.Eax, 32); Put(p, 0x20, a - g.Eax);
        if (g.Cf) { g.Eax = U32(p, 0x20); Put(p, 0x20, 0); r[0x23] = 1; }
        a = U32(r, 0x24); g.Cmp(a, g.Eax, 32); Put(r, 0x24, a - g.Eax);
        if (!g.Cf && !g.Zf) return;
        g.Al = r[0x28]; g.Ah = 0; w.Call(0xa6ae, g); r[0x15]--;
        g.Eax = 0x1a35; g.Ebx = r[3]; g.Eax *= g.Ebx; g.Edx = 0; g.Eax += U32(d, 0x6ba);
        int at = r[3] * 0x1a35 + 0x73; var st = w.Stats; uint v = BitConverter.ToUInt32(st, at); g.Cmp(v, 0x271, 32); Put(st, at, v - 0x271);
        g.Cmp(U32(r, 0x73), 0, 32);
        if (g.Jg) return;
        Put(r, 0x73, 0);
    }

    /// <summary>B319: every object of the chain of the owner of ESI whose template has flag 8 and a defined +4E, is alive and is more than 25 cells away from ESI is handed over (D1BA) to the owner [6C6] - 1.</summary>
    public static void FB319(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Eax = 0xf5; Mul8(g, 0xf5, r[3]); var part = w.PartOf(r[3]);
        uint edi = U32(part, 0xe) + w.PoolBase, ebp = U32(part, 9) + w.PoolBase;
        g.Edi = edi; g.Ebp = ebp; g.Cmp(edi, ebp, 32);
        while (!g.Zf)
        {
            g.Edi = edi; w.Call(0x68bd, g); var t = w.ObjectAt(g.Edx); var o = w.ObjectAt(edi);
            g.Test(U32(t, 0x40), 8, 32);
            if (!g.Zf)
            {
                g.Cmp(t[0x4e], 0xff, 8);
                if (!g.Zf)
                {
                    g.Al = t[0x46]; g.Cmp(o[0x19], g.Al, 8);
                    if (!g.Jle)
                    {
                        g.Eax = U32(o, 4); g.Ebx = U32(o, 8);
                        g.Cmp(g.Eax, U32(r, 4), 32); g.Eax -= U32(r, 4); if (g.Sf) g.Eax = (uint)-(int)g.Eax;
                        g.Cmp(g.Ebx, U32(r, 8), 32); g.Ebx -= U32(r, 8); if (g.Sf) g.Ebx = (uint)-(int)g.Ebx;
                        g.Cmp(g.Eax, g.Ebx, 32); if (g.Cf || g.Zf) g.Eax = g.Ebx;
                        g.Eax = g.Shr(g.Eax, 5, 32); g.Cmp(g.Al, 0x19, 8);
                        if (!g.Cf && !g.Zf)
                        {
                            uint esi = g.Esi; g.Esi = edi; g.Al = (byte)(d[0x6c6] - 1); w.Call(0xd1ba, g); g.Esi = esi; g.Edi = edi; g.Ebp = ebp;
                        }
                    }
                }
            }
            edi += U32(o, 0x10); g.Edi = edi; g.Cmp(edi, ebp, 32);
        }
    }

    /// <summary>B6F1: BA2E and 6EC1 for the object ESI ([1969] holds it meanwhile), then the objects of the chain (to EBP) whose +1B equals [1C87] clear the 6 slots (12 bytes each, from DS:1978) whose template entry (8 bytes from +0B)
    /// does not match: bit k of +0F set, byte +7 equal and non-zero keeps the slot.</summary>
    public static void FB6F1(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Al = d[0x1972]; g.Bl = d[0x1c87]; w.Call(0xba2e, g);
        uint esi = g.Esi, ebp = g.Ebp, saved = U32(d, 0x1969); Put(d, 0x1969, esi); w.Call(0x6ec1, g); Put(d, 0x1969, saved); g.Esi = esi; g.Ebp = ebp;
        for (; ; )
        {
            g.Al = d[0x1c87]; g.Esi += U32(w.ObjectAt(g.Esi), 0x10); g.Cmp(g.Esi, g.Ebp, 32);
            if (g.Zf) return;
            var o = w.ObjectAt(g.Esi); g.Cmp(o[0x1b], g.Al, 8);
            if (!g.Zf) { g.Al = d[0x1c87]; goto next; }
            F6885(w, g, o); uint tb = g.Edi; var t = w.ObjectAt(tb);
            g.Eax = 0; g.Ebx = 0x1978; g.Edi += 0xb; g.Ch = o[0xf]; g.Cl = 6;
            do
            {
                g.Ch = (byte)g.Shr(g.Ch, 1, 8);
                bool keep = false;
                if (g.Cf) { g.Al = t[g.Edi - tb + 7]; g.Cmp(g.Al, d[g.Ebx + 7], 8); if (g.Zf) { g.Test(g.Al, g.Al, 8); keep = !g.Zf; } }
                if (!keep) Put(d, (int)g.Ebx + 2, 0);
                g.Edi += 8; g.Ebx += 0xc; g.Cl--;
            } while (g.Cl != 0);
        next:;
        }
    }

    /// <summary>6EB2: clears the slot table of 6 * 12 bytes at DS:1978.</summary>
    public static void F6EB2(IUnitActionWorld w, ActionRegs g)
    {
        Array.Clear(w.Ds, 0x1978, 0x48); g.Edi = 0x19c0; g.Ecx = 0; g.Eax = 0; g.Logic(0, 32);
    }

    /// <summary>6EC1: fills the slot table at DS:1978 from the template of the object [1969]: for every bit of +0F (from bit 0) the 8-byte template entry (from +0B) gives the slot its word, the bytes +6 and +7, the pointer +2 (entry + 4 + dword)
    /// with the word behind it, and the template number (+0 of the object).</summary>
    public static void F6EC1(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Esi = U32(d, 0x1969); var o = w.ObjectAt(g.Esi); F6885(w, g, o); uint tb = g.Edi; var t = w.ObjectAt(tb);
        g.Eax = 0; g.Ebx = 0x1978; g.Edi += 0xb; g.Ch = o[0xf]; g.Cl = 6;
        do
        {
            Put(d, (int)g.Ebx + 2, 0); g.Ch = (byte)g.Shr(g.Ch, 1, 8);
            if (g.Cf)
            {
                int e = (int)(g.Edi - tb);
                g.Eax = (g.Eax & 0xffff0000) | U16(t, e); Put16(d, (int)g.Ebx, (ushort)g.Eax);
                g.Al = t[e + 6]; d[g.Ebx + 6] = g.Al; g.Al = t[e + 7]; d[g.Ebx + 7] = g.Al;
                g.Eax = g.Edi + 4 + U32(t, e + 2); Put(d, (int)g.Ebx + 2, g.Eax);
                g.Eax = U32(t, (int)(g.Eax - tb) - 2); Put16(d, (int)g.Ebx + 9, (ushort)g.Eax);
                g.Al = o[0]; d[g.Ebx + 0xb] = g.Al;
            }
            g.Edi += 8; g.Add(g.Ebx, 0xc); g.Ebx += 0xc; g.Cl--; g.Zf = g.Cl == 0;
        } while (g.Cl != 0);
    }

    /// <summary>D2D1 = FS:157: claims the rectangle (DS:273F x DS:2740 cells, centred on the unit ESI, in the plane +0D) of the occupancy planes for the value EAX: the cells equal to EBX are set to it, the other cells with an object
    /// (not equal to it) count in [1F31] and, when CL is not 0, the object is killed and gets the flags CH * 8 in +17. EAX = the count.</summary>
    public static void FD2D1(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; Put(d, 0x1f31, 0); d[0x1f41] = g.Cl; g.Ch = (byte)(g.Ch << 3); d[0x1f42] = g.Ch; g.Ebp = g.Ebx; g.Edx = g.Eax;
        g.Ecx = U32(d, 0x273f); g.Eax = U32(r, 8); g.Ch = (byte)(g.Ch >> 1); g.Eax >>= 5; g.Al = (byte)(g.Al - g.Ch); g.Ah = d[0x18de]; Mul8(g, g.Al, g.Ah);
        g.Ebx = U32(r, 4); g.Cl = (byte)(g.Cl >> 1); g.Ebx >>= 5; g.Bl = (byte)(g.Bl - g.Cl); g.Ebx += g.Eax; g.Ebx *= 4; g.Al = r[0xd]; g.Eax &= 3; g.Ebx += w.PlanePointer(g.Eax);
        g.Eax = U32(d, 0x18de); g.Al = (byte)(g.Al - d[0x273f]); g.Eax *= 4; g.Ch = d[0x2740];
        do
        {
            g.Cl = d[0x273f];
            do
            {
                uint v = w.PlaneRead(g.Ebx); g.Cmp(v, g.Ebp, 32);
                if (g.Zf) w.PlaneWrite(g.Ebx, g.Edx);
                else
                {
                    g.Cmp(v, g.Edx, 32);
                    if (!g.Zf)
                    {
                        g.Edi = v; g.Test(g.Edi, g.Edi, 32);
                        if (!g.Zf)
                        {
                            Put(d, 0x1f31, U32(d, 0x1f31) + 1); g.Cmp(d[0x1f41], 0, 8);
                            if (!g.Zf)
                            {
                                g.Edi += w.PoolBase; var o = w.ObjectAt(g.Edi); g.Cmp(o[0x19], 0, 8);
                                if (!g.Jle) { o[0x19] = 0; o[0x17] &= 0xe7; o[0x17] |= d[0x1f42]; g.Logic(o[0x17], 8); }
                            }
                        }
                    }
                }
                g.Ebx += 4; g.Cl--;
            } while (g.Cl != 0);
            g.Add(g.Ebx, g.Eax); g.Ebx += g.Eax; g.Ch--;
        } while (g.Ch != 0);
        g.Zf = true; g.Eax = U32(d, 0x1f31);
    }

    /// <summary>11DDE = FS:14B: the objects of the list at DS:1F45 (21A00 fills it) around the unit ESI: the living, visible ones that are not the unit and whose template has none of 8020h and which the owner of the unit can see (+20 bit of its owner)
    /// and whose layer is allowed ([1CA6] from the template +74). Hostile ones (the owner's bit in +22 of the unit) are counted in [1F39] and the nearest (C344; D157 = 1 limits it to 30h) is [1F31]; the others are counted in [1F3D].
    /// EDI = the nearest, EAX = the hostile count, EBX = the others.</summary>
    public static void FS14B(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; w.Call(0x21a00, g); uint esi = g.Esi; w.Call(0xd7b1, g); g.Esi = esi;
        g.Edi = U32(d, 0x1c49); d[0x1ca6] = w.ObjectAt(g.Edi)[0x74];
        g.Eax = 0; Put(d, 0x1f31, 0); Put(d, 0x1f39, 0); Put(d, 0x1f3d, 0); g.Eax--; Put(d, 0x1f35, g.Eax); g.Ebp = 0x1f45;
        for (; ; )
        {
            g.Edi = U32(d, (int)g.Ebp); g.Test(g.Edi, g.Edi, 32);
            if (g.Zf) break;
            Candidate();
            g.Ebp += 4;
        }
        g.Edi = U32(d, 0x1f31); g.Eax = U32(d, 0x1f39); g.Ebx = U32(d, 0x1f3d); g.Test(g.Edi, g.Edi, 32);

        void Candidate()
        {
            g.Cmp(g.Edi, g.Esi, 32);
            if (g.Zf) return;
            w.Call(0x68bd, g); var t = w.ObjectAt(g.Edx); var o = w.ObjectAt(g.Edi);
            g.Test(U32(t, 0x40), 0x8020, 32);
            if (!g.Zf) return;
            g.Eax = o[0x20]; g.Ebx = r[3]; g.Cf = ((g.Eax >> (int)(g.Ebx & 31)) & 1) != 0;
            if (!g.Cf) return;
            g.Al = t[0x46]; g.Cmp(o[0x19], g.Al, 8);
            if (g.Jle) return;
            g.Al = o[0xd]; g.Eax &= 3; g.Cf = ((d[0x1ca6] >> (int)g.Eax) & 1) != 0;
            if (!g.Cf) return;
            g.Eax = o[3]; g.Cf = ((r[0x22 + 4 * (int)(g.Eax >> 5) + (int)((g.Eax & 31) >> 3)] >> (int)(g.Eax & 7)) & 1) != 0;
            if (!g.Cf) { Put(d, 0x1f3d, U32(d, 0x1f3d) + 1); return; }
            Put(d, 0x1f39, U32(d, 0x1f39) + 1); w.Call(0xc344, g);
            uint saved = g.Eax; w.Call(0xd157, g); g.Cmp(g.Al, 1, 8); g.Eax = saved;
            if (g.Zf) { g.Cmp(g.Eax, 0x30, 32); if (!g.Cf) return; }
            g.Cmp(g.Eax, U32(d, 0x1f35), 32);
            if (!g.Cf && !g.Zf) return;
            Put(d, 0x1f31, g.Edi); Put(d, 0x1f35, g.Eax);
        }
    }

    /// <summary>10AD2 = FS:187: looks for a free spot (AC63) for the template AL of the army AH around the point (ECX, EDX): the point is snapped to 32 and shifted by the half size of the template (+3, +4); then the rings 1, 2, 3, ... are
    /// tried, ring k with k * k random spots (9E41, 9E55) of the 32-cells square of side k. ZF = a spot was found (ECX, EDX = it); EAX and EBX are kept.</summary>
    public static void FS187(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint a0 = g.Eax, b0 = g.Ebx;
        F68A0(w, g); var t = w.ObjectAt(g.Edi);
        g.Al = (byte)(t[3] << 4); g.Eax = (uint)(g.Al & 0x10); g.Ecx = (g.Ecx & 0xffffffe0u) + g.Eax;
        g.Al = (byte)(t[4] << 4); g.Eax = (uint)(g.Al & 0x10); g.Edx = (g.Edx & 0xffffffe0u) + g.Eax;
        uint ecx0 = g.Ecx, edx0 = g.Edx; g.Bh = 1;
        for (; ; )
        {
            g.Eax = 0; g.Al = g.Bh; Mul8(g, g.Al, g.Bh); g.Test(g.Ah, g.Ah, 8);
            if (!g.Zf) break;
            g.Bl = g.Al;
            for (; ; )
            {
                uint pushed = g.Ebx;
                w.Call(0x9e41, g); Mul8(g, g.Al, g.Bh); g.Al = g.Bh; g.Al = (byte)g.Shr(g.Al, 1, 8); g.Cmp(g.Ah, g.Al, 8); g.Ah = (byte)(g.Ah - g.Al);
                g.Edx = (uint)(int)(sbyte)g.Ah << 5; g.Edx += edx0;
                w.Call(0x9e55, g); Mul8(g, g.Al, g.Bh); g.Bh = (byte)g.Shr(g.Bh, 1, 8); g.Cmp(g.Ah, g.Bh, 8); g.Ah = (byte)(g.Ah - g.Bh);
                g.Ecx = (uint)(int)(sbyte)g.Ah << 5; g.Ecx += ecx0;
                g.Cmp(g.Ecx, 0x20, 32);
                if (!g.Cf)
                {
                    g.Eax = U32(d, 0x18e6) - 0x20; g.Cmp(g.Ecx, g.Eax, 32);
                    if (g.Cf)
                    {
                        g.Cmp(g.Edx, 0x20, 32);
                        if (!g.Cf)
                        {
                            g.Eax = U32(d, 0x18ea) - 0x20; g.Cmp(g.Edx, g.Eax, 32);
                            if (g.Cf) { g.Ebx = b0; g.Eax = a0; uint sc = g.Ecx, sd = g.Edx; w.Call(0xac63, g); g.Edx = sd; g.Ecx = sc; }
                        }
                    }
                }
                g.Ebx = pushed;
                if (g.Zf) goto done;
                g.Bl--; g.Zf = g.Bl == 0; g.Sf = (g.Bl & 0x80) != 0;
                if (g.Bl != 0) continue;
                break;
            }
            g.Bh++;
        }
    done:
        g.Eax = a0; g.Ebx = b0;
    }

    /// <summary>9064 = FS:16B: looks for a free spot (AC63) for the template AL of the army AH next to the object ESI: the 8 directions (the table of signed bytes at DS:19C7) are tried from a random one (9E41), the distance is the
    /// sum of the half sizes of both templates. ZF = 0 and BH = the direction when found (ECX, EDX = the spot); ZF = 1 when none fits. EAX is kept.</summary>
    public static void FS16B(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; uint a0 = g.Eax; F68A0(w, g); var t1 = w.ObjectAt(g.Edi); uint first = g.Edi;
        g.Bl = (byte)(t1[0x77] & 3); g.Logic(g.Bl, 8); uint s1 = g.Ebx; g.Edx = first;
        F6885(w, g, r); var t2 = w.ObjectAt(g.Edi);
        g.Ah = (byte)((t2[4] + t1[4]) << 4); g.Al = (byte)((t2[3] + t1[3]) << 4); uint s2 = g.Eax;
        w.Call(0x9e41, g); g.Al = (byte)(g.Al & 7); g.Bl = g.Al; g.Bh = g.Al;
        for (; ; )
        {
            uint s3 = g.Ebx; int idx = (int)(g.Ebx & 7); g.Ebx = (uint)idx;
            g.Al = (byte)(s2 >> 8); short p = (short)((sbyte)g.Al * (sbyte)d[0x19c8 + idx * 2]); g.Eax = (g.Eax & 0xffff0000) | (ushort)p;
            g.Edx = U32(r, 8); uint lo = g.Edx & 0xffff; g.Add(lo << 16, (g.Eax & 0xffff) << 16); g.Edx = (g.Edx & 0xffff0000) | ((lo + g.Eax) & 0xffff);
            g.Al = (byte)s2; p = (short)((sbyte)g.Al * (sbyte)d[0x19c7 + idx * 2]); g.Eax = (g.Eax & 0xffff0000) | (ushort)p;
            g.Ecx = U32(r, 4); lo = g.Ecx & 0xffff; g.Add(lo << 16, (g.Eax & 0xffff) << 16); g.Ecx = (g.Ecx & 0xffff0000) | ((lo + g.Eax) & 0xffff);
            g.Ebx = s1; g.Eax = a0; uint se = g.Esi, sc = g.Ecx, sd = g.Edx; w.Call(0xac63, g); g.Edx = sd; g.Ecx = sc; g.Esi = se; g.Ebx = s3;
            if (g.Zf)
            {
                g.Al = 0xff; g.Logic(g.Al, 8); g.Al = g.Bl; g.Ebx = s1; g.Bh = g.Al; g.Eax = a0; return;
            }
            g.Bl--; g.Bl = (byte)(g.Bl & 7); g.Logic(g.Bl, 8); g.Cmp(g.Bl, g.Bh, 8);
            if (g.Zf) break;
        }
        g.Ebx = s1; g.Eax = a0;
    }

    /// <summary>11CC9 = FS:18B: counts the campaign areas (records of 841h bytes from [3E87], [6C9] of them, those with +0 bit 0) whose [6C6] owner entries (84h bytes from +1) are all settled for the player AL: the area has a high enough
    /// value (+17 above 7CFh), no flag +59, the entry is not the player, and the relation +1 + AL is neither 3 nor 4 -- an entry that fails the relation test excludes the area. AL = the count, ESI/EDI/EDX/ECX are kept.</summary>
    public static void FS18B(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint esi0 = g.Esi, edi0 = g.Edi, edx0 = g.Edx, ecx0 = g.Ecx;
        g.Eax = g.Eax & 0xff; g.Bl = 0; g.Logic(0, 8); uint esi = U32(d, 0x3e87); byte outer = d[0x6c9]; int al = (int)g.Eax;
        do
        {
            uint areaStart = esi;
            if ((d[esi] & 1) != 0)
            {
                esi += 1; byte inner = d[0x6c6]; bool all = true;
                do
                {
                    bool ok = U32(d, (int)esi + 0x17) <= 0x7cf || d[esi + 0x59] != 0 || d[esi] == al || d[esi + (uint)al + 1] == 3;
                    if (!ok && d[esi + (uint)al + 1] != 4) { all = false; break; }
                    esi += 0x84; inner--;
                } while (inner != 0);
                if (all) g.Bl++;
            }
            esi = areaStart + 0x841; outer--;
        } while (outer != 0);
        g.Al = g.Bl; g.Eax &= 0xff; g.Esi = esi0; g.Edi = edi0; g.Edx = edx0; g.Ecx = ecx0; g.Cf = false; g.Zf = true;
    }
}
