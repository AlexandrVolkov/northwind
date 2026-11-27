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
    /// <summary>OUT to a port (the palette registers 3C8h/3C9h): the world records the byte.</summary>
    void Out(ushort port, byte value);
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
    /// <summary>FS:10: the counter of the object cycles (42BB).</summary>
    byte Fs10 { get; set; }
    /// <summary>fs:[17F] (the global tick copy).</summary>
    uint Fs17F { get; set; }
    byte G525 { get; } byte G526 { get; }
    byte G1C61 { get; set; }
    uint G2730 { get; }
    /// <summary>The route bytes ([6B6] points to them) and the first dword of the descriptor of a map tile.</summary>
    byte[] Route { get; }
    uint TileDescriptor(uint tile);
    /// <summary>The first bytes of the object LE:3 (the cursor state: [0] mode, [5] object under it, [9] its template, [D] kind, [12] click state, [13] button flags).</summary>
    byte[] L3 { get; }
    /// <summary>The call through the handler table of LE:3 (+14h, one dword per mode): the world runs the handler of the mode.</summary>
    void CallHandler(uint mode, ActionRegs g);
    /// <summary>A byte of flat memory outside the objects (the text buffers and the font of the interface routines).</summary>
    byte Peek(uint address);
    void Poke(uint address, byte value);
    /// <summary>The dword at <paramref name="offset"/> of the 8-byte descriptor of the tile of the map cell (the cell index, row by row).</summary>
    uint TileProperty(uint cell, int offset);
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

    // ---- the hidden trap script (DB47..DEC7): the footprint list, the strength nearby, the construction automaton, the check of the place ----
    private static void Flags8(ActionRegs g, uint result, bool carry) { g.Zf = (result & 0xff) == 0; g.Sf = (result & 0x80) != 0; g.Cf = carry; g.Of = false; }
    private static byte Add8(ActionRegs g, byte a, byte b) { uint r = (uint)a + b; g.Zf = (r & 0xff) == 0; g.Sf = (r & 0x80) != 0; g.Cf = r > 0xff; g.Of = ((a ^ r) & (b ^ r) & 0x80) != 0; return (byte)r; }
    private static byte Sub8(ActionRegs g, byte a, byte b) { g.Cmp(a, b, 8); return (byte)(a - b); }
    private static ushort Add16(ActionRegs g, ushort a, ushort b) { uint r = (uint)a + b; g.Zf = (r & 0xffff) == 0; g.Sf = (r & 0x8000) != 0; g.Cf = r > 0xffff; g.Of = ((a ^ r) & (b ^ r) & 0x8000) != 0; return (ushort)r; }
    private static byte Dec8(ActionRegs g, byte a) { byte r = (byte)(a - 1); g.Zf = r == 0; g.Sf = (r & 0x80) != 0; g.Of = a == 0x80; return r; }   // CF is kept
    /// <summary>DIV r/m8: AL = AX / divisor, AH = the remainder; the flags stay (the emulator leaves them); a zero divisor or a quotient above FFh faults in the original.</summary>
    private static void Div8(ActionRegs g, byte divisor)
    {
        uint ax = g.Eax & 0xffff; if (divisor == 0 || ax / divisor > 0xff) throw new OverflowException("DIV r/m8 faults");
        g.Eax = (g.Eax & 0xffff0000) | ((ax % divisor) << 8) | (ax / divisor);
    }

    /// <summary>4409: EBX = the address on the plane (+0D &amp; 3) of the corner of the rectangle (DS:273F x DS:2740 cells) centred on the object ESI.</summary>
    public static void F4409(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Ecx = U32(d, 0x273f); g.Eax = U32(r, 8); g.Ch = (byte)(g.Ch >> 1); g.Eax >>= 5; g.Al = (byte)(g.Al - g.Ch); g.Ah = d[0x18de]; Mul8(g, g.Al, g.Ah);
        g.Ebx = U32(r, 4); g.Cl = (byte)(g.Cl >> 1); g.Ebx >>= 5; g.Bl = (byte)(g.Bl - g.Cl); g.Ebx += g.Eax; g.Ebx *= 4; g.Al = r[0xd]; g.Eax &= 3;
        uint plane = w.PlanePointer(g.Eax); g.Add(g.Ebx, plane); g.Ebx += plane;
    }

    /// <summary>DB47: the list DS:1F45 of the distinct objects whose references lie in the rectangle of the object ESI on the plane above its own (+0D + 1); EAX = their number, ZF = none.</summary>
    public static void FDB47(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; r[0xd]++; F4409(w, g, r); r[0xd]--;
        g.Eax = U32(d, 0x18de); g.Al = (byte)(g.Al - d[0x273f]); g.Ebp = g.Eax * 4; g.Eax = 0; Put(d, 0x1f45, 0); g.Ch = d[0x2740];
        do
        {
            g.Cl = d[0x273f];
            do
            {
                g.Edi = w.PlaneRead(g.Ebx);
                if (g.Edi != 0)
                {
                    g.Edi += w.PoolBase; g.Edx = 0x1f41;
                    for (; ; )
                    {
                        g.Edx += 4; uint v = U32(d, (int)g.Edx);
                        if (v == g.Edi) break;
                        if (v == 0) { Put(d, (int)g.Edx, g.Edi); Put(d, (int)g.Edx + 4, 0); g.Eax++; break; }
                    }
                }
                g.Ebx += 4; g.Cl--;
            } while (g.Cl != 0);
            g.Ebx += g.Ebp; g.Ch--;
        } while (g.Ch != 0);
        g.Test(g.Eax, g.Eax, 32);
    }

    /// <summary>DBAE: the objects of the list (DB47) that lie within the template's half sizes (+3, +4 · 16 − 10) of the object ESI: EAX = the sum of their templates' +3F, ECX = the number of the others, EDX = the hostile ones (the mask +12 of the owner's participant).</summary>
    public static void FDBAE(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Eax = 0; Put(d, 0x1f31, 0); Put(d, 0x1f35, 0); Put(d, 0x1f39, 0);
        FDB47(w, g, r);
        if (!g.Zf)
        {
            g.Eax = 0xf5; Mul8(g, g.Al, r[3]); Put(d, 0x1ca2, U32(w.PartOf(r[3]), 0x12));
            F6885(w, g, r); var t = w.ObjectAt(g.Edi);
            g.Ebx = 0; g.Bl = t[3]; g.Bl = (byte)(g.Bl << 4); g.Bl = (byte)(g.Bl - 0x0a);
            g.Eax = U32(r, 4) - g.Ebx; Put(d, 0x1c76, g.Eax); g.Eax += g.Ebx * 2; Put(d, 0x1c6e, g.Eax);
            g.Bl = t[4]; g.Bl = (byte)(g.Bl << 4); g.Bl = (byte)(g.Bl - 0x0a);
            g.Eax = U32(r, 8) - g.Ebx; Put(d, 0x1c7a, g.Eax); g.Eax += g.Ebx * 2; Put(d, 0x1c72, g.Eax);
            g.Ebp = 0x1f45;
            do
            {
                g.Edi = U32(d, (int)g.Ebp); var o = w.ObjectAt(g.Edi);
                if (U32(o, 4) >= U32(d, 0x1c76) && U32(o, 4) <= U32(d, 0x1c6e) && U32(o, 8) >= U32(d, 0x1c7a) && U32(o, 8) <= U32(d, 0x1c72))
                {
                    F68BD(w, g); var tt = w.ObjectAt(g.Edx);
                    g.Eax = tt[0x3f]; Put(d, 0x1f31, U32(d, 0x1f31) + g.Eax);
                    g.Eax = o[3]; bool hostile = ((d[0x1ca2 + 4 * (int)(g.Eax >> 5) + (int)((g.Eax & 31) >> 3)] >> (int)(g.Eax & 7)) & 1) != 0;
                    if (hostile) { Put(d, 0x1f39, U32(d, 0x1f39) + 1); Put(d, 0x1f35, U32(d, 0x1f35) - 1); }
                    Put(d, 0x1f35, U32(d, 0x1f35) + 1);
                }
                g.Ebp += 4;
            } while (U32(d, (int)g.Ebp) != 0);
            g.Cmp(0, 0, 32);   // the last cmp dword [ebp], 0
        }
        g.Eax = U32(d, 0x1f31); g.Ecx = U32(d, 0x1f35); g.Edx = U32(d, 0x1f39);
    }

    private static void Frame(ActionRegs g, byte[] r, byte[] t, bool afterAdd)
    {
        // add ax, word [ebp + 48]; mov word [esi + 1], ax
        ushort sum = Add16(g, (ushort)g.Eax, U16(t, 0x48)); g.Eax = (g.Eax & 0xffff0000) | sum; PutW(r, 1, sum);
    }

    /// <summary>DC91: the script of a hidden trap (OBE[20] hides it until it springs): +15 the phase (0 the health rises to the template's, 1 armed and hidden: it springs when the hostile units near it outweigh the others, 2.. growing with the stages in the high nibble, 3 the last frames, 4 done), +22 the counter, +1 the frame, +19 the health.</summary>
    public static void FDC91(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Eax = 0; r[0xf] = 0; r[0x22]++; g.Ebp = U32(d, 0x1c49); var t = w.ObjectAt(g.Ebp);
        if (r[0x15] == 0)
        {
            g.Cmp(r[0x22], 0, 8);
            if (r[0x22] == 0) { r[0x15] = 4; return; }
            g.Al = r[0x19]; g.Cmp(g.Al, r[0x1a], 8); if (g.Zf) return;
            g.Bl = g.Al; Mul8(g, g.Al, t[0x4a]); Div8(g, t[0x45]); g.Ah = 0; g.Logic(0, 8); r[0x22] = g.Ah;
            Frame(g, r, t, false);
            g.Cmp(g.Bl, t[0x45], 8); if (g.Cf) return;
            r[0x15]++; g.Cl = r[3]; g.Al = 1; g.Al = (byte)(g.Al << (g.Cl & 31)); r[0x20] = g.Al;
        }
        g.Cmp(r[0x15], 1, 8);
        if (r[0x15] == 1)
        {
            g.Test(r[0x22], 3, 8); if (!g.Zf) return;
            FDBAE(w, g, r);
            g.Cmp(g.Edx, g.Ecx, 32); if (g.Edx <= g.Ecx) return;
            g.Ebp = U32(d, 0x1c49); t = w.ObjectAt(g.Ebp); g.Ebx = t[0x3f]; g.Cmp(g.Eax, g.Ebx, 32); if (g.Cf) return;
            r[0x20] = 0xff; r[0x15]++; g.Eax = 0; g.Logic(0, 32); r[0x22] = g.Al;
            g.Al = t[0x4a]; g.Al = Add8(g, g.Al, g.Al); g.Al = Add8(g, g.Al, t[0x4b]); Frame(g, r, t, true);
        }
        else
        {
            g.Al = (byte)(r[0x15] & 7); g.Logic(g.Al, 8); g.Cmp(g.Al, 2, 8);
            if (g.Al == 2)
            {
                g.Al = t[0x4c]; g.Cmp(r[0x22], g.Al, 8); if (g.Cf) return;
                PutW(r, 1, (ushort)(U16(r, 1) + 1)); r[0x15] = Add8(g, r[0x15], 0x10); r[0x22] = 0;
            }
            else { Growth(w, g, r, t); return; }
        }
        // dd5d
        g.Al = r[0x15]; g.Al = (byte)g.Shr(g.Al, 4, 8); g.Cmp(g.Al, t[0x52], 8); if (g.Cf) return;
        uint pushed = g.Eax;
        if (g.Zf) { g.Eax = U32(t, 0xa3); w.Call(0xa416, g); }
        g.Eax = g.Esi - w.PoolBase; g.Ebx = 0; g.Cl = 0xff; g.Ch = 2; r[0xd]++; w.Call(0xd2d1, g); r[0xd]--;
        g.Ebx = pushed; g.Ebp = U32(d, 0x1c49); t = w.ObjectAt(g.Ebp);
        g.Cmp(g.Bl, t[0x51], 8); if (g.Cf) return;
        PutW(r, 1, (ushort)(U16(r, 1) - 1)); r[0x15] -= 0x10; g.Test(g.Eax, g.Eax, 32); if (!g.Zf) return;
        r[0x22] = 0xff; r[0x15] = 3; g.Al = t[0x4a]; g.Al = Add8(g, g.Al, t[0x4b]); Frame(g, r, t, true);
        Growth(w, g, r, t);
    }

    private static void Growth(IUnitActionWorld w, ActionRegs g, byte[] r, byte[] t)
    {
        g.Cmp(r[0x22], 0x64, 8); if (g.Cf) return;
        r[0x22] = 0; r[0x19]--; g.Al = t[0x45]; g.Al--; g.Al = Sub8(g, g.Al, r[0x19]); Mul8(g, g.Al, t[0x4a]); Div8(g, t[0x45]); g.Ah = 0; g.Logic(0, 8);
        g.Bl = Sub8(g, t[0x4a], g.Al); g.Al = Add8(g, g.Al, t[0x4a]); g.Al = Add8(g, g.Al, t[0x4b]); Frame(g, r, t, true);
        g.Bl = Dec8(g, g.Bl); if (!g.Zf) return;
        g.Cmp(r[0x15], 3, 8); if (!g.Zf) return;
        r[0x15]++; r[0xd]++; g.Eax = 0; w.Call(0x43b0, g); r[0xd] = Dec8(g, r[0xd]);
    }

    /// <summary>DE0C: is the place free (AL = FFh free, 0 not): the rectangle BL x BH cells centred on the pixel (ECX, EDX) lies inside the map, the planes 0..2 are empty on it and the property dword at +2 of the tile descriptor of every
    /// cell has equal bytes 0 and 1 and the same after rotating AL by 4. The row step advances the planes by twice the row gap (EBP * 2), as the original does.</summary>
    public static void FDE0C(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds;
        g.Eax = g.Ebx; g.Al = (byte)(g.Al >> 1); g.Ecx >>= 5; g.Cl = (byte)(g.Cl - g.Al); g.Ah = (byte)(g.Ah >> 1); g.Edx >>= 5; g.Dl = (byte)(g.Dl - g.Ah); g.Dh = g.Dl; g.Dl = g.Cl;
        g.Al = d[0x18de]; g.Al = Sub8(g, g.Al, g.Bl); g.Cmp(g.Dl, g.Al, 8); if (!g.Cf && !g.Zf) goto none;
        g.Ah = d[0x18e2]; g.Ah = Sub8(g, g.Ah, g.Bh); g.Cmp(g.Dh, g.Ah, 8); if (!g.Cf && !g.Zf) goto none;
        g.Edi = g.Dl; g.Eax = U32(d, 0x18de); Mul8(g, g.Al, g.Dh); g.Edi += g.Eax; g.Edi *= 4; g.Esi = g.Edi; g.Edi += U32(d, 0x18ee); g.Esi += w.PlanePointer(0);
        g.Eax = U32(d, 0x18de); Mul8(g, g.Al, d[0x18e2]); g.Edx = g.Eax * 4;
        g.Eax = U32(d, 0x18de); g.Al = Sub8(g, g.Al, g.Bl); g.Ebp = g.Eax * 4;
        do
        {
            g.Cl = g.Bl;
            do
            {
                g.Eax = 0; g.Logic(0, 32);
                g.Cmp(w.PlaneRead(g.Esi), g.Eax, 32); if (!g.Zf) goto none;
                g.Cmp(w.PlaneRead(g.Esi + g.Edx), g.Eax, 32); if (!g.Zf) goto none;
                g.Cmp(w.PlaneRead(g.Esi + g.Edx * 2), g.Eax, 32); if (!g.Zf) goto none;
                uint cell = (g.Edi - U32(d, 0x18ee)) / 4;
                g.Eax = w.TileProperty(cell, 2);
                g.Cmp(g.Al, g.Ah, 8); if (!g.Zf) goto none;
                g.Al = (byte)((g.Al << 4) | (g.Al >> 4)); g.Cmp(g.Al, g.Ah, 8); if (!g.Zf) goto none;
                g.Edi += 4; g.Esi += 4; g.Cl = Dec8(g, g.Cl);
            } while (g.Cl != 0);
            g.Add(g.Edi, g.Ebp); g.Edi += g.Ebp; g.Esi += g.Ebp * 2; g.Bh = Dec8(g, g.Bh);
        } while (g.Bh != 0);
        g.Al = 0xff; g.Logic(0xff, 8); return;
    none:
        g.Al = 0; g.Logic(0, 8);
    }

    // ---- the text helpers 7012, 704F, 7069 (interface) ----
    /// <summary>7012: the width of the string ESI in pixels: EBX = the sum over the characters of the glyph width (+1 of the glyph) + 1; the string ends with FFh or 0Dh, «~» skips the next character, characters from B0h are shifted down by 30h; the font table at DS:19EE.</summary>
    public static void F7012(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint esi = g.Esi, sEsi = g.Esi, sEdi = g.Edi, sEcx = g.Ecx, sEdx = g.Edx; g.Edx = U32(d, 0x19ee); g.Ebx = 0;
        for (; ; )
        {
            g.Al = w.Peek(esi); g.Cmp(g.Al, 0xff, 8); if (g.Zf) break;
            g.Cmp(g.Al, 0x0d, 8); if (g.Zf) break;
            esi += 2; g.Cmp(g.Al, 0x7e, 8); if (g.Zf) continue;
            esi--; g.Cmp(g.Al, 0xb0, 8); if (!g.Cf) g.Al = (byte)(g.Al - 0x30);
            g.Edi = g.Al; uint a = g.Edx + g.Edi * 2; g.Edi = (uint)(w.Peek(a) | w.Peek(a + 1) << 8); g.Edi += g.Edx;
            byte width = w.Peek(g.Edi + 1); uint sum = (uint)g.Bl + width; g.Bl = (byte)sum; g.Bh = (byte)(g.Bh + (sum > 0xff ? 1 : 0)); g.Add(g.Ebx, 1); g.Ebx += 1;
        }
        g.Esi = sEsi; g.Edi = sEdi; g.Ecx = sEcx; g.Edx = sEdx;   // the pops
    }

    /// <summary>704F: AX = (the number of the lines of the string ESI, 0Dh breaks them, FFh ends it) * 12h.</summary>
    public static void F704F(IUnitActionWorld w, ActionRegs g)
    {
        uint esi = g.Esi; g.Eax = 0;
        do { g.Ah = w.Peek(esi); if (g.Ah == 0x0d) g.Al++; esi++; } while (g.Ah != 0xff);
        g.Al++; g.Ah = 0x12; Mul8(g, g.Al, g.Ah);
    }

    /// <summary>7069: copies the string ESI to EDI up to and including FFh; «%nK» (K = 1..4) is replaced by the name (DS:0A1E + 0F5h · owner, FFh-ended) of the owner in the low byte of EAX, EBX, ECX or EDI as the caller had it. ESI = the old EDI at the end, EDI = behind the copy.</summary>
    public static void F7069(IUnitActionWorld w, ActionRegs g)
    {
        uint esi = g.Esi, edi = g.Edi; uint[] saved = [g.Eax, g.Ebx, g.Ecx, g.Edi]; uint start = g.Edi;
        for (; ; )
        {
            g.Al = w.Peek(esi); w.Poke(edi, g.Al); g.Cmp(g.Al, 0xff, 8); if (g.Zf) break;
            g.Cmp(g.Al, 0x25, 8);
            if (g.Al == 0x25) { g.Cmp(w.Peek(esi + 1), 0x6e, 8); if (g.Zf)
                {
                    g.Eax = w.Peek(esi + 2); g.Al = (byte)(g.Al - 0x31); g.Ebx = saved[g.Eax];
                    g.Eax = 0xf5; Mul8(g, g.Al, g.Bl); var part = w.PartOf(g.Bl); int at = 0xa4;
                    do { g.Al = part[at]; w.Poke(edi, g.Al); at++; edi++; g.Cmp(g.Al, 0xff, 8); } while (!g.Zf);
                    edi -= 2; esi += 2;
                } }
            esi++; edi++;
        }
        g.Esi = start; g.Edi = edi; g.Eax = saved[0]; g.Ebx = saved[1]; g.Ecx = saved[2];
    }

    // ---- the game tick (9BF9, 9C57) ----
    /// <summary>9BF9: the tick counters: [19E8]++, and while the game runs ([1C4E]) [19E9]++, the global tick [1CA9]++ (copied to fs:[17F]) with 9E7D; then the state of the commands is reset ([1C61] = 5, [1C37] = 0) and C06E, C0A4 (three texts) run.</summary>
    public static void F9BF9(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; d[0x19e8]++;
        g.Cmp(d[0x1c4e], 0, 8);
        if (!g.Zf)
        {
            d[0x19e9]++; Put(d, 0x1ca9, U32(d, 0x1ca9) + 1); g.Eax = U32(d, 0x1ca9); w.Fs17F = g.Eax; w.Call(0x9e7d, g);
        }
        d[0x1c61] = 5; PutW(d, 0x1c37, 0); w.Call(0xc06e, g);
        g.Esi = 0x54c; w.Call(0xc0a4, g); g.Esi = 0x5b0; w.Call(0xc0a4, g); g.Esi = 0x745; w.Call(0xc0a4, g);
    }

    /// <summary>9C57: the per-tick events of the game loop: the countdown [1CAD] of the message rotation (list [1CB4], table [1CCB + 16h · id]), the key 1D001Fh of the pause toggle, and the actions of the command code [1C35] (44h, 58h, 0Fh, 3Bh..3Dh); every 1000h ticks FEC6.</summary>
    public static void F9C57(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Ecx = (g.Ecx & 0xffff0000) | U16(d, 0x1c35);
        g.Cmp(g.Cl, 2, 8); g.Cl = (byte)(g.Cl - 2);
        if (!g.Cf)
        {
            g.Cmp(g.Cl, 7, 8);
            if (!(g.Cl > 7)) { g.Al = 1; int count = g.Cl & 31; g.Al = (byte)(g.Al << count); if (count != 0) { g.Cf = false; g.Zf = g.Al == 0; g.Sf = (g.Al & 0x80) != 0; } }
        }
        g.Cmp(d[0x1c4e], 0, 8);
        if (!g.Zf)
        {
            g.Cmp(U32(d, 0x1cad), 0xffffffff, 32);
            if (!g.Zf)
            {
                g.Cmp(U32(d, 0x1cad), 0x400, 32);
                if (g.Zf) { g.Esi = 0x490; w.Call(0xdee4, g); }
                uint count = U32(d, 0x1cad) - 1; Put(d, 0x1cad, count); g.Zf = count == 0; g.Sf = (count & 0x80000000) != 0;
                if (g.Zf)
                {
                    g.Ebx = 0x1cb4; g.Al = d[0x1972];
                    do { g.Ebx++; g.Cmp(g.Al, d[(int)g.Ebx], 8); } while (!g.Zf);
                    for (; ; )
                    {
                        g.Cl = d[(int)g.Ebx + 1]; g.Cmp(g.Cl, 0xff, 8);
                        if (g.Zf) { g.Ebx = 0x1cb4; g.Cl = d[0x1cb5]; }
                        g.Ebx++; g.Eax = 0x16; Mul8(g, g.Al, g.Cl); g.Cmp(d[(int)g.Eax + 0x1ccb], 0xff, 8);
                        if (!g.Zf) break;
                    }
                    d[0x1972] = g.Cl; g.Eax = U32(d, 0x1cb1); Put(d, 0x1cad, g.Eax); d[0x1ec6] = 4;
                }
            }
        }
        w.Call(0xaab3, g); g.Cmp(d[0x9090], 0, 8);
        if (!g.Zf) { g.Cmp(g.Eax, 0x1d001f, 32); if (g.Zf) { d[0x9091] ^= 1; g.Logic(d[0x9091], 8); } }
        g.Cmp(U16(d, 0x1c35), 0x44, 8 * 2);
        if (g.Zf) { w.Call(0xc7ab, g); w.Call(0xfec6, g); }
        g.Cmp(U16(d, 0x1c35), 0x58, 16);
        if (g.Zf) { w.Call(0x151e3, g); PutW(d, 0x1c35, 0); }
        g.Test(d[0x19e9], 0xff, 8); g.Test(U32(d, 0x1ca9), 0xfff, 32);
        if (g.Zf) w.Call(0xfec6, g);
        g.Cmp(U16(d, 0x1c35), 0x0f, 16); if (g.Zf) w.Call(0x9d87, g);
        g.Cmp(U16(d, 0x1c35), 0x3b, 16); if (g.Zf) w.Call(0x68d8, g);
        g.Cmp(U16(d, 0x1c35), 0x3c, 16); if (g.Zf) w.Call(0x10005, g);
        g.Cmp(U16(d, 0x1c35), 0x3d, 16); if (g.Zf) w.Call(0x10032, g);
    }

    // ---- view and effects around the units (A320..A4C1) ----
    /// <summary>A320: puts the view corner [1906]/[190A] (cells) at the cell (EAX, EBX) minus 7, clamped to the map minus 14; when it moves the pixel corner [190E]/[1912] follows and the cache DS:33C.. (C4h dwords) is invalidated with FFFFFFFFh.</summary>
    public static void FA320(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds;
        g.Cmp(g.Eax, 7, 32); g.Eax -= 7; if (g.Cf) { g.Eax = 0; g.Logic(0, 32); }
        g.Ecx = U32(d, 0x18de); g.Cmp(g.Ecx, 0xe, 32); g.Ecx -= 0xe; g.Cmp(g.Eax, g.Ecx, 32); if (!g.Cf && !g.Zf) g.Eax = g.Ecx;
        g.Cmp(g.Ebx, 7, 32); g.Ebx -= 7; if (g.Cf) { g.Ebx = 0; g.Logic(0, 32); }
        g.Edx = U32(d, 0x18e2); g.Cmp(g.Edx, 0xe, 32); g.Edx -= 0xe; g.Cmp(g.Ebx, g.Edx, 32); if (!g.Cf && !g.Zf) g.Ebx = g.Edx;
        g.Cmp(U32(d, 0x1906), g.Eax, 32);
        if (g.Zf) { g.Cmp(U32(d, 0x190a), g.Ebx, 32); if (g.Zf) return; }
        Put(d, 0x1906, g.Eax); g.Eax <<= 5; Put(d, 0x190e, g.Eax); Put(d, 0x190a, g.Ebx);
        { uint before = g.Ebx; g.Ebx <<= 5; g.Cf = ((before >> 27) & 1) != 0; g.Zf = g.Ebx == 0; g.Sf = (g.Ebx & 0x80000000) != 0; g.Of = false; }   // SHL EBX, 5 sets the flags that stay
        Put(d, 0x1912, g.Ebx);
        g.Eax = 0xffffffff; g.Edi = 0x33c; g.Ecx = 0xc4;
        for (int k = 0; k < 0xc4; k++) Put(d, 0x33c + 4 * k, 0xffffffff);
        g.Edi += 0x310; g.Ecx = 0;
    }

    /// <summary>A416: the effect EAX (0 = none) at the cell of the object ESI: 1416B with ESI = the army's effect table entry ([6DB + 56h · army]), ECX, EDX = the cell, EBX = 100h.</summary>
    public static void FA416(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Test(g.Eax, g.Eax, 32); if (g.Zf) return;
        var d = w.Ds; uint edi = g.Edi, esi = g.Esi, eax = g.Eax;
        g.Eax = 0x56; Mul8(g, g.Al, r[0]); g.Ecx = g.Shr(U32(r, 4), 5, 32); g.Edx = g.Shr(U32(r, 8), 5, 32); g.Esi = U32(d, (int)(g.Eax & 0xffff) + 0x6db);
        g.Eax = eax; g.Ebx = 0x100; w.Call(0x1416b, g); g.Esi = esi; g.Edi = edi;
    }

    /// <summary>A444: a wounded object (health below 3/4 of the template's +45, signed) may raise the effect ECX: when (ESI − [1C49] + [19EA]) has no bit of BL and a random byte has none of BH.</summary>
    public static void FA444(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Ecx = g.Eax; g.Edi = U32(d, 0x1c49); var t = w.ObjectAt(g.Edi);
        g.Al = t[0x45]; g.Ah = g.Al; g.Ah = (byte)(g.Ah >> 2); g.Al = (byte)(g.Al - g.Ah);
        g.Cmp(r[0x19], g.Al, 8); if (g.Sf != g.Of) return;
        g.Eax = g.Esi - U32(d, 0x1c49); g.Al = (byte)(g.Al + d[0x19ea]); g.Test(g.Bl, g.Al, 8); if (!g.Zf) return;
        w.Call(0x9e41, g); g.Test(g.Bh, g.Al, 8); if (!g.Zf) return;
        g.Eax = g.Ecx; FA416(w, g, r);
    }

    /// <summary>A47E: as A416 for an object whose fields are shifted by one (+1 army, +7 / +0C position).</summary>
    public static void FA47E(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; uint edi = g.Edi, esi = g.Esi, eax = g.Eax;
        g.Eax = 0x56; Mul8(g, g.Al, r[1]); g.Ecx = g.Shr(U32(r, 7), 5, 32); g.Edx = g.Shr(U32(r, 0xc), 5, 32); g.Esi = U32(d, (int)(g.Eax & 0xffff) + 0x6db);
        g.Eax = eax; g.Ebx = 0x100; w.Call(0x1416b, g); g.Esi = esi; g.Edi = edi;
    }

    /// <summary>A4A9: the effect EAX (0 = none) with the table entry [191E] and EBX = 101h.</summary>
    public static void FA4A9(IUnitActionWorld w, ActionRegs g)
    {
        g.Test(g.Eax, g.Eax, 32); if (g.Zf) return;
        var d = w.Ds; uint esi = g.Esi, edi = g.Edi; g.Esi = U32(d, 0x191e); g.Ebx = 0x101; w.Call(0x1416b, g); g.Edi = edi; g.Esi = esi;
    }

    /// <summary>A38A: the object ESI looks for a free neighbour cell (7A36) from a random direction among the eight; none: the object is marked (+17 |= 20h of the one at [ECX]) and AL = 1; found: its next cell (AL, AH) is the goal and BL = 0.</summary>
    public static void FA38A(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Eax = 0; w.Call(0x43b0, g); w.Call(0x9e41, g); g.Al = (byte)(g.Al & 7); g.Ah = g.Al;
        for (; ; )
        {
            w.Call(0x7a36, g); if (g.Zf) goto found;
            g.Al = (byte)(g.Al + 1); g.Al = (byte)(g.Al & 7); g.Cmp(g.Al, g.Ah, 8); if (g.Zf) break;
        }
        g.Test(g.Ecx, g.Ecx, 32);
        if (!g.Zf)
        {
            g.Eax = U32(w.ObjectAt(g.Ecx), 0); g.Test(g.Eax, g.Eax, 32);
            if (!g.Zf) { g.Eax += w.PoolBase; var o = w.ObjectAt(g.Eax); PutW(o, 0x17, (ushort)(U16(o, 0x17) | 0x20)); }
        }
        g.Eax = g.Esi - w.PoolBase; w.Call(0x43b0, g); g.Al = 0; g.Logic(0, 8); g.Al++; g.Zf = false; g.Sf = false; return;
    found:
        g.Cl = d[0x273f]; g.Ch = d[0x2740]; g.Ebx = 0; g.Bl = g.Al; g.Eax = U32(r, 4); g.Cl = (byte)(g.Cl >> 1); g.Eax >>= 5; g.Al = (byte)(g.Al - g.Cl);
        g.Edx = U32(r, 8); g.Ch = (byte)(g.Ch >> 1); g.Edx >>= 5; g.Dl = (byte)(g.Dl - g.Ch); g.Ah = g.Dl;
        g.Al = (byte)(g.Al + d[(int)(g.Ebx * 2) + 0x19c7]); g.Ah = (byte)(g.Ah + d[(int)(g.Ebx * 2) + 0x19c8]);
        uint pushed = g.Eax; g.Eax = g.Esi - w.PoolBase; w.Call(0x43b0, g); g.Eax = pushed; g.Bl = 0; g.Logic(0, 8);
    }

    // ---- the tile descriptors of the map (CE9D, CF07, CF41): [18FE] the table of 8-byte descriptors, [1902] their number ----
    private static uint Desc(IUnitActionWorld w, uint tile, int offset) => w.TileProperty(tile, offset);

    /// <summary>CE9D: a random tile whose descriptor equals EAX in the bits FFFF0F00h: EAX = its index, ZF = 0; no such tile: ZF = 1. The registers but EAX are kept; 9E41 draws the random byte.</summary>
    public static void FCE9D(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint sEsi = g.Esi, sEbx = g.Ebx, sEcx = g.Ecx, sEdx = g.Edx;
        g.Eax &= 0xffff0f00; g.Logic(g.Eax, 32); g.Edx = g.Eax; uint count = U32(d, 0x1902); g.Ecx = 0;
        for (uint t = 0; t < count; t++) { g.Eax = Desc(w, t, 0) & 0xffff0f00; if (g.Eax == g.Edx) g.Ecx++; }
        g.Esi = U32(d, 0x18fe) + 8 * count; g.Ebx = 0;   // the scan has walked the table to its end
        g.Test(g.Ecx, g.Ecx, 32);
        if (!g.Zf)
        {
            w.Call(0x9e41, g); uint pushed = g.Edx; ulong product = (ulong)g.Eax * g.Ecx; g.Eax = (uint)product; g.Edx = (uint)(product >> 32); g.Cf = g.Of = g.Edx != 0;
            g.Eax >>= 8; g.Edx = pushed; g.Ecx = g.Eax + 1;
            g.Ebx = 0xffffffff; uint at = 0xffffffff;
            do
            {
                do { at++; g.Ebx++; g.Eax = Desc(w, at, 0) & 0xffff0f00; } while (g.Eax != g.Edx);
                g.Ecx--;
            } while (g.Ecx != 0);
            g.Eax = g.Ebx; g.Cl = 0xff; g.Logic(0xff, 8);
        }
        g.Esi = sEsi; g.Ebx = sEbx; g.Ecx = sEcx; g.Edx = sEdx;
    }

    /// <summary>CF07: the tiles whose descriptor has in the bits EBX the value EAX &amp; EBX: their indices as words at DS:1F45, ended by FFFFh; ECX = their number (ZF = 0 when there are some).</summary>
    public static void FCF07(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint sEdi = g.Edi; g.Eax &= g.Ebx; g.Logic(g.Eax, 32); g.Edi = 0x1f45; g.Ecx = 0; uint count = U32(d, 0x1902);
        for (uint t = 0; t < count; t++)
        {
            g.Edx = Desc(w, t, 0) & g.Ebx;
            if (g.Edx == g.Eax) { g.Ecx++; PutW(d, (int)g.Edi, (ushort)t); g.Edi += 2; }
        }
        PutW(d, (int)g.Edi, 0xffff); g.Test(g.Ecx, g.Ecx, 32); g.Edi = sEdi;
    }

    /// <summary>CF41: the first tile whose descriptor has the class AL (byte 0 &amp; 7), the byte AH as byte 1 and equal bytes 2 and 3 (the latter turned by 4 bits): EAX = its index, ZF = 0; none: ZF = 1 and ECX = the number of tiles.</summary>
    public static void FCF41(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Ecx = 0; uint count = U32(d, 0x1902);
        for (; ; )
        {
            g.Ebx = Desc(w, g.Ecx, 0); g.Bl = (byte)(g.Bl & 7); g.Cmp(g.Bl, g.Al, 8);
            if (g.Zf) { g.Cmp(g.Bh, g.Ah, 8); if (g.Zf) { g.Ebx = Desc(w, g.Ecx, 2); g.Cmp(g.Bl, g.Bh, 8); if (g.Zf) { g.Bl = (byte)((g.Bl << 4) | (g.Bl >> 4)); g.Cmp(g.Bl, g.Bh, 8); if (g.Zf) { g.Eax = g.Ecx; g.Cl = 0xff; g.Logic(0xff, 8); return; } } } }
            g.Ecx++; g.Cmp(g.Ecx, count, 32); if (g.Zf) return;
        }
    }

    // ---- the interface widgets that follow the selected object [1969] (6361..6877) ----
    private static void Redraw(ActionRegs g, byte[] widget) { widget[0] |= 0x80; g.Logic(widget[0], 8); }   // or byte [esi], 80h

    /// <summary>6377: can the settlement of the selected object [1969] afford the template AL of the army +14 of the widget ESI? ZF = 1 when it can (or the template has no price: bit 8 of +40), AL = 0.</summary>
    public static void F6377(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Al = r[2]; g.Ah = r[0x14]; F68A0(w, g); var t = w.ObjectAt(g.Edi);
        g.Test(U32(t, 0x40), 8, 32); if (g.Zf) return;
        g.Ebx = U32(d, 0x1969); var ob = w.ObjectAt(g.Ebx); g.Eax = 0xf5; Mul8(g, g.Al, ob[3]); g.Ecx = (g.Eax & 0xffff) + 0x97a; var part = w.PartOf((byte)ob[3]);
        for (int k = 0; k < 4; k++)
        {
            g.Eax = U32(t, 0x7c + 4 * k); g.Cmp(g.Eax, U32(part, 0x18 + 4 * k), 32); if (!g.Cf && !g.Zf) return;
        }
        g.Al = 0; g.Logic(0, 8);
    }

    /// <summary>6361: the widget with +1A = 1 that can be afforded (6377) drops +1A to 0 and asks for the picture +11 = 8.</summary>
    public static void F6361(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Cmp(r[0x1a], 1, 8); if (!g.Zf) return;
        F6377(w, g, r); if (!g.Zf) return;
        r[0x1a] = Dec8(g, r[0x1a]); r[0x11] = 8;
    }

    /// <summary>63CB: the health of the selected object (+19) in +14 of the widget with the colour +16 (B2h above half of +15, 51h above a quarter, else 22h); a change asks for the redraw (+0 bit 80h).</summary>
    public static void F63CB(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Eax = U32(d, 0x1969); g.Ah = w.ObjectAt(g.Eax)[0x19]; g.Cmp(r[0x14], g.Ah, 8); if (g.Zf) return;
        g.Al = r[0x15]; g.Bl = 0xb2; g.Al = (byte)g.Shr(g.Al, 1, 8); g.Cmp(g.Ah, g.Al, 8);
        if (g.Cf || g.Zf)
        {
            g.Bl = 0x51; g.Al = (byte)g.Shr(g.Al, 1, 8); g.Cmp(g.Ah, g.Al, 8);
            if (g.Cf || g.Zf) g.Bl = 0x22;
        }
        r[0x16] = g.Bl; r[0x14] = g.Ah; Redraw(g, r);
    }

    /// <summary>63F7: +14 follows the byte +16 of the selected object.</summary>
    public static void F63F7(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Eax = U32(d, 0x1969); g.Al = w.ObjectAt(g.Eax)[0x16]; g.Cmp(r[0x14], g.Al, 8); if (g.Zf) return;
        r[0x14] = g.Al; Redraw(g, r);
    }

    /// <summary>640B: +14 follows the byte of the selected object at the offset +1A of the widget.</summary>
    public static void F640B(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Eax = r[0x1a]; uint ptr = U32(d, 0x1969); g.Add(g.Eax, ptr); g.Eax += ptr; g.Al = w.ObjectAt(ptr)[r[0x1a]];
        g.Cmp(r[0x14], g.Al, 8); if (g.Zf) return; r[0x14] = g.Al; Redraw(g, r);
    }

    /// <summary>6423: +14 follows the byte +27 of the selected object while it does not exceed +15.</summary>
    public static void F6423(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Eax = U32(d, 0x1969); g.Al = w.ObjectAt(g.Eax)[0x27]; g.Cmp(g.Al, r[0x15], 8); if (!g.Cf && !g.Zf) return;
        g.Cmp(r[0x14], g.Al, 8); if (g.Zf) return; r[0x14] = g.Al; Redraw(g, r);
    }

    /// <summary>643C: +1A (a dword) follows the value at the pointer +15: a byte when +14 = 1, else three bytes.</summary>
    public static void F643C(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Ebx = U32(r, 0x15); var o = w.ObjectAt(g.Ebx); g.Eax = o[0];
        g.Cmp(r[0x14], 1, 8); if (!g.Zf) { g.Eax = U32(o, 0); g.Eax &= 0xffffff; g.Logic(g.Eax, 32); }
        g.Cmp(g.Eax, U32(r, 0x1a), 32); if (g.Zf) return; Put(r, 0x1a, g.Eax); Redraw(g, r);
    }

    /// <summary>645B: +14 follows the byte of the selected object at the offset +1A; a widget of the kind +3 = 3 first computes it from the drawn state [194D] - +4 scaled to +15 / (+12 + 2).</summary>
    public static void F645B(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; uint ptr = U32(d, 0x1969); g.Edx = r[0x1a]; g.Add(g.Edx, ptr); g.Edx += ptr; var ob = w.ObjectAt(ptr); int at = r[0x1a];
        g.Test(r[3], 1, 8);
        if (!g.Zf)
        {
            Put(d, 0x1946, 0xa5009c); Put(d, 0x1965, 0); g.Cmp(r[3], 3, 8);
            if (g.Zf)
            {
                g.Eax = U32(d, 0x194d); g.Cmp(g.Eax, U32(r, 4), 32); g.Eax -= U32(r, 4); Mul8(g, g.Al, r[0x15]); g.Bl = r[0x12]; g.Bl = Add8(g, g.Bl, 2); Div8(g, g.Bl); ob[at] = g.Al;
            }
        }
        g.Al = ob[at]; g.Cmp(g.Al, r[0x15], 8); if (!g.Cf && !g.Zf) return;
        g.Cmp(r[0x14], g.Al, 8); if (g.Zf) return; r[0x14] = g.Al; Redraw(g, r);
    }

    /// <summary>66BB: the countdown [1CAD] / 20h as a row of dots (2Eh, at most 20h) at DS:4C4 ended by FFh.</summary>
    public static void F66BB(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; Redraw(g, r); g.Eax = U32(d, 0x1cad); g.Edi = 0x4c4; g.Eax = g.Shr(g.Eax, 5, 32);
        if (!g.Zf)
        {
            g.Cmp(g.Al, 0x20, 8); if (!(g.Cf || g.Zf)) g.Al = 0x20;
            do { d[(int)g.Edi] = 0x2e; g.Edi++; g.Al = Dec8(g, g.Al); } while (g.Al != 0);
        }
        d[(int)g.Edi] = 0xff;
    }

    /// <summary>66DF: AL = +E + +3 + +F of the widget.</summary>
    public static void F66DF(IUnitActionWorld w, ActionRegs g, byte[] r) { g.Al = r[0xe]; g.Al = Add8(g, g.Al, r[3]); g.Al = Add8(g, g.Al, r[0xf]); }

    /// <summary>67BD / 67C0 / 67D3: what is on the map cell: 67BD takes the cell (EBX = x, EAX = y), 67C0 the pixel offset on the screen (EAX = x, EBX = y; the view corner is added). Returns AL = 1, CF = 0 for an object the player [1972] may select (not a fogged cell, not template flag 8000h, an enemy only when it is visible [1CA8] and the tile class is not 1), CF = 1 for a blocking one (template flag 4) and AL = 0, CF = 1 for nothing.</summary>
    public static void F67BD(IUnitActionWorld w, ActionRegs g) { uint t = g.Eax; g.Eax = g.Ebx; g.Ebx = t; F67D3(w, g); }
    public static void F67C0(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Eax >>= 5; g.Ebx >>= 5; uint t = g.Eax; g.Eax = g.Ebx; g.Ebx = t; g.Add(g.Eax, U32(d, 0x190a)); g.Eax += U32(d, 0x190a); g.Add(g.Ebx, U32(d, 0x1906)); g.Ebx += U32(d, 0x1906); F67D3(w, g);
    }
    public static void F67D3(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Ah = d[0x18de]; Mul8(g, g.Al, g.Ah); g.Add(g.Ebx, g.Eax); g.Ebx += g.Eax; g.Cl = d[0x1972]; g.Eax = 1; g.Eax <<= (int)(g.Cl & 31);
        g.Ecx = U32(d, 0x18da); g.Test(w.Peek(g.Ecx + g.Ebx), g.Al, 8); if (!g.Zf) goto none;
        g.Ebx *= 4;
        g.Eax = w.PlanePointer(2); g.Esi = w.PlaneRead(g.Eax + g.Ebx); g.Test(g.Esi, g.Esi, 32);
        if (g.Zf) { g.Eax = w.PlanePointer(1); g.Esi = w.PlaneRead(g.Eax + g.Ebx); g.Test(g.Esi, g.Esi, 32); }
        if (g.Zf) { g.Eax = w.PlanePointer(0); g.Esi = w.PlaneRead(g.Eax + g.Ebx); g.Test(g.Esi, g.Esi, 32); }
        if (g.Zf) goto none;
        g.Esi += w.PoolBase; g.Edi = U32(d, 0x18ee); g.Eax = g.Ebx / 4;   // the tile of the cell (the map word; the check map is the identity)
        g.Edi = U32(d, 0x18fe); g.Cl = (byte)w.TileProperty(g.Eax, 0); g.Cl = (byte)(g.Cl & 7); g.Logic(g.Cl, 8);
        F6885(w, g, w.ObjectAt(g.Esi)); var t = w.ObjectAt(g.Edi);
        g.Test(U32(t, 0x40), 0x8000, 32); if (!g.Zf) goto none;
        var o = w.ObjectAt(g.Esi); g.Al = o[3]; g.Cmp(g.Al, d[0x1972], 8);
        if (!g.Zf)
        {
            g.Cmp(g.Cl, 1, 8); if (g.Zf) goto none;
            g.Al = o[0x20]; g.Test(d[0x1ca8], g.Al, 8); if (g.Zf) goto none;
        }
        g.Test(U32(t, 0x40), 4, 32); if (!g.Zf) { g.Cf = true; return; }
        g.Al = 0; g.Logic(0, 8); g.Al++; g.Zf = false; g.Sf = false; return;
    none:
        g.Al = 0; g.Logic(0, 8); g.Cf = true;
    }

    // ---- the actions of the animals of ARMY__05 (12AE1..12E12): the handlers of the unit step tables of the birds, wolves and bears ----
    private static bool BitMem(byte[] t, int offset, uint bit) => ((U32(t, offset + 4 * (int)(bit >> 5)) >> (int)(bit & 31)) & 1) != 0;
    private static byte Inc8(ActionRegs g, byte a) { byte r = (byte)(a + 1); g.Zf = r == 0; g.Sf = (r & 0x80) != 0; g.Of = a == 0x7f; return r; }   // CF is kept
    private static void AnimalResult(IUnitActionWorld w, ActionRegs g)   // mov al, [1C9F]; not al; test al, 40h
    {
        g.Al = w.Ds[0x1c9f]; g.Al = (byte)~g.Al; g.Test(g.Al, 0x40, 8);
    }

    /// <summary>12AE1: state 1, the walk to the point (+22/+23): BL (the request bit of the step) goes to [1C9F]; 8D89 tells the situation (AL 1: the order stands, else a new one): a new point gives 12BD8 and +15 + 1.</summary>
    public static void F12AE1(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Ds[0x1c9f] = g.Bl; w.Call(0x8d89, g); g.Cmp(g.Al, 1, 8);
        if (!g.Zf) { g.Al = r[0x22]; g.Ah = r[0x23]; F12BD8(w, g, r); r[0x15] = Inc8(g, r[0x15]); AnimalResult(w, g); return; }
        g.Cmp(g.Dl, 4, 8); if (g.Zf) { AnimalResult(w, g); return; }
        g.Eax = g.Shr(U32(r, 4), 5, 32); g.Ebx = g.Shr(U32(r, 8), 5, 32);
        g.Cmp(g.Al, r[0x22], 8);
        if (g.Zf) { g.Bl = r[0x23]; return; }
        r[0x15] = 0; AnimalResult(w, g);
    }

    /// <summary>12B29: state 0, the animal at rest or hunting: a situation 0 turns away from a wall (DL or DH = 6) or hunts (8AF4, 8D3E; a target at [51A] when [512] is set), 1 goes on with 12C65.</summary>
    public static void F12B29(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; d[0x1c9f] = g.Bl; w.Call(0x8d89, g); g.Test(g.Al, g.Al, 8);
        if (g.Zf)
        {
            g.Cmp(g.Dl, 6, 8); bool turn = g.Zf; if (!turn) { g.Cmp(g.Dh, 6, 8); turn = g.Zf; }
            if (!turn)
            {
                w.Call(0x8af4, g); if (!g.Zf) return;
                w.Call(0x8d3e, g); g.Cmp(w.G512, 0, 32);
                if (!g.Zf) { g.Eax = w.G51A - w.PoolBase; Put(r, 0x1c, g.Eax); r[0x15] = Inc8(g, r[0x15]); }
            }
            else
            {
                g.Eax = g.Shr(U32(r, 4), 5, 32); g.Ebx = g.Shr(U32(r, 8), 5, 32); g.Ah = g.Bl; g.Ah = Dec8(g, g.Ah); g.Test(g.Bl, 1, 8);
                if (g.Zf) g.Ah = Add8(g, g.Ah, 2);
                F12BD8(w, g, r);
            }
            AnimalResult(w, g); return;
        }
        g.Cmp(g.Al, 1, 8); if (!g.Zf) return;
        F12C65(w, g, r); if (g.Zf) { r[0x15] &= 0xf0; g.Logic(r[0x15], 8); }
        AnimalResult(w, g);
    }

    /// <summary>12BA3: state 2: a situation 0 steps on (+15 + 1), 1 goes on with 12C65; the end of the approach gives AL = 0.</summary>
    public static void F12BA3(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Ds[0x1c9f] = g.Bl; w.Call(0x8d89, g); g.Test(g.Al, g.Al, 8);
        if (g.Zf) { r[0x15] = Inc8(g, r[0x15]); AnimalResult(w, g); return; }
        g.Cmp(g.Al, 1, 8); if (!g.Zf) return;
        F12C65(w, g, r);
        if (g.Zf) { g.Al = 0; g.Logic(0, 8); return; }
        g.Test(U16(r, 0x17), 2, 16); if (!g.Zf) { g.Al = 0; g.Logic(0, 8); return; }
        AnimalResult(w, g);
    }

    /// <summary>12BD8: the point (AL, AH) becomes the target +22/+23 and the request of the walk (+2D = request 4, 8120 when it is already walking).</summary>
    public static void F12BD8(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        r[0x22] = g.Al; r[0x23] = g.Ah; r[0x2d] &= 0xf; g.Logic(r[0x2d], 8); g.Cmp(r[0x2d], 4, 8);
        if (g.Zf) w.Call(0x8120, g);
        r[0x2d] |= 0x40; g.Logic(r[0x2d], 8);
    }

    /// <summary>12BF2: the request 6 (rest).</summary>
    public static void F12BF2(IUnitActionWorld w, ActionRegs g, byte[] r) { r[0x2d] &= 0xf; r[0x2d] |= 0x60; g.Logic(r[0x2d], 8); }

    /// <summary>12BFB: turn to the heading AL (+25 high bits) with the request 7.</summary>
    public static void F12BFB(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Al &= 7; g.Al = (byte)(g.Al << 3); r[0x25] &= 7; r[0x25] |= g.Al; r[0x2d] &= 0xf; r[0x2d] |= 0x70; g.Logic(r[0x2d], 8);
    }

    /// <summary>12C10: follow the target +1C: its cell becomes the point +22/+23 (BL = 3 when the target is there already, else 1) with the walk request.</summary>
    public static void F12C10(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Bl = 1; g.Edi = U32(r, 0x1c); g.Test(g.Edi, g.Edi, 32); if (g.Zf) return;
        g.Edi += w.PoolBase; var o = w.ObjectAt(g.Edi);
        g.Eax = g.Shr(U32(o, 4), 5, 32); g.Ebx = g.Shr(U32(o, 8), 5, 32); g.Ah = g.Bl; g.Bl = 3;
        g.Cmp(r[0x22], g.Al, 8);
        if (g.Zf) { g.Cmp(r[0x23], g.Ah, 8); if (g.Zf) return; }
        r[0x22] = g.Al; r[0x23] = g.Ah; r[0x2d] &= 0xf; r[0x2d] |= 0x40; g.Logic(r[0x2d], 8);
    }

    /// <summary>12C48: turn toward the object EDI (9688); ZF = 1 when the heading is right already, else 12BFB and AL = FFh.</summary>
    public static void F12C48(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var o = w.ObjectAt(g.Edi); g.Eax = U32(o, 4); g.Ebx = U32(o, 8); w.Call(0x9688, g);
        g.Ah = (byte)(r[0x25] & 7); g.Logic(g.Ah, 8); g.Cmp(g.Al, g.Ah, 8); if (g.Zf) return;
        F12BFB(w, g, r); g.Al = 0xff; g.Logic(0xff, 8);
    }

    /// <summary>12C65: the attack on the target: 8D89 and 8ABF; the target out of reach is followed (12C10), in reach it is faced (12C48) and struck (8C7C).</summary>
    public static void F12C65(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; w.Call(0x8d89, g); w.Call(0x8abf, g);
        if (g.Zf) { g.Al = 0; g.Logic(0, 8); return; }
        F12CB1(w, g, r);
        if (g.Zf)
        {
            g.Cmp(w.G522, 3, 8); if (!g.Zf)
            {
                w.Call(0x8af4, g);
                if (g.Zf) { F12C48(w, g, r); if (g.Zf) w.Call(0x8c7c, g); }
            }
            g.Al = 0xff; g.Logic(0xff, 8); return;
        }
        g.Cmp(g.Bl, 1, 8); if (!g.Zf) { g.Al = 0; g.Logic(0, 8); return; }
        g.Cmp(w.G522, 6, 8);
        if (g.Zf) { F12C10(w, g, r); if (g.Zf) { g.Al = 0; g.Logic(0, 8); return; } }
        g.Al = 0xff; g.Logic(0xff, 8);
    }

    /// <summary>12CB1: where is the target +1C: BL 0 in reach (the template's +73..+72 cells), 1 too far, 2 too near, 3 none or of another layer (than allowed by +74); AL = FFh when not in reach.</summary>
    public static void F12CB1(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Bl = 3; g.Edi = U32(r, 0x1c); g.Test(g.Edi, g.Edi, 32);
        if (!g.Zf)
        {
            g.Edi += w.PoolBase; var o = w.ObjectAt(g.Edi); g.Ebp = U32(d, 0x1c49); var t = w.ObjectAt(g.Ebp);
            g.Al = o[0xd]; g.Eax &= 3; g.Cf = BitMem(t, 0x74, g.Eax);
            if (g.Cf)
            {
                FC344(w, g, r); uint pushed = g.Eax; FD157(w, g); g.Bl = 1; g.Cmp(g.Al, 1, 8); g.Eax = pushed;
                bool far = false;
                if (g.Zf) { g.Cmp(g.Eax, 0x30, 32); if (!g.Cf) far = true; }
                if (!far)
                {
                    g.Ebp = U32(d, 0x1c49); t = w.ObjectAt(g.Ebp); g.Eax >>= 5; g.Al = Inc8(g, g.Al); g.Cmp(g.Al, t[0x72], 8);
                    if (!(!g.Cf && !g.Zf))
                    {
                        g.Bl = 2; g.Al = Dec8(g, g.Al); g.Cmp(g.Al, t[0x73], 8);
                        if (!g.Cf) { g.Bl = 0; g.Logic(0, 8); return; }
                    }
                }
            }
        }
        g.Al = 0xff; g.Logic(0xff, 8);
    }

    /// <summary>12D08: the action of a dying or flying animal (the frame from +25 heading and +27 step, the move by +26 on the heading table, the effect of the template +53 and 98DB at the end).</summary>
    public static void F12D08(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Edi = U32(d, 0x1c49); var t = w.ObjectAt(g.Edi);
        g.Test(U16(r, 0x17), 0x80, 16);
        if (!g.Zf)
        {
            r[0x19] = Dec8(g, r[0x19]); g.Cmp(r[0x19], 0xfa, 8); if (!g.Cf && !g.Zf) return;
            Settle(w, g, r); return;
        }
        g.Cmp(r[0x19], 0, 8);
        if (g.Zf)
        {
            g.Eax = 0; w.Call(0x43b0, g); r[0x27] = 0; r[0x2c] = 0x1e; r[0x26] = 0x14; r[0x19] = Dec8(g, r[0x19]);
            g.Eax = U32(w.ObjectAt(g.Edi), 0xa7); FA416(w, g, r);
        }
        g.Edi = U32(d, 0x1c49); t = w.ObjectAt(g.Edi); g.Test(d[0x19ea], 1, 8);
        if (g.Zf)
        {
            r[0x27] = Inc8(g, r[0x27]); g.Al = t[0x4f]; g.Cmp(r[0x27], g.Al, 8); if (!g.Cf) r[0x27] = 0;
        }
        g.Al = r[0x25]; g.Al = (byte)g.Shr(g.Al, 1, 8); g.Eax &= 3; Mul8(g, g.Al, t[0x4f]); g.Al = Add8(g, g.Al, r[0x27]);
        g.Ebx = t[0x4a]; g.Bl = Add8(g, g.Bl, t[0x4b]); g.Eax = g.Eax + g.Ebx * 8; ushort frame = Add16(g, (ushort)g.Eax, U16(t, 0x48)); g.Eax = (g.Eax & 0xffff0000) | frame; PutW(r, 1, frame);
        g.Dl = r[0x25]; g.Edx &= 6; g.Edx |= 1; g.Edx <<= 2;
        g.Al = r[0x26]; short y = (short)((sbyte)r[0x26] * (sbyte)OriginalUnitMove.Heading[g.Edx * 2 + 1]); g.Eax = (g.Eax & 0xffff0000) | (ushort)y; g.Ebx = (uint)(int)y;
        g.Al = r[0x26]; short x = (short)((sbyte)r[0x26] * (sbyte)OriginalUnitMove.Heading[g.Edx * 2]); g.Eax = (uint)(int)x;
        g.Eax = (uint)((int)g.Eax >> 4); g.Ebx = (uint)((int)g.Ebx >> 4); g.Eax += U32(r, 4); g.Ebx += U32(r, 8);
        g.Cmp(g.Eax, U32(d, 0x18e6), 32);
        if (g.Cf) { g.Cmp(g.Ebx, U32(d, 0x18ea), 32); if (g.Cf) { Put(r, 4, g.Eax); Put(r, 8, g.Ebx); } }
        r[0x2c] = Dec8(g, r[0x2c]); if (!g.Zf) return;
        FD12C(w, g, r);
        g.Cmp(g.Al, 1, 8); if (g.Zf) { Settle(w, g, r); return; }
        g.Cmp(g.Al, 2, 8); if (g.Zf) { Settle(w, g, r); return; }
        g.Edi = U32(d, 0x1c49); t = w.ObjectAt(g.Edi); g.Al = r[0x19]; g.Eax &= 3; g.Bl = t[0x53 + (int)g.Eax]; g.Cmp(g.Bl, 0xff, 8);
        if (!g.Zf)
        {
            g.Al = r[0]; g.Bh = 0; g.Ecx = U32(r, 4); g.Edx = U32(r, 8); uint esi = g.Esi; w.Call(0x9727, g); g.Esi = esi;
        }
        Settle(w, g, r);
    }
    private static void Settle(IUnitActionWorld w, ActionRegs g, byte[] r) { w.Call(0x98db, g); g.Al = 0; g.Logic(0, 8); }

    // ---- the resource counters of the panel (AAB3, AC23) ----
    /// <summary>AAB3: the three displayed resources [1C62], [1C66], [1C6A] follow the values +18, +1C, +20 of the participant of the player [1972] by one a step (the colour [1F10] D0h down, 61h up, 0 at rest;
    /// a gap of 3E8h or more jumps), each is drawn (ABE9 with ECX = 42h, 103h, 9Eh); the fourth (+24, red D0h below twice the statistic [stats + 19ED]) with ECX = 16Dh.</summary>
    public static void FAAB3(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Eax = 0xf5; Mul8(g, g.Al, d[0x1972]); g.Esi = (g.Eax & 0xffff) + 0x97a; var part = w.PartOf(d[0x1972]);
        int[] shown = [0x1c62, 0x1c66, 0x1c6a], at = [0x18, 0x1c, 0x20]; uint[] column = [0x42, 0x103, 0x9e];
        for (int k = 0; k < 3; k++)
        {
            uint value = U32(part, at[k]);
            g.Eax = U32(d, shown[k]); g.Ebx = g.Eax; g.Cmp(g.Ebx, value, 32); g.Ebx -= value;
            if (g.Zf) continue;
            d[0x1f10] = 0xd0; bool below = g.Cf; g.Eax--;
            if (below) { d[0x1f10] = 0x61; g.Add(g.Eax, 2); g.Eax += 2; g.Cf = g.Ebx != 0; g.Ebx = (uint)-(int)g.Ebx; }
            g.Cmp(g.Ebx, 0x3e8, 32); if (!g.Cf) g.Eax = value;
            Put(d, shown[k], g.Eax); g.Cmp(g.Eax, value, 32); if (g.Zf) d[0x1f10] = 0;
            g.Ecx = column[k]; g.Edx = 0; w.Call(0xabe9, g);
        }
        d[0x1f10] = 0; g.Al = d[0x1972]; w.Call(0x1ead8, g);
        g.Ecx = U32(w.Stats, (int)(g.Edx - 0x40a000) + 0x19ed); g.Add(g.Ecx, g.Ecx); g.Ecx += g.Ecx; g.Eax = U32(part, 0x24); g.Cmp(g.Eax, g.Ecx, 32);
        if (g.Cf) d[0x1f10] = 0xd0;
        g.Ecx = 0x16d; g.Edx = 0; w.Call(0xabe9, g); d[0x1f10] = 0;
    }

    /// <summary>AC23: EAX as a decimal string without leading zeros at DS:1F11 (the powers of ten from the table DS:19FE down to 10, then the last digit), FFh ended; 6C6D draws it.</summary>
    public static void FAC23(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Esi = 0x1f11; g.Edi = 0x19fe; uint esi0 = g.Esi, ecx0 = g.Ecx; g.Cl = 1;
        do
        {
            g.Ebx = U32(d, (int)g.Edi); g.Ch = 0x2f;
            do { g.Ch++; g.Cmp(g.Eax, g.Ebx, 32); g.Eax -= g.Ebx; } while (!g.Cf);
            g.Eax += g.Ebx; g.Cmp(g.Ch, 0x30, 8);
            bool skip = g.Zf && g.Cl != 0;
            if (!skip) { g.Cl = 0; d[(int)g.Esi] = g.Ch; g.Esi++; }
            g.Edi += 4; g.Cmp(g.Ebx, 0xa, 32);
        } while (!g.Zf);
        g.Al = Add8(g, g.Al, 0x30); d[(int)g.Esi] = g.Al; d[(int)g.Esi + 1] = 0xff; g.Ecx = ecx0; g.Esi = esi0; w.Call(0x6c6d, g);
    }

    // ---- the cursor and the player's orders (4580..486C) ----
    private static void SetCursor(byte[] d, uint frames) { Put(d, 0x1946, frames); Put(d, 0x1965, 0x0f000f); }

    /// <summary>4580: what the mouse points at: the button state [193A]/[194B] into L3[13], the cell under the pointer (inside the map from the view corner [1C58]/[1C5C]) classified by 67BD: the object L3[5] and its template L3[9],
    /// the cursor kind L3[0D] (from the template flags 18004h, one more for another settlement unless the template is 20020h or the owner is [1971], one more when the player's war mask +12 has the owner); then the click state L3[12] chooses 4672, 46F7 or 478D.</summary>
    public static void F4580(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3;
        g.Cmp(d[0x1c60], 0, 8); if (g.Zf) return;
        g.Eax = 0; g.Logic(0, 32); l[0x13] = g.Al; g.Cmp(l[0], g.Al, 8);
        if (!g.Zf) { g.Cmp(l[0], 0x2a, 8); if (!g.Zf) goto state; }
        g.Al = d[0x194b]; g.Al = (byte)(g.Al << 2); g.Al = (byte)(g.Al | d[0x193a]); g.Logic(g.Al, 8); g.Al = (byte)(g.Al << 1); l[0x13] = g.Al;
        g.Eax = U32(d, 0x194d); g.Ebx = U32(d, 0x1951);
        g.Cmp(g.Eax, U32(d, 0x1c58), 32); g.Eax -= U32(d, 0x1c58); if (g.Cf) goto state;
        g.Cmp(g.Ebx, U32(d, 0x1c5c), 32); g.Ebx -= U32(d, 0x1c5c); if (g.Cf) goto state;
        g.Cmp(g.Eax, U32(d, 0x18de), 32); if (!g.Cf) goto state;
        g.Cmp(g.Ebx, U32(d, 0x18e2), 32); if (!g.Cf) goto state;
        l[0x13] |= 1; g.Logic(l[0x13], 8);
        F67BD(w, g); if (g.Zf) goto state;
        Put(l, 5, g.Esi); Put(l, 9, g.Edi);
        var t = w.ObjectAt(g.Edi); var o = w.ObjectAt(g.Esi);
        g.Eax = U32(t, 0x40); g.Eax &= 0x18004; g.Logic(g.Eax, 32); g.Ecx = g.Eax; g.Eax >>= 0xc; g.Cl = (byte)(g.Cl | g.Al); g.Cl = Inc8(g, g.Cl);
        g.Ebx = o[3]; g.Cmp(g.Bl, d[0x1972], 8);
        if (!g.Zf)
        {
            g.Eax = U32(t, 0x40); g.Eax &= 0x20020; g.Logic(g.Eax, 32); g.Cmp(g.Eax, 0x20020, 32);
            if (!g.Zf)
            {
                g.Cmp(g.Bl, d[0x1971], 8);
                if (!g.Zf)
                {
                    g.Cl = Inc8(g, g.Cl); g.Eax = 0xf5; Mul8(g, g.Al, d[0x1972]);
                    var part = w.PartOf(d[0x1972]); g.Cf = BitMem(part, 0x12, g.Ebx); g.Cl = Add8(g, g.Cl, (byte)(g.Cf ? 1 : 0));
                }
            }
        }
        l[0xd] = g.Cl;
    state:
        g.Al = l[0x12]; g.Test(g.Al, g.Al, 8); if (g.Zf) { F4672(w, g); return; }
        g.Cmp(g.Al, 1, 8); if (g.Zf) { F46F7(w, g); return; }
        g.Cmp(g.Al, 2, 8); if (g.Zf) { F478D(w, g); return; }
    }

    /// <summary>4672: the pointer over the map with no click: the cursor frames 400037h; a press with the left button (L3[13] = 11h or 5) turns into a click (46F7); the right button scrolls the view by the pointer (A320).</summary>
    public static void F4672(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3; g.Test(l[0x13], 1, 8); if (g.Zf) return;
        SetCursor(d, 0x400037); g.Cmp(l[0x13], 0x11, 8);
        if (!g.Zf) { g.Cmp(l[0x13], 5, 8); if (!g.Zf) { g.Test(l[0x13], 2, 8); if (g.Zf) return; g.Eax = U32(d, 0x194d) - U32(d, 0x1c58); g.Ebx = U32(d, 0x1951) - U32(d, 0x1c5c); FA320(w, g); return; } }
        g.Al = l[0x13]; g.Al = (byte)g.Shr(g.Al, 1, 8); g.Al = (byte)(g.Al & 0xa); g.Logic(g.Al, 8); g.Al = (byte)(g.Al | 1); l[0x13] = g.Al;
        g.Cmp(d[0x1986], 0, 8); if (g.Zf) return;
        g.Cmp(d[0x198a], 1, 8); if (!g.Zf) return;
        d[0x19c0] = 0x48; F46F7(w, g);
    }

    /// <summary>46F7: the left button over the map: the cursor 93008Ah; with the button flag 8 the cell under the pointer becomes the goal +22/+23 of the selected object [1969] with the order [19C0] in +14 (or, in a group [1C89], 20232 and 1F464 with 80h); then 4938.</summary>
    public static void F46F7(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3; g.Test(l[0x13], 1, 8); if (g.Zf) return;
        SetCursor(d, 0x93008a); g.Test(l[0x13], 8, 8); if (g.Zf) return;
        g.Ecx = U32(d, 0x194d); g.Eax = U32(d, 0x1951); g.Ecx -= U32(d, 0x1c58); g.Eax -= U32(d, 0x1c5c); g.Ch = g.Al; g.Ah = d[0x19c0];
        g.Cmp(d[0x1c89], 0, 8);
        if (g.Zf)
        {
            g.Esi = U32(d, 0x1969); var o = w.ObjectAt(g.Esi); o[0x22] = g.Cl; o[0x23] = g.Ch; o[0x14] &= 7; o[0x14] |= g.Ah; g.Logic(o[0x14], 8); w.Call(0x4938, g); return;
        }
        uint pushed = g.Ecx; g.Edi = U32(d, 0xed20); g.Add(g.Edi, 0x9df); g.Edi += 0x9df; g.Bl = d[0x1c87]; w.Call(0x20232, g); g.Ecx = pushed;
        if (!g.Zf) { g.Bl = 0x80; w.Call(0x1f464, g); }
        w.Call(0x4938, g);
    }

    /// <summary>478D: the right button over an object L3[5] (not the selected one, not an own group member): the cursor 6D0064h (810078h on an enemy that may be attacked, 77006Eh when the kind has both low bits); with the button flag 8 the object becomes the target +1C of the selected object with the order [19C0] (a group: B851); then 4938.</summary>
    public static void F478D(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3; g.Test(l[0x13], 1, 8); if (g.Zf) return;
        SetCursor(d, 0x6d0064); g.Esi = U32(l, 5); g.Test(g.Esi, g.Esi, 32); if (g.Zf) return;
        g.Al = l[0xd]; g.Test(g.Al, 8, 8); if (!g.Zf) return;
        g.Cmp(g.Esi, U32(d, 0x1969), 32); if (g.Zf) return;
        var o = w.ObjectAt(g.Esi);
        g.Cmp(d[0x1c89], 0, 8);
        if (g.Zf) goto L47f4;
        g.Ah = o[3]; g.Cmp(g.Ah, d[0x1972], 8);
        if (g.Zf) { g.Ah = o[0x1b]; g.Cmp(g.Ah, d[0x1c87], 8); if (g.Zf) return; }
    L47f4:
        SetCursor(d, 0x810078); g.Al &= 3; g.Logic(g.Al, 8); g.Cmp(g.Al, 3, 8);
        if (g.Zf) SetCursor(d, 0x77006e);
        g.Test(l[0x13], 8, 8); if (g.Zf) return;
        g.Ecx = g.Esi; g.Cmp(g.Ecx, w.PoolBase, 32); g.Ecx -= w.PoolBase; g.Ah = d[0x19c0];
        g.Cmp(d[0x1c89], 0, 8);
        if (!g.Zf) { g.Al = d[0x1972]; g.Bl = d[0x1c87]; w.Call(0xb851, g); w.Call(0x4938, g); return; }
        g.Esi = U32(d, 0x1969); var sel = w.ObjectAt(g.Esi); Put(sel, 0x1c, g.Ecx); sel[0x14] &= 7; sel[0x14] |= g.Ah; g.Logic(sel[0x14], 8); w.Call(0x4938, g);
    }

    /// <summary>486D: the pointer over the minimap (pixels 10h.., 1C0h square): L3[4] the button flags, the cell by 67C0, a living object (+19 &gt; 0) as L3[5] / L3[9], the cursor kind L3[0D] as in 4580 (no 20020h exception), then the handler of the mode L3[2] (table +14h).</summary>
    public static void F486D(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3;
        g.Eax = 0; g.Logic(0, 32); Put(l, 5, 0); l[4] = g.Al; g.Cmp(l[0], g.Al, 8);
        if (!g.Zf) { g.Cmp(l[0], 0x28, 8); if (!g.Zf) goto dispatch; }
        g.Al = d[0x194b]; g.Al = (byte)(g.Al << 2); g.Al = (byte)(g.Al | d[0x193a]); g.Logic(g.Al, 8); g.Al = (byte)(g.Al << 1); l[4] = g.Al;
        g.Eax = U32(d, 0x194d); g.Ebx = U32(d, 0x1951);
        g.Cmp(g.Eax, 0x10, 32); g.Eax -= 0x10; if (g.Cf) goto dispatch;
        g.Cmp(g.Ebx, 0x10, 32); g.Ebx -= 0x10; if (g.Cf) goto dispatch;
        g.Cmp(g.Eax, 0x1c0, 32); if (!g.Cf) goto dispatch;
        g.Cmp(g.Ebx, 0x1c0, 32); if (!g.Cf) goto dispatch;
        l[4] |= 1; g.Logic(l[4], 8);
        F67C0(w, g); if (g.Zf) goto dispatch;
        var o = w.ObjectAt(g.Esi); g.Cmp(o[0x19], 0, 8); if (g.Zf || (g.Sf != g.Of)) goto dispatch;
        Put(l, 5, g.Esi); Put(l, 9, g.Edi);
        var t = w.ObjectAt(g.Edi);
        g.Eax = U32(t, 0x40); g.Eax &= 0x18004; g.Logic(g.Eax, 32); g.Ecx = g.Eax; g.Eax >>= 0xc; g.Cl = (byte)(g.Cl | g.Al); g.Cl = Inc8(g, g.Cl);
        g.Ebx = o[3]; g.Cmp(g.Bl, d[0x1972], 8);
        if (!g.Zf)
        {
            g.Cmp(g.Bl, d[0x1971], 8);
            if (!g.Zf)
            {
                g.Cl = Inc8(g, g.Cl); g.Eax = 0xf5; Mul8(g, g.Al, d[0x1972]);
                var part = w.PartOf(d[0x1972]); g.Cf = BitMem(part, 0x12, g.Ebx); g.Cl = Add8(g, g.Cl, (byte)(g.Cf ? 1 : 0));
            }
        }
        l[0xd] = g.Cl;
    dispatch:
        g.Eax = l[2]; w.CallHandler(g.Eax, g);
    }

    /// <summary>4938: ends a click: [19C2] = 1, the click state L3[2], L3[3], L3[12] = 0 and, in a group [1C89], [1A1B] = 0.</summary>
    public static void F4938(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3; d[0x19c2] = 1; l[2] = 0; l[3] = 0; l[0x12] = 0; g.Cmp(d[0x1c89], 0, 8); if (g.Zf) return;
        d[0x1a1b] = 0;
    }

    /// <summary>4965: the pointer over the map in the play mode: a pending group command [1C86] = 2 goes to 4B12; the cursor frames by the kind L3[0D] (400037h / 54004Bh / 4A0041h) while L3[3] counts out; the keys [1C35] = 39h (centre the view on the object [1974], A320) and 1Ch (200B5, B616 for the
    /// selected record, then 4B79); the button states L3[4] = 11h or 5 go to 4C46, 3 starts a drag [1C86] = 1 with the corner [1955]/[1959] (a moved corner goes to 4B03); otherwise the object under the pointer L3[5] becomes the selected object [1969] (6A73, the effect 4 of A4A9).</summary>
    public static void F4965(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3;
        g.Cmp(d[0x1c86], 2, 8); if (g.Zf) { w.Call(0x4b12, g); return; }
        g.Cmp(U16(l, 0), 0x28, 16); if (g.Zf) PutW(l, 0, 0);
        l[3] = Dec8(g, l[3]);
        if (!g.Sf) goto after;
        l[3] = Inc8(g, l[3]); g.Esi = U32(l, 5); g.Test(g.Esi, g.Esi, 32); if (g.Zf) goto after;
        g.Al = l[0xd]; g.Test(g.Al, 4, 8); if (!g.Zf) goto after;
        g.Al &= 3; g.Logic(g.Al, 8); SetCursor(d, 0x400037); g.Al = Dec8(g, g.Al); if (g.Zf) goto after;
        SetCursor(d, 0x54004b); g.Al = Dec8(g, g.Al); if (g.Zf) goto after;
        SetCursor(d, 0x4a0041);
    after:
        g.Cmp(l[0], 0, 8); if (!g.Zf) goto pointer;
        g.Cmp(U16(d, 0x1c35), 0x39, 16);
        if (g.Zf)
        {
            g.Esi = U32(d, 0x1974); g.Test(g.Esi, g.Esi, 32); if (g.Zf) goto pointer;
            g.Cmp(g.Esi, U32(d, 0x1969), 32); if (g.Zf) goto pointer;
            var o = w.ObjectAt(g.Esi); g.Eax = g.Shr(U32(o, 4), 5, 32); g.Ebx = g.Shr(U32(o, 8), 5, 32); FA320(w, g); g.Eax = U32(d, 0x1974); goto select;
        }
        g.Cmp(U16(d, 0x1c35), 0x1c, 16); if (!g.Zf) goto pointer;
        w.Call(0x200b5, g); if (!g.Zf) return;
        d[0x1c88] = g.Ah; Put(d, 0xf0b7, g.Edi); g.Cl = d[0x1906]; g.Ch = d[0x190a]; g.Edx = g.Ecx + 0xd0d; var rec = w.ObjectAt(g.Edi); g.Bl = rec[1]; g.Al = rec[0];
        w.Call(0xb616, g); w.Call(0x4b79, g); return;
    pointer:
        g.Cmp(l[4], 0x11, 8); if (g.Zf) { w.Call(0x4c46, g); return; }
        g.Cmp(l[4], 5, 8); if (g.Zf) { w.Call(0x4c46, g); return; }
        g.Cmp(l[4], 3, 8);
        if (g.Zf)
        {
            g.Eax = U32(d, 0x1955); g.Ebx = U32(d, 0x1959); g.Cmp(d[0x1c86], 0, 8);
            if (g.Zf) { Put(d, 0x1c7e, g.Eax); Put(d, 0x1c82, g.Ebx); d[0x1c86] = 1; return; }
            g.Cmp(U32(d, 0x1c7e), g.Eax, 32); if (!g.Zf) { w.Call(0x4b03, g); return; }
            g.Cmp(U32(d, 0x1c82), g.Ebx, 32); if (!g.Zf) { w.Call(0x4b03, g); return; }
        }
        g.Eax = 0; g.Logic(0, 32); d[0x1c86] = g.Al; g.Cmp(U32(l, 5), g.Eax, 32); if (g.Zf) return;
        g.Test(l[4], 8, 8); if (g.Zf) return;
        g.Test(l[0xd], 4, 8); if (!g.Zf) return;
        g.Eax = U32(l, 5);
    select:
        Put(d, 0x1969, g.Eax); w.Call(0x6a73, g); g.Eax = 4; FA4A9(w, g);
    }

    /// <summary>4B03: the drag ends here: L3[0] = 28h, [1C86] + 1, then 4B12.</summary>
    public static void F4B03(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; PutW(w.L3, 0, 0x28); d[0x1c86] = Inc8(g, d[0x1c86]); F4B12(w, g);
    }

    /// <summary>4B12: the selection box is closed (the button flag 2 not set): the records of the group (200B5) in the box [1955]/[1959] .. [1C7E]/[1C82] (cells) go to B616; none: the group of the settlement is
    /// emptied (20143, 200AB); one: it is the selected object (BA2E, 6A73, the effect 4); more: a new group is made (6ACD, 205FB, 1F3E9, DF7E, the record +2 / +1D / +18, 65DF, the effect 5; at most 0F groups [4C]).</summary>
    public static void F4B12(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3;
        SetCursor(d, 0xe0000); Put(d, 0x1965, 0);
        g.Test(l[4], 2, 8); if (!g.Zf) return;
        d[0x1c86] = 0; w.Call(0x200b5, g); if (!g.Zf) return;
        d[0x1c88] = g.Ah; Put(d, 0xf0b7, g.Edi);
        g.Ecx = g.Shr(U32(d, 0x1955), 5, 32); g.Eax = g.Shr(U32(d, 0x1959), 5, 32); g.Ch = g.Al;
        g.Edx = g.Shr(U32(d, 0x1c7e), 5, 32); g.Eax = g.Shr(U32(d, 0x1c82), 5, 32); g.Dh = g.Al;
        var rec = w.ObjectAt(g.Edi); g.Bl = rec[1]; g.Al = rec[0]; w.Call(0xb616, g);
        F4B79(w, g);
    }

    private static void F4B79(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds;
        g.Test(g.Eax, g.Eax, 32);
        if (!g.Zf)
        {
            g.Cmp(g.Eax, 1, 32);
            if (!g.Zf)
            {
                g.Al = d[0x1c88]; d[0x1c87] = g.Al; w.Call(0x6acd, g); g.Edi = U32(d, 0xf0b7); w.Call(0x205fb, g); g.Bl = 0; g.Logic(0, 8); w.Call(0x1f3e9, g);
                g.Cmp(d[0x4c], 0xf, 8); if (!g.Cf) return;
                d[0x4c] = Inc8(g, d[0x4c]); g.Esi = 0x2be; g.Al = 0; g.Logic(0, 8); w.Call(0xdf7e, g);
                g.Al = d[0x1972]; g.Bl = d[0x1c87]; var e = w.ObjectAt(g.Edi); e[2] = g.Bl; e[0x1d] = g.Al;
                uint pushed = g.Edi; w.Call(0xba2e, g); g.Edi = pushed;
                g.Cmp(g.Esi, w.PoolBase, 32); g.Esi -= w.PoolBase; Put(e, 0x18, g.Esi);
                g.Esi = g.Edi; w.Call(0x65df, g); g.Eax = 5; FA4A9(w, g); return;
            }
            g.Bl = d[0x1c88]; g.Al = d[0x1972]; w.Call(0xba2e, g); w.ObjectAt(g.Esi)[0x1b] = 0; Put(d, 0x1969, g.Esi); w.Call(0x6a73, g); g.Eax = 4; FA4A9(w, g);
        }
        g.Esi = U32(d, 0xed20); g.Edi = g.Esi + 0x9df; g.Bl = d[0x1c88]; w.Call(0x20143, g);
        g.Al = d[0x1c88]; g.Esi = U32(d, 0xed20); w.Call(0x200ab, g);
    }

    /// <summary>4C46: the button is released: L3[4] shifted into the click flags (&gt;&gt; 1 &amp; Ah | 1); on the map (no object) the tool by the pressed key flags [199E]/[19A2] = 4 (D101 = 1: 50F4 with the order 58h), else [1986]/[198A] (4D50, 48h); over an object by its template class (flags +41 &amp; 0Fh = 4: 53D3, 60h;
    /// kind bit 10h and [199E]/[19A2] = 6: 52A6, 58h; [1992]/[1996] = 2: 4E40, 50h).</summary>
    public static void F4C46(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3;
        g.Al = l[4]; g.Al = (byte)g.Shr(g.Al, 1, 8); g.Al = (byte)(g.Al & 0xa); g.Logic(g.Al, 8); g.Al = (byte)(g.Al | 1); l[4] = g.Al;
        g.Cmp(U32(l, 5), 0, 32);
        if (g.Zf)
        {
            g.Cmp(d[0x199e], 0, 8);
            if (!g.Zf) { g.Cmp(d[0x19a2], 4, 8); if (g.Zf)
                {
                    g.Ecx = g.Shr(U32(d, 0x1955), 5, 32); g.Eax = g.Shr(U32(d, 0x1959), 5, 32); g.Ch = g.Al; w.Call(0xd101, g); g.Cmp(g.Eax, 1, 32);
                    if (g.Zf) { d[0x19c0] = 0x58; w.Call(0x50f4, g); return; }
                } }
            g.Cmp(d[0x1986], 0, 8); if (g.Zf) return; g.Cmp(d[0x198a], 1, 8); if (!g.Zf) return;
            d[0x19c0] = 0x48; w.Call(0x4d50, g); return;
        }
        g.Edi = U32(l, 9); var t = w.ObjectAt(g.Edi); g.Eax = U32(t, 0x40); g.Ah = (byte)(g.Ah & 0xf); g.Logic(g.Ah, 8); g.Cmp(g.Ah, 4, 8);
        if (g.Zf) { g.Cmp(d[0x19aa], 0, 8); if (!g.Zf) { g.Cmp(d[0x19ae], 8, 8); if (g.Zf) { d[0x19c0] = 0x60; w.Call(0x53d3, g); return; } } }
        g.Test(l[0xd], 0x10, 8);
        if (!g.Zf) { g.Cmp(d[0x199e], 0, 8); if (!g.Zf) { g.Cmp(d[0x19a2], 6, 8); if (g.Zf) { d[0x19c0] = 0x58; w.Call(0x52a6, g); return; } } }
        g.Cmp(d[0x1992], 0, 8); if (g.Zf) return; g.Cmp(d[0x1996], 2, 8); if (!g.Zf) return;
        d[0x19c0] = 0x50; w.Call(0x4e40, g);
    }

    /// <summary>4D41: the click state: L3[2] = 1, L3[12] = 1.</summary>
    public static void F4D41(IUnitActionWorld w, ActionRegs g) { w.L3[2] = 1; w.L3[0x12] = 1; }

    /// <summary>4D50: the tool «go to the point» (the key [1C35] = 1Dh shows the cursor 1D0000h): the left button over the map (flag 8): effect 4, the point [1955]/[1959] becomes the goal +22/+23 of the selected object [1969] with the order [19C0] (a group [1C89]: 20232 and 1F464), then 4938; otherwise the key 1 or the right button ends the tool.</summary>
    public static void F4D50(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3;
        g.Test(l[4], 1, 8);
        if (!g.Zf)
        {
            SetCursor(d, 0x93008a); g.Cmp(d[0x1c35], 0x1d, 8); if (g.Zf) SetCursor(d, 0x1d0000);
            g.Test(l[4], 8, 8);
            if (!g.Zf)
            {
                g.Eax = 7; g.Eax = 4; FA4A9(w, g);
                g.Ecx = g.Shr(U32(d, 0x1955), 5, 32); g.Eax = g.Shr(U32(d, 0x1959), 5, 32); g.Ch = g.Al; g.Ah = d[0x19c0];
                g.Cmp(d[0x1c89], 0, 8);
                if (g.Zf) { g.Esi = U32(d, 0x1969); var o = w.ObjectAt(g.Esi); o[0x22] = g.Cl; o[0x23] = g.Ch; o[0x14] &= 7; o[0x14] |= g.Ah; g.Logic(o[0x14], 8); w.Call(0x4938, g); return; }
                uint pushed = g.Ecx; g.Edi = U32(d, 0xed20); g.Add(g.Edi, 0x9df); g.Edi += 0x9df; g.Bl = d[0x1c87]; w.Call(0x20232, g); g.Ecx = pushed;
                if (!g.Zf) { g.Bl = 0x80; w.Call(0x1f464, g); }
                w.Call(0x4938, g); return;
            }
        }
        EndTool(w, g);
    }

    private static void EndTool(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Cmp(U16(d, 0x1c35), 1, 16); if (g.Zf) { w.Call(0x4938, g); return; }
        g.Test(w.L3[4], 0x10, 8); if (!g.Zf) { w.Call(0x4938, g); return; }
    }

    /// <summary>4E27: the tool state 2: L3[2] = 2, L3[12] = 0, L3[0E] = 0.</summary>
    public static void F4E27(IUnitActionWorld w, ActionRegs g) { w.L3[2] = 2; w.L3[0x12] = 0; Put(w.L3, 0xe, 0); }

    /// <summary>4E40: the tool «attack or talk»: the right-button cursors (6D0064h, 810078h, 77006Eh), the object L3[5] under the pointer as the target +1C with the order [19C0] (a group: 20232, then the area around it by 219C3 and 1F306); a friendly one (kind bits = 2) is
    /// spoken to (EE69 with 93h, EDD5; the second press 5), remembered in L3[0E].</summary>
    public static void F4E40(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3;
        g.Test(l[4], 1, 8); if (g.Zf) { EndTool(w, g); return; }
        SetCursor(d, 0x6d0064); g.Esi = U32(l, 5); g.Test(g.Esi, g.Esi, 32); if (g.Zf) { EndTool(w, g); return; }
        g.Al = l[0xd]; g.Test(g.Al, 8, 8); if (!g.Zf) { EndTool(w, g); return; }
        g.Cmp(g.Esi, U32(d, 0x1969), 32); if (g.Zf) { EndTool(w, g); return; }
        var o = w.ObjectAt(g.Esi);
        g.Cmp(d[0x1c89], 0, 8);
        if (!g.Zf)
        {
            g.Ah = o[3]; g.Cmp(g.Ah, d[0x1972], 8);
            if (g.Zf) { g.Ah = o[0x1b]; g.Cmp(g.Ah, d[0x1c87], 8); if (g.Zf) { EndTool(w, g); return; } }
        }
        SetCursor(d, 0x810078); g.Al &= 3; g.Logic(g.Al, 8); g.Cmp(g.Al, 2, 8);
        if (!g.Zf) { g.Cmp(g.Al, 3, 8); if (g.Zf) SetCursor(d, 0x77006e); }
        g.Test(l[4], 8, 8); if (g.Zf) { EndTool(w, g); return; }
        g.Eax = 8; g.Eax = 4; FA4A9(w, g);
        g.Al = l[0xd]; g.Al &= 3; g.Logic(g.Al, 8); g.Cmp(g.Al, 2, 8);
        if (g.Zf)
        {
            g.Cmp(U32(l, 0xe), g.Esi, 32);
            if (!g.Zf)
            {
                Put(l, 0xe, g.Esi); g.Al = 0x93; g.Bl = d[0x1972]; g.Ah = g.Bl; w.Call(0xee69, g);
                if (!g.Zf) w.ObjectAt(g.Edi)[0] = 0xff;
                w.Call(0xedd5, g); EndTool(w, g); return;
            }
            g.Al = 5; g.Bl = d[0x1972]; g.Ah = o[3]; w.Call(0xedd5, g); g.Esi = U32(l, 5); o = w.ObjectAt(g.Esi);
        }
        g.Ecx = g.Esi; g.Cmp(g.Ecx, w.PoolBase, 32); g.Ecx -= w.PoolBase; g.Ah = d[0x19c0];
        g.Cmp(d[0x1c89], 0, 8);
        if (!g.Zf)
        {
            g.Edi = U32(d, 0xed20); g.Add(g.Edi, 0x9df); g.Edi += 0x9df; g.Bl = d[0x1c87]; w.Call(0x20232, g);
            if (g.Zf) { w.Call(0x4938, g); return; }
            g.Ecx = g.Shr(U32(o, 4), 5, 32); g.Eax = g.Shr(U32(o, 8), 5, 32); g.Ch = g.Al; g.Dl = 0xa; g.Dh = 0xa; w.Call(0x219c3, g); g.Bl = 0x80; w.Call(0x1f306, g); w.Call(0x4938, g); return;
        }
        g.Esi = U32(d, 0x1969); var sel = w.ObjectAt(g.Esi); Put(sel, 0x1c, g.Ecx); sel[0x14] &= 7; sel[0x14] |= g.Ah; g.Logic(sel[0x14], 8); w.Call(0x4938, g);
    }

    /// <summary>4FCE: starts the area tool (a spell of the selected object): the template +22 of the army of the object [1969] is remembered ([284D], [284E], its record [2849]), the rectangle [4F0] is cleared, AE57, the tool state 3.</summary>
    public static void F4FCE(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Esi = U32(d, 0x1969); var o = w.ObjectAt(g.Esi); g.Al = o[0x22]; g.Ah = o[0]; d[0x284d] = g.Al; d[0x284e] = g.Ah;
        F68A0(w, g); Put(d, 0x2849, g.Edi); Put(d, 0x4f0, 0); w.Call(0xae57, g); w.L3[2] = 3; w.L3[0x12] = 0;
    }

    /// <summary>500D: the area tool: the cursor 2C0023h (400037h when [27F4] allows), the left button (flag 8) drags the rectangle [4F0]/[4F4] .. [4F8]/[4FC] (a template with flag 80h only once started) and with effect 6 stores it as words at +29..+2F of the selected object with the order [19C0]; then 4938.</summary>
    public static void F500D(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3;
        g.Test(l[4], 1, 8);
        if (!g.Zf)
        {
            SetCursor(d, 0x2c0023); g.Cmp(d[0x27f4], 0, 8);
            if (!g.Zf)
            {
                SetCursor(d, 0x400037); g.Test(l[4], 8, 8);
                if (!g.Zf)
                {
                    g.Cmp(U32(d, 0x4f0), 0, 32);
                    bool go = !g.Zf;
                    if (!go)
                    {
                        g.Eax = U32(d, 0x4f8); Put(d, 0x4f0, g.Eax); g.Eax = U32(d, 0x4fc); Put(d, 0x4f4, g.Eax);
                        g.Edi = U32(d, 0x2849); g.Test(U32(w.ObjectAt(g.Edi), 0x40), 0x80, 32); go = g.Zf;
                    }
                    if (go)
                    {
                        g.Eax = 6; FA4A9(w, g); g.Esi = U32(d, 0x1969); var o = w.ObjectAt(g.Esi);
                        g.Eax = U32(d, 0x4f0); PutW(o, 0x29, (ushort)g.Eax); g.Eax = U32(d, 0x4f4); PutW(o, 0x2b, (ushort)g.Eax); g.Eax = U32(d, 0x4f8); PutW(o, 0x2d, (ushort)g.Eax); g.Eax = U32(d, 0x4fc); PutW(o, 0x2f, (ushort)g.Eax);
                        g.Al = d[0x19c0]; o[0x14] &= 7; o[0x14] |= g.Al; g.Logic(o[0x14], 8); w.Call(0x4938, g); return;
                    }
                }
            }
        }
        EndTool(w, g);
    }

    /// <summary>50E5: the tool state 4.</summary>
    public static void F50E5(IUnitActionWorld w, ActionRegs g) { w.L3[2] = 4; w.L3[0x12] = 0; }

    /// <summary>51AC: the tool «ready the cursor» (state 5): D182 gives the template table of the selected object ESI = [1969] (AH = 6), the byte at [EDX + 7 + 4 · (((cursor [19C0] >> 3) & 7) - 4)] and the first byte of the object go to DS:284D/284E, 68A0 gives the pointer for [2849], the counter [4F0] is cleared, AE57 draws, L3[2] = 5, L3[12] = 0.</summary>
    public static void F51AC(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Esi = U32(d, 0x1969); g.Ah = 6; w.Call(0xd182, g);
        g.Eax = 4; g.Ah = d[0x19c0]; g.Ah = (byte)(g.Ah >> 3); g.Ah &= 7; g.Ah = (byte)(g.Ah - 4); Mul8(g, g.Al, g.Ah);
        g.Edx = g.Edx + g.Eax + 7; g.Al = d[g.Edx]; g.Ah = w.ObjectAt(g.Esi)[0]; d[0x284d] = g.Al; d[0x284e] = g.Ah;
        w.Call(0x68a0, g); Put(d, 0x2849, g.Edi); Put(d, 0x4f0, 0); w.Call(0xae57, g); w.L3[2] = 5; w.L3[0x12] = 0;
    }

    /// <summary>AE57: the cursor frame of the template [2849]: the bits 0 of the bytes +3 and +4 (shifted to 10h) go to [27EC] and [27F0], the bytes themselves halved (EBX, ECX) draw the two frames 1DB64 (ESI = 27F5 to EDI = 279C and ESI = 281B to EDI = 27C4).</summary>
    public static void FAE57(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Edi = U32(d, 0x2849); var o = w.ObjectAt(g.Edi); g.Ebx = 0; g.Ecx = 0; g.Eax = 0;
        g.Bh = o[3]; g.Ch = o[4]; g.Al = g.Bh; g.Al = (byte)(g.Al << 4); g.Al &= 0x10; Put(d, 0x27ec, g.Eax);
        g.Al = g.Ch; g.Al = (byte)(g.Al << 4); g.Al &= 0x10; Put(d, 0x27f0, g.Eax);
        g.Ebx = g.Shr(g.Ebx, 1, 32); g.Ecx = g.Shr(g.Ecx, 1, 32); g.Esi = 0x27f5; g.Edi = 0x279c;
        uint ebx = g.Ebx, ecx = g.Ecx; w.Call(0x1db64, g); g.Ecx = ecx; g.Ebx = ebx;
        g.Esi = 0x281b; g.Edi = 0x27c4; w.Call(0x1db64, g);
    }

    /// <summary>AEA8: the cursor of the selected object: AF07 chooses the frame (AL/AH = [284D]/[284E], BL = the owner +3, ECX/EDX = [2841]/[2845]); with a failure the second frame 27C4 (EDI = FEh) and the bit 0 of [27F4] cleared, otherwise 279C (EDI = FDh); 1D970 draws at the point minus the view [190E]/[1912].</summary>
    public static void FAEA8(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Esi = U32(d, 0x1969); g.Al = d[0x284d]; g.Ah = d[0x284e]; g.Bl = w.ObjectAt(g.Esi)[3]; g.Ecx = U32(d, 0x2841); g.Edx = U32(d, 0x2845);
        w.Call(0xaf07, g); g.Esi = 0x279c; g.Edi = 0xfd;
        if (!g.Zf) { d[0x27f4] &= 0xfe; g.Esi = 0x27c4; g.Edi = 0xfe; }
        g.Eax = U32(d, 0x2841); g.Ebx = U32(d, 0x2845); g.Cmp(g.Eax, U32(d, 0x190e), 32); g.Eax -= U32(d, 0x190e); g.Cmp(g.Ebx, U32(d, 0x1912), 32); g.Ebx -= U32(d, 0x1912);
        w.Call(0x1d970, g);
    }

    /// <summary>50F4: the tool «go and do» (cursor 9B0094h): the left button over the map: effect 4, the point [1955]/[1959] as +32/+33 of the selected object with the order [19C0] (a group: 20232, 1F62B); then 4938.</summary>
    public static void F50F4(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3;
        g.Test(l[4], 1, 8);
        if (!g.Zf)
        {
            SetCursor(d, 0x9b0094); g.Test(l[4], 8, 8);
            if (!g.Zf)
            {
                g.Eax = 7; g.Eax = 4; FA4A9(w, g);
                g.Ecx = g.Shr(U32(d, 0x1955), 5, 32); g.Eax = g.Shr(U32(d, 0x1959), 5, 32); g.Ch = g.Al; g.Ah = d[0x19c0];
                g.Cmp(d[0x1c89], 0, 8);
                if (g.Zf) { g.Esi = U32(d, 0x1969); var o = w.ObjectAt(g.Esi); o[0x32] = g.Cl; o[0x33] = g.Ch; o[0x14] &= 7; o[0x14] |= g.Ah; g.Logic(o[0x14], 8); w.Call(0x4938, g); return; }
                uint pushed = g.Ecx; g.Edi = U32(d, 0xed20); g.Add(g.Edi, 0x9df); g.Edi += 0x9df; g.Bl = d[0x1c87]; w.Call(0x20232, g); g.Ecx = pushed;
                if (!g.Zf) w.Call(0x1f62b, g);
                w.Call(0x4938, g); return;
            }
        }
        EndTool(w, g);
    }

    /// <summary>520B: the second point of the area tool (state 5): the point [4F8]/[4FC] (cells) as +32/+33 of the selected object with the order [19C0]; the cursors 400037h / 2C0023h by [27F4]; then 4938.</summary>
    public static void F520B(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3;
        g.Test(l[4], 1, 8);
        if (!g.Zf)
        {
            SetCursor(d, 0x400037); g.Cmp(d[0x27f4], 0, 8);
            if (!g.Zf)
            {
                SetCursor(d, 0x2c0023); g.Test(l[4], 8, 8);
                if (!g.Zf)
                {
                    g.Esi = U32(d, 0x1969); var o = w.ObjectAt(g.Esi); g.Eax = g.Shr(U32(d, 0x4f8), 5, 32); o[0x32] = g.Al; g.Eax = g.Shr(U32(d, 0x4fc), 5, 32); o[0x33] = g.Al;
                    g.Al = d[0x19c0]; o[0x14] &= 7; o[0x14] |= g.Al; g.Logic(o[0x14], 8); w.Call(0x4938, g); return;
                }
            }
        }
        EndTool(w, g);
    }

    /// <summary>5297: the tool state 6.</summary>
    public static void F5297(IUnitActionWorld w, ActionRegs g) { w.L3[2] = 6; w.L3[0x12] = 0; }

    /// <summary>52A6: the tool «spell on a friend» (cursor 36002Dh, 2C0023h on a unit of kind bit 10h that is not of kind 2): the left button (flag 8): effect 4, the object L3[5] as the target +1C of the selected object with the order [19C0] (a group: 20232, then 1FB9C with 96h); then 4938.</summary>
    public static void F52A6(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3;
        g.Test(l[4], 1, 8); if (g.Zf) { EndTool(w, g); return; }
        SetCursor(d, 0x36002d); g.Esi = U32(l, 5); g.Test(g.Esi, g.Esi, 32); if (g.Zf) { EndTool(w, g); return; }
        g.Al = l[0xd]; g.Test(g.Al, 0x10, 8); if (g.Zf) { EndTool(w, g); return; }
        g.Al &= 3; g.Logic(g.Al, 8); g.Cmp(g.Al, 2, 8); if (g.Zf) { EndTool(w, g); return; }
        g.Cmp(g.Al, 3, 8); SetCursor(d, 0x2c0023);
        g.Test(l[4], 8, 8); if (g.Zf) { EndTool(w, g); return; }
        g.Eax = 9; g.Eax = 4; FA4A9(w, g);
        SpellOrder(w, g, 0x1fb9c, true);
    }

    private static void SpellOrder(IUnitActionWorld w, ActionRegs g, uint service, bool withBl)
    {
        var d = w.Ds; g.Ecx = g.Esi; g.Cmp(g.Ecx, w.PoolBase, 32); g.Ecx -= w.PoolBase; g.Ah = d[0x19c0];
        g.Cmp(d[0x1c89], 0, 8);
        if (!g.Zf)
        {
            uint pushed = g.Esi; g.Edi = U32(d, 0xed20); g.Add(g.Edi, 0x9df); g.Edi += 0x9df; g.Bl = d[0x1c87]; w.Call(0x20232, g); g.Edx = pushed;
            if (g.Zf) { w.Call(0x4938, g); return; }
            if (withBl) g.Bl = 0x96;
            w.Call(service, g); w.Call(0x4938, g); return;
        }
        g.Esi = U32(d, 0x1969); var sel = w.ObjectAt(g.Esi); Put(sel, 0x1c, g.Ecx); sel[0x14] &= 7; sel[0x14] |= g.Ah; g.Logic(sel[0x14], 8); w.Call(0x4938, g);
    }

    /// <summary>538D: the tool state 7 and the message 1D1h (DEE4).</summary>
    public static void F538D(IUnitActionWorld w, ActionRegs g)
    {
        w.L3[2] = 7; w.L3[0x12] = 0; uint esi = g.Esi; g.Esi = 0x1d1; w.Call(0xdee4, g); g.Esi = esi;
    }

    /// <summary>53A8: the end of a tool: the key 1 or the right button (flag 10h) goes to 4938.</summary>
    public static void F53A8(IUnitActionWorld w, ActionRegs g) => EndTool(w, g);

    /// <summary>53C4: the tool state 8.</summary>
    public static void F53C4(IUnitActionWorld w, ActionRegs g) { w.L3[2] = 8; w.L3[0x12] = 0; }

    /// <summary>53D3: the tool «spell on a thing» (cursor 36002Dh, 2C0023h on an object whose template class +41 &amp; 0F is 4): as 52A6 with 1F71A.</summary>
    public static void F53D3(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3;
        g.Test(l[4], 1, 8); if (g.Zf) { EndTool(w, g); return; }
        SetCursor(d, 0x36002d); g.Esi = U32(l, 5); g.Test(g.Esi, g.Esi, 32); if (g.Zf) { EndTool(w, g); return; }
        g.Edi = U32(l, 9); var t = w.ObjectAt(g.Edi); g.Eax = U32(t, 0x40); g.Ah = (byte)(g.Ah & 0xf); g.Logic(g.Ah, 8); g.Cmp(g.Ah, 4, 8); if (!g.Zf) { EndTool(w, g); return; }
        SetCursor(d, 0x2c0023); g.Test(l[4], 8, 8); if (g.Zf) { EndTool(w, g); return; }
        g.Eax = 7; g.Eax = 4; FA4A9(w, g);
        SpellOrder(w, g, 0x1f71a, false);
    }

    /// <summary>5679: the tool «gift» (cursor 6D0064h, 77006Eh over an object not of kind bit 8): the left button (flag 8): effect 4, 124FD with the item [4190] and the player [1972], then 4938; every other press first calls 124D8 and the key 1 or the right button ends the tool.</summary>
    public static void F5679(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3;
        g.Test(l[4], 1, 8);
        if (!g.Zf)
        {
            SetCursor(d, 0x6d0064); g.Edi = U32(l, 5); g.Test(g.Edi, g.Edi, 32);
            if (!g.Zf)
            {
                g.Al = l[0xd]; g.Test(g.Al, 8, 8);
                if (g.Zf)
                {
                    SetCursor(d, 0x77006e); g.Test(l[4], 8, 8);
                    if (!g.Zf)
                    {
                        g.Eax = 8; g.Eax = 4; FA4A9(w, g); g.Al = d[0x4190]; g.Ah = d[0x1972]; w.Call(0x124fd, g); w.Call(0x4938, g); return;
                    }
                }
            }
        }
        g.Al = d[0x4190]; g.Dl = d[0x1972]; w.Call(0x124d8, g);
        EndTool(w, g);
    }

    // ---- the queue of the pending messages in LE:3 (header 7D4, records of 1Eh bytes from 7F2, +1 the id; the end pointer at 0DCE) ----
    /// <summary>DEC8: empties the queue (the end is the first record place) and clears the flag at LE:3[0DD3].</summary>
    public static void FDEC8(IUnitActionWorld w, ActionRegs g)
    {
        var l = w.L3; Put(l, 0xdce, 0x7f2); l[0xdd3] = 0;
    }

    /// <summary>DF2A: removes the records of the id AL from the queue (consecutive ones together, the tail moves down) and clears the mode L3[0] when it is that id; ESI = the header (7D4) at the end.</summary>
    public static void FDF2A(IUnitActionWorld w, ActionRegs g)
    {
        var l = w.L3; g.Cmp(g.Al, l[0], 8); if (g.Zf) PutW(l, 0, 0);
        for (; ; )
        {
            g.Esi = 0x7d4; g.Ecx = U32(l, 0xdce);
            for (; ; )
            {
                g.Esi += 0x1e; g.Cmp(g.Esi, g.Ecx, 32);
                if (!g.Cf) { g.Esi = 0x7d4; return; }
                g.Cmp(g.Al, l[(int)g.Esi + 1], 8);
                if (g.Zf) break;
            }
            g.Edi = g.Esi;
            do { g.Esi += 0x1e; g.Cmp(g.Esi, g.Ecx, 32); if (!g.Cf) break; g.Cmp(g.Al, l[(int)g.Esi + 1], 8); } while (g.Zf);
            g.Ecx -= g.Esi; g.Ah = g.Cl; uint count = g.Ecx; g.Ah = (byte)(g.Ah & 3); g.Logic(g.Ah, 8);
            for (uint k = 0; k < count; k++) l[(int)g.Edi + (int)k] = l[(int)g.Esi + (int)k];
            g.Edi += count; g.Esi += count; g.Ecx = 0;
            Put(l, 0xdce, g.Edi);
        }
    }

    /// <summary>DF7E: puts the 1Eh-byte record at ESI into the queue when 1Eh bytes are left before 0DCE: AL ≠ 0 behind the last record, AL = 0 in front of the first one (the others move up); EAX = 2, ECX = 0.</summary>
    public static void FDF7E(IUnitActionWorld w, ActionRegs g)
    {
        var l = w.L3; g.Ebx = 0xdce; g.Edi = U32(l, 0xdce); g.Ecx = 0x1e; g.Ebx -= g.Edi; g.Cmp(g.Ebx, g.Ecx, 32); if (g.Cf) return;
        g.Ebx = g.Edi + g.Ecx; Put(l, 0xdce, g.Ebx); g.Test(g.Al, g.Al, 8);
        uint edi = g.Edi;
        if (g.Zf)
        {
            uint total = g.Edi - 0x7f2;
            for (int k = (int)total - 1; k >= 0; k--) l[0x7f2 + k + 0x1e] = l[0x7f2 + k];
            edi = 0x7f2;
        }
        for (int k = 0; k < 0x1e; k++) l[(int)edi + k] = w.Peek(g.Esi + (uint)k);
        g.Edi = edi;
        g.Eax = 2; g.Logic(2, 32); g.Ecx = 0;
    }

    // ---- the widgets of the panel that follow the selected object (5960, 5AF6, 5B55) ----
    /// <summary>5960: the widget EDI shows nothing (+1A = +1B = FFh).</summary>
    public static void F5960(IUnitActionWorld w, ActionRegs g, byte[] widget) { widget[0x1a] = 0xff; widget[0x1b] = 0xff; }

    /// <summary>5AF6: the widget ESI keeps its object template +1A = +E of the selected object [1969]; the object that died, is hidden from the player (the fog [18DA]) or is not visible (+17 bit 80h) clears the panel (672F, DF2A with 29h); another template clears it and sends 4E (DEDA).</summary>
    public static void F5AF6(IUnitActionWorld w, ActionRegs g, byte[] widget)
    {
        var d = w.Ds; g.Edi = U32(d, 0x1969); var o = w.ObjectAt(g.Edi); g.Al = o[0xe]; g.Cmp(widget[0x1a], g.Al, 8);
        if (!g.Zf) { w.Call(0x672f, g); g.Esi = 0x4e; w.Call(0xdeda, g); return; }
        g.Cmp(o[0x19], 0, 8);
        bool clear = g.Zf || (g.Sf != g.Of);
        if (!clear)
        {
            g.Ebx = g.Shr(U32(o, 4), 5, 32); g.Eax = g.Shr(U32(o, 8), 5, 32); Mul8(g, g.Al, d[0x18de]); g.Add(g.Ebx, g.Eax); g.Ebx += g.Eax; g.Add(g.Ebx, U32(d, 0x18da)); g.Ebx += U32(d, 0x18da);
            g.Eax = d[0x1972]; uint word = (uint)(w.Peek(g.Ebx) | w.Peek(g.Ebx + 1) << 8 | w.Peek(g.Ebx + 2) << 16 | w.Peek(g.Ebx + 3) << 24); g.Cf = ((word >> (int)(g.Eax & 31)) & 1) != 0;
            clear = g.Cf;
            if (!clear) { g.Test(U16(o, 0x17), 0x80, 16); if (g.Zf) return; }
        }
        w.Call(0x672f, g); g.Al = 0x29; FDF2A(w, g);
    }

    /// <summary>5B55: the tick of a panel widget (+1C the new frame, +1D the shown one, +12 the hint timer, +1B the age): a new frame restarts the timer (674F); every second update the timer runs out and the hint text of the frame (the table [1978 + 0C · frame] + 2) is measured (7012) and drawn (6C66); then the widget is compared with the selected object [1969]
    /// (alone: +1A with 66DF; in a group [1C89]: B9EB) and a mismatch rebuilds the panel (5787, 66E9) or clears it (66E9, 674F, 6EB2, 4938, DF2A with 2Bh).</summary>
    public static void F5B55(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds;
        g.Al = r[0x1c]; r[0x1c] = 0xff; g.Cmp(g.Al, r[0x1d], 8);
        if (!g.Zf) { r[0x1d] = g.Al; r[0x12] = 0xc; uint esi = g.Esi; w.Call(0x674f, g); g.Esi = esi; }
        g.Cmp(r[0x1d], 0xff, 8);
        if (!g.Zf)
        {
            g.Cmp(r[0x12], 0, 8);
            if (!g.Zf)
            {
                g.Test(d[0x19e7], 1, 8);
                if (!g.Zf)
                {
                    r[0x12] = Dec8(g, r[0x12]);
                    if (g.Zf)
                    {
                        uint esi = g.Esi; g.Eax = 0xc; Mul8(g, g.Al, r[0x1d]); g.Esi = (g.Eax & 0xffff) + 0x1978; g.Esi = U32(d, (int)g.Esi + 2); F7012(w, g);
                        g.Ecx = 0x1c0; g.Ecx -= g.Ebx; g.Edx = 0x1d0; w.Call(0x6c66, g); g.Esi = esi;
                    }
                }
            }
        }
        r[0x1b] = Inc8(g, r[0x1b]); g.Cmp(d[0x1c89], 0, 8);
        if (g.Zf)
        {
            g.Edi = g.Esi; g.Esi = U32(d, 0x1969); var o = w.ObjectAt(g.Esi); g.Cmp(o[0x19], 0, 8);
            if (!(g.Zf || (g.Sf != g.Of))) { g.Test(U16(o, 0x17), 0x80, 16); if (g.Zf) { F66DF(w, g, o); g.Esi = g.Edi; g.Cmp(r[0x1a], g.Al, 8); if (g.Zf) return; Rebuild(w, g); return; } }
            Clear5B55(w, g); return;
        }
        g.Test(r[0x1b], 0x3f, 8); if (!g.Zf) return;
        g.Al = d[0x1972]; g.Bl = d[0x1c87]; uint saved = g.Esi; w.Call(0xb9eb, g); g.Esi = saved; g.Test(g.Eax, g.Eax, 32);
        if (g.Zf) { Clear5B55(w, g); return; }
        g.Eax--; g.Zf = g.Eax == 0; if (g.Zf) { Rebuild(w, g); return; }
        g.Cmp(r[0x1a], g.Cl, 8); if (g.Zf) return;
        Rebuild(w, g);
    }
    private static void Rebuild(IUnitActionWorld w, ActionRegs g) { g.Edi = g.Esi; w.Call(0x5787, g); w.Call(0x66e9, g); }
    private static void Clear5B55(IUnitActionWorld w, ActionRegs g) { w.Call(0x66e9, g); w.Call(0x674f, g); w.Call(0x6eb2, g); w.Call(0x4938, g); g.Al = 0x2b; FDF2A(w, g); }

    /// <summary>5C2E: the slope of the panel in the state 7 (L3[2]): the height EBX = the word +1A of the record ESI and the length 36h · (the count +1C) + EBX decide, with the flags [19E7] and the distance [1951], the step of the speed +1D of the record (to +/-7 or +/-F9h, otherwise it comes back to 0); with the bit 2 of [19E7] the speed moves EBX and the dword +8 of every following record (+1E each); in any other state it goes on into 5CC2 (the panel is closed).</summary>
    public static void F5C2E(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Cmp(w.L3[2], 7, 8); if (!g.Zf) { F5CC2(w, g); return; }
        uint e = g.Esi; byte B(uint o) => w.Peek(e + o);
        g.Ebx = (uint)(int)(short)(B(0x1a) | B(0x1b) << 8); g.Eax = 0x36; g.Cl = B(0x1c); Mul8(g, g.Al, g.Cl); g.Add(g.Eax, g.Ebx); g.Eax += g.Ebx;
        g.Test(B(3), 1, 8);
        if (g.Zf) goto L91;
        g.Test(d[0x19e7], 1, 8); if (g.Zf) goto La0;
        g.Cmp(g.Eax, 0x1a2, 32);
        if (g.Sf == g.Of)
        {
            g.Cmp(U32(d, 0x1951), 0x19a, 32);
            if (!g.Cf)
            {
                g.Cmp(B(0x1d), 0xf9, 8); if (g.Zf || g.Sf != g.Of) goto La0;
                w.Poke(e + 0x1d, Dec8(g, B(0x1d))); goto La0;
            }
        }
        g.Cmp(g.Ebx, 0x1e, 32); if (!(g.Zf || g.Sf != g.Of)) goto L91;
        g.Cmp(U32(d, 0x1951), 0x46, 32); if (!g.Cf && !g.Zf) goto L91;
        g.Cmp(B(0x1d), 7, 8); if (g.Sf == g.Of) goto La0;
        w.Poke(e + 0x1d, Inc8(g, B(0x1d))); goto La0;
    L91:
        g.Cmp(B(0x1d), 0, 8); if (g.Zf) goto La0;
        if (!g.Sf) w.Poke(e + 0x1d, Sub8(g, B(0x1d), 2));
        w.Poke(e + 0x1d, Inc8(g, B(0x1d)));
    La0:
        g.Test(d[0x19e7], 2, 8); if (g.Zf) return;
        g.Edx = (uint)(int)(sbyte)B(0x1d); g.Test(g.Edx, g.Edx, 32); if (g.Zf) return;
        g.Add(g.Ebx, g.Edx); g.Ebx += g.Edx; w.Poke(e + 0x1a, (byte)g.Ebx); w.Poke(e + 0x1b, (byte)(g.Ebx >> 8));
        do
        {
            g.Add(e, 0x1e); e += 0x1e; g.Esi = e; uint v = (uint)(w.Peek(e + 8) | w.Peek(e + 9) << 8 | w.Peek(e + 10) << 16 | w.Peek(e + 11) << 24); g.Add(v, g.Edx); v += g.Edx;
            for (uint k = 0; k < 4; k++) w.Poke(e + 8 + k, (byte)(v >> (8 * (int)k)));
            g.Cl = Dec8(g, g.Cl);
        } while (!g.Zf);
    }

    /// <summary>5CD9: the panel picture of the record ESI: when the wanted number +1A differs from the shown +1B it is shown (674F clears, +1B = +1A); a number other than FFh asks 68A0 for the template EDI (AH = the kind +1C), 6C66 draws its name (ECX = 14h, EDX = 1D0h, ESI = the string +7 of the template) and, with the flag +1D, the four prices (+7C, +84, +80, +88 of the template; pictures 138h, 137h, 139h, 13Ah) go through 5D68 one below the other from ECX = AAh; at the end +1A = FFh.</summary>
    public static void F5CD9(IUnitActionWorld w, ActionRegs g)
    {
        uint e = g.Esi; g.Al = w.Peek(e + 0x1a); g.Cmp(g.Al, w.Peek(e + 0x1b), 8);
        if (!g.Zf)
        {
            w.Poke(e + 0x1b, g.Al); w.Call(0x674f, g); g.Esi = e; g.Al = w.Peek(e + 0x1a); w.Poke(e + 0x1b, g.Al); g.Cmp(g.Al, 0xff, 8);
            if (!g.Zf)
            {
                g.Ah = w.Peek(e + 0x1c); w.Call(0x68a0, g); uint edi = g.Edi;
                g.Esi = edi + 7; g.Add(g.Esi, Peek32(w, g.Esi)); g.Esi += Peek32(w, g.Esi); g.Ecx = 0x14; g.Edx = 0x1d0; w.Call(0x6c66, g);
                g.Edi = edi; g.Esi = e; g.Ecx = 0xaa; g.Cmp(w.Peek(e + 0x1d), 0, 8);
                if (!g.Zf)
                {
                    foreach (var (off, pic) in new[] { (0x7cu, 0x138u), (0x84u, 0x137u), (0x80u, 0x139u), (0x88u, 0x13au) }) { g.Ebx = Peek32(w, g.Edi + off); g.Eax = pic; F5D68(w, g); }
                    g.Esi = e;
                }
            }
        }
        w.Poke(e + 0x1a, 0xff);
    }

    /// <summary>5D68: one price: EBX = the picture of the template (0 - nothing), EAX = its number (ECX = the row): the picture [191A] (1E133), the blit 1E098 (EBX = 1D0h, ECX = 20h, EDX = 10h), the figure AC23 (ECX + 28h, EDX = 1D0h); the next row ECX = EAX + 1Eh.</summary>
    public static void F5D68(IUnitActionWorld w, ActionRegs g)
    {
        g.Test(g.Ebx, g.Ebx, 32); if (g.Zf) return;
        uint edi = g.Edi, ebx = g.Ebx, ecx = g.Ecx; g.Esi = U32(w.Ds, 0x191a); w.Call(0x1e133, g);
        g.Eax = ecx; g.Ebx = 0x1d0; g.Ecx = 0x20; g.Edx = 0x10; w.Call(0x1e098, g);
        g.Ecx = ecx; g.Eax = ebx; g.Add(g.Ecx, 0x28); g.Ecx += 0x28; g.Edx = 0x1d0; w.Call(0xac23, g); g.Ecx = g.Eax + 0x1e; g.Edi = edi;
    }

    /// <summary>C010: one step of the palette fade of the record ESI (the colour number +0, the start +1..3, the end +4..6, the length +7, the step +8, the position +9): the quotient position / length / 2 (BL) blends every component: out 3C8h = the number, then 3C9h three times start + ((end - start) * BL * 2 >> 8); the position moves by the step and the step is reversed at the end (+8 negated when position + step is not below the length).</summary>
    public static void FC010(IUnitActionWorld w, ActionRegs g)
    {
        uint e = g.Esi; byte B(uint o) => w.Peek(e + o);
        g.Eax = 0; g.Ah = B(9); uint ax = g.Eax & 0xffff, dv = B(7); g.Al = (byte)(ax / dv); g.Ah = (byte)(ax % dv); g.Al = (byte)(g.Al >> 1); g.Bl = g.Al;
        w.Out(0x3c8, B(0)); g.Edx = (g.Edx & 0xffff0000) | 0x3c9;
        for (uint k = 0; k < 3; k++)
        {
            g.Al = (byte)(B(4 + k) - B(1 + k)); int r = (sbyte)g.Al * (sbyte)g.Bl; g.Eax = (g.Eax & 0xffff0000) | (ushort)r; g.Eax += g.Eax;
            g.Al = B(1 + k); g.Al = (byte)(g.Al + g.Ah); w.Out(0x3c9, g.Al);
        }
        g.Al = B(8); g.Al = Add8(g, g.Al, B(9)); g.Cmp(g.Al, B(7), 8);
        if (!g.Cf) w.Poke(e + 8, (byte)-B(8));
        g.Al = B(8); w.Poke(e + 9, Add8(g, B(9), g.Al));
    }

    /// <summary>C06E: every second tick of the timer [19E4] (against [52D]) the three palette records DS:52E, 538 and 542 take a fade step (C010).</summary>
    public static void FC06E(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Al = d[0x19e4]; g.Ah = g.Al; g.Al = Sub8(g, g.Al, d[0x52d]); g.Cmp(g.Al, 2, 8); if (g.Cf) return;
        d[0x52d] = g.Ah; foreach (uint a in new uint[] { 0x52e, 0x538, 0x542 }) { g.Esi = a; FC010(w, g); }
    }

    /// <summary>C0A4: the palette cycling of the record ESI (the count +0, the interval +1, the phase +2, the last time +3, then the blocks: the colour number and 3 * count bytes of the colours): after the interval of the timer [19E4] the phase advances (round the count) and every block (to the zero number) sends its colour of the phase.</summary>
    public static void FC0A4(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint e = g.Esi; byte B(uint o) => w.Peek(e + o);
        g.Al = d[0x19e4]; g.Ah = g.Al; g.Al = Sub8(g, g.Al, B(3)); g.Cmp(g.Al, B(1), 8); if (g.Cf) return;
        w.Poke(e + 3, g.Ah); g.Eax = B(2); g.Ecx = B(0); g.Al = Inc8(g, g.Al); g.Cmp(g.Al, g.Cl, 8); if (!g.Cf) { g.Al = 0; g.Logic(0, 8); }
        w.Poke(e + 2, g.Al); g.Ebx = g.Eax + g.Eax * 2 + 1; g.Ecx = g.Ecx + g.Ecx * 2 + 1; g.Esi = e + 4; g.Edx = (g.Edx & 0xffff0000) | 0x3c9;
        do
        {
            g.Al = w.Peek(g.Esi); w.Out(0x3c8, g.Al); for (uint k = 0; k < 3; k++) { g.Al = w.Peek(g.Esi + g.Ebx + k); w.Out(0x3c9, g.Al); }
            g.Add(g.Esi, g.Ecx); g.Esi += g.Ecx; g.Cmp(w.Peek(g.Esi), 0, 8);
        } while (!g.Zf);
    }

    /// <summary>CD72: stamp a tile on the map layer [18EE] (EAX = the point x + 256 y, EBX = the tile): the 3 x 3 cells round the point (the mask dwords DS:7C0, 0 = take the cell) whose tile descriptor, merged by the mask into the descriptor of EBX (DS:7B8), differs get a tile found by CE9D (none: the function ends) - the cells are noted at DS:2045 (point, address, tile), then written as words and the cells in the 14 x 14 view [1906]/[190A] are marked in the dirty cache DS:33C (FFFFh, the rows 38h bytes, the second row +1Ch).</summary>
    public static void FCD72(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Ecx = g.Eax; g.Eax = U32(d, 0x18de); g.Ebp = (g.Eax << 2) + 4; Mul8(g, g.Al, g.Ch);
        g.Edi = g.Cl; g.Edi += g.Eax; g.Edi <<= 2; g.Edi += U32(d, 0x18ee); g.Ch--; g.Cl--; g.Edi -= g.Ebp; g.Ebp -= 0x10;
        Put(d, 0x7ea, g.Ebp); Put(d, 0x7bc, g.Ebx); g.Eax = Desc(w, g.Ebx, 0); Put(d, 0x7b8, g.Eax); d[0x7e8] = g.Cl;
        g.Esi = 0x2045; g.Ebx = 0x7c0; g.Dh = 3;
        do
        {
            g.Dl = 3; g.Cl = d[0x7e8];
            do
            {
                g.Cmp(g.Cl, d[0x18de], 8);
                if (!g.Cf) goto skip;
                g.Cmp(g.Ch, d[0x18e2], 8);
                if (!g.Cf) goto skip;
                Put(d, (int)g.Esi, g.Ecx); g.Ebp = U32(d, (int)g.Ebx); g.Eax = U32(d, 0x7bc); g.Test(g.Ebp, g.Ebp, 32);
                if (!g.Zf)
                {
                    g.Eax = Desc(w, w.PlaneRead(g.Edi) & 0xffff, 0);
                    uint pushed = g.Eax; g.Eax &= g.Ebp; g.Ebp = ~g.Ebp; g.Ebp &= U32(d, 0x7b8); g.Eax |= g.Ebp; g.Ebp = pushed;
                    g.Cmp(g.Eax, g.Ebp, 32); if (g.Zf) goto skip;
                    FCE9D(w, g); if (g.Zf) return;
                }
                Put(d, (int)g.Esi + 2, g.Edi); d[g.Esi + 6] = g.Al; d[g.Esi + 7] = g.Ah; g.Esi += 8;
            skip:
                g.Ebx += 4; g.Edi += 4; g.Cl++; g.Dl = Dec8(g, g.Dl);
            } while (!g.Zf);
            g.Edi += U32(d, 0x7ea); g.Ch++; g.Dh = Dec8(g, g.Dh);
        } while (!g.Zf);
        g.Eax = 0; Put(d, (int)g.Esi + 2, 0); g.Esi = 0x2045; g.Edx = (g.Edx & 0xffff0000) | 0xffff;
        for (; ; )
        {
            g.Edi = U32(d, (int)g.Esi + 2); g.Test(g.Edi, g.Edi, 32); if (g.Zf) return;
            g.Eax = U32(d, (int)g.Esi + 6); w.PlaneWrite(g.Edi, (w.PlaneRead(g.Edi) & 0xffff0000) | (g.Eax & 0xffff));
            g.Eax = U32(d, (int)g.Esi); g.Cmp(g.Al, d[0x1906], 8); g.Al = (byte)(g.Al - d[0x1906]);
            if (g.Cf) goto next;
            g.Cmp(g.Al, 0xe, 8); if (!g.Cf) goto next;
            g.Cmp(g.Ah, d[0x190a], 8); g.Ah = (byte)(g.Ah - d[0x190a]); if (g.Cf) goto next;
            g.Cmp(g.Ah, 0xe, 8); if (!g.Cf) goto next;
            g.Ebx = 0; g.Bl = g.Ah; g.Eax &= 0xff; g.Ecx = g.Ebx * 8; g.Ebx <<= 6; g.Ebx -= g.Ecx; g.Ebx = g.Ebx + g.Eax * 2 + 0x33c;
            d[g.Ebx] = (byte)g.Edx; d[g.Ebx + 1] = (byte)(g.Edx >> 8); d[g.Ebx + 0x1c] = (byte)g.Edx; d[g.Ebx + 0x1d] = (byte)(g.Edx >> 8);
        next:
            g.Esi += 8;
        }
    }

    /// <summary>CF77: stamp the tile found by CF41 (AL = the class, AH = the byte 1) on a rectangle (CL x CH cells from the point EDX = x + 256 y): CD72 for every cell with EBX = the tile, the point in EAX; no such tile: nothing happens.</summary>
    public static void FCF77(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint esi = g.Esi, edx0 = g.Edx, ecx0 = g.Ecx;
        FCF41(w, g); g.Ecx = ecx0; g.Ebx = g.Eax; g.Eax = edx0;
        if (g.Zf) { g.Esi = esi; return; }
        d[0x7e9] = g.Al;
        do
        {
            g.Dl = g.Cl; g.Al = d[0x7e9];
            do
            {
                uint eax = g.Eax, ebx = g.Ebx, ecx = g.Ecx, edx = g.Edx; FCD72(w, g); g.Eax = eax; g.Ebx = ebx; g.Ecx = ecx; g.Edx = edx;
                g.Al = Inc8(g, g.Al); g.Dl = Dec8(g, g.Dl);
            } while (!g.Zf);
            g.Ah = Inc8(g, g.Ah); g.Ch = Dec8(g, g.Ch);
        } while (!g.Zf);
        g.Esi = esi;
    }

    /// <summary>CFAC: stamp tiles over a rectangle (CL x CH cells from the point EDX = x + 256 y; AL = the way, AH = the parameter): every cell inside the map reads its tile descriptor; way 0: the bytes 2 and 3 are equal after the turn by 4 bits - CE9D finds the tile of the same class (AH = the byte 1, AL = the descriptor &amp; 7); another way: the nibbles of the bytes 2 and 3 choose the colours (BL, BH) and CF07 lists the tiles (AH, mask F00h), of which the first two that CE9D finds for the colours form the tile; CD72 stamps it at the cell.</summary>
    public static void FCFAC(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint esi0 = g.Esi, eax0 = g.Eax;
        d[0x1f0a] = g.Cl; g.Dl = (byte)(g.Dl + g.Cl); g.Cl = d[0x1f0a];
        do
        {
            g.Cl = d[0x1f0a]; g.Dl = (byte)(g.Dl - g.Cl);
            do
            {
                uint ecx = g.Ecx, edx = g.Edx; uint saved = eax0;
                g.Cmp(g.Dl, d[0x18de], 8);
                if (g.Cf)
                {
                    g.Cmp(g.Dh, d[0x18e2], 8);
                    if (g.Cf)
                    {
                        g.Edi = U32(d, 0x18ee); g.Eax = U32(d, 0x18de); Mul8(g, g.Al, g.Dh); g.Edi += g.Eax * 4; g.Eax = g.Dl; g.Eax = w.PlaneRead(g.Edi + g.Eax * 4) & 0xffff;
                        g.Edx = U32(d, 0x18fe); g.Ebx = Desc(w, g.Eax, 2); g.Eax = Desc(w, g.Eax, 0);
                        g.Cmp((byte)saved, 0, 8);
                        if (g.Zf)
                        {
                            g.Cmp(g.Bl, g.Bh, 8);
                            if (g.Zf)
                            {
                                g.Bl = (byte)((g.Bl << 4) | (g.Bl >> 4)); g.Cmp(g.Bl, g.Bh, 8);
                                if (g.Zf)
                                {
                                    g.Ah = (byte)(saved >> 8); g.Al &= 7; FCE9D(w, g);
                                    if (!g.Zf) { g.Ebx = g.Eax; g.Eax = edx; FCD72(w, g); }
                                }
                            }
                        }
                        else
                        {
                            g.Cl = g.Bh; g.Bh = g.Bl; g.Bl &= 0xf; g.Bh >>= 4; g.Ch = g.Cl; g.Cl &= 0xf; g.Ch >>= 4;
                            g.Cmp(g.Bl, g.Bh, 8);
                            if (g.Zf) { g.Bh = g.Cl; g.Cmp(g.Bl, g.Bh, 8); if (g.Zf) g.Bh = g.Ch; }
                            g.Al = g.Bl; g.Al = (byte)(g.Al << 4); g.Bl |= g.Al; g.Al = g.Bh; g.Al = (byte)(g.Al << 4); g.Bh |= g.Al;
                            g.Ah = (byte)(saved >> 8); uint ebx = g.Ebx; g.Ebx = 0xf00; FCF07(w, g); g.Ebx = ebx;
                            if (!g.Zf)
                            {
                                g.Esi = 0x1f45;
                                for (; ; )
                                {
                                    g.Eax = U16(d, (int)g.Esi); g.Cmp(g.Eax, 0xffff, 32); if (g.Zf) break;
                                    g.Esi += 2; g.Ecx = U32(d, 0x18fe); g.Ecx = Desc(w, g.Eax, 0); g.Ecx &= 0xff000000; g.Al = g.Bl; g.Eax <<= 16; g.Eax |= g.Ecx;
                                    uint pc = g.Ecx, pb = g.Ebx; FCE9D(w, g); g.Ebx = pb; g.Ecx = pc; if (g.Zf) continue;
                                    g.Eax = g.Bh; g.Eax <<= 16; g.Eax |= g.Ecx; pb = g.Ebx; FCE9D(w, g); g.Ebx = pb; if (g.Zf) continue;
                                    g.Eax = U16(d, (int)g.Esi - 2); g.Ebx = g.Eax; g.Eax = edx; FCD72(w, g); break;
                                }
                            }
                        }
                    }
                }
                g.Ecx = ecx; g.Edx = edx; g.Dl = (byte)(g.Dl + 1); g.Cl = Dec8(g, g.Cl);
            } while (!g.Zf);
            g.Dh = (byte)(g.Dh + 1); g.Ch = Dec8(g, g.Ch);
        } while (!g.Zf);
        g.Eax = eax0; g.Esi = esi0;
    }

    /// <summary>D0D5: stamp the tile class 6 under a building: the rectangle of the template [1C49] (the width and the height +3/+4) round the object ESI (the point +4/+8 in cells, the corner at half the size back) goes to CF77 (EAX = 6).</summary>
    public static void FD0D5(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Edi = U32(d, 0x1c49); var t = w.ObjectAt(g.Edi); var o = w.ObjectAt(g.Esi);
        g.Ecx = U32(t, 3); g.Eax = g.Ecx; g.Edx = U32(o, 4) >> 5; g.Al = (byte)(g.Al >> 1); g.Dl = (byte)(g.Dl - g.Al);
        g.Ebx = U32(o, 8) >> 5; g.Ah = (byte)(g.Ah >> 1); g.Bl = (byte)(g.Bl - g.Ah); g.Dh = g.Bl; g.Eax = 6; FCF77(w, g);
    }

    /// <summary>A5F4: the list of the cursor-command entries (the list EDX of four-byte entries ended by FFh; the first byte is the key) for the object ESI that the table of its owner accepts (6885 gives the army entry, [entry + owner * 4 + 15h] the table of records: +0 the way, +1 the key, +2 the flag, +0F the number of the 2-byte extras, size 10h + 2 * number; FFh at +1 ends it): AL = the key of the way [1F41], AH = the way [1F0A]; the accepted entries go to DS:1F45 (ended by FFh); AL = their number, AH = the index of the first with the key AL ([1F42] = FFh when none).</summary>
    public static void FA5F4(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint esi0 = g.Esi, edx0 = g.Edx; d[0x1f41] = g.Al; d[0x1f0a] = g.Ah; d[0x1f42] = 0xff;
        var obj = w.ObjectAt(g.Esi); F6885(w, g, obj); g.Eax = obj[3]; g.Ebp = Peek32(w, g.Ebx + g.Eax * 4 + 0x15); g.Esi = edx0;
        g.Edx = 0; g.Ah = 0; g.Edi = 0x1f45;
        do
        {
            g.Ebx = g.Ebp;
            for (; ; )
            {
                g.Al = w.Peek(g.Ebx + 1); g.Cmp(g.Al, 0xff, 8); if (g.Zf) goto next;
                g.Cmp(g.Al, w.Peek(g.Esi), 8); if (g.Zf) break;
                g.Dl = w.Peek(g.Ebx + 0xf); g.Ebx = g.Ebx + g.Edx * 2 + 0x10;
            }
            g.Cmp(w.Peek(g.Ebx + 2), g.Dh, 8); if (!g.Zf) goto next;
            g.Cl = d[0x1f0a]; g.Cmp(g.Cl, w.Peek(g.Ebx), 8); if (!g.Zf) goto next;
            g.Cmp(g.Al, d[0x1f41], 8); if (g.Zf) d[0x1f42] = g.Ah;
            g.Ah = Inc8(g, g.Ah); g.Ecx = 4;
            for (int k = 0; k < 4; k++) w.Poke(g.Edi + (uint)k, w.Peek(g.Esi + (uint)k)); g.Edi += 4; g.Esi += 4; g.Ecx = 0; g.Esi -= 4;
        next:
            g.Esi += 4; g.Cmp(w.Peek(g.Esi), 0xff, 8);
        } while (!g.Zf);
        d[g.Edi] = 0xff; g.Al = g.Ah; g.Ah = d[0x1f42]; g.Esi = esi0;
    }

    /// <summary>5CC2: the panel is closed: 674F, DF2A with 2Ch, the effect 2 (A4A9).</summary>
    public static void F5CC2(IUnitActionWorld w, ActionRegs g)
    {
        w.Call(0x674f, g); g.Al = 0x2c; FDF2A(w, g); g.Eax = 2; FA4A9(w, g);
    }

    /// <summary>5DA5: the check before a command on the selected object: neither the key 1 nor the flag 2 of [193A] and a health above the template's +46 keep the panel (RET); else E5E6, E070 with 2Dh, F791, the cache DS:33C.. is invalidated and 4938.</summary>
    public static void F5DA5(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Cmp(U16(d, 0x1c35), 1, 16);
        if (!g.Zf)
        {
            g.Test(U32(d, 0x193a), 2, 32);
            if (g.Zf)
            {
                g.Esi = U32(d, 0x1969); var o = w.ObjectAt(g.Esi); F6885(w, g, o); var t = w.ObjectAt(g.Edi); g.Al = t[0x46]; g.Cmp(o[0x19], g.Al, 8);
                if (!(g.Zf || (g.Sf != g.Of))) return;
            }
        }
        w.Call(0xe5e6, g); g.Al = 0x2d; w.Call(0xe070, g); w.Call(0xf791, g);
        g.Eax = 0xffffffff; g.Edi = 0x33c; g.Ecx = 0xc4; for (int k = 0; k < 0xc4; k++) Put(d, 0x33c + 4 * k, 0xffffffff); g.Edi += 0x310; g.Ecx = 0;
        w.Call(0x4938, g);
    }

    /// <summary>5DF6: the group of the settlement answers 20232; when it fails the panel is cleared (672F, DF2A with 32h).</summary>
    public static void F5DF6(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Edi = U32(d, 0xed20); g.Add(g.Edi, 0x9df); g.Edi += 0x9df; g.Bl = d[0x1c87]; w.Call(0x20232, g);
        if (!g.Zf) return;
        w.Call(0x672f, g); g.Al = 0x32; FDF2A(w, g);
    }

    /// <summary>5E1C: scroll of the widget list (record ESI, +1C records after it, +1D speed, +1A offset): the offset follows the key and the flag 19E7; then the settlement group 20232 and its +22 are compared with the widget's +16: a mismatch (or no group, or key 1 / flag 10h of +3) clears the panel (674F, DF2A with 33h and 2Ch; DEE4 with 44Bh when the group exists).</summary>
    public static void F5E1C(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Cmp(U16(d, 0x1c35), 1, 16);
        if (g.Zf) { Fail5E1C(w, g); return; }
        g.Test(r[3], 0x10, 8); if (!g.Zf) { Fail5E1C(w, g); return; }
        g.Ebx = (uint)(int)(short)U16(r, 0x1a); g.Eax = 0x36; g.Cl = r[0x1c]; Mul8(g, g.Al, g.Cl); g.Add(g.Eax, g.Ebx); g.Eax += g.Ebx;
        g.Test(r[3], 1, 8);
        bool toDec = false;
        if (!g.Zf)
        {
            g.Test(d[0x19e7], 1, 8);
            if (g.Zf) goto L5E99;
            g.Cmp(g.Eax, 0x1a2, 32);
            if (g.Sf == g.Of)
            {
                g.Cmp(U32(d, 0x1951), 0x19a, 32);
                if (!g.Cf)
                {
                    g.Cmp(r[0x1d], 0xf9, 8);
                    if (g.Jle) goto L5E99;
                    r[0x1d] = Dec8(g, r[0x1d]); goto L5E99;
                }
            }
            g.Cmp(g.Ebx, 0x1e, 32);
            if (g.Jg) toDec = true;
            else
            {
                g.Cmp(U32(d, 0x1951), 0x46, 32);
                if (!g.Cf && !g.Zf) toDec = true;
                else
                {
                    g.Cmp(r[0x1d], 7, 8);
                    if (g.Sf == g.Of) goto L5E99;
                    r[0x1d] = Inc8(g, r[0x1d]); goto L5E99;
                }
            }
        }
        else toDec = true;
        if (toDec)
        {
            g.Cmp(r[0x1d], 0, 8);
            if (!g.Zf)
            {
                if (!g.Sf) r[0x1d] = Sub8(g, r[0x1d], 2);
                r[0x1d] = Inc8(g, r[0x1d]);
            }
        }
    L5E99:
        g.Test(d[0x19e7], 2, 8);
        if (!g.Zf)
        {
            g.Edx = (uint)(int)(sbyte)r[0x1d]; g.Test(g.Edx, g.Edx, 32);
            if (!g.Zf)
            {
                g.Ebx += g.Edx; r[0x1a] = (byte)g.Ebx; r[0x1b] = (byte)(g.Ebx >> 8);
                int o = 0; do { o += 0x1e; Put(r, o + 8, U32(r, o + 8) + g.Edx); g.Cl = Dec8(g, g.Cl); } while (!g.Zf);
            }
        }
        g.Edi = U32(d, 0xed20); g.Edi += 0x9df; g.Bl = d[0x1c87]; w.Call(0x20232, g);
        if (g.Zf) { Fail5E1C(w, g); return; }
        g.Eax = U32(w.ObjectAt(g.Edi), 0x22); g.Cmp(U32(r, 0x16), g.Eax, 32);
        if (g.Zf) return;
        g.Al = 0x33; FDF2A(w, g); g.Al = 0x2c; FDF2A(w, g); g.Esi = 0x44b; w.Call(0xdee4, g);
    }
    private static void Fail5E1C(IUnitActionWorld w, ActionRegs g) { w.Call(0x674f, g); g.Al = 0x33; FDF2A(w, g); g.Al = 0x2c; FDF2A(w, g); }

    /// <summary>5F0A: tick of the group-health widget (ESI): +1A counts; every 20h-th tick the objects of the chain of the owner [1972] with +1B = [1C87] and positive +19 contribute (+19 * 128) / (+45 of their template) to the sum [1F31], their number to [1F35]; the mean AH is compared with +14: a change stores it, picks the colour +16 (B2h, 51h or 22h by the thresholds +15 / 2 and / 4) and marks the widget (+0 |= 80h).</summary>
    public static void F5F0A(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; r[0x1a] = Inc8(g, r[0x1a]); g.Test(r[0x1a], 0x1f, 8); if (!g.Zf) return;
        Chain5F0A(w, g, d, r, true);
        g.Ah = g.Al; g.Cmp(r[0x14], g.Ah, 8); if (g.Zf) return;
        g.Al = r[0x15]; g.Bl = 0xb2; g.Al = (byte)g.Shr(g.Al, 1, 8); g.Cmp(g.Ah, g.Al, 8);
        if (!(g.Cf || g.Zf)) { }
        else
        {
            g.Bl = 0x51; g.Al = (byte)g.Shr(g.Al, 1, 8); g.Cmp(g.Ah, g.Al, 8);
            if (g.Cf || g.Zf) g.Bl = 0x22;
        }
        r[0x16] = g.Bl; r[0x14] = g.Ah; r[0] |= 0x80; g.Logic((uint)r[0], 8);
    }

    /// <summary>5FC5: as 5F0A, but sums the byte +16 of the objects; the mean AL is stored in +14 (+0 |= 80h) when it changed.</summary>
    public static void F5FC5(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; r[0x1a] = Inc8(g, r[0x1a]); g.Test(r[0x1a], 0x1f, 8); if (!g.Zf) return;
        Chain5F0A(w, g, d, r, false);
        g.Cmp(r[0x14], g.Al, 8); if (g.Zf) return;
        r[0x14] = g.Al; r[0] |= 0x80; g.Logic((uint)r[0], 8);
    }

    private static void Chain5F0A(IUnitActionWorld w, ActionRegs g, byte[] d, byte[] r, bool health)
    {
        g.Eax = 0xf5; Mul8(g, 0xf5, d[0x1972]); var part = w.PartOf(d[0x1972]);
        g.Ebp = U32(part, 9); g.Add(g.Ebp, w.PoolBase); g.Ebp += w.PoolBase;
        g.Edi = U32(part, 0xe); g.Add(g.Edi, w.PoolBase); g.Edi += w.PoolBase;
        Put(d, 0x1f31, 0); Put(d, 0x1f35, 0);
        g.Cmp(g.Edi, g.Ebp, 32);
        while (!g.Zf)
        {
            uint edi = g.Edi; var o = w.ObjectAt(edi); g.Al = o[0x1b]; g.Cmp(g.Al, d[0x1c87], 8);
            if (g.Zf)
            {
                g.Cmp(o[0x19], 0, 8);
                if (!g.Jle)
                {
                    if (health)
                    {
                        w.Call(0x68bd, g); var t = w.ObjectAt(g.Edx);
                        g.Eax = (uint)o[0x19] << 7; Div8(g, t[0x45]); g.Eax &= 0xff;
                    }
                    else g.Eax = o[0x16];
                    Put(d, 0x1f31, U32(d, 0x1f31) + g.Eax); Put(d, 0x1f35, U32(d, 0x1f35) + 1);
                }
            }
            edi += U32(o, 0x10); g.Edi = edi; g.Cmp(g.Edi, g.Ebp, 32);
        }
        g.Eax = 0; g.Edx = 0; g.Logic(0, 32); g.Cmp(U32(d, 0x1f35), 0, 32);
        if (!g.Zf)
        {
            uint cnt = U32(d, 0x1f35), sum = U32(d, 0x1f31); g.Eax = sum / cnt; g.Edx = sum % cnt;
        }
    }

    /// <summary>6057: the settlement group (20232) whose +22 differs from the widget's +1A is stored there and the widget is rebuilt (B3B6 with EBX = 20h, ECX = 252h, EDX = +8).</summary>
    public static void F6057(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Edi = U32(d, 0xed20); g.Add(g.Edi, 0x9df); g.Edi += 0x9df; g.Bl = d[0x1c87]; w.Call(0x20232, g);
        if (g.Zf) return;
        g.Eax = U32(w.ObjectAt(g.Edi), 0x22); g.Cmp(g.Eax, U32(r, 0x1a), 32); if (g.Zf) return;
        Put(r, 0x1a, g.Eax); g.Ebx = 0x20; g.Ecx = 0x252; g.Edx = U32(r, 8); w.Call(0xb3b6, g);
    }

    /// <summary>608E: widget ESI (+2 = slot, +14 = class, +1B = speed, +4 = position): with flag 1 in +3 E03C (AL = 2Ch, AH = FAh) may give the object EDI +1A = slot, +1C = class, +1D = 0; with the flag 2 of [19E7] the position moves by the signed speed (a position above 186h keeps the speed, otherwise it grows by one); E571, then (E5AB with C04h) the objects of the owner's chain with +1B = [1C87], +E = slot and +0 = class lose their +1B.</summary>
    public static void F608E(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Test(r[3], 1, 8);
        if (!g.Zf)
        {
            g.Al = 0x2c; g.Ah = 0xfa; w.Call(0xe03c, g);
            if (!g.Zf) { var o = w.ObjectAt(g.Edi); o[0x1a] = r[2]; o[0x1c] = r[0x14]; o[0x1d] = 0; g.Al = r[0x14]; }
        }
        g.Test(d[0x19e7], 2, 8);
        if (!g.Zf)
        {
            g.Eax = (uint)(int)(sbyte)r[0x1b]; g.Test(g.Eax, g.Eax, 32);
            if (!g.Zf)
            {
                g.Add(U32(r, 4), g.Eax); Put(r, 4, U32(r, 4) + g.Eax); g.Cmp(U32(r, 4), 0x186, 32);
                if (g.Cf || g.Zf) r[0x1b] = Inc8(g, r[0x1b]);
            }
        }
        w.Call(0xe571, g); g.Eax = 0xc04; w.Call(0xe5ab, g);
        if (!g.Zf) return;
        g.Eax = 0xf5; Mul8(g, 0xf5, d[0x1972]); var part = w.PartOf(d[0x1972]);
        g.Ebp = U32(part, 9); g.Add(g.Ebp, w.PoolBase); g.Ebp += w.PoolBase;
        g.Edi = U32(part, 0xe); g.Add(g.Edi, w.PoolBase); g.Edi += w.PoolBase;
        g.Cmp(g.Edi, g.Ebp, 32);
        while (!g.Zf)
        {
            var o = w.ObjectAt(g.Edi); g.Al = o[0x1b]; g.Cmp(g.Al, d[0x1c87], 8);
            if (g.Zf) { g.Al = o[0xe]; g.Cmp(g.Al, r[2], 8); if (g.Zf) { g.Al = o[0]; g.Cmp(g.Al, r[0x14], 8); if (g.Zf) o[0x1b] = 0; } }
            g.Edi += U32(o, 0x10); g.Cmp(g.Edi, g.Ebp, 32);
        }
    }

    /// <summary>612B: E5AB with C04h; when it answers zero, the message 44Bh (DEE4).</summary>
    public static void F612B(IUnitActionWorld w, ActionRegs g)
    {
        g.Eax = 0xc04; w.Call(0xe5ab, g); if (!g.Zf) return;
        g.Esi = 0x44b; w.Call(0xdee4, g);
    }

    /// <summary>6142: with [19C2] set and E5AB (C04h) answering zero the view centres (A320) on the selected object [1969].</summary>
    public static void F6142(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Cmp(d[0x19c2], 0, 8); if (g.Zf) return;
        g.Eax = 0xc04; w.Call(0xe5ab, g); if (!g.Zf) return;
        g.Esi = U32(d, 0x1969); var o = w.ObjectAt(g.Esi); g.Eax = g.Shr(U32(o, 4), 5, 32); g.Ebx = g.Shr(U32(o, 8), 5, 32); FA320(w, g);
    }

    /// <summary>616F: widget ESI (+2 = slot, +1A = state, +1B = kind, +3 = flags, +14): with the flag 1 E03C (2Bh, FFh) gives the object EDI +1C = +2; a started widget (+1A) goes to 6267; else the slot DS:1978 + 12 * slot with its word +9 equal to [1C35] marks +3 = 3, E5AB (B05h), and with +3 = 3 the widget starts (+1A = 1, [19C0] = slot * 8 | 40h, +1B = byte +6 of the slot) and the tool of that kind is chosen: 1 - 4D41, 2 - 4E27, 3 - 4FCE, 4 - 50E5, 5 - 51AC, 6 - 5297, 7 - 538D, 8 - 53C4, 9 - 54B4, 0Bh - 5593; kind 0: 4938 and the class [19C0] goes to +14 of the selected object (B8A2 for a group).</summary>
    public static void F616F(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Test(r[3], 1, 8);
        if (!g.Zf)
        {
            g.Al = 0x2b; g.Ah = 0xff; w.Call(0xe03c, g);
            if (!g.Zf) { var o = w.ObjectAt(g.Edi); g.Al = r[2]; o[0x1c] = g.Al; }
        }
        g.Cmp(r[0x1a], 0, 8);
        if (!g.Zf) { F6267(w, g, r); return; }
        g.Eax = 0xc; Mul8(g, g.Al, r[2]); g.Edi = g.Eax + 0x1978;
        g.Eax = (g.Eax & 0xffff0000) | U16(d, (int)g.Edi + 9); g.Cmp(g.Eax & 0xffff, U16(d, 0x1c35), 16);
        if (g.Zf) r[3] = 3;
        g.Eax = 0xb05; w.Call(0xe5ab, g);
        g.Cmp(r[3], 3, 8); if (!g.Zf) return;
        r[0x1a] = 1; g.Al = r[2]; g.Al = (byte)(g.Al << 3); g.Al |= 0x40; d[0x19c0] = g.Al; g.Al = d[g.Edi + 6]; r[0x1b] = g.Al; g.Test(g.Al, g.Al, 8);
        if (!g.Zf)
        {
            byte[] kinds = [1, 2, 3, 4, 5, 6, 7, 8, 9, 0xb]; int[] targets = [0x4d41, 0x4e27, 0x4fce, 0x50e5, 0x51ac, 0x5297, 0x538d, 0x53c4, 0x54b4, 0x5593];
            for (int k = 0; k < kinds.Length; k++) { g.Cmp(g.Al, kinds[k], 8); if (g.Zf) { w.Call((uint)targets[k], g); return; } }
            return;
        }
        w.Call(0x4938, g); g.Cmp(d[0x1c89], 0, 8);
        if (!g.Zf) { g.Ah = d[0x19c0]; g.Al = d[0x1972]; g.Bl = d[0x1c87]; w.Call(0xb8a2, g); return; }
        g.Edi = U32(d, 0x1969); var s = w.ObjectAt(g.Edi); g.Al = d[0x19c0]; s[0x14] &= 7; s[0x14] |= g.Al; g.Logic(s[0x14], 8);
    }

    /// <summary>6267: E571; a widget with kind +1B = 0 and +3 = 3 stays; kind equal to the tool L3[2] (or 7 with the tool 0Ah) stays; else +1A = 0.</summary>
    public static void F6267(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        w.Call(0xe571, g); g.Al = r[0x1b]; g.Test(g.Al, g.Al, 8);
        if (g.Zf) { g.Cmp(r[3], 3, 8); if (g.Zf) return; r[0x1a] = 0; return; }
        g.Cmp(w.L3[2], g.Al, 8); if (g.Zf) return;
        g.Cmp(g.Al, 7, 8); if (!g.Zf) { r[0x1a] = 0; return; }
        g.Cmp(w.L3[2], 0xa, 8); if (g.Zf) return;
        r[0x1a] = 0;
    }

    /// <summary>6294: the tick of a widget with a price: as 608E up to E571, then a started widget goes to 6361; else 6377: when it cannot be afforded +1A grows and the picture +11 = 1Ah; when it can, a template without the price flag (or with +26 of the selected object = FFh) asks for the picture 8 (4 with the flag 1), otherwise E5AB (C04h) puts the slot into the first free +21.. of the selected object (or, with the price flag, +22 and the cursor 43h, 5573).</summary>
    public static void F6294(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Test(r[3], 1, 8);
        if (!g.Zf)
        {
            g.Al = 0x2c; g.Ah = 0xfa; w.Call(0xe03c, g);
            if (!g.Zf) { var o = w.ObjectAt(g.Edi); o[0x1a] = r[2]; o[0x1c] = r[0x14]; g.Al = r[0x14]; }
        }
        g.Test(d[0x19e7], 2, 8);
        if (!g.Zf)
        {
            g.Eax = (uint)(int)(sbyte)r[0x1b]; g.Test(g.Eax, g.Eax, 32);
            if (!g.Zf)
            {
                g.Add(U32(r, 4), g.Eax); Put(r, 4, U32(r, 4) + g.Eax); g.Cmp(U32(r, 4), 0x186, 32);
                if (g.Cf || g.Zf) r[0x1b] = Inc8(g, r[0x1b]);
            }
        }
        w.Call(0xe571, g); g.Cmp(r[0x1a], 0, 8);
        if (!g.Zf) { F6361(w, g, r); return; }
        F6377(w, g, r);
        if (!g.Zf) { r[0x1a] = Inc8(g, r[0x1a]); r[0x11] = 0x1a; return; }
        var t = w.ObjectAt(g.Edi); g.Test(U32(t, 0x40), 8, 32);
        bool price = !g.Zf;
        if (!price)
        {
            g.Ebx = U32(d, 0x1969); var ob = w.ObjectAt(g.Ebx); g.Cmp(ob[0x26], 0xff, 8);
            if (!g.Zf) { r[0x11] = 8; g.Test(r[3], 1, 8); if (g.Zf) return; r[0x11] = 4; return; }
        }
        g.Eax = 0xc04; uint edi = g.Edi; w.Call(0xe5ab, g); g.Edi = edi;
        if (!g.Zf) return;
        g.Test(U32(t, 0x40), 8, 32);
        g.Ebx = U32(d, 0x1969); var s = w.ObjectAt(g.Ebx);
        if (g.Zf)
        {
            g.Ecx = 0;
            do { g.Ecx++; g.Cmp(s[g.Ecx + 0x21], 0xff, 8); } while (!g.Zf);
            g.Al = r[2]; s[g.Ecx + 0x21] = g.Al; return;
        }
        g.Al = r[2]; s[0x22] = g.Al; Put(s, 0x23, 0xffffffff); d[0x19c0] = 0x43; w.Call(0x5573, g);
    }

    /// <summary>5573: the tool area (4FCE), then the tool state 0Ah.</summary>
    public static void F5573(IUnitActionWorld w, ActionRegs g) { w.Call(0x4fce, g); w.L3[2] = 0xa; }

    /// <summary>5580: 500D (the area tool step); when the tool state is not 0Ah, 538D.</summary>
    public static void F5580(IUnitActionWorld w, ActionRegs g)
    {
        w.Call(0x500d, g); g.Cmp(w.L3[2], 0xa, 8); if (!g.Zf) w.Call(0x538d, g);
    }

    /// <summary>5593: the tool state 0Bh (L3[12] = 0Bh), DF2A with 30h, the message 466Eh (DEE4); ESI is kept.</summary>
    public static void F5593(IUnitActionWorld w, ActionRegs g)
    {
        w.L3[2] = 0xb; w.L3[0x12] = 0xb; uint esi = g.Esi; g.Al = 0x30; FDF2A(w, g); g.Esi = 0x466e; w.Call(0xdee4, g); g.Esi = esi;
    }

    /// <summary>55B5: the end of a tool (the same as 53A8).</summary>
    public static void F55B5(IUnitActionWorld w, ActionRegs g) => EndTool(w, g);

    /// <summary>55D1: the tool state 0Ch (L3[12] = 0).</summary>
    public static void F55D1(IUnitActionWorld w, ActionRegs g) { w.L3[2] = 0xc; w.L3[0x12] = 0; }

    /// <summary>566A: the tool state 0Dh (L3[12] = 0).</summary>
    public static void F566A(IUnitActionWorld w, ActionRegs g) { w.L3[2] = 0xd; w.L3[0x12] = 0; }

    /// <summary>55E0: the tool «use the item on the ground» (cursor 93008Ah): the left button (flag 8) - effect 4, 124FD with the item [4190], the player [1972] and the cell of the pointer [1955]/[1959] (&gt;&gt; 5), then 4938; every other press first calls 124D8 (4938 when it answers zero), then the end of the tool.</summary>
    public static void F55E0(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3; g.Test(l[4], 1, 8);
        if (!g.Zf)
        {
            SetCursor(d, 0x93008a); g.Test(l[4], 8, 8);
            if (!g.Zf)
            {
                g.Eax = 4; FA4A9(w, g); g.Ecx = g.Shr(U32(d, 0x1955), 5, 32); g.Eax = g.Shr(U32(d, 0x1959), 5, 32); g.Ch = g.Al;
                g.Al = d[0x4190]; g.Ah = d[0x1972]; w.Call(0x124fd, g); w.Call(0x4938, g); return;
            }
        }
        g.Al = d[0x4190]; g.Dl = d[0x1972]; w.Call(0x124d8, g);
        if (g.Zf) { w.Call(0x4938, g); return; }
        EndTool(w, g);
    }

    /// <summary>64AD: builds the five widgets (EDI = the panel widget, +1A = the sum of the bytes +22..+26 of the selected object [1969] plus 0Dh): for every slot ECX = 5..1 whose byte +21+ECX is not FFh the record 23Dh is queued (DF7E; EDI = the new record in L3, which is filled further: army +14, picture +15 from the template, slot 10h + ECX in +2, the price position [1F31] in +4, which falls by 0Fh); the slot 1 also gets the handler 6562, two more records (25Bh, 279h; +12 from the template; +15 = template +45) and 6423 for the last one.</summary>
    public static void F64AD(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3; var panel = w.ObjectAt(g.Edi); g.Esi = U32(d, 0x1969); var sel = w.ObjectAt(g.Esi); g.Ecx = 5; g.Al = 0xd;
        do { g.Al = Add8(g, g.Al, sel[g.Ecx + 0x21]); g.Ecx--; } while (g.Ecx != 0);
        panel[0x1a] = g.Al; Put(d, 0x1f31, 0x220); g.Ecx = 5;
        for (; ; )
        {
            g.Cmp(sel[g.Ecx + 0x21], 0xff, 8);
            if (!g.Zf)
            {
                uint ecx = g.Ecx, esi = g.Esi; g.Esi = 0x23d; g.Al = 1; FDF7E(w, g); g.Esi = esi; g.Ecx = ecx;
                int e = (int)g.Edi;
                g.Ah = sel[0]; l[e + 0x14] = g.Ah; g.Al = sel[g.Ecx + 0x21]; uint edi = g.Edi; F68A0(w, g); g.Edx = g.Edi; g.Edi = edi;
                var t = w.ObjectAt(g.Edx); g.Eax = (g.Eax & 0xffff0000) | U16(t, 5); l[e + 0x15] = (byte)g.Eax; l[e + 0x16] = (byte)(g.Eax >> 8);
                g.Al = 0x10; g.Al = Add8(g, g.Al, g.Cl); l[e + 2] = g.Al; g.Eax = U32(d, 0x1f31); Put(l, e + 4, g.Eax);
                g.Cmp(g.Ecx, 1, 32);
                if (g.Zf)
                {
                    ecx = g.Ecx; Put(l, e + 0xc, 0x6562); uint edx = g.Edx; g.Esi = 0x25b; g.Al = 1; FDF7E(w, g);
                    e = (int)g.Edi; g.Edx = edx; g.Eax = g.Edx + 7; g.Eax += U32(t, 7); Put(l, e + 0x12, g.Eax);
                    g.Esi = 0x279; g.Al = 1; FDF7E(w, g); e = (int)g.Edi; g.Edx = edx; g.Al = t[0x45]; l[e + 0x15] = g.Al;
                    g.Esi = g.Edi; var rec = new byte[0x30]; Array.Copy(l, e, rec, 0, 0x30); F6423(w, g, rec); Array.Copy(rec, 0, l, e, 0x30); g.Ecx = ecx;
                }
            }
            uint old = U32(d, 0x1f31); Put(d, 0x1f31, old - 0xf); bool cf = old < 0xf;
            g.Ecx--; g.Zf = g.Ecx == 0; g.Sf = (g.Ecx & 0x80000000) != 0; g.Of = false; g.Cf = cf;
            if (g.Zf) return;
        }
    }

    /// <summary>6562: the handler: a selected object whose +F is not 1 and for which E5AB (C04h) answers zero loses its order table (+22 = FFFFFFFFh, +26 = FFh).</summary>
    public static void F6562(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Eax = U32(d, 0x1969); var o = w.ObjectAt(g.Eax); g.Cmp(o[0xf], 1, 8); if (g.Zf) return;
        g.Eax = 0xc04; w.Call(0xe5ab, g); if (!g.Zf) return;
        g.Eax = U32(d, 0x1969); o = w.ObjectAt(g.Eax); Put(o, 0x22, 0xffffffff); o[0x26] = 0xff;
    }

    /// <summary>658A: when the sum of the bytes +22..+26 of the selected object plus 0Dh differs from +1A of the widget ESI: E02A (AL = +1, EBX = 1710h), the rectangle of the picture [1922] + 2520h is redrawn (1DF08) and the message 216h (DEE4).</summary>
    public static void F658A(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Edi = U32(d, 0x1969); var sel = w.ObjectAt(g.Edi); g.Ecx = 5; g.Al = 0xd;
        do { g.Al = Add8(g, g.Al, sel[g.Ecx + 0x21]); g.Ecx--; } while (g.Ecx != 0);
        g.Cmp(r[0x1a], g.Al, 8); if (g.Zf) return;
        g.Al = r[1]; g.Ebx = 0x1710; w.Call(0xe02a, g);
        g.Esi = U32(d, 0x1922); g.Add(g.Esi, 0x2520); g.Esi += 0x2520; g.Eax = 0x1e0; g.Ebx = 0xe2; g.Ecx = 0x90; g.Edx = 0x52; w.Call(0x1df08, g);
        g.Esi = 0x216; w.Call(0xdee4, g);
    }

    /// <summary>65DF: the widget ESI follows the object +18 (a pool offset): a living object of the player +2 gives the position (-[190E] - 17h, -[1912] - 3Ch), the frame +1C counts to 0Ah with the flag 1 of [19E7], the picture +15 = E7h + 5 * ((([1C96] + 8) &amp; 0Eh)) + frame; with the tool L3[2] = 0 and E5AB (C02h) answering zero for the player +1D the widget selects its group (+2 to [1C87], 6ACD); with another tool the widget asks the pictures +10 = +11 = 8. A dead or foreign object is looked up again (BA60 with the position; when it is found, the pool offset is stored in +18; otherwise DFE5 with 2Dh, and [4C] decrements).</summary>
    public static void F65DF(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Edi = U32(r, 0x18); g.Add(g.Edi, w.PoolBase); g.Edi += w.PoolBase; var o = w.ObjectAt(g.Edi);
        g.Cmp(o[0x19], 0, 8);
        if (!g.Jle)
        {
            g.Al = o[0x1b]; g.Cmp(g.Al, r[2], 8);
            if (g.Zf)
            {
                g.Eax = U32(o, 4); g.Eax -= U32(d, 0x190e); g.Eax -= 0x17; Put(r, 4, g.Eax);
                g.Eax = U32(o, 8); g.Eax -= U32(d, 0x1912); g.Eax -= 0x3c; Put(r, 8, g.Eax);
                g.Test(d[0x19e7], 1, 8);
                if (!g.Zf) { r[0x1c] = Inc8(g, r[0x1c]); g.Cmp(r[0x1c], 0xa, 8); if (g.Cf) { } else r[0x1c] = 0; }
                g.Eax = d[0x1c96]; g.Al = Add8(g, g.Al, 8); g.Al &= 0xe; g.Eax = g.Eax + g.Eax * 4; g.Al = Add8(g, g.Al, r[0x1c]); g.Add(g.Eax, 0xe7); g.Eax += 0xe7;
                r[0x15] = (byte)g.Eax; r[0x16] = (byte)(g.Eax >> 8);
                g.Cmp(w.L3[2], 0, 8);
                if (!g.Zf) { r[0x10] = 8; r[0x11] = 8; return; }
                w.Call(0xe5f6, g); g.Eax = 0xc02; w.Call(0xe5ab, g); if (!g.Zf) return;
                g.Al = r[0x1d]; g.Cmp(d[0x1972], g.Al, 8); if (!g.Zf) return;
                g.Al = r[2]; d[0x1c87] = g.Al; w.Call(0x6acd, g); return;
            }
        }
        g.Ecx = U32(o, 4); g.Edx = U32(o, 8); g.Bl = r[2]; g.Al = r[0x1d]; uint esi = g.Esi; w.Call(0xba60, g); g.Esi = esi;
        g.Cmp(g.Eax, 1, 32);
        if (!(g.Cf || g.Zf)) { g.Edi -= w.PoolBase; Put(r, 0x18, g.Edi); return; }
        g.Al = 0x2d; g.Ah = r[2]; w.Call(0xdfe5, g); d[0x4c] = Dec8(g, d[0x4c]);
    }

    /// <summary>66E9: the redraw (1DF08) of the rectangle 1E0h x 138h at [1922] + 5580h (the picture of the panel: ESI = the source, EAX = 1E0h, EBX = 138h, ECX = 90h, EDX = 7Fh).</summary>
    public static void F66E9(IUnitActionWorld w, ActionRegs g)
    {
        g.Esi = U32(w.Ds, 0x1922); g.Add(g.Esi, 0x5580); g.Esi += 0x5580; g.Eax = 0x1e0; g.Ebx = 0x138; g.Ecx = 0x90; g.Edx = 0x7f; w.Call(0x1df08, g);
    }

    /// <summary>670F: the redraw (1DF08) from [1922] with EAX = 1E0h, EBX = A0h, ECX = 90h, EDX = 118h.</summary>
    public static void F670F(IUnitActionWorld w, ActionRegs g)
    {
        g.Esi = U32(w.Ds, 0x1922); g.Eax = 0x1e0; g.Ebx = 0xa0; g.Ecx = 0x90; g.Edx = 0x118; w.Call(0x1df08, g);
    }

    /// <summary>672F: the redraw (1DF08) from [1922] with EAX = 1E0h, EBX = A0h, ECX = 90h, EDX = 99h.</summary>
    public static void F672F(IUnitActionWorld w, ActionRegs g)
    {
        g.Esi = U32(w.Ds, 0x1922); g.Eax = 0x1e0; g.Ebx = 0xa0; g.Ecx = 0x90; g.Edx = 0x99; w.Call(0x1df08, g);
    }

    /// <summary>674F: the clearing of the picture: 3EEA (EAX = 0, EBX = ECX = 1D0h, EDX = 10h), then 6FCC with ESI = [192A], EDI = 1001D0h, EBP = 280h, ECX = EDX = 0.</summary>
    public static void F674F(IUnitActionWorld w, ActionRegs g)
    {
        g.Eax = 0; g.Logic(0, 32); g.Ebx = 0x1d0; g.Ecx = 0x1d0; g.Edx = 0x10; w.Call(0x3eea, g);
        g.Ecx = 0; g.Edx = 0; g.Logic(0, 32); g.Ebp = 0x280; g.Esi = U32(w.Ds, 0x192a); g.Edi = 0x1001d0; w.Call(0x6fcc, g);
    }

    /// <summary>6996: the direction (0..FFh, 0 = east, the clockwise quarter 40h) from the point (EAX, EBX) to the point (ECX, EDX): the quarter and the swap of the axes give [50C]; the ratio of the smaller to the larger distance (a 16-bit division of the shifted value) indexes the table DS:287E; equal distances give 20h. AL = the direction.</summary>
    public static void F6996(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint ebx = g.Ebx, ecx = g.Ecx, edx = g.Edx, eax = g.Eax; byte q = 0x40;
        bool borrow = ecx < eax; ecx -= eax;
        if (borrow) { ecx = 0 - ecx; (edx, ebx) = (ebx, edx); q += 0x80; }
        borrow = edx < ebx; edx -= ebx;
        if (borrow) { edx = 0 - edx; (edx, ecx) = (ecx, edx); q -= 0x40; }
        d[0x50c] = q; eax = (eax & 0xffffff00) | 0x20;
        if (ecx != edx)
        {
            bool bigger = ecx > edx;
            if (!bigger) (edx, ecx) = (ecx, edx);
            eax = edx << 8; edx >>= 8; uint divisor = ecx & 0xffff; uint dividend = (edx << 16) | (eax & 0xffff);
            if (divisor == 0 || dividend / divisor > 0xffff) throw new OverflowException("DIV r/m16 faults");
            uint quo = dividend / divisor, rem = dividend % divisor; eax = (eax & 0xffff0000) | quo; edx = (edx & 0xffff0000) | rem; eax &= 0xff;
            if (bigger) eax = d[0x287e + (int)eax];
            else { byte ah = d[0x287e + (int)eax]; eax = ((uint)ah << 8) | (byte)(0x40 - ah); }
        }
        g.Eax = eax; g.Ebx = ebx; g.Ecx = ecx; g.Edx = edx; g.Al = Add8(g, g.Al, d[0x50c]);
    }

    /// <summary>69FE: the approach step: the distances |EAX - ECX| and |EBX - EDX| against the half sizes (+3, +4) &lt;&lt; 5 of the object EDI; when both are within, AL = 0 (EAX, EBX are kept below); else the direction (6996) selects, with the toggle [50D], the offset (+4, +3 times DS:19C8/19C7) added to BX / AX; ECX = the old EAX, CL = FFh.</summary>
    public static void F69FE(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint e0 = g.Eax, b0 = g.Ebx; uint a = g.Eax - g.Ecx; if ((a & 0x80000000) != 0) a = 0 - a; uint b = g.Ebx - g.Edx; if ((b & 0x80000000) != 0) b = 0 - b;
        var o = w.ObjectAt(g.Edi); g.Ebp = (uint)o[3] << 5; g.Cmp(a, g.Ebp, 32);
        bool far = a > g.Ebp;
        if (!far) { g.Ebp = (uint)o[4] << 5; far = b > g.Ebp; }
        if (!far) { g.Eax = a & 0xffffff00; g.Ebx = b; g.Logic(0, 8); return; }
        g.Eax = e0; g.Ebx = b0; F6996(w, g);
        g.Al = Add8(g, g.Al, 0x10); g.Al = (byte)g.Shr(g.Al, 5, 8); g.Al = Add8(g, g.Al, d[0x50d]); g.Al &= 6; d[0x50d] ^= 1;
        g.Ebx = b0; g.Ecx = e0; g.Edx = g.Al;
        g.Eax = (uint)o[4] << 5; short p = (short)((sbyte)d[(int)g.Edx * 2 + 0x19c8] * (sbyte)(byte)g.Eax); g.Eax = (g.Eax & 0xffff0000) | (ushort)p;
        g.Ebx = (g.Ebx & 0xffff0000) | (ushort)(g.Ebx + g.Eax);
        g.Eax = (uint)o[3] << 5; p = (short)((sbyte)d[(int)g.Edx * 2 + 0x19c7] * (sbyte)(byte)g.Eax); g.Eax = (g.Eax & 0xffff0000) | (ushort)p;
        g.Eax = (g.Eax & 0xffff0000) | (ushort)(g.Eax + g.Ecx);
        g.Cl = 0xff; g.Logic(g.Cl, 8);
    }

    /// <summary>6A73: the selection is cleared: DF2A with 29h, 32h, 2Bh, 670F, 6EB2, [1C89] = 0, [1A1B] = 1, the message 4Eh (DEE4); an own object [1969] (+3 = [1972]) also D182 (AH = 2) and the message 147h.</summary>
    public static void F6A73(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Al = 0x29; FDF2A(w, g); g.Al = 0x32; FDF2A(w, g); g.Al = 0x2b; FDF2A(w, g); w.Call(0x670f, g); w.Call(0x6eb2, g);
        d[0x1c89] = 0; d[0x1a1b] = 1; g.Esi = 0x4e; w.Call(0xdee4, g);
        g.Esi = U32(d, 0x1969); var o = w.ObjectAt(g.Esi); g.Al = o[3]; g.Cmp(g.Al, d[0x1972], 8); if (!g.Zf) return;
        g.Ah = 2; w.Call(0xd182, g); g.Esi = 0x147; w.Call(0xdee4, g);
    }

    /// <summary>6ACD: the group is selected: DF2A with 29h, 32h, 2Bh, 670F, [1969] = 0, [1C89] = 1, [1A1B] = 1, the messages 147h and 37Bh (DEE4).</summary>
    public static void F6ACD(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Al = 0x29; FDF2A(w, g); g.Al = 0x32; FDF2A(w, g); g.Al = 0x2b; FDF2A(w, g); w.Call(0x670f, g);
        Put(d, 0x1969, 0); d[0x1c89] = 1; d[0x1a1b] = 1; g.Esi = 0x147; w.Call(0xdee4, g); g.Esi = 0x37b; w.Call(0xdee4, g);
    }

    /// <summary>5969: the list of the order records of the selected object [1969] (the pointer DS:6DF + 56h * army + 4 * owner): every record whose byte +0 is 80h and whose dword +3 is not zero is queued (DF7E with 33Fh; EDI = the new record in L3: +12 = the end of the record data, +8 = the height 28h, +14h per record); the next record starts 10h + 2 * (+F) bytes on, the list ends with the byte +1 = FFh.</summary>
    public static void F5969(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3; l[0] = 0x2e; l[1] = 0; g.Esi = U32(d, 0x1969); var sel = w.ObjectAt(g.Esi);
        g.Eax = 0x56; Mul8(g, g.Al, sel[0]); g.Ebx = sel[3]; g.Esi = U32(d, (int)(g.Eax & 0xffff) + (int)g.Ebx * 4 + 0x6df); g.Ebx = 0x28;
        for (; ; )
        {
            g.Cmp(w.Peek(g.Esi), 0x80, 8);
            if (g.Zf)
            {
                uint dw = Peek32(w, g.Esi + 3); g.Cmp(dw, 0, 32);
                if (!g.Zf)
                {
                    uint esi = g.Esi, ebx = g.Ebx; g.Esi = 0x33f; g.Al = 1; FDF7E(w, g); g.Ebx = ebx; g.Esi = esi;
                    int e = (int)g.Edi; g.Eax = g.Esi + 3 + dw; Put(l, e + 0x12, g.Eax); Put(l, e + 8, g.Ebx); g.Ebx += 0x14;
                }
            }
            g.Ecx = w.Peek(g.Esi + 0xf); g.Esi = g.Esi + g.Ecx * 2 + 0x10; g.Cmp(w.Peek(g.Esi + 1), 0xff, 8);
            if (g.Zf) return;
        }
    }
    private static uint Peek32(IUnitActionWorld w, uint a) => (uint)(w.Peek(a) | w.Peek(a + 1) << 8 | w.Peek(a + 2) << 16 | w.Peek(a + 3) << 24);

    /// <summary>59DE: the group widget list: L3[0] = 33h; the distinct kinds (+E, +0 of the objects of the owner's chain with +1B = [1C87] and +19 &gt; 0) are collected at DS:1F45 (dwords ended with FFh, their number in [1F41], the objects in [1F31]); with any: the widget EDI gets the number +16, the top +1A = E0h - 1Bh * count and +1C = count; for every kind a record (DF7E with 472h; EDI = the new record: +2 = kind, +14 = army, +15 = the picture of the template 68A0, +8 = the running position [1F31] that grows by 32h); then the message 297h (DEE4) and A4A9 with 0.</summary>
    public static void F59DE(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3; l[0] = 0x33; l[1] = 0; uint widget = g.Edi; var wd = w.ObjectAt(widget);
        g.Eax = 0xf5; Mul8(g, 0xf5, d[0x1972]); var part = w.PartOf(d[0x1972]);
        g.Ebp = U32(part, 9) + w.PoolBase; g.Edi = U32(part, 0xe) + w.PoolBase;
        d[0x1f45] = 0xff; d[0x1f41] = 0; Put(d, 0x1f31, 0);
        g.Cmp(g.Edi, g.Ebp, 32);
        while (!g.Zf)
        {
            var o = w.ObjectAt(g.Edi); g.Al = o[0x1b]; g.Cmp(g.Al, d[0x1c87], 8);
            if (g.Zf)
            {
                g.Cmp(o[0x19], 0, 8);
                if (!g.Jle)
                {
                    Put(d, 0x1f31, U32(d, 0x1f31) + 1); g.Eax = 0; g.Al = o[0xe]; g.Ah = o[0]; g.Esi = 0x1f41;
                    for (; ; )
                    {
                        g.Esi += 4; g.Cmp(U32(d, (int)g.Esi), g.Eax, 32); if (g.Zf) break;
                        g.Cmp(d[g.Esi], 0xff, 8); if (g.Zf) { Put(d, (int)g.Esi, g.Eax); d[g.Esi + 4] = 0xff; d[0x1f41] = Inc8(g, d[0x1f41]); break; }
                    }
                }
            }
            g.Edi += U32(o, 0x10); g.Cmp(g.Edi, g.Ebp, 32);
        }
        g.Edi = widget; g.Cmp(d[0x1f41], 0, 8); if (g.Zf) return;
        g.Eax = U32(d, 0x1f31); Put(wd, 0x16, g.Eax); g.Eax = 0x1b; Mul8(g, g.Al, d[0x1f41]); g.Eax = 0 - (g.Eax & 0xffff); g.Eax += 0xe0; Put(d, 0x1f31, g.Eax);
        wd[0x1a] = (byte)g.Eax; wd[0x1b] = (byte)(g.Eax >> 8); g.Al = d[0x1f41]; wd[0x1c] = g.Al; g.Esi = 0x1f45;
        for (; ; )
        {
            uint esi = g.Esi; g.Esi = 0x472; g.Al = 1; FDF7E(w, g); g.Esi = esi; int e = (int)g.Edi;
            g.Eax = U32(d, (int)g.Esi); l[e + 2] = g.Al; l[e + 0x14] = g.Ah;
            uint edi = g.Edi; F68A0(w, g); var t = w.ObjectAt(g.Edi); g.Eax = (g.Eax & 0xffff0000) | U16(t, 5); g.Edi = edi;
            l[e + 0x15] = (byte)g.Eax; l[e + 0x16] = (byte)(g.Eax >> 8); g.Eax = U32(d, 0x1f31); Put(l, e + 8, g.Eax);
            Put(d, 0x1f31, U32(d, 0x1f31) + 0x32); g.Esi += 4; g.Cmp(d[g.Esi], 0xff, 8);
            if (g.Zf) break;
        }
        g.Esi = 0x297; w.Call(0xdee4, g); g.Eax = 0; FA4A9(w, g);
    }

    /// <summary>42BB: the object cycle, every second world step ([1C4E] set, [1C4F] counts 2 down): [19EA] and FS:10 count; for every participant (from 97Ah, [6C6] of them, 0F5h apart) every object of its chain: the template (the table 6CAh: army * 56h, +4 = DAT, +9 = WRK base; + kind * 0ABh) gives [1C49], the footprint [273F] and the callback [19C3] = WRK base + template +3B, called with AH = 1 (ESI = the object); then +1A = +19 (the health of the last cycle), a lost or dead target (+1C: its +17 bit 200h or health below 0) is forgotten, and every eighth cycle +17 &amp;= FDD8h.</summary>
    public static void F42BB(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Cmp(d[0x1c4e], 0, 8); if (g.Zf) return;
        d[0x1c4f] = Dec8(g, d[0x1c4f]); if (!g.Zf) return;
        d[0x1c4f] = 2; d[0x19ea] = Inc8(g, d[0x19ea]); w.Fs10 = Inc8(g, w.Fs10);
        g.Cl = d[0x6c6]; g.Esi = 0x97a;
        do
        {
            uint ecx = g.Ecx, esi = g.Esi; var part = w.PartOf((byte)((esi - 0x97a) / 0xf5));
            g.Eax = U32(part, 9) + w.PoolBase; g.Esi = U32(part, 0xe) + w.PoolBase; g.Cmp(g.Esi, g.Eax, 32);
            if (!g.Zf)
            {
                for (; ; )
                {
                    var o = w.ObjectAt(g.Esi); g.Eax = o[0]; g.Ah = 0x56; Mul8(g, g.Al, g.Ah); g.Eax += 0x6ca;
                    int table = (int)g.Eax; g.Ecx = U32(d, table + 9); g.Edi = w.ArmyTable(o[0]);
                    g.Eax = o[0xe]; g.Ah = 0xab; Mul8(g, g.Al, g.Ah); g.Edi += g.Eax; Put(d, 0x1c49, g.Edi);
                    var t = w.ObjectAt(g.Edi); g.Ebx = U32(t, 3); d[0x273f] = (byte)g.Ebx; d[0x2740] = (byte)(g.Ebx >> 8);
                    g.Add(g.Ecx, U32(t, 0x3b)); g.Ecx += U32(t, 0x3b); Put(d, 0x19c3, g.Ecx); g.Ah = 1;
                    uint cur = g.Esi; w.Call(g.Ecx, g); g.Esi = cur; o = w.ObjectAt(g.Esi);
                    g.Al = o[0x19]; o[0x1a] = g.Al; g.Eax = U32(o, 0x1c); g.Test(g.Eax, g.Eax, 32);
                    if (!g.Zf)
                    {
                        g.Eax += w.PoolBase; var tgt = w.ObjectAt(g.Eax); g.Test(U16(tgt, 0x17), 0x200, 16);
                        bool clear = !g.Zf;
                        if (!clear) { g.Cmp(tgt[0x19], 0, 8); clear = g.Sf != g.Of; }
                        if (clear) Put(o, 0x1c, 0);
                    }
                    g.Test(d[0x19ea], 7, 8);
                    if (g.Zf) { ushort f = (ushort)(U16(o, 0x17) & 0xfdd8); o[0x17] = (byte)f; o[0x18] = (byte)(f >> 8); g.Logic(f, 16); }
                    g.Eax = U32(part, 9) + w.PoolBase; g.Cmp(g.Esi, g.Eax, 32);
                    if (!g.Cf) break;
                    g.Esi += U32(o, 0x10); g.Cmp(g.Esi, g.Eax, 32);
                    if (!g.Cf) break;
                }
            }
            g.Ecx = ecx; g.Esi = esi; g.Add(g.Esi, 0xf5); g.Esi += 0xf5; g.Cl = Dec8(g, g.Cl);
        } while (!g.Zf);
    }

    /// <summary>4473: the pending event [1CAD] (-1 = none) is dropped when no player but the own one is waiting: [1CB6] = FFh drops it at once; else for every owner AL below [6C6] other than [1972]: when AL is not in the list DS:1CB5 (ended by FFh) the byte DS:1CCB + 16h * AL must be FFh, or the event stays.</summary>
    public static void F4473(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Cmp(U32(d, 0x1cad), 0xffffffff, 32); if (g.Zf) return;
        g.Cmp(d[0x1cb6], 0xff, 8);
        if (!g.Zf)
        {
            g.Eax = 0; g.Ebx = 0x1ccb;
            do
            {
                g.Cmp(g.Al, d[0x1972], 8);
                if (!g.Zf)
                {
                    g.Ecx = 0x1cb4; bool skip = false;
                    for (; ; )
                    {
                        g.Ecx++; g.Cmp(d[g.Ecx], 0xff, 8); if (g.Zf) { skip = true; break; }
                        g.Cmp(d[g.Ecx], g.Al, 8); if (g.Zf) break;
                    }
                    if (!skip) { g.Cmp(d[g.Ebx], 0xff, 8); if (!g.Zf) return; }
                }
                g.Al = Inc8(g, g.Al); g.Ebx += 0x16; g.Cmp(g.Al, d[0x6c6], 8);
            } while (!g.Zf);
        }
        Put(d, 0x1cad, 0xffffffff);
    }

    /// <summary>44C0: the input step of the frame: with [1EC6] = 0 and the selected object [1974] dead (+19 not above 0) the view goes to it (A320, cells +4/+8 &gt;&gt; 5), EDD5 (AL = 0Eh, AH = BL = [1972]), the selection is cleared, the entry DS:1CCB + 16h * [1972] = FFh and a pending event [1CAD] becomes 12Ch; without a selection and a pending event, on a zero [19EA] [1EC6] = 2; then ED20 = the settlement 1A35h * [1972] + [6BA], the cursor [1946] = [1942], [1965] = 0 and E559, 486D, 4580.</summary>
    public static void F44C0(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Cmp(d[0x1ec6], 0, 8);
        if (g.Zf)
        {
            g.Esi = U32(d, 0x1974); g.Test(g.Esi, g.Esi, 32);
            if (!g.Zf)
            {
                var o = w.ObjectAt(g.Esi); g.Cmp(o[0x19], 0, 8);
                if (g.Jle)
                {
                    g.Eax = g.Shr(U32(o, 4), 5, 32); g.Ebx = g.Shr(U32(o, 8), 5, 32); FA320(w, g);
                    g.Al = 0xe; g.Bl = d[0x1972]; g.Ah = g.Bl; w.Call(0xedd5, g);
                    Put(d, 0x1974, 0); g.Eax = 0x16; Mul8(g, g.Al, d[0x1972]); d[(g.Eax & 0xffff) + 0x1ccb] = 0xff;
                    g.Cmp(U32(d, 0x1cad), 0xffffffff, 32); if (!g.Zf) Put(d, 0x1cad, 0x12c);
                }
            }
            else
            {
                g.Cmp(U32(d, 0x1cad), 0xffffffff, 32);
                if (g.Zf) { g.Test(d[0x19ea], 0xff, 8); if (g.Zf) d[0x1ec6] = 2; }
            }
        }
        g.Eax = 0x1a35; g.Ebx = d[0x1972]; ulong p = (ulong)g.Eax * g.Ebx; g.Eax = (uint)p; g.Edx = (uint)(p >> 32); g.Cf = g.Of = g.Edx != 0;
        g.Add(g.Eax, U32(d, 0x6ba)); g.Eax += U32(d, 0x6ba); Put(d, 0xed20, g.Eax); g.Eax = U32(d, 0x1942); Put(d, 0x1946, g.Eax); Put(d, 0x1965, 0);
        w.Call(0xe559, g); w.Call(0x486d, g); w.Call(0x4580, g);
    }

    /// <summary>F5CE: the gate of the message step: with [1C4E] set and the mode L3[0] = 0 or at least 0Ah (but not 31h, 32h) F629.</summary>
    public static void FF5CE(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Cmp(d[0x1c4e], 0, 8); if (g.Zf) return;
        g.Al = w.L3[0]; g.Test(g.Al, g.Al, 8);
        if (!g.Zf)
        {
            g.Cmp(g.Al, 0x32, 8); if (g.Zf) return; g.Cmp(g.Al, 0x31, 8); if (g.Zf) return; g.Cmp(g.Al, 0xa, 8); if (g.Cf) return;
        }
        w.Call(0xf629, g);
    }

    /// <summary>F5F2: looks for a free message number 14h..17h: AL runs from 14h up to 17h while the number is in the queue (L3 from 7D4h: the records of 1Eh bytes, the number in +1, the end in 0DCE); a free number is stored in L3[3CD9], its slot (AL - 14h) * 60h + 14h in the dword L3[3CD1]; ZF = 0 (AL = FFh); all four taken - ZF = 1.</summary>
    public static void FF5F2(IUnitActionWorld w, ActionRegs g)
    {
        var l = w.L3; g.Eax = 0x14;
        for (; ; )
        {
            g.Edi = 0x7d4; bool taken = false;
            for (; ; )
            {
                g.Edi += 0x1e; g.Cmp(g.Edi, U32(l, 0xdce), 32);
                if (g.Zf)
                {
                    l[0x3cd9] = g.Al; g.Al = Sub8(g, g.Al, 0x14); g.Ah = 0x60; Mul8(g, g.Al, g.Ah); g.Add(g.Eax, 0x14); g.Eax += 0x14; Put(l, 0x3cd1, g.Eax); g.Al |= 0xff; g.Logic(g.Al, 8); return;
                }
                g.Cmp(g.Al, l[g.Edi + 1], 8); if (g.Zf) { taken = true; break; }
            }
            if (taken) { g.Al = Inc8(g, g.Al); g.Cmp(g.Al, 0x18, 8); if (g.Zf) return; }
        }
    }

    /// <summary>F781: E055 for AL = 14h..17h.</summary>
    public static void FF781(IUnitActionWorld w, ActionRegs g, bool real = false)
    {
        g.Al = 0x14;
        do { uint e = g.Eax; if (real) FE055(w, g); else w.Call(0xe055, g); g.Eax = e; g.Al = Inc8(g, g.Al); g.Cmp(g.Al, 0x18, 8); } while (!g.Zf);
    }

    /// <summary>F791: E070 for AL = 14h..17h.</summary>
    public static void FF791(IUnitActionWorld w, ActionRegs g, bool real = false)
    {
        g.Al = 0x14;
        do { uint e = g.Eax; if (real) FE070(w, g); else w.Call(0xe070, g); g.Eax = e; g.Al = Inc8(g, g.Al); g.Cmp(g.Al, 0x18, 8); } while (!g.Zf);
    }

    private static void Dec16(ActionRegs g, byte[] b, int at)
    {
        ushort v = U16(b, at), r = (ushort)(v - 1); b[at] = (byte)r; b[at + 1] = (byte)(r >> 8); g.Zf = r == 0; g.Sf = (r & 0x8000) != 0; g.Of = v == 0x8000;   // CF is kept
    }

    /// <summary>F7A1: the message widget ESI: the timer +1A counts down on the even steps; at 0, with the type +3 = 11h or E5AB (E02h) answering zero, the widget is dropped from the queue (DF2A with +1).</summary>
    public static void FF7A1(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Test(d[0x19ea], 1, 8);
        if (g.Zf) { Dec16(g, r, 0x1a); if (g.Zf) { g.Al = r[1]; FDF2A(w, g); return; } }
        w.Call(0xe5f6, g); g.Cmp(r[3], 0x11, 8); if (g.Zf) { g.Al = r[1]; FDF2A(w, g); return; }
        g.Eax = 0xe02; w.Call(0xe5ab, g); if (!g.Zf) return;
        g.Al = r[1]; FDF2A(w, g);
    }

    /// <summary>F7D0: the widget of the demand message: a refused EE69 (+1C, +1D, [1972]) or the timer or the type 11h or E5AB (E02h) answering non-zero ends it as in F7A1; otherwise, when the owner +1D has more than 7CFh in the settlement field +19F1, the message 3B94h (the names are equal: [DD4] = +1D) or 3BCCh (AL = +1C, AH = +1D) goes out (DEE4); then DF2A with +1.</summary>
    public static void FF7D0(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; var l = w.L3; g.Al = r[0x1c]; g.Bl = r[0x1d]; g.Ah = d[0x1972]; w.Call(0xee69, g);
        if (g.Zf) { Drop(w, g, r); return; }
        g.Test(d[0x19ea], 1, 8);
        if (g.Zf) { Dec16(g, r, 0x1a); if (g.Zf) { Drop(w, g, r); return; } }
        w.Call(0xe5f6, g); g.Cmp(r[3], 0x11, 8); if (g.Zf) { Drop(w, g, r); return; }
        g.Eax = 0xe02; w.Call(0xe5ab, g); if (!g.Zf) return;
        g.Al = r[0x1d]; w.Call(0x1ead8, g);
        g.Cmp(BitConverter.ToUInt32(w.Stats, (int)(g.Edx - 0x40a000u) + 0x19f1), 0x7cf, 32);
        if (g.Cf || g.Zf) { Drop(w, g, r); return; }
        uint esi = g.Esi; g.Eax = 0x13; Mul8(g, g.Al, r[0x1c]); g.Edx = (g.Eax & 0xffff) + 0xefb; int t = (int)g.Edx;
        g.Al = l[t + 0x12]; g.Cmp(g.Al, l[t + 0x11], 8);
        if (g.Zf) { g.Al = r[0x1d]; l[0xdd4] = g.Al; g.Esi = 0x3b94; w.Call(0xdee4, g); }
        else { g.Al = r[0x1c]; g.Ah = r[0x1d]; g.Esi = 0x3bcc; w.Call(0xdee4, g); }
        g.Esi = esi; Drop(w, g, r);
    }
    private static void Drop(IUnitActionWorld w, ActionRegs g, byte[] r) { g.Al = r[1]; FDF2A(w, g); }

    /// <summary>F862: the widget of the offer message: as F7D0 up to E5AB; with EE69 answering non-zero the item of the object EDI (+0A) is shown (EDD5 with 95h, A320 on the cell of its position), then DF2A with +1.</summary>
    public static void FF862(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Test(d[0x19ea], 1, 8);
        if (g.Zf) { Dec16(g, r, 0x1a); if (g.Zf) { Drop(w, g, r); return; } }
        w.Call(0xe5f6, g); g.Cmp(r[3], 0x11, 8); if (g.Zf) { Drop(w, g, r); return; }
        g.Eax = 0xe02; w.Call(0xe5ab, g); if (!g.Zf) return;
        g.Al = r[0x1c]; g.Bl = r[0x1d]; g.Ah = d[0x1972]; w.Call(0xee69, g); if (g.Zf) { Drop(w, g, r); return; }
        uint esi = g.Esi; var o = w.ObjectAt(g.Edi); g.Ebx = U32(o, 0xa); uint saved = g.Ebx; g.Ebx >>= 16; g.Bl &= 0xf; g.Al = 0x95; g.Ah = d[0x1972]; w.Call(0xedd5, g);
        g.Eax = saved; g.Ebx = (uint)(byte)(g.Eax >> 8); g.Eax = (byte)g.Eax; FA320(w, g); g.Esi = esi; Drop(w, g, r);
    }

    /// <summary>F8C9: the moving message widget: with +1D from 6 on and the flag 2 of [19E7] the speed +1D falls by 6 and the position +4 grows by 6.</summary>
    public static void FF8C9(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Cmp(r[0x1d], 6, 8); if (g.Cf) return;
        g.Test(w.Ds[0x19e7], 2, 8); if (g.Zf) return;
        r[0x1d] = Sub8(g, r[0x1d], 6); g.Add(U32(r, 4), 6); Put(r, 4, U32(r, 4) + 6);
    }

    /// <summary>F629: a message to the players: on every 16th step ([19E9] &amp; 0Fh = 0) for the owners [6C6] - 1 .. 0 and their 8 entries of 0Eh bytes (DS:97A + 0F5h * owner + 34h): an entry whose kind +0 is not FFh, whose owner +9 is [1972] and whose dword +1 is above L3[3CDA] gets a free message number (F5F2) and a record in the queue (DF7E to the front; for another owner first EAD8 with 0FFFFFFCEh and the handler F8C9 of the two records before), the handler F7D0 (F862 for the kind 3), the text chosen at random by 9E69 among the +0 variants (ended by FFh) of the table L3:0EFB + 13h * kind, its position +4 = 11Ah - the half width (7012), +8 = the slot + 40h and the effects (A4A9) of the table +4 and 1. The step ends with L3[3CDA] = [1CA9].</summary>
    public static void FF629(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3; g.Test(d[0x19e9], 0xf, 8); if (!g.Zf) return;
        g.Cl = d[0x6c6]; g.Cl = Dec8(g, g.Cl);
        for (; ; )
        {
            g.Eax = 0xf5; Mul8(g, g.Al, g.Cl); g.Esi = (g.Eax & 0xffff) + 0x9ae; g.Ch = 8; byte owner = g.Cl; var part = w.PartOf(owner);
            do
            {
                int off = (int)(g.Esi - 0x97a - 0xf5u * owner);
                g.Cmp(part[off], 0xff, 8);
                if (!g.Zf)
                {
                    g.Al = part[off + 9]; g.Cmp(g.Al, d[0x1972], 8);
                    if (g.Zf)
                    {
                        g.Eax = U32(part, off + 1); g.Cmp(g.Eax, U32(l, 0x3cda), 32);
                        if (!(g.Cf || g.Zf))
                        {
                            FF5F2(w, g);
                            if (!g.Zf)
                            {
                                uint ecx = g.Ecx, esi = g.Esi;
                                g.Cmp(g.Cl, d[0x1972], 8);
                                if (!g.Zf)
                                {
                                    g.Eax = g.Cl; g.Bl = l[0x3cd9]; g.Ecx = 0xffffffce; g.Edx = U32(l, 0x3cd1); g.Ebp = 0xe65a; w.Call(0xead8, g);
                                    int e0 = (int)g.Edi; Put(l, e0 + 4, U32(l, e0 + 4) + 0x46); Put(l, e0 - 0x12, 0xf8c9); l[e0 - 1] = 0x46; Put(l, e0 - 0x30, 0xf8c9); l[e0 - 0x1f] = 0x46;
                                }
                                g.Esi = 0x3cde; g.Al = 0; g.Logic(0, 8); FDF7E(w, g); g.Esi = esi;
                                g.Eax = 0x13; Mul8(g, g.Al, part[off]); g.Edx = (g.Eax & 0xffff) + 0xefb; int t = (int)g.Edx; int e = (int)g.Edi;
                                g.Eax = ecx; g.Cmp(g.Al, d[0x1972], 8); if (!g.Zf) Put(l, e + 0xc, 0xf7d0);
                                g.Ebx = U32(l, t); w.Call(0x9e69, g); Mul8(g, g.Al, w.Peek(g.Ebx));
                                bool skip = true;
                                for (; ; )
                                {
                                    if (!skip) { g.Ebx++; if (w.Peek(g.Ebx) != 0xff) continue; }
                                    skip = false; g.Ebx++; g.Ah = Dec8(g, g.Ah); if (g.Sf) break;
                                }
                                Put(l, e + 0x12, g.Ebx); g.Al = d[0x1972]; l[e + 0x17] = g.Al; g.Eax = ecx; l[e + 0x18] = g.Al; l[e + 0x1d] = g.Al; g.Al = part[off]; l[e + 0x1c] = g.Al;
                                g.Al = l[0x3cd9]; l[e + 1] = g.Al; g.Eax = U32(l, 0x3cd1); g.Eax += 0x40; Put(l, e + 8, g.Eax);
                                uint esi2 = g.Esi; g.Esi = g.Ebx; F7012(w, g); g.Eax = 0x11a; g.Ebx = g.Shr(g.Ebx, 1, 32); g.Eax -= g.Ebx; Put(l, e + 4, g.Eax); g.Esi = esi2;
                                g.Cmp(part[off], 3, 8); if (g.Zf) Put(l, e + 0xc, 0xf862);
                                g.Eax = U32(l, t + 4); FA4A9(w, g); g.Eax = 1; FA4A9(w, g); g.Ecx = ecx;
                            }
                        }
                    }
                }
                g.Add(g.Esi, 0xe); g.Esi += 0xe; g.Ch = Dec8(g, g.Ch);
            } while (!g.Zf);
            g.Cl = Dec8(g, g.Cl); if (g.Sf) break;
        }
        g.Eax = U32(d, 0x1ca9); Put(l, 0x3cda, g.Eax);
    }

    /// <summary>DFE5: removes the first queue record whose word +1 (number, and its next byte) equals AX; the mode L3[0] equal to AL is cleared first (word 0). The later records move up (DWORDs then bytes), the end L3[0DCE] follows; AL = FFh. Without a match ZF = 1 and nothing changes. AH = the remainder of the moved bytes (modulo 4).</summary>
    public static void FDFE5(IUnitActionWorld w, ActionRegs g)
    {
        var l = w.L3; g.Cmp(g.Al, l[0], 8); if (g.Zf) { l[0] = 0; l[1] = 0; }
        g.Edi = 0x7d4; g.Ecx = U32(l, 0xdce);
        for (; ; )
        {
            g.Edi += 0x1e; g.Cmp(g.Edi, g.Ecx, 32); if (g.Zf) return;
            g.Cmp(g.Eax & 0xffff, U16(l, (int)g.Edi + 1), 16); if (g.Zf) break;
        }
        g.Esi = g.Edi + 0x1e; g.Ecx -= g.Esi; g.Ah = (byte)g.Ecx; uint dwords = g.Ecx >> 2; g.Ecx = dwords;
        uint rem = (uint)(g.Ah & 3); int total = (int)(dwords * 4 + rem);
        for (int k = 0; k < total; k++) l[(int)g.Edi + k] = l[(int)g.Esi + k];
        g.Edi += (uint)total; g.Esi += (uint)total; g.Ah = (byte)rem; g.Ecx = 0;
        Put(l, 0xdce, g.Edi); g.Al |= 0xff; g.Logic(g.Al, 8);
    }

    /// <summary>E02A: DFE5 for AH = BL .. BH (AL = the number).</summary>
    public static void FE02A(IUnitActionWorld w, ActionRegs g)
    {
        do
        {
            uint eax = g.Eax, ebx = g.Ebx; g.Ah = g.Bl; FDFE5(w, g); g.Ebx = ebx; g.Eax = eax;
            g.Bl = Inc8(g, g.Bl); g.Cmp(g.Bl, g.Bh, 8);
        } while (g.Cf || g.Zf);
    }

    /// <summary>E03C: looks for the queue record whose word +1 equals AX: EDI = it, AL = FFh and ZF = 0, otherwise ZF = 1 (EDI = the end).</summary>
    public static void FE03C(IUnitActionWorld w, ActionRegs g)
    {
        var l = w.L3; g.Edi = 0x7d4;
        for (; ; )
        {
            g.Edi += 0x1e; g.Cmp(g.Edi, U32(l, 0xdce), 32); if (g.Zf) return;
            g.Cmp(g.Eax & 0xffff, U16(l, (int)g.Edi + 1), 16); if (g.Zf) break;
        }
        g.Al |= 0xff; g.Logic(g.Al, 8);
    }

    /// <summary>E055: the flag 40h of every queue record with the number AL is set.</summary>
    public static void FE055(IUnitActionWorld w, ActionRegs g)
    {
        var l = w.L3; g.Edi = 0x7f2;
        for (; ; )
        {
            g.Cmp(g.Edi, U32(l, 0xdce), 32); if (g.Zf) return;
            g.Cmp(l[g.Edi + 1], g.Al, 8); if (g.Zf) { l[g.Edi] |= 0x40; g.Logic(l[g.Edi], 8); }
            g.Add(g.Edi, 0x1e); g.Edi += 0x1e;
        }
    }

    /// <summary>E070: the flag 40h of every queue record with the number AL is cleared.</summary>
    public static void FE070(IUnitActionWorld w, ActionRegs g)
    {
        var l = w.L3; g.Edi = 0x7f2;
        for (; ; )
        {
            g.Cmp(g.Edi, U32(l, 0xdce), 32); if (g.Zf) return;
            g.Cmp(l[g.Edi + 1], g.Al, 8); if (g.Zf) { l[g.Edi] &= 0xbf; g.Logic(l[g.Edi], 8); }
            g.Add(g.Edi, 0x1e); g.Edi += 0x1e;
        }
    }

    /// <summary>DEE4: puts the message record at ESI (length dword +0, handler +4, byte +8 -> L3[0DD3], data from +9) into the queue when the room up to 0DCE allows it (else only the registers return, CF = 1), then calls the handler +4 with ESI the record; ESI = 7D4h afterwards.</summary>
    public static void FDEE4(IUnitActionWorld w, ActionRegs g)
    {
        var l = w.L3; uint eax0 = g.Eax, ebx0 = g.Ebx; g.Eax = 0xdce; g.Edi = U32(l, 0xdce); g.Ecx = Peek32(w, g.Esi); g.Cmp(g.Eax - g.Edi, g.Ecx, 32); g.Eax -= g.Edi;
        if (g.Cf) { g.Ebx = ebx0; g.Eax = eax0; return; }
        uint esi = g.Esi, edi = g.Edi, cnt = g.Ecx; l[0xdd3] = w.Peek(esi + 8);
        for (uint k = 0; k < cnt; k++) l[edi + k] = w.Peek(esi + 9 + k);
        Put(l, 0xdce, edi + cnt); g.Ecx = 0; g.Eax = cnt & 3; g.Edi = edi; g.Ebx = ebx0; g.Eax = eax0;
        g.Esi = esi; w.Call(Peek32(w, esi + 4), g); g.Esi = 0x7d4;
    }

    /// <summary>E571: the picture animation of the widget ESI (+10 current, +11 wanted): on every odd step of [19E7] a differing value marks the widget (+0 |= 80h); a picture with the low nibble 8 steps its high nibble by one towards the wanted one, any other jumps at once.</summary>
    public static void FE571(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Test(w.Ds[0x19e7], 1, 8); if (g.Zf) return;
        g.Al = r[0x11]; g.Cmp(g.Al, r[0x10], 8); if (g.Zf) return;
        r[0] |= 0x80; g.Logic(r[0], 8); g.Bl = g.Al; g.Ah = g.Al; g.Ah &= 0xf; g.Cmp(g.Ah, 8, 8);
        if (!g.Zf) { r[0x10] = g.Al; return; }
        g.Al &= 0xf0; r[0x10] &= 0xf; r[0x10] |= g.Al; g.Cmp(g.Bl, r[0x10], 8);
        if (!(g.Cf || g.Zf)) { r[0x10] = Inc8(g, r[0x10]); return; }
        r[0x10] = Dec8(g, r[0x10]);
    }

    /// <summary>E5AB: sets the wanted picture +11 of the widget ESI (low nibble AH for the types 1 and 3, else 8; cursor [1946] = A5009Ch) and animates (E571); ZF = the type +3 is 9.</summary>
    public static void FE5AB(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Test(r[3], 1, 8);
        if (g.Zf) g.Al = 8;
        else
        {
            Put(d, 0x1946, 0xa5009c); Put(d, 0x1965, 0); g.Cmp(r[3], 1, 8);
            if (!g.Zf) { g.Al = g.Ah; g.Cmp(r[3], 3, 8); if (!g.Zf) g.Al = 8; }
        }
        r[0x11] &= 0xf0; r[0x11] |= g.Al; FE571(w, g, r); g.Cmp(r[3], 9, 8);
    }

    /// <summary>E5E6: the widget ESI leaves the queue (DF2A with +1) and [DD3] = 0.</summary>
    public static void FE5E6(IUnitActionWorld w, ActionRegs g, byte[] r) { g.Al = r[1]; FDF2A(w, g); w.L3[0xdd3] = 0; }

    /// <summary>E5F6: the mode word L3[0]: with a non-zero mode equal to the widget's word +1 it is cleared; then a widget of the type bit 1 sets it to its word +1.</summary>
    public static void FE5F6(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var l = w.L3; g.Ebx = 0; g.Eax = (g.Eax & 0xffff0000) | U16(r, 1); g.Cmp(l[0], g.Bl, 8);
        if (!g.Zf)
        {
            g.Cmp(U16(l, 0), g.Eax & 0xffff, 16); if (!g.Zf) return;
            l[0] = 0; l[1] = 0;
        }
        g.Test(r[3], 1, 8); if (g.Zf) return;
        l[0] = (byte)g.Eax; l[1] = (byte)(g.Eax >> 8);
    }

    /// <summary>E621: E5AB with AH = E04h; when it answers non-zero (the type is not 9) nothing more, else the widget leaves the queue (DF2A).</summary>
    public static void FE621(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Eax = 0xe04; FE5AB(w, g, r); if (!g.Zf) return;
        g.Al = r[1]; FDF2A(w, g);
    }

    /// <summary>E636: a closing widget (the key 1 or the type bit 10h): the widgets 14h..17h lose the flag 40h (F791), the widget leaves the queue (E5E6) and the mode is 0.</summary>
    public static void FE636(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        g.Cmp(U16(w.Ds, 0x1c35), 1, 16);
        if (!g.Zf) { g.Test(r[3], 0x10, 8); if (g.Zf) return; }
        FF791(w, g, true); FE5E6(w, g, r); w.L3[0] = 0; w.L3[1] = 0;
    }

    /// <summary>E65B: the list of the players to talk to: the widgets 14h..17h are marked (F781), [DD4] = 0, mode 1; for every owner AL below [6C6] other than [1972] with the relation byte +B9+AL of the own record non-zero and more than 7CFh in the settlement field +19F1 its number goes to the list DS:1F45 (ended by FFh); the numbers are drawn in rows of at most four (a longer list splits half and half): EAD8 with the number, BL = 1, EBP = 0EC38h, ECX = 0F0h - 38h * count + 70h * column, EDX = 82h + 96h * row.</summary>
    public static void FE65B(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; var l = w.L3; FF781(w, g, true); l[0xdd4] = 0; l[0] = 1; l[1] = 0; g.Edi = 0x1f45;
        g.Eax = 0xf5; Mul8(g, g.Al, d[0x1972]); uint rec = g.Eax & 0xffff; var part = w.PartOf(d[0x1972]); g.Ecx = rec + 0x97a; g.Ebx = 0; g.Eax = 0; g.Esi = U32(d, 0x6ba);
        do
        {
            g.Cmp(part[0xb9 + g.Al], 0, 8);
            if (!g.Zf)
            {
                g.Cmp(g.Al, d[0x1972], 8);
                if (!g.Zf)
                {
                    g.Cmp(BitConverter.ToUInt32(w.Stats, g.Al * 0x1a35 + 0x19f1), 0x7cf, 32);
                    if (!(g.Cf || g.Zf)) { d[g.Edi] = g.Al; g.Edi++; g.Ebx++; }
                }
            }
            g.Al = Inc8(g, g.Al); g.Add(g.Esi, 0x1a35); g.Esi += 0x1a35; g.Cmp(g.Al, d[0x6c6], 8);
        } while (!g.Zf);
        d[g.Edi] = 0xff; g.Test(g.Ebx, g.Ebx, 32); if (g.Zf) return;
        g.Edi = 0x1f45; g.Edx = 0x82; g.Cmp(g.Ebx, 4, 32);
        if (!(g.Cf || g.Zf)) { g.Bh = g.Bl; g.Bh = (byte)g.Shr(g.Bh, 1, 8); g.Bl = Sub8(g, g.Bl, g.Bh); }
        for (; ; )
        {
            g.Eax = 0x38; Mul8(g, g.Al, g.Bl); g.Ecx = 0xf0; g.Ecx -= g.Eax & 0xffff;
            do
            {
                uint ebx = g.Ebx, ecx = g.Ecx, edx = g.Edx, edi = g.Edi; g.Eax = d[g.Edi]; g.Bl = 1; g.Ebp = 0xec38; w.Call(0xead8, g);
                g.Edi = edi; g.Edx = edx; g.Ecx = ecx; g.Ebx = ebx; g.Edi++; g.Add(g.Ecx, 0x70); g.Ecx += 0x70; g.Bl = Dec8(g, g.Bl);
            } while (!g.Zf);
            g.Add(g.Edx, 0x96); g.Edx += 0x96; g.Ebx = g.Shr(g.Ebx, 8, 32); if (g.Zf) return;
        }
    }

    /// <summary>11B4E: the wild spark of the map: with [1C4E] set ten times a random cell (9E41 / 9E55 scaled by the map size [18DE] x [18E2]) outside the rectangle [1906],[190A] (+0Dh) whose tile has the terrain class 3 in the high nibble of the byte +1 of its descriptor gets the effect CFAC (EAX = 0, EDX = the cell, ECX = 101h).</summary>
    public static void F11B4E(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Cmp(d[0x1c4e], 0, 8); if (g.Zf) return;
        g.Ecx = 10;
        for (; ; )
        {
            uint saved = g.Ecx; w.Call(0x9e41, g); Mul8(g, g.Al, d[0x18de]); g.Cl = g.Ah; w.Call(0x9e55, g); Mul8(g, g.Al, d[0x18e2]); g.Ch = g.Ah; g.Dl = d[0x1906];
            bool inside = false; g.Cmp(g.Cl, g.Dl, 8);
            if (!g.Cf)
            {
                g.Dh = d[0x190a]; g.Cmp(g.Ch, g.Dh, 8);
                if (!g.Cf)
                {
                    g.Add(g.Edx, 0xd0d); g.Edx += 0xd0d; g.Cmp(g.Cl, g.Dl, 8);
                    if (g.Cf || g.Zf) { g.Cmp(g.Ch, g.Dh, 8); inside = g.Cf || g.Zf; }
                }
            }
            if (!inside)
            {
                g.Eax = U32(d, 0x18de); Mul8(g, g.Al, g.Ch); g.Ebx = g.Cl; g.Add(g.Eax, g.Ebx); g.Eax += g.Ebx; g.Eax *= 4; uint cell = g.Eax / 4; g.Eax += U32(d, 0x18ee);
                g.Ebx = (g.Ebx & 0xffff0000) | (cell & 0xffff); g.Edx = w.TileProperty(cell, 0); g.Dh = (byte)(g.Dh & 0xf); g.Cmp(g.Dh, 3, 8);
                if (g.Zf) { g.Eax = 0; g.Edx = g.Ecx; g.Ecx = 0x101; w.Call(0xcfac, g); }
            }
            g.Ecx = saved; g.Ecx--; g.Zf = g.Ecx == 0; g.Sf = (g.Ecx & 0x80000000) != 0; g.Of = false; if (g.Zf) return;
        }
    }

    /// <summary>BE9D: the message 216h (DEE4).</summary>
    public static void FBE9D(IUnitActionWorld w, ActionRegs g) { g.Esi = 0x216; w.Call(0xdee4, g); }

    private static byte[] LiveRecord(IUnitActionWorld w, ActionRegs g, uint source)
    {
        g.Esi = source; g.Al = 1; FDF7E(w, g); var rec = new byte[0x30]; Array.Copy(w.L3, (int)g.Edi, rec, 0, 0x30); return rec;
    }
    private static void StoreRecord(IUnitActionWorld w, ActionRegs g, byte[] rec) { Array.Copy(rec, 0, w.L3, (int)g.Edi, 0x30); }

    /// <summary>BEA7: the record CFh into the queue, its +14 follows the byte +1A of the selected object (640B).</summary>
    public static void FBEA7(IUnitActionWorld w, ActionRegs g) { var rec = LiveRecord(w, g, 0xcf); g.Esi = g.Edi; F640B(w, g, rec); StoreRecord(w, g, rec); }

    /// <summary>BEBB: the record EDh into the queue with +1A = AL, +16 = AH; its +14 follows the byte +16 of the selected object (63F7).</summary>
    public static void FBEBB(IUnitActionWorld w, ActionRegs g)
    {
        uint eax = g.Eax; var rec = LiveRecord(w, g, 0xed); g.Eax = eax; rec[0x1a] = g.Al; rec[0x16] = g.Ah; g.Esi = g.Edi; F63F7(w, g, rec); StoreRecord(w, g, rec);
    }

    /// <summary>BED7: the record 10Bh into the queue (ESI is kept).</summary>
    public static void FBED7(IUnitActionWorld w, ActionRegs g) { uint esi = g.Esi; g.Esi = 0x10b; g.Al = 1; FDF7E(w, g); g.Esi = esi; }

    /// <summary>BEE6: the record 129h into the queue with +1A = AL, +16 = AH, +15 = BL; its +14 follows the selected object (645B).</summary>
    public static void FBEE6(IUnitActionWorld w, ActionRegs g)
    {
        uint eax = g.Eax, ebx = g.Ebx; var rec = LiveRecord(w, g, 0x129); g.Eax = eax; g.Ebx = ebx; rec[0x1a] = g.Al; rec[0x16] = g.Ah; rec[0x15] = g.Bl; g.Esi = g.Edi; F645B(w, g, rec); StoreRecord(w, g, rec);
    }

    /// <summary>BF07: the picture of the widget kind +31 (1, 2, 3 = 137h, 138h, 139h): 1E133 with [191A], then 1E098 for the rectangle 1E2h, ECh, 20h x 10h; ESI is kept.</summary>
    public static void FBF07(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        uint esi = g.Esi; g.Cmp(r[0x31], 1, 8);
        if (g.Zf) g.Eax = 0x137;
        else { g.Cmp(r[0x31], 2, 8); if (g.Zf) g.Eax = 0x138; else { g.Cmp(r[0x31], 3, 8); if (g.Zf) g.Eax = 0x139; else { g.Esi = esi; return; } } }
        g.Esi = U32(w.Ds, 0x191a); w.Call(0x1e133, g); g.Eax = 0x1e2; g.Ebx = 0xec; g.Ecx = 0x20; g.Edx = 0x10; w.Call(0x1e098, g); g.Esi = esi;
    }

    /// <summary>BF55: the scroll widget (type bit 1): above the middle of the widget (+14 / 2 + +8 against the pointer [1951]) the position +17 grows (cursor 18000Fh) while below +1C - 1 + +1A, below the pointer it falls to +1A (cursor 2C0023h); the type 9 steps it and marks the widget.</summary>
    public static void FBF55(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        var d = w.Ds; g.Test(r[3], 1, 8); if (g.Zf) return;
        g.Eax = (uint)U16(r, 0x14); g.Eax = g.Shr(g.Eax, 1, 32); g.Add(g.Eax, U32(r, 8)); g.Eax += U32(r, 8); g.Cmp(U32(d, 0x1951), g.Eax, 32);
        if (!(g.Cf || g.Zf))
        {
            g.Eax = (g.Eax & 0xffff0000) | U16(r, 0x1a); g.Cmp(U16(r, 0x17), g.Eax & 0xffff, 16); if (g.Cf || g.Zf) return;
            Put(d, 0x1946, 0x2c0023); Put(d, 0x1965, 0xf000f); g.Cmp(r[3], 9, 8); if (!g.Zf) return;
            Put16(r, 0x17, (ushort)(U16(r, 0x17) - 1)); r[0] |= 0x80; g.Logic(r[0], 8); return;
        }
        g.Eax = r[0x1c]; g.Eax--; g.Eax = (g.Eax & 0xffff0000) | (ushort)((ushort)g.Eax + U16(r, 0x1a)); g.Cmp(U16(r, 0x17), g.Eax & 0xffff, 16); if (!g.Cf) return;
        Put(d, 0x1946, 0x18000f); Put(d, 0x1965, 0xf000f); g.Cmp(r[3], 9, 8); if (!g.Zf) return;
        Put16(r, 0x17, (ushort)(U16(r, 0x17) + 1)); r[0] |= 0x80; g.Logic(r[0], 8);
    }
    private static void Put16(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }

    /// <summary>6BA3: the sprite AL (AL - 30h from B0h on) of the bank DS:19EE (the word table of offsets, then per sprite: the line offset +0, the dword +1 with the width CL and the height CH, the bytes from +3) is drawn at the cell (ECX, EDX + line offset) of the picture [68E] (640 bytes a line); AH is added to every non-zero byte; EAX = the width.</summary>
    public static void F6BA3(IUnitActionWorld w, ActionRegs g) => Blit(w, g, false);

    /// <summary>6C04: as 6BA3, but every non-zero byte goes through the table at EBP (the palette change).</summary>
    public static void F6C04(IUnitActionWorld w, ActionRegs g) => Blit(w, g, true);

    private static void Blit(IUnitActionWorld w, ActionRegs g, bool table)
    {
        var d = w.Ds; if (!table) g.Bh = g.Ah;
        if (g.Al >= 0xb0) g.Al -= 0x30;
        g.Esi = g.Al; g.Eax = U32(d, 0x19ee); uint bank = g.Eax; g.Esi = (uint)(w.Peek(bank + g.Esi * 2) | w.Peek(bank + g.Esi * 2 + 1) << 8); g.Esi += g.Eax;
        g.Eax = 0; g.Al = w.Peek(g.Esi); g.Edx += g.Eax; g.Eax = g.Edx; g.Edx <<= 9; g.Eax <<= 7; g.Edx += g.Eax; g.Edx += g.Ecx; g.Edi = g.Edx + U32(d, 0x68e);
        g.Ecx = Peek32(w, g.Esi + 1); g.Esi += 3;
        if (g.Ch != 0)
        {
            if (!table) { g.Edx = 0x280; g.Dl = (byte)(g.Dl - g.Cl); bool borrow = (0x280 & 0xff) < g.Cl; g.Dh = (byte)(g.Dh - (borrow ? 1 : 0)); }
            else { g.Eax = 0; g.Edx = 0x280; bool borrow = (0x280 & 0xff) < g.Cl; g.Dl = (byte)(g.Dl - g.Cl); g.Dh = (byte)(g.Dh - 0 - (borrow ? 1 : 0)); }
            do
            {
                g.Bl = g.Cl;
                do
                {
                    g.Al = w.Peek(g.Esi);
                    if (g.Al != 0) { if (table) g.Al = w.Peek(g.Ebp + g.Al); else g.Al = (byte)(g.Al + g.Bh); w.Poke(g.Edi, g.Al); }
                    g.Esi++; g.Edi++; g.Bl--;
                } while (g.Bl != 0);
                g.Edi += g.Edx; g.Ch--;
            } while (g.Ch != 0);
        }
        g.Eax = 0; g.Logic(0, 32); g.Al = g.Cl;
    }

    /// <summary>6C66: draws the string ESI (bytes, 0Dh = a new line 12h lower from the first column, 7Eh + a colour byte = the colour added to the glyph bytes [1F10], FFh = the end) at (ECX, EDX) glyph by glyph (6BA3, each glyph one more cell than its width), then clears the rectangle of the text (3EEA with EAX = the first column, EBX = the first line, ECX = the width reached, EDX = the lines + 10h) when the text is wider than 0.</summary>
    public static void F6C66(IUnitActionWorld w, ActionRegs g) => TextLine(w, g, false);

    /// <summary>6CD6: as 6C66 through the palette table at EBP (6C04); the colour byte moves EBP; the cleared width is one more.</summary>
    public static void F6CD6(IUnitActionWorld w, ActionRegs g) => TextLine(w, g, true);

    private static void TextLine(IUnitActionWorld w, ActionRegs g, bool table)
    {
        var d = w.Ds; d[0x1f10] = 0; Put(d, 0x1c6e, g.Ecx); uint x0 = g.Ecx, y0 = g.Edx;
        for (; ; )
        {
            g.Al = w.Peek(g.Esi); g.Cmp(g.Al, 0xff, 8); if (g.Zf) break;
            g.Esi++; g.Cmp(g.Al, 0xd, 8);
            if (g.Zf) { g.Ecx = x0; g.Edx += 0x12; continue; }
            g.Cmp(g.Al, 0x7e, 8);
            if (g.Zf) { g.Al = w.Peek(g.Esi); g.Esi++; d[0x1f10] = g.Al; continue; }
            uint ecx = g.Ecx, edx = g.Edx, esi = g.Esi, ebp = g.Ebp; g.Ah = d[0x1f10];
            if (table) { g.Esi = (uint)(int)(sbyte)d[0x1f10]; g.Ebp += g.Esi; Blit(w, g, true); } else Blit(w, g, false);
            g.Ebp = ebp; g.Esi = esi; g.Edx = edx; g.Ecx = ecx;
            g.Add(g.Ecx, g.Eax); g.Ecx += g.Eax; g.Add(g.Ecx, 1); g.Ecx += 1; g.Cmp(g.Ecx, U32(d, 0x1c6e), 32);
            if (!(g.Cf || g.Zf)) Put(d, 0x1c6e, g.Ecx);
        }
        g.Ebx = y0; g.Eax = x0; g.Ecx = U32(d, 0x1c6e);
        if (table) g.Ecx++;
        g.Cmp(g.Ecx, g.Eax, 32); g.Ecx -= g.Eax;
        if (g.Cf || g.Zf) return;
        g.Edx -= g.Ebx; g.Edx += 0x10; w.Call(0x3eea, g);
    }

    /// <summary>677F: the picture [68E] from the cell (16, 16) is cleared: 1C0h lines of 70h dwords (448 bytes; the line step is 640), then 3EEA (EAX = EBX = 10h, ECX = 280h, EDX = 1E0h).</summary>
    public static void F677F(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Edi = U32(d, 0x68e); g.Add(g.Edi, 0x2810); g.Edi += 0x2810; g.Eax = 0; g.Logic(0, 32); g.Ebp = 0xc0; g.Ebx = 0x1c0;
        do
        {
            for (uint k = 0; k < 0x70 * 4; k++) w.Poke(g.Edi + k, 0);
            g.Edi += 0x70 * 4; g.Ecx = 0; g.Add(g.Edi, g.Ebp); g.Edi += g.Ebp; g.Ebx = Dec32(g, g.Ebx);
        } while (!g.Zf);
        g.Eax = 0x10; g.Ebx = 0x10; g.Ecx = 0x280; g.Edx = 0x1e0; w.Call(0x3eea, g);
    }
    private static uint Shl32(ActionRegs g, uint a, int n) { uint r = a << n; g.Cf = ((a >> (32 - n)) & 1) != 0; g.Zf = r == 0; g.Sf = (r & 0x80000000) != 0; return r; }
    private static uint Dec32(ActionRegs g, uint a) { uint r = a - 1; g.Zf = r == 0; g.Sf = (r & 0x80000000) != 0; g.Of = a == 0x80000000; return r; }   // CF is kept

    /// <summary>6D4A: a frame (DL wide, DH high, at the cell of 3EEA): the first line of 44h, the interior lines with 44h on both sides and the fill AL (0, then - the second pass - the low byte of the pushed EDI), the last line of 44h; ECX holds the share of the interior lines (the quotient (DH - 2) * CL / CH).</summary>
    public static void F6D4A(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; d[0x1f08] = g.Dh; d[0x1f09] = g.Dl; uint savedEdi = g.Edi, savedEcx = g.Ecx;
        g.Ecx = d[0x1f09]; g.Edx = d[0x1f08]; w.Call(0x3eea, g);
        g.Edi = g.Ebx << 9; g.Ebx <<= 7; g.Edi += g.Ebx; g.Edi += g.Eax; g.Edi += U32(d, 0x68e);
        g.Ecx = d[0x1f09]; g.Edx = 0x280 - g.Ecx; g.Al = 0x44;
        do { w.Poke(g.Edi, g.Al); g.Edi++; g.Cl = Dec8(g, g.Cl); } while (!g.Zf);
        g.Edi += g.Edx; g.Ecx = savedEcx;
        g.Al = (byte)(d[0x1f08] - 2); Mul8(g, g.Al, g.Cl); g.Test(g.Eax & 0xffff, g.Eax & 0xffff, 16);
        if (!g.Zf)
        {
            uint ax = g.Eax & 0xffff; byte div = g.Ch; if (div == 0 || ax / div > 0xff) throw new OverflowException("DIV r/m8 faults");
            g.Eax = (g.Eax & 0xffff0000) | ((ax % div) << 8) | (ax / div);
        }
        g.Edx++; g.Ch = d[0x1f08]; g.Ch = (byte)(g.Ch - 2); g.Cl = g.Al; g.Al = 0; g.Ch = Sub8(g, g.Ch, g.Cl);
        bool rows = !g.Zf; bool first = true; uint stack = savedEdi;
        for (; ; )
        {
            if (rows)
            {
                do
                {
                    g.Ah = 0x44; w.Poke(g.Edi, g.Ah); g.Edi++; g.Bl = (byte)(d[0x1f09] - 2);
                    do { w.Poke(g.Edi, g.Al); g.Edi++; g.Bl = Dec8(g, g.Bl); } while (!g.Zf);
                    w.Poke(g.Edi, g.Ah); g.Edi += g.Edx; g.Ch = Dec8(g, g.Ch);
                } while (!g.Zf);
                g.Test(g.Al, g.Al, 8); if (!g.Zf) break;
            }
            if (!first) throw new InvalidOperationException("6D4A pops the return address (the fill byte is zero)");
            first = false; g.Eax = stack; g.Ch |= g.Cl; g.Logic(g.Ch, 8); rows = !g.Zf; if (!rows) break;
        }
        g.Ecx = (g.Ecx & 0xffffff00) | d[0x1f09]; g.Al = 0x44;
        do { w.Poke(g.Edi, g.Al); g.Edi++; g.Cl = Dec8(g, g.Cl); } while (!g.Zf);
    }

    /// <summary>A1D1: the minimap: with [1C4E] and [1C60] set, 3EEA gives the cell of the picture [68E] (EAX, EBX); every map cell (the map [18DE] x [18E2]) is drawn as one byte: hidden from the owner [1972] (bit of the byte of the table [18DA]) - 0; else an object of the planes 2 / 1 (the owner +3 equal to [1971] keeps the tile colour, else the byte DS:1C50 + owner & 0Fh); else the tile colour (the table [18FA] + tile). Then a frame of 1010101h is drawn around the view [1906], [190A]. The map word of a cell is its tile (the check maps tile = cell index).</summary>
    public static void FA1D1(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Cmp(d[0x1c4e], 0, 8); if (g.Zf) return; g.Cmp(d[0x1c60], 0, 8); if (g.Zf) return;
        g.Ebx = U32(d, 0x1c5c); g.Eax = U32(d, 0x1c58); g.Edx = U32(d, 0x18e2); g.Ecx = U32(d, 0x18de); w.Call(0x3eea, g);
        uint x = g.Eax, y = g.Ebx; g.Edi = y << 9; g.Ebx = y << 7; g.Edi += g.Ebx; g.Edi += x; g.Edi += U32(d, 0x68e);
        g.Eax = 0x280 - g.Ecx; Put(d, 0x1f31, g.Eax); g.Cl = d[0x1972]; g.Al = 1; g.Al = (byte)(g.Cl >= 8 ? 0 : g.Al << g.Cl); d[0x1f41] = g.Al;
        g.Eax = U32(d, 0x18de); Mul8(g, g.Al, d[0x18e2]); g.Edx = (g.Eax & 0xffff) * 4 + (g.Eax & 0xffff0000) * 4;
        g.Ebx = w.PlanePointer(0); g.Ebp = U32(d, 0x18da); g.Esi = U32(d, 0x18ee); uint mapBase = g.Esi; g.Ch = d[0x18e2]; g.Cl = d[0x18de];
        do
        {
            do
            {
                g.Al = w.Peek(g.Ebp); g.Test(d[0x1f41], g.Al, 8);
                if (!g.Zf) { g.Al = 0; goto store; }
                g.Eax = w.PlaneRead(g.Ebx + g.Edx * 2); g.Test(g.Eax, g.Eax, 32);
                if (!g.Zf) goto obj;
                g.Eax = w.PlaneRead(g.Ebx + g.Edx); g.Test(g.Eax, g.Eax, 32);
                if (!g.Zf) goto obj;
                goto drawTile;
            obj:
                g.Eax += w.PoolBase; g.Al = w.ObjectAt(g.Eax)[3]; g.Cmp(g.Al, d[0x1971], 8);
                if (g.Zf) goto drawTile;
                g.Eax &= 0xf; g.Al = d[0x1c50 + (int)g.Eax]; goto store;
            drawTile:
                g.Eax = (g.Esi - mapBase) / 4 & 0xffff; g.Add(g.Eax, U32(d, 0x18fa)); g.Eax += U32(d, 0x18fa); g.Al = w.Peek(g.Eax);
            store:
                w.Poke(g.Edi, g.Al); g.Ebp++; g.Ebx += 4; g.Esi += 4; g.Edi++; g.Cl = Dec8(g, g.Cl);
            } while (!g.Zf);
            g.Edi += U32(d, 0x1f31); g.Ch = Dec8(g, g.Ch);
            g.Cl = d[0x18de];
        } while (!g.Zf);
        g.Ebx = y; g.Eax = x; g.Eax += U32(d, 0x1906); g.Ebx += U32(d, 0x190a);
        g.Edi = g.Ebx << 9; g.Ebx <<= 7; g.Edi += g.Ebx; g.Edi += g.Eax; g.Edi += U32(d, 0x68e); g.Eax = 0x1010101;
        for (uint k = 0; k < 14; k++) w.Poke(g.Edi + k, k < 12 ? (byte)1 : (byte)1);
        g.Edi += 0x280; g.Cl = 0xc;
        do { w.Poke(g.Edi, g.Al); w.Poke(g.Edi + 0xd, g.Al); g.Edi += 0x280; g.Cl = Dec8(g, g.Cl); } while (!g.Zf);
        for (uint k = 0; k < 14; k++) w.Poke(g.Edi + k, 1);
    }

    /// <summary>3DB5: the frame of the cursor animation [193E]: a step of the clock [19E4] of at least 4 since [194A] advances it by one (and stores the clock); a frame outside the range of the words [1946] .. [1948] goes back to the first.</summary>
    public static void F3DB5(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Eax = U32(d, 0x193e); g.Bl = d[0x19e4]; g.Bh = g.Bl; g.Bl = Sub8(g, g.Bl, d[0x194a]); g.Cmp(g.Bl, 4, 8);
        if (!g.Cf) { g.Eax++; d[0x194a] = g.Bh; }
        g.Cmp(g.Eax & 0xffff, U16(d, 0x1946), 16);
        if (!g.Cf) { g.Cmp(g.Eax & 0xffff, U16(d, 0x1948), 16); if (g.Cf || g.Zf) { Put(d, 0x193e, g.Eax); return; } }
        g.Eax = (g.Eax & 0xffff0000) | U16(d, 0x1946); Put(d, 0x193e, g.Eax);
    }

    /// <summary>3FA1: the scroll of the view [1906], [190A] (cells) by the mouse at the edges of the screen ([1932], [1936]) or the arrow keys ([1C35] = E04Bh, E04Dh, E048h, E050h) when [19C2] is set: a step of the clock [19E4] of at least 10h - [908F] since [19C1] makes the step 1, else 0; the cursor [1946] shows the direction; the view stays within 0 .. map size - 0Eh; a changed view stores the pixels [190E], [1912] (cells * 32) and wipes the cache DS:33C (C4h dwords).</summary>
    public static void F3FA1(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Cmp(d[0x19c2], 0, 8); if (g.Zf) return;
        g.Bl = 0x10; g.Bl = Sub8(g, g.Bl, d[0x908f]); g.Al = d[0x19e4]; g.Ah = g.Al; g.Al = Sub8(g, g.Al, d[0x19c1]); g.Cmp(g.Al, g.Bl, 8); g.Ebx = 0;
        if (!g.Cf) { g.Ebx = 1; d[0x19c1] = g.Ah; }
        g.Ecx = U32(d, 0x1932); g.Edx = U32(d, 0x1936); g.Esi = U32(d, 0x1906); g.Edi = U32(d, 0x190a); g.Ebp = U16(d, 0x1c35);
        g.Cmp(g.Ebp, 0xe04b, 32);
        if (!g.Zf) { g.Cmp(g.Ecx, 1, 32); if (!g.Cf) goto right; }
        Put(d, 0x1946, 0x36002d); g.Cmp(g.Esi, g.Ebx, 32); g.Esi -= g.Ebx; if (!g.Cf) goto vertical; g.Esi = 0; goto right;
    right:
        g.Cmp(g.Ebp, 0xe04d, 32);
        if (!g.Zf) { g.Cmp(g.Ecx, 0x27f, 32); if (g.Cf) goto vertical; }
        Put(d, 0x1946, 0x220019); g.Add(g.Esi, g.Ebx); g.Esi += g.Ebx; g.Eax = U32(d, 0x18de); g.Eax -= 0xe; g.Cmp(g.Esi, g.Eax, 32);
        if (!(g.Cf || g.Zf)) g.Esi = g.Eax;
    vertical:
        g.Cmp(g.Ebp, 0xe048, 32);
        if (!g.Zf) { g.Cmp(g.Edx, 1, 32); if (!g.Cf) goto down; }
        Put(d, 0x1946, 0x18000f); g.Cmp(g.Edi, g.Ebx, 32); g.Edi -= g.Ebx; if (!g.Cf) goto settle; g.Edi = 0; goto down;
    down:
        g.Cmp(g.Ebp, 0xe050, 32);
        if (!g.Zf) { g.Cmp(g.Edx, 0x1dd, 32); if (g.Cf) goto settle; }
        Put(d, 0x1946, 0x2c0023); g.Add(g.Edi, g.Ebx); g.Edi += g.Ebx; g.Eax = U32(d, 0x18e2); g.Eax -= 0xe; g.Cmp(g.Edi, g.Eax, 32);
        if (!(g.Cf || g.Zf)) g.Edi = g.Eax;
    settle:
        g.Cmp(U32(d, 0x1906), g.Esi, 32);
        if (g.Zf) { g.Cmp(U32(d, 0x190a), g.Edi, 32); if (g.Zf) return; }
        Put(d, 0x1906, g.Esi); g.Esi = Shl32(g, g.Esi, 5); Put(d, 0x190e, g.Esi); Put(d, 0x190a, g.Edi); g.Edi = Shl32(g, g.Edi, 5); Put(d, 0x1912, g.Edi);
        g.Eax = 0xffffffff; for (int k = 0; k < 0xc4; k++) Put(d, 0x33c + 4 * k, 0xffffffff); g.Edi = 0x33c + 0xc4 * 4; g.Ecx = 0;
    }

    /// <summary>3F79: the dirty-cell cache DS:33C (C4h dwords: 1Ch rows of 7) is OR-ed into the table [64C] + 29h (the rows 28h bytes apart).</summary>
    public static void F3F79(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Esi = 0x33c; g.Edi = U32(d, 0x64c); g.Add(g.Edi, 0x29); g.Edi += 0x29; g.Ch = 0x1c;
        do
        {
            g.Cl = 7;
            do
            {
                g.Eax = U32(d, (int)g.Esi); uint cur = Peek32(w, g.Edi); uint r = cur | g.Eax; for (int k = 0; k < 4; k++) w.Poke(g.Edi + (uint)k, (byte)(r >> (8 * k))); g.Logic(r, 32);
                g.Add(g.Edi, 4); g.Edi += 4; g.Add(g.Esi, 4); g.Esi += 4; g.Cl = Dec8(g, g.Cl);
            } while (!g.Zf);
            g.Add(g.Edi, 0xc); g.Edi += 0xc; g.Ch = Dec8(g, g.Ch);
        } while (!g.Zf);
    }

    /// <summary>3D40: with the cursor on ([194C] set) the redraw 1DF08 of the rectangle 20h x 20h at ([195D], [1961]) from the picture [192E].</summary>
    public static void F3D40(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Cmp(d[0x194c], 0, 8); if (g.Zf) return;
        g.Esi = U32(d, 0x192e); g.Eax = U32(d, 0x195d); g.Ebx = U32(d, 0x1961); g.Edx = 0x20; g.Ecx = g.Edx; w.Call(0x1df08, g);
    }

    /// <summary>3D67: with the cursor on: 1DECF (the picture [192E] saved at ([195D], [1961]), 20h x 20h), 1E133 (EAX = the frame [193E], ESI = [191A]) and 1E098 (the rectangle redrawn).</summary>
    public static void F3D67(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Cmp(d[0x194c], 0, 8); if (g.Zf) return;
        g.Eax = U32(d, 0x195d); g.Ebx = U32(d, 0x1961); g.Edi = U32(d, 0x192e); g.Ecx = 0x20; g.Edx = g.Ecx; w.Call(0x1decf, g);
        g.Esi = U32(d, 0x191a); g.Eax = U32(d, 0x193e); w.Call(0x1e133, g);
        g.Eax = U32(d, 0x195d); g.Ebx = U32(d, 0x1961); g.Edx = 0x20; g.Ecx = g.Edx; w.Call(0x1e098, g);
    }

    /// <summary>3EEA: marks the cells of the dirty table [64C] (a byte per 16 x 16 pixels, 40 a row) that the rectangle (EAX, EBX) of ECX x EDX pixels touches with FFh. The registers are kept; the flags are those of the last row step.</summary>
    public static void F3EEA(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint eax = g.Eax, ebx = g.Ebx, ecx = g.Ecx, edx = g.Edx, edi = g.Edi;
        uint width = ecx + (eax & 0xf), height = edx + (ebx & 0xf); uint row = ebx & 0xfff0; uint at = row + row + (eax >> 4) + (row >> 1) + U32(d, 0x64c);
        for (; ; )
        {
            uint left = width, cell = at; bool above;
            do { w.Poke(cell, 0xff); cell++; g.Cmp(left, 0x10, 32); above = !g.Cf && !g.Zf; left -= 0x10; } while (above);
            at += 0x28; g.Cmp(height, 0x10, 32); above = !g.Cf && !g.Zf; height -= 0x10; if (!above) break;
        }
        g.Eax = eax; g.Ebx = ebx; g.Ecx = ecx; g.Edx = edx; g.Edi = edi;
    }

    /// <summary>3F32: as 3EEA, but into the cache DS:33C (28 bytes a row): the cells that the rectangle (EAX, EBX) of ECX x EDX pixels touches get FFh; the registers are kept, the flags are those of the last row step.</summary>
    public static void F3F32(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint eax = g.Eax, ebx = g.Ebx, ecx = g.Ecx, edx = g.Edx, edi = g.Edi;
        uint width = ecx + (eax & 0xf), height = edx + (ebx & 0xf); uint row = ebx >> 4; uint at = (row << 5) - row * 4 + (eax >> 4) + 0x33c;
        for (; ; )
        {
            uint left = width, cell = at; bool above;
            do { d[cell] = 0xff; cell++; g.Cmp(left, 0x10, 32); above = !g.Cf && !g.Zf; left -= 0x10; } while (above);
            at += 0x1c; g.Cmp(height, 0x10, 32); above = !g.Cf && !g.Zf; height -= 0x10; if (!above) break;
        }
        g.Eax = eax; g.Ebx = ebx; g.Ecx = ecx; g.Edx = edx; g.Edi = edi;
    }

    /// <summary>3DF2: the dirty table [64C] (30 rows of 40 cells) is cut into rectangles: every marked cell starts a run along its row (the cells are cleared), the rows below with the whole run marked extend it (cleared too; a partly marked row gives its cleared cells back), and 3E77 copies the rectangle (EAX = x pixels + the running offset, ECX = width, EDX = height in pixels) to the screen; EDI keeps the running offset (16 per cell, 2580h per row).</summary>
    public static void F3DF2(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Esi = U32(d, 0x64c); g.Eax = 0; g.Edi = 0; g.Ch = 0x1e;
        do
        {
            g.Cl = 0x28;
            for (; ; )
            {
                g.Cmp(w.Peek(g.Esi), g.Al, 8);
                if (g.Zf) { g.Esi++; g.Edi += 0x10; g.Cl = Dec8(g, g.Cl); }
                else
                {
                    g.Edx = g.Ecx; g.Ebx = g.Esi;
                    do
                    {
                        g.Cmp(w.Peek(g.Esi), g.Al, 8); if (g.Zf) break;
                        w.Poke(g.Esi, g.Al); g.Esi++; g.Dl = Dec8(g, g.Dl);
                    } while (!g.Zf);
                    uint pushedEsi = g.Esi; g.Dl = Sub8(g, g.Dl, g.Cl); g.Dl = (byte)(0 - g.Dl);
                    for (; ; )
                    {
                        g.Ebx += 0x28; g.Ah = g.Dl; g.Dh = Dec8(g, g.Dh); if (g.Zf) break;
                        g.Esi = g.Ebx; bool stop = false;
                        for (; ; )
                        {
                            g.Cmp(w.Peek(g.Esi), g.Al, 8); if (g.Zf) { stop = true; break; }
                            w.Poke(g.Esi, g.Al); g.Esi++; g.Ah = Dec8(g, g.Ah); if (g.Zf) break;
                        }
                        if (stop) break;
                    }
                    g.Ah = Sub8(g, g.Ah, g.Dl);
                    if (!g.Zf) { g.Ah = (byte)(0 - g.Ah); do { g.Esi--; w.Poke(g.Esi, 0xff); g.Ah = Dec8(g, g.Ah); } while (!g.Zf); }
                    g.Cl = Sub8(g, g.Cl, g.Dl); uint ecxPushed = g.Ecx; g.Eax = 0; g.Al = g.Dl; g.Ch = Sub8(g, g.Ch, g.Dh); g.Edx = 0; g.Dl = g.Ch;
                    g.Edx <<= 4; g.Eax <<= 4; g.Ecx = g.Eax; g.Eax += g.Edi; uint pushedEax = g.Eax; w.Call(0x3e77, g);
                    g.Edi = pushedEax; g.Ecx = ecxPushed; g.Esi = pushedEsi; g.Eax = 0; g.Test(g.Cl, g.Cl, 8);
                }
                if (g.Zf) break;
            }
            g.Edi += 0x2580; g.Ch = Dec8(g, g.Ch);
        } while (!g.Zf);
    }

    /// <summary>1E133: the address of the picture in the sprite file at ESI (the table of 24-bit offsets, three bytes per number EAX, after the word of the number of pictures): ESI += 2 + 3 * (the count word) + the offset of the number EAX; EBX, ECX and EAX are consumed.</summary>
    public static void F1E133(IUnitActionWorld w, ActionRegs g)
    {
        g.Ebx = g.Eax; g.Eax += g.Eax; g.Ebx += g.Eax;
        g.Eax = (uint)(w.Peek(g.Esi) | w.Peek(g.Esi + 1) << 8); g.Esi += 2; g.Ecx = g.Eax; g.Eax += g.Eax; g.Ecx += g.Eax;
        g.Ebx = Peek32(w, g.Esi + g.Ebx) & 0xffffff; g.Esi += g.Ebx; g.Esi += g.Ecx;
    }

    /// <summary>9F38: the fog of war on the picture: the fog bytes of the 16 x 16 cells around the view [1906], [190A] (the table [18DA], one bit per owner [1972]) are copied to DS:1F45 (the map borders are marked FFh); for the 14 x 14 visible cells (32 x 32 pixels each, from [68E] + 2810h) a cell hidden from the owner is blended through the table [19F6] + 700h with the fog tile (1E133 chooses it from the hidden neighbours: the mask of four bits, the checkerboard [1F41]) or, with all four neighbours hidden, blacked out; the touched cells go to the dirty cache DS:33C.</summary>
    public static void F9F38(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; g.Eax = U32(d, 0x68e); g.Eax += 0x2810; Put(d, 0x1f31, g.Eax);
        g.Eax = 0; g.Al = d[0x190a]; g.Ah = d[0x1906]; g.Edx = g.Eax; g.Ah = (byte)(g.Ah + g.Al); g.Ah &= 1; d[0x1f41] = g.Ah;
        Mul8(g, g.Al, d[0x18de]); g.Eax += U32(d, 0x1906); g.Esi = g.Eax; g.Esi += U32(d, 0x18da);
        g.Cl = d[0x1972]; g.Al = 1; g.Al = (byte)((g.Cl & 31) >= 8 ? 0 : g.Al << (g.Cl & 31)); d[0x1f42] = g.Al;
        g.Edi = 0x1f45; g.Ebp = U32(d, 0x18de); g.Bl = d[0x18e2]; g.Bh = d[0x18de]; g.Ebx -= 0xe0e;
        g.Esi--; g.Esi -= g.Ebp; g.Eax = 0x10; g.Ebp -= g.Eax;
        do
        {
            for (uint k = 0; k < 16; k++) d[g.Edi + k] = w.Peek(g.Esi + k);
            g.Edi += 16; g.Esi += 16; g.Ecx = 0; g.Esi += g.Ebp; g.Al = Dec8(g, g.Al);
        } while (!g.Zf);
        g.Eax = 0xffffffff; g.Edi -= 0x10; g.Cmp(g.Dl, g.Bl, 8);
        bool fillRow = g.Zf;
        if (!fillRow) { g.Edi = 0x1f45; g.Test(g.Dl, g.Dl, 8); fillRow = g.Zf; }
        if (fillRow) { for (uint k = 0; k < 16; k++) d[g.Edi + k] = 0xff; g.Edi += 16; g.Ecx = 0; }
        g.Edi = 0x1f45; g.Test(g.Dh, g.Dh, 8); bool fillColumn = g.Zf;
        if (!fillColumn) { g.Edi += 0xf; g.Cmp(g.Dh, g.Bh, 8); fillColumn = g.Zf; }
        if (fillColumn) { g.Cl = 0x10; do { d[g.Edi] = g.Al; g.Edi += 0x10; g.Cl = Dec8(g, g.Cl); } while (!g.Zf); }
        g.Esi = 0x1f56; g.Edi = 0x33c; g.Ch = 0xe; g.Cl = 0xe;
        for (; ; )
        {
            for (; ; )
            {
                uint ecx = g.Ecx, esi = g.Esi, edi = g.Edi; g.Ah = d[0x1f42]; g.Test(d[g.Esi], g.Ah, 8);
                if (!g.Zf)
                {
                    g.Eax = 0xffff; d[g.Edi] = 0xff; d[g.Edi + 1] = 0xff; d[g.Edi + 0x1c] = 0xff; d[g.Edi + 0x1d] = 0xff;
                    g.Eax = Peek32(w, g.Esi - 0x10) | (g.Eax & 0); g.Eax = U32(d, (int)g.Esi - 0x10); g.Dl = d[g.Esi - 0x11]; g.Bl = d[g.Esi - 1]; g.Bh = d[g.Esi + 1]; g.Dh = d[g.Esi + 0xf]; g.Ecx = U32(d, (int)g.Esi + 0x10);
                    g.Dl &= g.Al; g.Dl &= g.Bl; g.Ah &= g.Al; g.Ah &= g.Bh; g.Dh &= g.Cl; g.Dh &= g.Bl; g.Ch &= g.Cl; g.Ch &= g.Bh;
                    g.Al = 0xf; g.Cl = d[0x1f42];
                    g.Test(g.Cl, g.Ah, 8); if (g.Zf) g.Al &= 0xe; g.Test(g.Cl, g.Dl, 8); if (g.Zf) g.Al &= 0xd; g.Test(g.Cl, g.Ch, 8); if (g.Zf) g.Al &= 7; g.Test(g.Cl, g.Dh, 8); if (g.Zf) g.Al &= 0xb;
                    g.Test(g.Al, g.Al, 8);
                    if (!g.Zf)
                    {
                        g.Cmp(g.Al, 0xf, 8);
                        if (g.Zf)
                        {
                            g.Edi = U32(d, 0x1f31); g.Eax = 0; g.Bl = 0x20;
                            do { for (uint k = 0; k < 32; k++) w.Poke(g.Edi + k, 0); g.Edi += 32; g.Ecx = 0; g.Edi += 0x260; g.Bl = Dec8(g, g.Bl); } while (!g.Zf);
                        }
                        else
                        {
                            g.Al = (byte)(g.Al + g.Al); g.Al = (byte)(g.Al + d[0x1f41]); g.Eax &= 0xff; g.Eax += 0xa4; g.Esi = U32(d, 0x191a); F1E133(w, g);
                            g.Edi = U32(d, 0x1f31); g.Ebx = U32(d, 0x19f6); g.Ebx += 0x700; g.Eax = 0; g.Ch = 0x20;
                            do
                            {
                                g.Cl = 0x20;
                                do { g.Ah = w.Peek(g.Esi); g.Esi++; g.Al = w.Peek(g.Edi); g.Al = w.Peek(g.Ebx + g.Eax); w.Poke(g.Edi, g.Al); g.Edi++; g.Cl = Dec8(g, g.Cl); } while (!g.Zf);
                                g.Edi += 0x260; g.Ch = Dec8(g, g.Ch);
                            } while (!g.Zf);
                        }
                    }
                }
                Put(d, 0x1f31, U32(d, 0x1f31) + 0x20); d[0x1f41] ^= 1;
                g.Ecx = ecx; g.Esi = esi; g.Edi = edi; g.Edi += 2; g.Esi++; g.Cl = Dec8(g, g.Cl);
                if (g.Zf) break;
            }
            g.Edi += 0x1c; Put(d, 0x1f31, U32(d, 0x1f31) + 0x4e40); d[0x1f41] ^= 1; g.Add(g.Esi, 2); g.Esi += 2; g.Ch = Dec8(g, g.Ch);
            if (g.Zf) break;
            g.Cl = 0xe;
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

    /// <summary>7A03: the direction (0..7) from the point (DS:274E, 274F) to (DS:2791, 2792) by the angle function 6996: (angle + 10h) >> 5 in AL; EBX, ECX, EDX are kept, the upper part of EAX too.</summary>
    public static void F7A03(IUnitActionWorld w, ActionRegs g)
    {
        var d = w.Ds; uint a0 = g.Eax, b0 = g.Ebx, c0 = g.Ecx, d0 = g.Edx;
        g.Eax = d[0x274e]; g.Ebx = d[0x274f]; g.Ecx = d[0x2791]; g.Edx = d[0x2792]; w.Call(0x6996, g);
        g.Al = (byte)(g.Al + 0x10); g.Al = (byte)g.Shr(g.Al, 5, 8); g.Bl = g.Al;
        g.Eax = a0; g.Al = g.Bl; g.Ebx = b0; g.Ecx = c0; g.Edx = d0;
    }

    /// <summary>AE3A: the unit faces the direction AL (0..7): +25 low bits, the frame +1 = direction * DAT[4A] + DAT[48].</summary>
    public static void FAE3A(IUnitActionWorld w, ActionRegs g, byte[] r)
    {
        uint a0 = g.Eax; F6885(w, g, r); var t = w.ObjectAt(g.Edi); g.Eax = a0; g.Eax &= 7; g.Logic(g.Eax, 32);
        r[0x25] &= 0xf8; r[0x25] |= g.Al; Mul8(g, g.Al, t[0x4a]);
        uint lo = g.Eax & 0xffff, add = U16(t, 0x48); g.Add(lo << 16, add << 16); g.Eax = (g.Eax & 0xffff0000) | ((lo + add) & 0xffff);
        r[1] = (byte)g.Eax; r[2] = (byte)(g.Eax >> 8);
    }
}
