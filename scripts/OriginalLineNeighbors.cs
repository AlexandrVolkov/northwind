namespace Northwind;

/// <summary>Neighbour tables of the line templates (data/line_neighbors.json, tools/export_line_neighbors.py): the mask of the
/// directions that count and the accepted (frame, neighbour mask) pairs.</summary>
public sealed class OriginalLineNeighbors(IReadOnlyDictionary<byte, (byte Mask, byte[] Pairs)> tables)
{
    private static readonly (int X, int Y)[] Directions = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];   // DS:19C7

    public static OriginalLineNeighbors From(LineNeighborData data) =>
        new(data.Templates.ToDictionary(p => byte.Parse(p.Key), p => (p.Value.Mask, Convert.FromHexString(p.Value.PairsHex))));

    /// <summary>The records in the cell of a plane (null when empty).</summary>
    public delegate byte[]? Occupant(int plane, int cell);

    /// <summary>The cell one footprint away from the record's centre in a direction (BB26: byte arithmetic, off the map = none).</summary>
    private static int Neighbor(byte width, byte height, ReadOnlySpan<byte> template, uint x, uint y, int direction)
    {
        byte column = unchecked((byte)((x >> 5) + (ushort)(sbyte)(byte)(template[3] * Directions[direction].X))), row = unchecked((byte)((y >> 5) + (ushort)(sbyte)(byte)(template[4] * Directions[direction].Y)));
        return row >= height || column >= width ? -1 : (ushort)(row * width) + column;
    }

    /// <summary>BADF: the mask of directions in which the record has a neighbour of its own template.</summary>
    private static byte Around(byte width, byte height, Occupant occupant, Func<byte, byte[]> template, byte[] record)
    {
        byte mask = 0; var tp = template(record[0x0e]);
        for (int direction = 0; direction < 8; direction++)
        {
            int cell = Neighbor(width, height, tp, BitConverter.ToUInt32(record, 4), BitConverter.ToUInt32(record, 8), direction);
            if (cell < 0 || occupant(record[0x0d] & 3, cell) is not { } other) continue;
            if (other[0x0e] == record[0x0e]) mask |= (byte)(1 << direction);
        }
        return mask;
    }

    private bool Known(byte index, byte mask)
    {
        var (_, pairs) = tables[index];
        for (int i = 0; i < pairs.Length; i += 2) if (pairs[i + 1] == mask) return true;
        return false;
    }

    /// <summary>BC2E: may a segment of the line template be placed at the point. Every neighbour of the same template (with the new
    /// segment counted in its pattern) and the new segment itself need a pattern of the table.</summary>
    public bool Allowed(byte width, byte height, Occupant occupant, Func<byte, byte[]> template, byte index, uint x, uint y)
    {
        if (!tables.TryGetValue(index, out var table)) return true;
        var tp = template(index); byte plane = (byte)(tp[0x77] & 3), seen = 0;
        for (int direction = 0; direction < 8; direction++)
        {
            int cell = Neighbor(width, height, tp, x, y, direction);
            if (cell < 0 || occupant(plane, cell) is not { } other || other[0x0e] != index) continue;
            byte pattern = (byte)(Around(width, height, occupant, template, other) | (1 << ((direction + 4) & 7)));
            if (!Known(index, (byte)(pattern & table.Mask))) return false;
            seen |= (byte)(1 << direction);
        }
        return Known(index, (byte)(seen & table.Mask));
    }
}

public sealed class LineNeighborData { public Dictionary<string, LineNeighborTemplate> Templates { get; set; } = []; }
public sealed class LineNeighborTemplate { public byte Mask { get; set; } public string PairsHex { get; set; } = ""; }
