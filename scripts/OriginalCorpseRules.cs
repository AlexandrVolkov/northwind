using System.Buffers.Binary;
namespace Northwind;

/// <summary>Corpse of a unit, ARMY__02.WRK effect type 1 (161D), and the blood splash, ARMY__00.WRK effect type 3 (6C7).
/// A4DF creates the corpse through 9727 with the dead unit as the handler's EBX, in plane 0. See docs/combat.md, «Трупы».</summary>
public static class OriginalCorpseRules
{
    public const byte Corpse = 1, Blood = 3;
    private static ushort U16(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(raw[offset..]);
    private static void W16(Span<byte> raw, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(raw[offset..], value);
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    /// <summary>WRK:02 161D AH=0. <paramref name="support"/>: 982B found an object with DAT[40] bit 2 under the point (a
    /// bridge); otherwise on water (terrain class 2) the corpse is freed. The corpse keeps the unit's frame and owner; its
    /// variant +6 = (hit flags bit 4 → 4) + heading / 2; 4 stages of 600 ticks. Unless the unit died of a kind-3 hit
    /// (flags 18h), a blood splash (army 0, type 3, plane 0) is thrown within −7..+8 of the corpse (9E41: low nibble X,
    /// high nibble Y). Returns that point, or null.</summary>
    public static (uint X, uint Y)? Create(Span<byte> effect, ReadOnlySpan<byte> unit, bool support, byte terrain, Func<byte> random)
    {
        if (!support && terrain == 2) { effect[0] = 0xff; return null; }
        W16(effect, 3, U16(unit, 1)); effect[2] = unit[3];
        byte flags = unit[0x17];
        effect[6] = (byte)(((flags >> 2) & 4) + ((unit[0x25] & 7) >> 1));
        W32(effect, 0x18, 0x258); effect[0] = Corpse; effect[0x17] = 4;
        if ((flags & 0x18) == 0x18) return null;
        byte r = random();
        return (unchecked(U32(effect, 7) + (uint)((r & 0x0f) - 7)), unchecked(U32(effect, 0x0c) + (uint)((r >> 4) - 7)));
    }

    /// <summary>WRK:02 16AA: every 600 ticks the next decay frame (the first stage switches to the corpse frames
    /// 235h + 3 · variant); after the fourth stage the corpse is freed.</summary>
    public static void Step(Span<byte> effect)
    {
        uint timer = U32(effect, 0x18) - 1; W32(effect, 0x18, timer);
        if (timer != 0) return;
        W32(effect, 0x18, 0x258);
        if (effect[0x17] == 4) W16(effect, 3, (ushort)(effect[6] * 3 + 0x235));
        W16(effect, 3, unchecked((ushort)(U16(effect, 3) + 1)));
        if (--effect[0x17] == 0) effect[0] = 0xff;
    }

    /// <summary>WRK:00 6C7 AH=0: blood splash, frame 72h, 11 frames.</summary>
    public static void CreateBlood(Span<byte> effect)
    {
        W16(effect, 3, 0x72); effect[0x16] = 5; effect[0] = Blood; effect[0x17] = 0x0b;
    }

    /// <summary>WRK:00 6DE: frames of 5 ticks; the last six stay 220 ticks each (the stain); then freed.</summary>
    public static void StepBlood(Span<byte> effect)
    {
        if (--effect[0x16] != 0) return;
        effect[0x16] = effect[0x17] > 6 ? (byte)5 : (byte)0xdc;
        W16(effect, 3, unchecked((ushort)(U16(effect, 3) + 1)));
        if (--effect[0x17] == 0) effect[0] = 0xff;
    }
}
