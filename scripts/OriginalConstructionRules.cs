namespace Northwind;

public readonly record struct ResourceQuad(uint First, uint Second, uint Third, uint Fourth);
public readonly record struct ConstructionPayment(bool Accepted, ResourceQuad Balances);
public enum ConstructionPlacementStatus : byte { Clear, Rejected, Occupied }
public readonly record struct ConstructionPlacement(ConstructionPlacementStatus Status, uint BlockingObjectOffset = 0);

/// <summary>Original debit prefix and AD5F placement function; not the full build transaction.</summary>
public static class OriginalConstructionRules
{
    // EXE B083..B0E8. Later allocation/placement can still fail; no rollback is implied.
    public static ConstructionPayment TryDebitUpfront(ResourceQuad balances, ResourceQuad costs)
    {
        if (balances.First < costs.First || balances.Second < costs.Second ||
            balances.Third < costs.Third || balances.Fourth < costs.Fourth)
            return new(false, balances);
        return new(true, new(balances.First - costs.First, balances.Second - costs.Second,
            balances.Third - costs.Third, balances.Fourth - costs.Fourth));
    }

    // EXE AD5F..AE39. Valid-map domain: nonzero byte dimensions and footprint <= map.
    // Cells retain their original u32 values; properties contain eight bytes per tile.
    // Occupancy is three consecutive planes of relative object offsets (zero = empty).
    public static ConstructionPlacement ProbePlacement(byte width, byte height,
        ReadOnlySpan<uint> cells, ReadOnlySpan<byte> properties, ReadOnlySpan<uint> occupancy,
        byte footprintWidth, byte footprintHeight, ReadOnlySpan<byte> terrainMask, uint x, uint y)
    {
        if (width == 0 || height == 0 || footprintWidth == 0 || footprintHeight == 0 ||
            footprintWidth > width || footprintHeight > height)
            throw new ArgumentOutOfRangeException(nameof(footprintWidth), "Unsupported map or footprint dimensions");
        int count = width * height;
        if (cells.Length != count || occupancy.Length != 3 * count || terrainMask.Length != 8 ||
            properties.Length == 0 || properties.Length % 8 != 0)
            throw new ArgumentException("Invalid construction map buffers");

        // Preserve the original low-byte subtraction and wrapping, even for raw
        // coordinates that the full UI would normally reject before this routine.
        byte left = unchecked((byte)((x >> 5) - (uint)(footprintWidth >> 1)));
        byte top = unchecked((byte)((y >> 5) - (uint)(footprintHeight >> 1)));
        if (left > width - footprintWidth || top > height - footprintHeight)
            return new(ConstructionPlacementStatus.Rejected);

        for (int row = 0; row < footprintHeight; row++)
        for (int column = 0; column < footprintWidth; column++)
        {
            int cell = (top + row) * width + left + column;
            for (int plane = 0; plane < 3; plane++)
            {
                uint reference = occupancy[plane * count + cell];
                if (reference != 0) return new(ConstructionPlacementStatus.Occupied, reference);
            }
            int property = (int)(cells[cell] & 0xffff) * 8;
            if (property >= properties.Length) throw new InvalidDataException("Missing SEG tile property");
            if (terrainMask[properties[property] & 7] == 0)
                return new(ConstructionPlacementStatus.Rejected);
        }
        return new(ConstructionPlacementStatus.Clear);
    }
}
