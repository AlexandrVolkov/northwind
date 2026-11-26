using System.Buffers.Binary;
namespace Northwind;

/// <summary>LE:1 9064 (FS:16B): a free place for a new object next to an existing one — the mages summon phantoms with it. The offset is the sum of the two objects' footprints (cells «
/// 4, as bytes) times the unit step of a direction (DS:19C7, eight signed pairs); the search starts at a random direction (9E41) and turns counter-clockwise until the placement
/// check AC63 accepts the point. Verified against the original x86 by tests/fixtures/spot.json (tools/verify_spot.py; AC63 is a recorded stub).</summary>
public static class OriginalSpotFinder
{
    public static readonly sbyte[] Directions = [0, -1, 1, -1, 1, 0, 1, 1, 0, 1, -1, 1, -1, 0, -1, -1];   // DS:19C7: x, y per direction

    /// <param name="target">the object next to which the new one stands (ESI)</param>
    /// <param name="newTemplate">the template of the new object; <paramref name="targetTemplate"/> that of the target</param>
    /// <param name="placementFree">AC63 for the layer, the point and the template (AL)</param>
    public static (bool Found, byte Direction, byte Layer, uint X, uint Y) Find(ReadOnlySpan<byte> target, ReadOnlySpan<byte> newTemplate, ReadOnlySpan<byte> targetTemplate, Func<byte> random,
        Func<byte, uint, uint, bool> placementFree)
    {
        byte layer = (byte)(newTemplate[0x77] & 3);
        byte w = (byte)((byte)(targetTemplate[3] + newTemplate[3]) << 4), h = (byte)((byte)(targetTemplate[4] + newTemplate[4]) << 4);
        byte start = (byte)(random() & 7), d = start;
        uint x0 = BinaryPrimitives.ReadUInt32LittleEndian(target[4..]), y0 = BinaryPrimitives.ReadUInt32LittleEndian(target[8..]);
        while (true)
        {
            int ox = (sbyte)w * Directions[2 * d], oy = (sbyte)h * Directions[2 * d + 1];
            uint y = (y0 & 0xffff0000) | (ushort)((ushort)y0 + (ushort)oy), x = (x0 & 0xffff0000) | (ushort)((ushort)x0 + (ushort)ox);
            if (placementFree(layer, x, y)) return (true, d, layer, x, y);
            d = (byte)((d - 1) & 7);
            if (d == start) return (false, 0, layer, x, y);
        }
    }
}
