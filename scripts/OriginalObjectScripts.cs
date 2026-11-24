using System.Buffers.Binary;
namespace Northwind;

/// <summary>The services of the scripts of the map objects (recorded stubs in tools/verify_object_scripts.py).</summary>
public interface IObjectScriptWorld
{
    byte Random();                                    // FS:109 = 9E41
    byte Random2();                                   // FS:10D = 9E55
    (uint Count, uint Hostile) Area(uint center, uint size);   // FS:14B = 11DDE: the objects in the rectangle (CL, CH the corner, DX the size)
    void Effect(byte[] record, byte type, byte layer, uint x, uint y);   // FS:19 with the three stack arguments (the object, x, y)
    void Sound(uint id);                              // FS:31
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
        var (count, _) = w.Area(CornerLeft(r), 0x404);
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
            var (count, hostile) = w.Area(CornerLeft(r), 0x404);
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
        var (_, still) = w.Area(CornerLeft(r), 0x404);
        if (still != 0) return;
        r[0x15] = 0;
    }
}
