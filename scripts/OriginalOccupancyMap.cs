using System.Buffers.Binary;
using System.Security.Cryptography;
namespace Northwind;

/// <summary>Three mutable occupancy planes. Supported coordinates keep the whole footprint in the map.</summary>
public sealed class OriginalOccupancyMap
{
    public byte Width { get; }
    public byte Height { get; }
    private readonly uint[] _cells;
    public ReadOnlySpan<uint> Cells => _cells;

    public OriginalOccupancyMap(byte width, byte height)
    {
        if (width == 0 || height == 0) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Height = height; _cells = new uint[3 * width * height];
    }

    public void LoadCells(ReadOnlySpan<uint> cells)
    {
        if (cells.Length != _cells.Length) throw new ArgumentException("Wrong plane buffer size", nameof(cells));
        cells.CopyTo(_cells);
    }

    /// <summary>Clears every cell of every plane that holds the reference (a vectorised pass; the removal of a dead object).</summary>
    public void ClearReference(uint reference) => _cells.AsSpan().Replace(reference, 0u);

    private (int Start, int Width, int Height) Locate(byte footprintWidth, byte footprintHeight, byte rawLayer, uint x, uint y)
    {
        int plane = rawLayer & 3;
        long left = (long)(x >> 5) - (footprintWidth >> 1);
        long top = (long)(y >> 5) - (footprintHeight >> 1);
        if (plane >= 3 || footprintWidth == 0 || footprintHeight == 0 || left < 0 || top < 0 ||
            left + footprintWidth > Width || top + footprintHeight > Height)
            throw new ArgumentOutOfRangeException(nameof(x), "Unsupported occupancy plane or out-of-map footprint");
        return (plane * Width * Height + (int)top * Width + (int)left, footprintWidth, footprintHeight);
    }

    internal void ValidateFootprint(byte footprintWidth, byte footprintHeight, byte rawLayer, uint x, uint y)
        => Locate(footprintWidth, footprintHeight, rawLayer, x, y);

    // EXE 43E5 -> 6885 -> 43B0 -> 4409. Zero clears; other references are overwritten.
    public void WriteFootprint(byte footprintWidth, byte footprintHeight, byte rawLayer, uint x, uint y, uint value)
    {
        var area = Locate(footprintWidth, footprintHeight, rawLayer, x, y);
        for (int row = 0; row < area.Height; row++)
            _cells.AsSpan(area.Start + row * Width, area.Width).Fill(value);
    }

    // EXE D2D1..D362 with CL=0: replace matching cells and count other nonzero cells.
    // The optional victim-damage branch of the original routine is outside this API.
    public uint ReplaceMatchingFootprint(byte footprintWidth, byte footprintHeight, byte rawLayer,
        uint x, uint y, uint expected, uint replacement)
    {
        var area = Locate(footprintWidth, footprintHeight, rawLayer, x, y);
        uint conflicts = 0;
        for (int row = 0; row < area.Height; row++)
        for (int column = 0; column < area.Width; column++)
        {
            int cell = area.Start + row * Width + column;
            uint current = _cells[cell];
            if (current == expected) _cells[cell] = replacement;
            else if (current != replacement && current != 0) conflicts++;
        }
        return conflicts;
    }

    public string ComputeSha256()
    {
        var bytes = new byte[_cells.Length * 4];
        for (int index = 0; index < _cells.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), _cells[index]);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}

public sealed record OriginalOccupancySnapshot(OriginalOccupancyMap Grid,
    IReadOnlyDictionary<uint, ObjectRecord> References, uint ClaimConflicts, bool MatchesReference)
{
    public static OriginalOccupancySnapshot Build(MapData map, ScenarioData scenario, TemplatesData templates)
    {
        if (scenario.InitialOccupancy.Status != "ready")
            throw new InvalidDataException("Initial registration is not verified for these source files; repeat import");
        if (map.Width is < 1 or > 255 || map.Height is < 1 or > 255)
            throw new InvalidDataException("Unsupported original occupancy dimensions");
        var grid = new OriginalOccupancyMap((byte)map.Width, (byte)map.Height);
        var references = new Dictionary<uint, ObjectRecord>();
        uint conflicts = 0;
        foreach (var slot in scenario.Slots)
        foreach (var obj in slot.Objects)
        {
            if (obj.RuntimeReference == 0 || !references.TryAdd(obj.RuntimeReference, obj))
                throw new InvalidDataException("Missing or duplicate runtime object reference");
            var parameters = templates.Armies[obj.TemplateFile].Templates[obj.TemplateIndex].Construction;
            switch (obj.OccupancyMode)
            {
                case "overwrite":
                    grid.WriteFootprint(parameters.FootprintWidth, parameters.FootprintHeight, obj.RawLayer,
                        obj.RawX, obj.RawY, obj.RuntimeReference); break;
                case "claim_empty":
                    conflicts += grid.ReplaceMatchingFootprint(parameters.FootprintWidth, parameters.FootprintHeight,
                        obj.RawLayer, obj.RawX, obj.RawY, 0, obj.RuntimeReference); break;
                case "excluded_common_flag": case "deferred_first_update": break;
                default: throw new InvalidDataException("Unverified initial registration policy");
            }
        }
        bool matches = references.Count == scenario.InitialOccupancy.ObjectCount &&
            conflicts == scenario.InitialOccupancy.ClaimConflicts && grid.ComputeSha256() == scenario.InitialOccupancy.ExpectedSha256;
        return new(grid, references, conflicts, matches);
    }
}
