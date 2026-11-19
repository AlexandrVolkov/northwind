namespace Northwind;

/// <summary>6DEF..6EB1 column gauge and the health color thresholds at 63D8..63ED.</summary>
public static class OriginalStatusBarRules
{
    public static byte HealthColor(byte current, byte maximum) => current > (maximum >> 1) ? (byte)0xb2
        : current > (maximum >> 2) ? (byte)0x51 : (byte)0x22;

    public static int FilledColumns(int width, byte current, byte maximum)
    {
        if (width is < 3 or > 255) throw new ArgumentOutOfRangeException(nameof(width));
        if (current > maximum) throw new ArgumentOutOfRangeException(nameof(current));
        int product = (width - 2) * current;
        if (product == 0) return 0; // Native skips DIV when AX == 0, even if CH is zero.
        if (maximum == 0) throw new ArgumentOutOfRangeException(nameof(maximum));
        return product / maximum;
    }

    public static byte[] Pixels(int width, int height, byte current, byte maximum, byte color)
    {
        if (height is < 3 or > 255) throw new ArgumentOutOfRangeException(nameof(height));
        int filled = FilledColumns(width, current, maximum); var pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            pixels[y * width + x] = x == 0 || y == 0 || x == width - 1 || y == height - 1 ? (byte)0x44
                : x <= filled ? color : (byte)0;
        return pixels;
    }
}
