using System.Buffers.Binary;
namespace Northwind;

/// <summary>Collaborators of the death path that are bridged or verified separately.</summary>
public interface IDeathWorld
{
    /// <summary>9A8E then 43B0 with zero: snap a mid-step unit and clear its occupancy area.</summary>
    void ClearOccupancy();
    /// <summary>9727: create effect/object <paramref name="code"/> of army <paramref name="army"/> at the unit position (corpse).</summary>
    void Corpse(byte army, byte code, uint x, uint y);
    /// <summary>98DB: remove the object.</summary>
    void Remove();
    /// <summary>D12C: terrain class (descriptor &amp; 7) under the unit.</summary>
    byte TerrainClass();
}

/// <summary>Death dispatch 95D3 and death animation/decay A4DF from LE:1. See docs/combat.md.</summary>
public static class OriginalDeathRules
{
    /// <summary>95D3: true for the special death path 12D08 (DAT flag 100000h); otherwise the voice reference to play, if any.</summary>
    public static (bool Special, uint? Voice) Dispatch(ReadOnlySpan<byte> unit, ReadOnlySpan<byte> template)
    {
        if ((BinaryPrimitives.ReadUInt32LittleEndian(template[0x40..]) & 0x100000) != 0) return (true, null);
        bool voice = (sbyte)unit[0x19] >= -12 && (unit[0x17] & 0x80) == 0;
        return (false, voice ? BinaryPrimitives.ReadUInt32LittleEndian(template[0xa7..]) : null);
    }

    /// <summary>A4DF..A5B7: one update of a dead object.</summary>
    public static void Step(Span<byte> unit, ReadOnlySpan<byte> template, IDeathWorld world)
    {
        if ((unit[0x17] & 0x80) == 0)
        {
            if ((sbyte)unit[0x19] >= -12) { Begin(unit, template, world); return; }
            if ((unit[0x2e] & 1) != 0) return;
            unit[0x15] = unchecked((byte)(unit[0x15] - 1));
            if (unit[0x15] != 0)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(unit[1..], unchecked((ushort)(BinaryPrimitives.ReadUInt16LittleEndian(unit[1..]) + 1)));
                return;
            }
            byte terrain = world.TerrainClass();
            if (terrain != 1 && terrain != 2)
            {
                byte code = template[0x53 + (unit[0x19] & 3)];
                if (code != 0xff) world.Corpse(unit[0], code, BinaryPrimitives.ReadUInt32LittleEndian(unit[4..]), BinaryPrimitives.ReadUInt32LittleEndian(unit[8..]));
            }
        }
        unit[0x19] = unchecked((byte)(unit[0x19] - 1));
        if (unit[0x19] > 0xfa) return;
        world.Remove();
    }

    private static void Begin(Span<byte> unit, ReadOnlySpan<byte> template, IDeathWorld world)
    {
        world.ClearOccupancy();
        uint frame = (uint)(byte)(template[0x4a] + template[0x4b]) << 3;
        byte kind = (byte)((unit[0x17] >> 3) & 3), offset = 0;
        int slot = 0;
        if (kind != 0)
        {
            for (slot = 0; slot != kind; slot++) offset = unchecked((byte)(offset + template[0x4f + slot]));
            if (template[0x4f + slot] == 0) { slot = 0; offset = 0; }
        }
        frame += (uint)offset << 2;
        byte frames = template[0x4f + slot];
        unit[0x19] = (byte)(kind + 0xf0);
        ushort word = unchecked((ushort)(frame + (((unit[0x25] & 7) >> 1) * frames) + BinaryPrimitives.ReadUInt16LittleEndian(template[0x48..])));
        BinaryPrimitives.WriteUInt16LittleEndian(unit[1..], word);
        unit[0x15] = frames; unit[0x2e] = 0;
    }
}
