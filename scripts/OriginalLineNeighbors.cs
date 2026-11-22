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

    /// <summary>BADF with the neighbours collected (al = 1): the mask and the records of the same template around the record.</summary>
    private static (byte Mask, List<byte[]> Neighbors) AroundWithNeighbors(byte width, byte height, Occupant occupant, Func<byte, byte[]> template, byte[] record)
    {
        byte mask = 0; var found = new List<byte[]>(); var tp = template(record[0x0e]);
        for (int direction = 0; direction < 8; direction++)
        {
            int cell = Neighbor(width, height, tp, BitConverter.ToUInt32(record, 4), BitConverter.ToUInt32(record, 8), direction);
            if (cell < 0 || occupant(record[0x0d] & 3, cell) is not { } other || other[0x0e] != record[0x0e]) continue;
            mask |= (byte)(1 << direction); found.Add(other);
        }
        return (mask, found);
    }

    /// <summary>BBE4: the shape of a segment (+22) from the mask of its neighbours: the first pair of the table whose mask equals the neighbour mask
    /// (an unknown pattern is retried with mask 0); the frame word moves by (old shape − new shape) · (2·DAT[4A] + DAT[4B]).</summary>
    private void Apply(byte[] record, int stride, byte mask)
    {
        var (tableMask, pairs) = tables[record[0x0e]];
        for (int attempt = 0, m = mask & tableMask; attempt < 2; attempt++, m = 0)
            for (int i = 0; i + 1 < pairs.Length && pairs[i] != 0xff; i += 2)
            {
                if (pairs[i + 1] != m) continue;
                short delta = (short)((sbyte)(byte)(record[0x22] - pairs[i]) * (sbyte)(byte)stride);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1), (ushort)(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(1)) - delta));
                record[0x22] = pairs[i];
                return;
            }
    }

    /// <summary>BB8B (called by the WRK AH=0 of a line segment through FS:55): the new segment and each neighbour take the shape that matches their neighbours
    /// (vertical, horizontal, the ends where a line starts and stops).</summary>
    public void Reshape(byte width, byte height, Occupant occupant, Func<byte, byte[]> template, byte[] record)
    {
        if (!tables.ContainsKey(record[0x0e])) return;
        var tp = template(record[0x0e]); int stride = 2 * tp[0x4a] + tp[0x4b];
        var (mask, neighbors) = AroundWithNeighbors(width, height, occupant, template, record);
        Apply(record, stride, mask);
        foreach (var other in neighbors) Apply(other, stride, Around(width, height, occupant, template, other));
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
