namespace Northwind;

public readonly record struct MineDeposit(byte Type, int Count, uint Remaining);

/// <summary>Bounded ARMY__02.WRK 5DC..61A / LE:1 B3F7 deposit initialization.</summary>
public static class OriginalMineRules
{
    public static MineDeposit Initialize(ReadOnlySpan<uint> cells, int width, int height, uint rawX, uint rawY)
    {
        if (width <= 0 || height <= 0 || width > 255 || height > 255 || cells.Length != width * height)
            throw new ArgumentException("Invalid source map");
        int cx = (int)(rawX >> 5), cy = (int)(rawY >> 5);
        if (cx < 0 || cy < 0 || cx >= width || cy >= height) throw new ArgumentOutOfRangeException(nameof(rawX));
        int first = 0, second = 0;
        for (int y = Math.Max(0, cy - 1); y <= Math.Min(height - 1, cy + 1); y++)
        for (int x = Math.Max(0, cx - 1); x <= Math.Min(width - 1, cx + 1); x++)
        {
            byte kind = (byte)(cells[y * width + x] >> 16);
            if (kind == 1) first++;
            else if (kind == 2) second++;
        }
        byte type = (byte)(second > first ? 2 : 1);
        int count = Math.Max(first, second);
        return new(type, count, (uint)count << 11);
    }
}
