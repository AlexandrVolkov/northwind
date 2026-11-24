using System;

namespace Northwind;

/// <summary>The registers and the flags of the unit action handlers (LE:1 8227..9210). The flag helpers follow the x86 semantics, the jumps of the handlers test them as the original does.</summary>
public sealed class ActionRegs
{
    public uint Eax, Ebx, Ecx, Edx, Edi;
    public bool Zf, Cf, Sf, Of;
    public byte Al { get => (byte)Eax; set => Eax = (Eax & 0xffffff00) | value; }
    public byte Ah { get => (byte)(Eax >> 8); set => Eax = (Eax & 0xffff00ff) | ((uint)value << 8); }
    public byte Bl { get => (byte)Ebx; set => Ebx = (Ebx & 0xffffff00) | value; }
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
    uint PoolBase { get; }
    uint G512 { get; set; }
    uint G51A { get; }
    uint G51E { get; }
    byte G1C9F { get; set; }
    byte G522 { get; }
    uint G50E { get; set; }
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
}
