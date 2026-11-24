using System.Buffers.Binary;
namespace Northwind;

/// <summary>The registers of the original routines that return values in them.</summary>
public sealed class OrderRegs
{
    public uint Eax, Ebx, Ecx, Edx, Edi; public bool Zf;
    public byte Al { get => (byte)Eax; set => Eax = (Eax & 0xffffff00) | value; }
    public byte Ah { get => (byte)(Eax >> 8); set => Eax = (Eax & 0xffff00ff) | ((uint)value << 8); }
    public byte Bl { get => (byte)Ebx; set => Ebx = (Ebx & 0xffffff00) | value; }
    public byte Cl { get => (byte)Ecx; set => Ecx = (Ecx & 0xffffff00) | value; }
    public byte Ch { get => (byte)(Ecx >> 8); set => Ecx = (Ecx & 0xffff00ff) | ((uint)value << 8); }
    public byte Dl { get => (byte)Edx; set => Edx = (Edx & 0xffffff00) | value; }
    public byte Dh { get => (byte)(Edx >> 8); set => Edx = (Edx & 0xffff00ff) | ((uint)value << 8); }
}

/// <summary>The services of the order helpers (recorded stubs in tools/verify_unit_orders.py).</summary>
public interface IOrderWorld
{
    void ReleaseRoute();                                              // 8120
    (bool Found, uint Eax, uint Ebx, uint Edi) SearchEnemy(byte mode); // D895 (ZF clear = found)
    uint Nearest(byte owner, uint x, uint y);                          // DA7F: the object (EDI)
    byte[]? Resolve(uint reference);                                   // the object of OBE[1C]
}

/// <summary>The helpers of the unit action handlers, LE:1 8CA1..8DB0: the move request 8CA1, the following of the referenced object 8CBB, the state-5 request 8D0B, the enemy scans of the idle
/// action 8D1A and 8D3E (results in the globals DS:512 target, 516, 51A, 51E) and the decoding of the motion nibbles 8D89 (DL, DH: current and requested state; CL, CH: +30; AL, AH: +15).
/// Verified against the original x86 by tests/fixtures/unit-orders.json (tools/verify_unit_orders.py).</summary>
public static class OriginalUnitOrders
{
    /// <summary>The globals 512 (enemy x), 516, 51A (enemy object), 51E (nearest object), 522 (the current state).</summary>
    public sealed class Globals { public uint G512, G516, G51A, G51E; public byte G522; }

    private static void Set2D(byte[] r, byte high) => r[0x2d] = (byte)((r[0x2d] & 0x0f) | high);

    /// <summary>8CA1: AL, AH the cell.</summary>
    public static void Move(byte[] r, OrderRegs g, IOrderWorld w)
    {
        r[0x22] = g.Al; r[0x23] = g.Ah; r[0x2d] &= 0x0f;
        if (r[0x2d] == 2) w.ReleaseRoute();
        r[0x2d] |= 0x20; g.Zf = false;
    }

    /// <summary>8CBB: follows the referenced object: its cell becomes the goal when it changed.</summary>
    public static void Follow(byte[] r, OrderRegs g, IOrderWorld w)
    {
        g.Bl = 1; uint reference = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(0x1c));
        if (reference == 0) { g.Zf = true; return; }
        var o = w.Resolve(reference) ?? new byte[0x40];
        uint x = BinaryPrimitives.ReadUInt32LittleEndian(o.AsSpan(4)) >> 5, y = BinaryPrimitives.ReadUInt32LittleEndian(o.AsSpan(8)) >> 5;
        g.Eax = x; g.Ebx = y; g.Ah = (byte)y; g.Bl = 3;
        if (r[0x22] == g.Al && r[0x23] == g.Ah) { g.Zf = true; return; }
        r[0x22] = g.Al; r[0x23] = g.Ah; r[0x2d] = (byte)((r[0x2d] & 0x0f) | 0x20); g.Zf = false;
    }

    /// <summary>8D0B: a request of state 5 (a frame range) to the cells AL..AH.</summary>
    public static void RequestRange(byte[] r, OrderRegs g)
    {
        r[0x22] = g.Al; r[0x23] = g.Ah; Set2D(r, 0x50); g.Zf = false;
    }

    /// <summary>8D1A: every 8th update looks for an enemy (D895 mode 1).</summary>
    public static void Scan(byte[] r, OrderRegs g, Globals globals, IOrderWorld w)
    {
        if ((r[0x2e] & 7) != 0) { g.Al = 0; g.Zf = true; return; }
        g.Al = 1; var (found, eax, ebx, edi) = w.SearchEnemy(1);
        g.Eax = eax; g.Ebx = ebx; g.Edi = edi;   // the service leaves its results in the registers whether it found something or not
        if (!found) { g.Al = 0; g.Zf = true; return; }
        g.Edx = g.Edx; globals.G512 = eax; globals.G516 = ebx; globals.G51A = edi; g.Zf = false; g.Edi = edi;
    }

    /// <summary>8D3E: clears the results, every 8th update searches an enemy (D895 mode 0) and the nearest object (DA7F).</summary>
    public static void Scan2(byte[] r, OrderRegs g, Globals globals, IOrderWorld w)
    {
        globals.G51E = 0; globals.G512 = 0; globals.G516 = 0; globals.G51A = 0; g.Eax = 0; g.Zf = true;
        if ((r[0x2e] & 7) != 0) { g.Zf = false; return; }
        g.Al = 0; var (found, eax, ebx, edi) = w.SearchEnemy(0);
        g.Eax = eax; g.Ebx = ebx; g.Edi = edi; g.Zf = !found;
        if (found) { globals.G512 = eax; globals.G516 = ebx; globals.G51A = edi; g.Edi = edi; }
        g.Al = r[3]; g.Ecx = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(4)); g.Edx = BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(8));
        uint near = w.Nearest(g.Al, g.Ecx, g.Edx); globals.G51E = near; g.Edi = near;
    }

    /// <summary>8D89: DL/DH the current/requested state (+2D), CL/CH the nibbles of +30, AL/AH those of +15.</summary>
    public static void Decode(byte[] r, OrderRegs g, Globals globals)
    {
        byte d = r[0x2d]; g.Dl = (byte)(d & 15); g.Dh = (byte)(d >> 4); globals.G522 = g.Dl;
        byte c = r[0x30]; g.Cl = (byte)(c & 15); g.Ch = (byte)(c >> 4);
        byte a = r[0x15]; g.Al = (byte)(a & 15); g.Ah = (byte)(a >> 4); g.Zf = g.Al == 0;
    }
}
