using System.Buffers.Binary;
namespace Northwind;

/// <summary>The services of the scripts of the map objects (recorded stubs in tools/verify_object_scripts.py).</summary>
public interface IObjectScriptWorld
{
    byte Random();                                    // FS:109 = 9E41
    byte Random2();                                   // FS:10D = 9E55
    (uint Count, uint Hostile, byte[]? Object) Area(uint center, uint size);   // FS:14B = 11DDE: the objects in the rectangle (CL, CH the corner, DX the size); EDI the nearest hostile
    byte Tick { get; }                                // FS:10 the update counter
    uint Reference(byte[] record);                    // ESI − FS:8
    uint Claim(uint eax, uint ebx, ushort cx);        // FS:157 = D2D1: claim or crush the square (EAX the reference or 0)
    byte Distance(uint x, uint y);                    // FS:15B: AL
    void Spawn(ushort eax, byte bl, ushort cx, uint edx);   // FS:69
    void Redraw();                                    // FS:1D
    void Remove(byte[] record);                       // FS:15
    void Vitals(); void Actions();                    // FS:DD, FS:E1
    void Effect(byte[] record, byte type, byte layer, uint x, uint y);   // FS:19 with the three stack arguments (the object, x, y)
    void Sound(uint id);                              // FS:31
    byte TemplateByte3 { get; }                       // DAT[3] of the object (EDI in AH = 1)
    ushort TemplateBase { get; }                      // DAT[48], the base frame
    bool Service1BF(uint edx);                        // FS:1BF: ZF (true = skip)
    (bool Found, uint X, uint Y) FreeSpot(byte al, byte ah, byte bl, uint x, uint y);   // FS:187 = 10AD2
    (bool Found, byte Al, byte Ah, byte Bl) Beside(byte al, byte ah);   // FS:16B = 9064
    (bool Created, byte[]? Object) Create(byte army, byte owner, byte template, byte layer, uint x, uint y);   // FS:45 = A758
    void Occupy(byte[] record);                       // FS:19B = 43E5
    IEnumerable<byte> OwnTemplates(byte[] record);    // FS:6D: the templates of the objects of the owner's participant
}

