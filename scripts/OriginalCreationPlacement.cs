using System.Buffers.Binary;
namespace Northwind;

/// <summary>The placement check before an object is created (LE:1 AC63 with its footprint scan 7B79) and the free-spot search
/// 10AD2 that tries random points in growing squares around a point.</summary>
public static class OriginalCreationPlacement
{
    /// <summary>AC63. The plane (0..2) must be empty under the whole footprint (otherwise <see cref="BuilderProbe.Blocked"/> with the
    /// first object found); every cell needs a terrain value above zero for the template: DAT[58 + class] reduced by the class
    /// strength (bit 7 of the value: taken as it is), and on the plane 1 a cell that fails may be water carried by a supporting
    /// object of plane 0 (DAT[40] bit 1, condition above DAT[46], DAT[3F] not below the placed object's).</summary>
    public static (BuilderProbe Status, uint Blocker) Probe(byte width, byte height, ReadOnlySpan<uint> cells, ReadOnlySpan<byte> properties,
        ReadOnlySpan<uint> planes, byte plane, ReadOnlySpan<byte> template, uint x, uint y, Func<uint, (byte[] Record, byte[] Template)> resolve)
    {
        if (plane > 2) throw new ArgumentOutOfRangeException(nameof(plane));
        int area = width * height; byte fw = template[3], fh = template[4];
        byte left = unchecked((byte)((x >> 5) - (uint)(fw >> 1))), top = unchecked((byte)((y >> 5) - (uint)(fh >> 1)));
        if (left > (byte)(width - fw) || top > (byte)(height - fh)) return (BuilderProbe.Rejected, 0);
        int start = (ushort)(left + width * top);
        for (int j = 0; j < fh; j++) for (int i = 0; i < fw; i++)
        {
            uint reference = planes[plane * area + start + j * width + i];
            if (reference != 0) return (BuilderProbe.Blocked, reference);
        }
        for (int j = 0; j < fh; j++) for (int i = 0; i < fw; i++)
        {
            int index = start + j * width + i;
            byte terrain = properties[(int)(cells[index] & 0xffff) * 8], value = template[0x58 + (terrain & 15)];
            byte adjusted = (value & 0x80) != 0 ? (byte)(value & 0x7f) : unchecked((byte)(value - (terrain >> 3)));
            if ((sbyte)adjusted > 0) continue;
            if (plane != 1 || planes[index] == 0) return (BuilderProbe.Rejected, 0);
            var (record, other) = resolve(planes[index]);
            if ((BinaryPrimitives.ReadUInt32LittleEndian(other.AsSpan(0x40)) & 2) == 0 || (sbyte)record[0x19] <= (sbyte)other[0x46] || other[0x3f] < template[0x3f])
                return (BuilderProbe.Rejected, 0);
        }
        return (BuilderProbe.Clear, 0);
    }

    /// <summary>10AD2: the squares of side 1, 2, … (up to 15) around the point (the point rounded to the cell, the odd footprint
    /// shifted by half a cell): in each, side² random points from the two generators (9E41 for y, 9E55 for x) are tried with AC63;
    /// points near the map border are skipped. The point of a cell exactly at the far limit counts as found without a check (the
    /// original's flag after the compare). Returns the found flag and the last point tried.</summary>
    public static (bool Found, uint X, uint Y) Search(Func<byte> randomY, Func<byte> randomX, uint mapWidthPixels, uint mapHeightPixels,
        ReadOnlySpan<byte> template, uint x, uint y, Func<uint, uint, BuilderProbe> probe)
    {
        x = (x & 0xffffffe0) + (uint)((template[3] << 4) & 0x10);
        y = (y & 0xffffffe0) + (uint)((template[4] << 4) & 0x10);
        uint cx = x, cy = y;
        for (byte side = 1; ; side++)
        {
            int squared = side * side;
            if (squared > 255) return (false, cx, cy);
            byte counter = (byte)squared;
            do
            {
                int first = randomY() * side; int offsetY = (sbyte)(byte)((first >> 8) - (side >> 1));
                cy = (uint)(offsetY << 5) + y;
                int second = randomX() * side; int offsetX = (sbyte)(byte)((second >> 8) - (side >> 1));
                cx = (uint)(offsetX << 5) + x;
                bool equal;
                if (cx < 0x20) equal = false;
                else if (cx >= mapWidthPixels - 0x20) equal = cx == mapWidthPixels - 0x20;
                else if (cy < 0x20) equal = false;
                else if (cy >= mapHeightPixels - 0x20) equal = cy == mapHeightPixels - 0x20;
                else { equal = probe(cx, cy) == BuilderProbe.Clear; }
                if (equal) return (true, cx, cy);
            } while (--counter != 0);
        }
    }
}
