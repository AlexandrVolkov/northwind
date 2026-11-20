using System.Buffers.Binary;
namespace Northwind;

/// <summary>World of a fire: objects of the pool, the lookups 982B (footprint) and 97C6 (cell), new effects (9727),
/// sounds (A47E), the scorched ground (CFAC) and the global drift vector [1C8E]/[1C92] (FS:[0C]).</summary>
public interface IFireWorld
{
    (byte[] Record, byte[] Template) Resolve(uint reference);
    /// <summary>982B at the point in the effect's plane: the object reference (0 none) and the terrain class.</summary>
    (uint Reference, byte Terrain) Struck(uint x, uint y, byte layer);
    /// <summary>97C6: the object in the point's cell of the plane (no footprint test) and the terrain class.</summary>
    (uint Reference, byte Terrain) InCell(uint x, uint y, byte layer);
    /// <summary>9727: a new effect of the army/type at the point; <paramref name="height"/> is copied to its +11 when not null.</summary>
    void Spawn(byte army, byte type, byte layer, uint x, uint y, byte? height);
    void Sound(int id);
    void Scorch(byte x, byte y);
    (int X, int Y) Drift { get; }
}

/// <summary>Fire, ARMY__00.WRK effect types 13h..15h (CE3, sizes 26×32, 48×70, 68×100), its embers 16h (FA4) and the
/// smoke column 0Bh it ends in (9D5). Effect records are 22h bytes; the bytes +6 and +0B (fractions of the position)
/// serve as the growth timer and the frame counter. See docs/combat.md, «Огонь».</summary>
public static class OriginalBurnRules
{
    public const byte Small = 0x13, Medium = 0x14, Large = 0x15, Embers = 0x16, Column = 0x0b;
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static ushort U16(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(raw[offset..]);
    private static void W16(Span<byte> raw, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(raw[offset..], value);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    /// <summary>CE3 AH=0: a small fire on the object at the point (not one with DAT[40] bit 4000h) — +18 its reference,
    /// +17 = 2 for a building (raised toward its top) or 0 for a unit (at its feet) — or on forest ground (+17 = FFh);
    /// elsewhere the effect is freed.</summary>
    public static void Ignite(Span<byte> effect, IFireWorld world)
    {
        W16(effect, 3, 0xce); effect[0] = Small; effect[0x16] = 0; effect[6] = 0x5a; effect[0x0b] = 0x36; effect[0x11] = 5;
        var (reference, terrain) = world.Struck(U32(effect, 7), U32(effect, 0x0c), effect[5]);
        if (reference != 0 && (U32(world.Resolve(reference).Template, 0x40) & 0x4000) == 0)
        {
            var (o, t) = world.Resolve(reference);
            W32(effect, 0x18, reference);
            effect[0x17] = (byte)((U32(t, 0x40) & 8) >> 2);
            if (effect[0x17] != 0)
            {
                uint rise = unchecked(t[2] + U32(o, 8) - U32(effect, 0x0c)) >> 1;
                W32(effect, 0x0c, unchecked(U32(effect, 0x0c) + rise)); effect[0x11] = unchecked((byte)(effect[0x11] + rise));
            }
            else { W32(effect, 7, U32(o, 4)); W32(effect, 0x0c, U32(o, 8) + 1); }
            world.Sound(8); return;
        }
        if (terrain != 1) { effect[0] = 0xff; return; }
        effect[0x17] = 0xff; effect[0x1e] = 0xff; W32(effect, 0x18, 0);
        world.Sound(8);
    }

    /// <summary>CE3 AH=1: one tick of the fire.</summary>
    public static void Step(Span<byte> effect, IFireWorld world)
    {
        byte[]? obj = null, template = null;
        uint reference = U32(effect, 0x18);
        if (reference != 0)
        {
            (obj, template) = world.Resolve(reference);
            W16(obj, 0x17, (ushort)(U16(obj, 0x17) | 0x1f));
            if (effect[0x17] == 0) { W32(effect, 7, U32(obj, 4)); W32(effect, 0x0c, U32(obj, 8) + 1); }
            if ((sbyte)obj[0x19] <= 0) { W32(effect, 0x18, 0); obj = null; }
        }
        W16(effect, 3, unchecked((ushort)(U16(effect, 3) + 1)));
        if (effect[0x16] == 0)
        {
            // Flaring up: frames until the counter +0B falls to 19h.
            if (--effect[0x0b] > 0x19) return;
            if (effect[0x17] != 0xff)
            {
                if (obj == null) { Shrink(effect, world); return; }
                effect[0x1e] = obj[0x19]; W16(effect, 0x1c, U16(obj, 1));
            }
            effect[0x16]++; return;
        }
        if (--effect[0x0b] == 0) { W16(effect, 3, unchecked((ushort)(U16(effect, 3) - 0x19))); effect[0x0b] = 0x19; }
        if ((effect[6] & 0x0f) == 0) world.Sound(8);
        if (effect[0] > Small && (effect[6] & 0x0f) == 0) world.Spawn(effect[1], Embers, effect[5], U32(effect, 7), U32(effect, 0x0c) + 0x0f, effect[0x11]);
        if (effect[0x17] == 0xff)
        {
            // Forest: every 16 ticks of +1E the object in the cell burns for 5; when the counter runs out the ground is
            // scorched (CFAC) and the fire dies down; off forest it dies down at once.
            if ((effect[0x1e] & 0x0f) == 0)
            {
                var (inCell, terrain) = world.InCell(U32(effect, 7), U32(effect, 0x0c), effect[5]);
                if (inCell != 0)
                {
                    var o = world.Resolve(inCell).Record;
                    W16(o, 0x17, (ushort)(U16(o, 0x17) | 0x1f));
                    if ((sbyte)o[0x19] > 0) { int left = (sbyte)o[0x19] - 5; o[0x19] = left < 0 ? (byte)0 : (byte)left; }
                }
                if (terrain != 1) { Shrink(effect, world); return; }
            }
            if (--effect[0x1e] == 0)
            {
                world.Scorch((byte)(U32(effect, 7) >> 5), (byte)(U32(effect, 0x0c) >> 5));
                effect[0x1e] = 1; Shrink(effect, world); return;
            }
        }
        else
        {
            if (obj == null) { Shrink(effect, world); return; }
            byte condition = obj[0x19]; bool repaired = condition > effect[0x1e]; effect[0x1e] = condition;
            if (repaired) { Shrink(effect, world); return; }
            int shift = (byte)(0x15 - effect[0] + effect[0x17]) & 31;
            byte mask = (byte)((shift < 8 ? 1 << shift : 0) - 1);
            if ((effect[6] & mask) == 0) { obj[0x19]--; if ((sbyte)obj[0x19] < 0) obj[0x19] = 0; }
            if (effect[0x17] != 0 && (effect[6] & 0x0f) == 0 && U16(obj, 1) != U16(effect, 0x1c))
            {
                // A building that has fallen to its ruin frames lowers the flames.
                ushort ruin = unchecked((ushort)((byte)(template![0x4a] + template[0x4b]) + U16(template, 0x48)));
                if (U16(obj, 1) >= ruin)
                {
                    effect[0x11] = effect[0x11] >= 3 ? (byte)(effect[0x11] - 3) : (byte)0;
                    Restart(effect, world); return;
                }
            }
        }
        if (--effect[6] != 0) return;
        if (effect[0] > Medium) return;
        if (effect[0] == Medium)
        {
            // The medium fire grows only with a strong wind, which also carries it to the next point.
            var (dx, dy) = world.Drift;
            int sx = dx >> 4, sy = dy >> 4;
            uint strength = Math.Max((uint)Math.Abs(sx), (uint)Math.Abs(sy));
            if (strength < 0x20) return;
            world.Spawn(effect[1], Small, effect[5], unchecked(U32(effect, 7) + (uint)sx), unchecked(U32(effect, 0x0c) + (uint)sy), null);
        }
        effect[0]++; effect[0x11] = unchecked((byte)(effect[0x11] + 10));
        Restart(effect, world);
    }

    /// <summary>F01: frames of the current size, flaring up again; sound 8.</summary>
    private static void Restart(Span<byte> effect, IFireWorld world)
    {
        W16(effect, 3, effect[0] switch { Small => (ushort)0xce, Medium => (ushort)0x104, _ => (ushort)0x13a });
        effect[0x16] = 0; effect[6] = 0x5a; effect[0x0b] = 0x36;
        world.Sound(8);
    }

    /// <summary>F88: one size smaller (height −10), or the smoke column after the small fire.</summary>
    private static void Shrink(Span<byte> effect, IFireWorld world)
    {
        if (effect[0] == Small) { CreateColumn(effect); return; }
        effect[0]--; effect[0x11] = unchecked((byte)(effect[0x11] - 10));
        Restart(effect, world);
    }

    /// <summary>9D5 AH=0: smoke column, frame 54h, 14 frames of 2 ticks, rising speed 300h.</summary>
    public static void CreateColumn(Span<byte> effect)
    {
        W16(effect, 3, 0x54); effect[0] = Column; effect[0x16] = 2; effect[0x17] = 0x0e; W16(effect, 0x20, 0x300);
    }

    /// <summary>9F2: the column rises and drifts; outside the map or after the last frame it is freed.</summary>
    public static void StepColumn(Span<byte> effect, int driftX, int driftY, uint widthPixels, uint heightPixels)
    {
        W16(effect, 0x10, unchecked((ushort)(U16(effect, 0x10) + U16(effect, 0x20))));
        ushort rise = U16(effect, 0x20); W16(effect, 0x20, rise >= 0x30 ? (ushort)(rise - 0x30) : (ushort)0);
        W32(effect, 6, unchecked(U32(effect, 6) + (uint)driftX)); W32(effect, 0x0b, unchecked(U32(effect, 0x0b) + (uint)driftY));
        if (U32(effect, 7) >= widthPixels || U32(effect, 0x0c) >= heightPixels) { effect[0] = 0xff; return; }
        if (--effect[0x16] != 0) return;
        effect[0x16] = 2; W16(effect, 3, unchecked((ushort)(U16(effect, 3) + 1)));
        if (--effect[0x17] == 0) effect[0] = 0xff;
    }

    /// <summary>FA4 AH=0: embers (also the deflected fire shot), frame 170h, 10 frames of 3 ticks.</summary>
    public static void CreateEmbers(Span<byte> effect)
    {
        W16(effect, 3, 0x170); effect[0] = Embers; effect[0x16] = 3; effect[0x17] = 0x0a;
    }

    /// <summary>FBB: frames of the embers, then free.</summary>
    public static void StepEmbers(Span<byte> effect)
    {
        if (--effect[0x16] != 0) return;
        effect[0x16] = 3; W16(effect, 3, unchecked((ushort)(U16(effect, 3) + 1)));
        if (--effect[0x17] == 0) effect[0] = 0xff;
    }
}
