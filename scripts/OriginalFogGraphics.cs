using Godot;

namespace Northwind;

/// <summary>Prepared WORK fog weights and FADE_COL palette-index mapping. No ISO is needed at runtime.</summary>
public sealed class OriginalFogGraphics
{
    public byte[] Weights { get; }
    public byte[] ShadeIndices { get; }
    public byte[] Palette { get; }

    private OriginalFogGraphics(byte[] weights, byte[] shadeIndices, byte[] palette)
    {
        Weights = weights; ShadeIndices = shadeIndices; Palette = palette;
    }

    private static byte[] ReadPng(string path, int width, int height, Image.Format format)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read)
            ?? throw new InvalidDataException($"Missing prepared fog resource: {path}");
        using var image = new Image();
        if (image.LoadPngFromBuffer(file.GetBuffer((long)file.GetLength())) != Error.Ok || image.GetSize() != new Vector2I(width, height))
            throw new InvalidDataException($"Invalid fog image: {path}");
        image.Convert(format);
        return image.GetData();
    }

    public static OriginalFogGraphics Load(string directory = "res://assets/imported/visibility") => new(
        ReadPng(directory + "/fog_weights.png", 256, 128, Image.Format.L8),
        ReadPng(directory + "/shade_indices.png", 256, 9, Image.Format.L8),
        ReadPng(directory + "/palette.png", 256, 1, Image.Format.Rgb8));

    /// <summary>A080..A0CD: transform a 32x32 tile in an indexed framebuffer. This is a palette lookup,
    /// not alpha blending of RGB colours. Mask 15 is black; mask 0 leaves every pixel untouched.</summary>
    public void ShadeTile(Span<byte> pixels, int stride, int left, int top, byte corners, int worldX, int worldY)
    {
        if (stride < 32 || left < 0 || top < 0 || left + 32 > stride || (top + 31) * stride + left + 32 > pixels.Length)
            throw new ArgumentOutOfRangeException(nameof(left));
        if (corners == 0) return;
        if (corners > 15) throw new ArgumentOutOfRangeException(nameof(corners));
        int sprite = 164 + 2 * corners + ((worldX + worldY) & 1);
        int index = sprite - 166;
        int maskLeft = (index % 8) * 32, maskTop = (index / 8) * 32;
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
        {
            int at = (top + y) * stride + left + x;
            pixels[at] = corners == 15 ? (byte)0 : ShadeIndices[Weights[(maskTop + y) * 256 + maskLeft + x] * 256 + pixels[at]];
        }
    }

    /// <summary>9F38..A10B, excluding DOS dirty-cache writes: shade a 14x14 viewport at the original (16,16).
    /// The map outside its boundary is treated as unexplored; world parity stays stable while scrolling.</summary>
    public void ShadeViewport(Span<byte> framebuffer, int stride, ReadOnlySpan<byte> map, int width, int height,
        int cameraX, int cameraY, byte participant, int screenLeft = 16, int screenTop = 16)
    {
        if (width < 14 || height < 14 || width > 255 || height > 255 || map.Length != width * height ||
            cameraX < 0 || cameraY < 0 || cameraX + 14 > width || cameraY + 14 > height)
            throw new ArgumentOutOfRangeException(nameof(cameraX));
        if (stride < screenLeft + 448 || screenLeft < 0 || screenTop < 0 || (screenTop + 447) * stride + screenLeft + 448 > framebuffer.Length)
            throw new ArgumentOutOfRangeException(nameof(framebuffer));
        Span<byte> neighbours = stackalloc byte[8];
        for (int row = 0; row < 14; row++)
        for (int column = 0; column < 14; column++)
        {
            int x = cameraX + column, y = cameraY + row, i = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                neighbours[i++] = nx < 0 || ny < 0 || nx >= width || ny >= height ? (byte)255 : map[ny * width + nx];
            }
            byte corners = FogCorners(map[y * width + x], neighbours, participant);
            ShadeTile(framebuffer, stride, screenLeft + column * 32, screenTop + row * 32, corners, x, y);
        }
    }

    // A019..A05B: NW,N,NE,W,E,SW,S,SE. Keep rendering geometry here;
    // OriginalVisibilityRules in main owns only the 813E reveal operation.
    private static byte FogCorners(byte centre, ReadOnlySpan<byte> neighbours, byte participant)
    {
        byte bit = participant < 8 ? (byte)(1 << participant) : (byte)0;
        if ((centre & bit) == 0) return 0;
        byte mask = 0;
        if ((neighbours[1] & neighbours[2] & neighbours[4] & bit) != 0) mask |= 1;
        if ((neighbours[0] & neighbours[1] & neighbours[3] & bit) != 0) mask |= 2;
        if ((neighbours[3] & neighbours[5] & neighbours[6] & bit) != 0) mask |= 4;
        if ((neighbours[4] & neighbours[6] & neighbours[7] & bit) != 0) mask |= 8;
        return mask;
    }

    public byte[] ToRgb(ReadOnlySpan<byte> indices)
    {
        byte[] rgb = new byte[indices.Length * 3];
        for (int i = 0; i < indices.Length; i++) Palette.AsSpan(indices[i] * 3, 3).CopyTo(rgb.AsSpan(i * 3));
        return rgb;
    }
}
