using System.Buffers.Binary;
namespace Northwind;

public sealed class OriginalTerrainData
{
    public uint[] PatchMasks { get; set; } = [];
    public List<byte> RandomValues { get; set; } = [];
}
public readonly record struct TerrainCell(byte X, byte Y);
public enum HarvestWork { Waiting, SwingQueued }

public static class OriginalForestryRules
{
    public static uint TerrainCode(uint property) => property & 0xf07;
    public static bool IsTimber(uint property) => TerrainCode(property) == 1;

    // C35F..C438: row-major search for a valid footprint, without byte overflow of the search end.
    public static bool FindTimber(byte width, byte height, ReadOnlySpan<uint> cells, ReadOnlySpan<byte> properties,
        uint rawX, uint rawY, byte footprintWidth, byte footprintHeight, byte radius, out TerrainCell result)
    {
        int cx = (int)(rawX >> 5), cy = (int)(rawY >> 5);
        if (width == 0 || height == 0 || cells.Length != width * height || cx >= width || cy >= height ||
            footprintWidth == 0 || footprintHeight == 0 || footprintWidth + 2 * radius > 255 || footprintHeight + 2 * radius > 255 ||
            cx < footprintWidth / 2 || cy < footprintHeight / 2 ||
            cx - footprintWidth / 2 + footprintWidth > width || cy - footprintHeight / 2 + footprintHeight > height)
            throw new ArgumentException("Unsupported forestry search domain");
        int left = cx - footprintWidth / 2 - radius, top = cy - footprintHeight / 2 - radius;
        if (left + footprintWidth + 2 * radius > 255 || top + footprintHeight + 2 * radius > 255)
            throw new ArgumentException("Search end overflows original byte coordinates");
        int right = Math.Min(width, left + footprintWidth + 2 * radius), bottom = Math.Min(height, top + footprintHeight + 2 * radius);
        for (int y = Math.Max(0, top); y < bottom; y++)
        for (int x = Math.Max(0, left); x < right; x++)
        {
            if (!IsTimber(Property(properties, cells[y * width + x]))) continue;
            result = new((byte)x, (byte)y); return true;
        }
        result = new(255, 255); return false;
    }
    public static uint Property(ReadOnlySpan<byte> properties, uint cell)
    {
        int offset = (int)(cell & 65535) * 8;
        if (offset + 8 > properties.Length) throw new ArgumentException("Missing SEG property", nameof(properties));
        return BinaryPrimitives.ReadUInt32LittleEndian(properties[offset..]);
    }
    // 8542..8552 and the action increment at 84E1; target coordinates are caller-owned.
    public static void InitializeWork(Span<byte> record, ReadOnlySpan<byte> template)
    {
        record[0x34] = (byte)((record[0x34] & 0xe0) | template[0x78]);
        record[0x15] = unchecked((byte)(record[0x15] + 1));
    }
    // 8585..85A9 with 8AF4 and 8C89. Entry requires nonzero low five work bits.
    public static HarvestWork AdvanceWork(Span<byte> record, ReadOnlySpan<byte> template)
    {
        if ((record[0x34] & 31) == 0) throw new ArgumentException("No pending work count");
        byte threshold = unchecked((byte)(template[0x75] - ((template[0x75] * record[0x16]) >> 8)));
        if (record[0x2f] < threshold) { record[0x2f]++; return HarvestWork.Waiting; }
        record[0x22] = record[0x32]; record[0x23] = record[0x33]; record[0x2d] = (byte)((record[0x2d] & 15) | 0x30);
        record[0x34]--; return HarvestWork.SwingQueued;
    }
    // 85A9..85CC before the cargo callback. Returns whether CFAC must be called.
    public static bool FinishLoad(Span<byte> record)
    {
        if ((record[0x34] & 31) != 0) throw new ArgumentException("Work is incomplete");
        byte next = unchecked((byte)(record[0x34] + 0x20));
        bool clear = next >= 0xe0;
        record[0x34] = clear ? (byte)0 : next; return clear;
    }
    // 85D5..85E1: signed-result clamp, not an unsigned saturating addition.
    public static void ApplyLoadModifier(Span<byte> record)
    {
        byte next = unchecked((byte)(record[0x16] + 5)); record[0x16] = (next & 128) != 0 ? (byte)127 : next;
    }
}

