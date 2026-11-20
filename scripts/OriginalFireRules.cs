using System.Buffers.Binary;
namespace Northwind;

/// <summary>Outcome of one fire-shot update (ARMY__00.WRK BED): still flying, freed, set alight (the effect becomes the
/// fire of CE3, type 13h) or deflected (FA4, type 16h). Both continuations are the caller's.</summary>
public enum FireOutcome : byte { Flying, Freed, Ignite, Deflect }

/// <summary>Fire shot of the fire-starter and the ballista, ARMY__00.WRK effect type 11h (BAB), and its smoke trail, type
/// 12h (C8A). Effect records are 22h bytes. See docs/combat.md, «Огненные снаряды».</summary>
public static class OriginalFireRules
{
    public const byte Shot = 0x11, Trail = 0x12, Attack = 4;
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static ushort U16(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(raw[offset..]);
    private static void W16(Span<byte> raw, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(raw[offset..], value);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    /// <summary>WRK BAB AH=0: frame 19h; with a shooter its owner and an aim scattered with accuracy 2 (9A1B); speed 15
    /// (996F); direction A5B8 added to the frame twice (19h + 2·direction). A shot spawned without a shooter (the ballista's) flies straight to the
    /// point. Returns the sound (4).</summary>
    public static int Create(Span<byte> effect, ReadOnlySpan<byte> shooter, uint targetX, uint targetY, uint widthPixels, uint heightPixels,
        Func<byte> random, Func<byte> secondary)
    {
        effect[0] = Shot; W16(effect, 3, 0x19);
        uint x = targetX, y = targetY;
        if (!shooter.IsEmpty)
        {
            effect[2] = shooter[3];
            (x, y) = OriginalArrowRules.Scatter(shooter, targetX, targetY, 2, widthPixels, heightPixels, random, secondary);
        }
        OriginalArrowRules.Velocity(effect, x, y, 0x0f);
        byte direction = OriginalArrowRules.Direction(effect);   // A5B8 adds it to the frame, BDB once more
        W16(effect, 3, (ushort)(U16(effect, 3) + direction));
        return 4;
    }

    /// <summary>WRK BED (AH=1). <paramref name="trail"/> is 9727 for the smoke (type 12h at the shot, layer 1);
    /// <paramref name="lookup"/> is 982B at the shot's current point (the object or null, and the terrain class).</summary>
    public static (FireOutcome Outcome, int? Sound) Step(Span<byte> effect, Action<uint, uint> trail,
        Func<uint, uint, ((byte[] Record, byte[] Template)? Struck, byte Terrain)> lookup, uint widthPixels, uint heightPixels, Func<byte> random)
    {
        trail(U32(effect, 7), U32(effect, 0x0c));
        var (struck, _) = lookup(U32(effect, 7), U32(effect, 0x0c));
        // A foreign building on the way catches fire with probability ½.
        if (struck is var (o, t) && (U32(t, 0x40) & 8) != 0 && effect[2] != o[3] && (random() & 1) != 0) return (FireOutcome.Ignite, 5);
        if (OriginalCatapultRules.Fly(effect, widthPixels, heightPixels)) return (FireOutcome.Flying, null);
        var (target, terrain) = lookup(U32(effect, 7), U32(effect, 0x0c));
        if (target is not var (record, template))
        {
            // On the ground only forest (class 1) catches fire.
            if (terrain == 1) return (FireOutcome.Ignite, 5);
            effect[0] = 0xff; return (FireOutcome.Freed, null);
        }
        if ((sbyte)record[0x19] > 0)
        {
            ushort flags = 9;
            if (Attack > template[0x44])
            {
                flags |= 2;
                int left = (sbyte)record[0x19] - (Attack - template[0x44]);
                record[0x19] = left < 0 ? (byte)0 : (byte)left;
            }
            W16(record, 0x17, (ushort)(U16(record, 0x17) & 0xffe0 | flags));
        }
        // A strong defence deflects the fire: random · DAT[44] reaching 256.
        if ((random() * template[0x44]) >> 8 != 0) return (FireOutcome.Deflect, null);
        return (FireOutcome.Ignite, 5);
    }

    /// <summary>WRK C8A AH=0: smoke frame 49h, 11 frames of 3 ticks, rising speed 1F4h.</summary>
    public static void CreateTrail(Span<byte> effect)
    {
        W16(effect, 3, 0x49); effect[0] = Trail; effect[0x16] = 3; effect[0x17] = 0x0b; W16(effect, 0x20, 0x1f4);
    }

    /// <summary>WRK CA7: the smoke rises (+10 by +20, which slows by 30h to 0), drifts with the global vector and plays its
    /// frames, then is freed.</summary>
    public static void StepTrail(Span<byte> effect, int driftX, int driftY)
    {
        W16(effect, 0x10, unchecked((ushort)(U16(effect, 0x10) + U16(effect, 0x20))));
        ushort rise = U16(effect, 0x20); W16(effect, 0x20, rise >= 0x30 ? (ushort)(rise - 0x30) : (ushort)0);
        W32(effect, 6, unchecked(U32(effect, 6) + (uint)driftX)); W32(effect, 0x0b, unchecked(U32(effect, 0x0b) + (uint)driftY));
        if (--effect[0x16] != 0) return;
        effect[0x16] = 3; W16(effect, 3, unchecked((ushort)(U16(effect, 3) + 1)));
        if (--effect[0x17] == 0) effect[0] = 0xff;
    }
}