/// <summary>The scripts of the map objects of ARMY__03.WRK. The rockfall (template 2, WRK 189): AH = 0 sets +22 = FFFFFFBF; AH = 1: every 4th random draw looks at the rectangle of 4×4 cells
/// from two cells left of the object; with something in it a stone (effect 98h) falls at a random point of the 96-pixel square around the object, and the sound 6 plays every 32 updates
/// of the counter +26. The stone face (template 4, 2B9) is the same trap with a face that blinks: frames 140h..14Bh, the states of +15 (0 waiting, 1 opening with +2A counting the frames,
/// 2 closing, 3 pause) and the sounds 5 and 6. Verified against the original x86 by tests/fixtures/obj-rockfall.json and obj-face.json (tools/verify_object_scripts.py).</summary>
public static class OriginalObjectScripts
{
    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static void Put(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);
    private static void Word(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), v);
    private static ushort Word(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));

    private static uint Corner(byte[] r)
    {
        uint cx = U32(r, 4) >> 5, cy = U32(r, 8) >> 5;
        return (cx & 0xffff00ff) | ((cy & 0xff) << 8);
    }
    private static uint CornerLeft(byte[] r) { uint c = Corner(r); return (c & 0xffffff00) | (byte)((byte)c - 2); }

    /// <summary>The stone falling at a random point of the square (the common part of 19B..211 and 31A..36D).</summary>
    private static void Stone(IObjectScriptWorld w, byte[] r, int dx, int dy)
    {
        uint eax = (uint)((w.Random() * 0x60) >> 8);
        uint x = (uint)(U32(r, 4) + eax - 0x30), y = U32(r, 8) - (uint)dy;
        eax = (uint)((w.Random2() * 0x60) >> 8);
        w.Effect(r, 0x98, 1, x, y + eax + (uint)dx);
        if ((U32(r, 0x26) & 0x1f) == 0) w.Sound(6);
    }

    public static void Rockfall(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { Put(r, 0x22, 0xffffffbf); return; }
        if (ah != 1) return;
        if ((w.Random2() & 3) != 0) return;
        var (count, _, _) = w.Area(CornerLeft(r), 0x404);
        if (count == 0) return;
        Stone(w, r, 0x32, 0);
    }

    public static void Face(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { Put(r, 0x22, 0xffffffbf); r[0x15] = 0; Put(r, 8, U32(r, 8) + 0x10); Put(r, 4, U32(r, 4) + 4); return; }
        if (ah != 1) return;
        Put(r, 0x26, U32(r, 0x26) + 1);
        if (r[0x15] == 0)
        {
            if ((w.Random2() & 3) != 0) return;
            var (count, hostile, _) = w.Area(CornerLeft(r), 0x404);
            if (hostile != 0) { r[0x15] = 1; r[0x2a] = 0; Word(r, 1, 0x140); }
            else
            {
                if (count == 0) return;
                Stone(w, r, 0x46, 0x20);
                return;
            }
        }
        if ((U32(r, 0x26) & 3) != 0) return;
        if (r[0x15] == 1)
        {
            Word(r, 1, (ushort)(Word(r, 1) + 1)); r[0x2a]++;
            if (r[0x2a] == 0x0a) w.Sound(5);
            if (r[0x2a] < 0x15) return;
            r[0x15] = 2; r[0x2a] = 0x0b; Word(r, 1, 0x14b);
        }
        if (r[0x15] == 2)
        {
            Word(r, 1, (ushort)(Word(r, 1) - 1)); r[0x2a]--;
            if (r[0x2a] != 0) return;
            r[0x15] = 3;
        }
        if ((U32(r, 0x26) & 0x1f) != 0) return;
        var (_, still, _) = w.Area(CornerLeft(r), 0x404);
        if (still != 0) return;
        r[0x15] = 0;
    }

    /// <summary>The chest with the birds (template 29, 2A6B): the chest hurt below 100 opens by its frames (+15 counts two updates per frame), then ten ravens (template 8, layer 2) fly up at free spots
    /// around it; opened it lies and its health runs down every 50 updates. DAT[3] is the owner of the birds, DAT[48] the base frame.</summary>
    public static void Chest(IObjectScriptWorld w, byte[] r, int ah)
    {
        byte dat3 = w.TemplateByte3; ushort dat48 = w.TemplateBase;
        if (ah == 0) { r[0x19] = 0x64; r[0x31] = 0x22; return; }
        if (ah != 1) return;
        if ((sbyte)r[0x19] <= 0) { r[0x19]--; if (r[0x19] > 0xfa) return; w.Remove(r); return; }
        if ((r[0x14] & 0x80) != 0) { r[0x14] = 0; w.Redraw(); }
        if (r[0x14] == 0)
        {
            if ((sbyte)r[0x19] >= 0x64) return;
            r[0x14] = 1; r[0x15] = 0;
        }
        if (r[0x14] == 1)
        {
            r[0x15]++;
            if ((sbyte)r[0x15] < 2) return;
            r[0x15] = 0; Word(r, 1, (ushort)(Word(r, 1) + 1));
            if (Word(r, 1) < (ushort)(dat48 + 0x0a)) return;
            if (!w.Service1BF(0x505))
            {
                for (int i = 0; i < 10; i++)
                {
                    var (found, x, y) = w.FreeSpot(8, r[0], 2, U32(r, 4), U32(r, 8));
                    if (!found) continue;
                    var (created, bird) = w.Create(r[0], dat3, 8, 2, x, y);
                    if (created) w.Occupy(bird!);
                }
            }
            r[0x14] = 3; r[0x31] = 0;
        }
        if (r[0x14] == 3)
        {
            r[0x15]--;
            if (r[0x15] != 0) return;
            r[0x19]--; r[0x15] = 0x32;
        }
    }

    /// <summary>The generator (template 28, 29F1): every 256 updates creates a raven (template 8) beside itself while there are fewer ravens than four times the number of the objects of template 1Ch.</summary>
    public static void Generator(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah != 1 || w.Tick != 0) return;
        uint birds = 0, others = 0;
        foreach (byte t in w.OwnTemplates(r)) { if (t == 8) birds++; if (t == 0x1c) others++; }
        if (birds >= others << 2) return;
        var (found, al, ahOut, bl) = w.Beside(8, r[0]);
        if (!found) return;
        var (_, created) = w.Create(ahOut, r[3], al, bl, birds, others << 2);   // ECX and EDX still hold the two counts
        if (created != null) w.Occupy(created);
    }

    private static void Redraw(IObjectScriptWorld w, byte[] r) { if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); } }
    private static uint CornerMinus(byte[] r, int dx, int dy)
    {
        uint c = Corner(r); return (c & 0xffff0000) | (byte)((byte)c - dx) | ((uint)(byte)((byte)(c >> 8) - dy) << 8);
    }

    /// <summary>The stone wall (template 5, 3F8): a wall that sinks into the ground (frame 15Fh + 11·k, +2A counts to 10) while nobody is near and rises when somebody comes within 3 cells.</summary>
    public static void Wall(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0)
        {
            Put(r, 0x22, 0xffffffbf); r[0x15] = 0;
            int k = (w.Random() * 3) >> 8; Word(r, 1, (ushort)(11 * k + 0x15f)); r[0x2a] = 0x0a; return;
        }
        if (ah != 1) return;
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); r[0x0d]++; w.Redraw(); r[0x0d]--; }
        if (r[0x15] == 0)
        {
            if (r[0x2a] != 0x0a && (w.Tick & 1) == 0)
            {
                r[0x2a]++; Word(r, 1, (ushort)(Word(r, 1) + 1));
                r[0x0d]++; uint claimed = w.Claim(w.Reference(r), 0, 0x02ff); r[0x0d]--;
                if (claimed != 0 && r[0x2a] >= 0x0a) { r[0x2a]--; Word(r, 1, (ushort)(Word(r, 1) - 1)); }
            }
            if ((w.Tick & 0x1f) != 0) return;
            var (_, hostile, _) = w.Area(CornerMinus(r, 3, 3), 0x606);
            if (hostile == 0) return;
            r[0x15] = 1; return;
        }
        if (r[0x2a] != 0 && (w.Tick & 1) == 0)
        {
            Word(r, 1, (ushort)(Word(r, 1) - 1)); r[0x2a]--;
            if (r[0x2a] == 0) { r[0x0d]++; w.Claim(0, w.Reference(r), 0); r[0x0d]--; }
        }
        if ((w.Tick & 0x1f) != 0) return;
        var (_, still, _) = w.Area(CornerMinus(r, 3, 3), 0x606);
        if (still == 0) r[0x15] = 0;
    }

    /// <summary>The knight's head (template 6, 522): turns toward the nearest hostile in the rectangle of 8×8 cells (frames 15Eh + the turn, +27 the current and +28 the wanted turn) and, when it
    /// already looks that way, blinks (frames 187h..); a dead head is removed after 5 updates.</summary>
    public static void Head(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { Put(r, 0x22, 0xffffffbf); r[0x27] = 0x20; Word(r, 1, 0x17e); return; }
        if (ah != 1) return;
        Redraw(w, r);
        if ((sbyte)r[0x19] <= 0)
        {
            r[0x19]--; if (r[0x19] > 0xfa) return;
            w.Remove(r); return;
        }
        if (r[0x15] == 0)
        {
            if ((w.Tick & 7) != 0) return;
            var (_, _, target) = w.Area(CornerMinus(r, 4, 0), 0x408);
            if (target == null) return;
            byte al = w.Distance(U32(target, 4), U32(target, 8));
            if (al < 3) al = 3; if (al > 5) al = 5;
            al = (byte)(al << 3);
            if (al == r[0x27]) { r[0x15] = 2; r[0x28] = 0; }
            else { r[0x28] = al; r[0x15] = 1; }
        }
        if (r[0x15] == 1)
        {
            byte a = r[0x27]; if (!(a < r[0x28])) a -= 2; a++;
            r[0x27] = a; Word(r, 1, (ushort)(a + 0x15e));
            if (a == r[0x28]) r[0x15] = 0;
            return;
        }
        if ((w.Tick & 1) != 0) return;
        byte hi = (byte)((byte)(r[0x27] >> 3) - 3); int product = 0x0c * hi;
        int ax = (product & 0xff00) | (byte)((byte)product + r[0x28]);
        Word(r, 1, (ushort)(ax + 0x187));
        r[0x28]++;
        if (r[0x28] == 0x0c) r[0x15] = 0;
    }

    /// <summary>The living fir (template 7, 624): when somebody comes within 3 cells it sways through the frames 1ABh..1B6h and strikes at frame 1B3h (FS:69).</summary>
    public static void Tree(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { Put(r, 0x22, 0xffffffbf); return; }
        if (ah != 1) return;
        Redraw(w, r);
        if (r[0x15] == 0)
        {
            if ((w.Tick & 0x1f) != 0) return;
            var (count, _, _) = w.Area(CornerMinus(r, 3, 3), 0x505);
            if (count == 0) return;
            r[0x15] = 1;
        }
        Word(r, 1, (ushort)(Word(r, 1) + 1));
        if (Word(r, 1) == 0x1b3) w.Spawn(8, 2, (ushort)Corner(r), 0x505);
        if (Word(r, 1) < 0x1b7) return;
        Word(r, 1, 0x1ab); r[0x15] = 0;
    }

    /// <summary>The raven (template 8, 6BA): a bird — AH = 0 asks for the resting animation; AH = 1 the standard unit step.</summary>
    public static void Raven(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { r[0x0f] = 7; r[0x2d] = (byte)((r[0x2d] & 0x0f) | 0x60); return; }
        if (ah != 1) return;
        Redraw(w, r); w.Vitals(); w.Actions();
    }
}