/// <summary>CFAC single-cell subtype replacement and CD72's atomic 3x3 blend. Dirty-render cache is external.</summary>
public sealed class OriginalTerrainCatalog
{
    private readonly byte[] _properties, _random;
    private readonly uint[] _masks;
    private readonly Dictionary<uint, List<ushort>> _variants = [];
    private readonly List<ushort> _cleared = [];
    public byte RandomIndex { get; set; }
    public OriginalTerrainCatalog(byte[] properties, OriginalTerrainData constants)
    {
        if (properties.Length == 0 || properties.Length % 8 != 0 || properties.Length / 8 >= 65536 ||
            constants.PatchMasks.Length != 9 || constants.RandomValues.Count != 256) throw new ArgumentException("Invalid terrain catalog");
        _properties = properties; _masks = constants.PatchMasks; _random = constants.RandomValues.ToArray();
        for (int i = 0; i < properties.Length / 8; i++)
        {
            uint descriptor = Descriptor(i), key = descriptor & 0xffff0f00;
            if (!_variants.TryGetValue(key, out var group)) _variants[key] = group = [];
            group.Add((ushort)i);
            if ((descriptor & 0xf00) == 0x200) _cleared.Add((ushort)i);
        }
    }
    public uint Descriptor(int tile) => OriginalForestryRules.Property(_properties, (uint)tile);
    /// <summary>CE9D: random tile whose descriptor matches under FFFF0F00; consumes RNG only when a candidate exists.</summary>
    public bool ReplaceSurface(uint descriptor, out ushort tile) => Choose(descriptor, out tile);
    /// <summary>Second index into the same table, used by 9E55 ([19EC]).</summary>
    public byte SecondaryIndex { get; set; }
    public byte NextSecondaryByte()
    {
        byte value = _random[SecondaryIndex]; SecondaryIndex = unchecked((byte)(SecondaryIndex + 1)); return value;
    }
    public byte NextRandomByte()
    {
        byte value = _random[RandomIndex]; RandomIndex = unchecked((byte)(RandomIndex + 1)); return value;
    }
    private bool Choose(uint descriptor, out ushort tile)
    {
        if (!_variants.TryGetValue(descriptor & 0xffff0f00, out var candidates)) { tile = 0; return false; }
        byte value = NextRandomByte();
        tile = candidates[(value * candidates.Count) >> 8]; return true;
    }
    public int ClearTimber(uint[] cells, byte width, byte height, int x, int y)
    {
        if (cells.Length != width * height || width == 0 || height == 0) throw new ArgumentException("Invalid map");
        if (x < 0 || y < 0 || x >= width || y >= height) return 0;
        uint original = Descriptor((int)(cells[y * width + x] & 65535));
        byte a = (byte)((original >> 16) & 15), b = (byte)((original >> 20) & 15);
        byte c = (byte)((original >> 24) & 15), d = (byte)((original >> 28) & 15);
        if (a == b) { b = c; if (a == b) b = d; }
        byte aa = (byte)(a | (a << 4)), bb = (byte)(b | (b << 4));
        foreach (ushort anchor in _cleared)
        {
            uint theme = Descriptor(anchor) & 0xff000000;
            // The first probe retains AH from the candidate index before SHL EAX,16.
            if (!Choose(theme | ((uint)(anchor >> 8) << 24) | ((uint)aa << 16), out _) ||
                !Choose(theme | ((uint)bb << 16), out _)) continue;
            return Blend(cells, width, height, x, y, anchor);
        }
        return 0;
    }
    private int Blend(uint[] cells, byte width, byte height, int x, int y, ushort anchor)
    {
        uint replacement = Descriptor(anchor);
        var writes = new List<(int Index, ushort Tile)>();
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 3; column++)
        {
            int xx = x + column - 1, yy = y + row - 1;
            if (xx < 0 || yy < 0 || xx >= width || yy >= height) continue;
            int index = yy * width + xx; uint mask = _masks[row * 3 + column];
            ushort tile = anchor;
            if (mask != 0)
            {
                uint before = OriginalForestryRules.Property(_properties, cells[index]);
                uint after = (before & mask) | (replacement & ~mask);
                if (after == before) continue;
                if (!Choose(after, out tile)) return 0; // RNG advances, but no cell is committed on failure.
            }
            writes.Add((index, tile));
        }
        int changed = 0;
        foreach (var (index, tile) in writes)
        {
            uint value = (cells[index] & 0xffff0000) | tile;
            if (value != cells[index]) changed++;
            cells[index] = value;
        }
        return changed;
    }
}