/// <summary>The services of the standard unit scripts (recorded stubs in tools/verify_unit_scripts.py).</summary>
public interface IUnitScriptWorld
{
    byte Random();                    // FS:109
    void Appearance(byte army);       // FS:4D
    void Voice();                     // FS:D1
    void Redraw();                    // FS:1D
    void Vitals(); void Actions();    // FS:DD, FS:E1
}

/// <summary>The standard unit script of ARMY__01.WRK and ARMY__02.WRK (the swordsman 0, the archer 7F, the horseman 4F0 / 11B4 and their kin): AH = 0 the first frame state (+0F = 0Fh, the horseman
/// 07h), the appearance of the army BL and the first random update counter +2E; AH = 1 the redraw, the vitals and the unit step; AH = 2 the voice. Verified against the original x86 by
/// tests/fixtures/unit-scripts.json (tools/verify_unit_scripts.py).</summary>
public static class OriginalUnitScript
{
    public static void Run(IUnitScriptWorld w, byte[] r, int ah, byte bl, bool horseman)
    {
        if (ah == 0) { r[0x0f] = horseman ? (byte)7 : (byte)0x0f; w.Appearance(bl); r[0x2e] = w.Random(); return; }
        if (ah == 2) { w.Voice(); return; }
        if (ah != 1) return;
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        w.Vitals(); w.Actions();
    }
}
