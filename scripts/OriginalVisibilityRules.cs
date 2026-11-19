using System.Buffers.Binary;
using System.Numerics;

namespace Northwind;

public readonly record struct VisibilityArea(int Left, int Top, int Width, int Height);

/// <summary>LE:1 813E..81D9: persistent exploration. A set bit in [18DA] means UNEXPLORED.
/// This map is separate from the object's OBE[20] mask used by target selection.</summary>
public static class OriginalVisibilityRules
{
    public static byte[] CreateUnexplored(int width, int height)
    {
        ValidateMap(width, height);
        var map = new byte[width * height];
        Array.Fill(map, (byte)255);
        return map;
    }

    private static void ValidateMap(int width, int height)
    {
        if (width is < 1 or > 255 || height is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(width), "Original map dimensions are nonzero bytes.");
    }

    private static void ValidateMap(ReadOnlySpan<byte> map, int width, int height)
    {
        ValidateMap(width, height);
        if (map.Length != width * height) throw new ArgumentException("Exploration map dimensions disagree.", nameof(map));
    }

    /// <summary>813E: square radius DAT[47], plus one at OBE[16] >= 40, even for building templates.
    /// The footprint and DAT flags are not read. Reject invalid byte-overflow geometry instead of corrupting memory.</summary>
    public static VisibilityArea Area(int width, int height, ReadOnlySpan<byte> record, ReadOnlySpan<byte> template)
    {
        ValidateMap(width, height);
        int x = (int)(BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) >> 5);
        int y = (int)(BinaryPrimitives.ReadUInt32LittleEndian(record[8..]) >> 5);
        int radius = unchecked((byte)(template[0x47] + (record[0x16] >= 40 ? 1 : 0)));
        if (x >= width || y >= height || radius > 127 || x + radius + 1 > 255 || y + radius + 1 > 255)
            throw new ArgumentOutOfRangeException(nameof(record), "Outside the verified original byte geometry.");
        int left = Math.Max(0, x - radius), top = Math.Max(0, y - radius);
        return new(left, top, Math.Min(width, x + radius + 1) - left, Math.Min(height, y + radius + 1) - top);
    }

    public static VisibilityArea Reveal(Span<byte> map, int width, int height, ReadOnlySpan<byte> record, ReadOnlySpan<byte> template)
    {
        ValidateMap(map, width, height);
        var area = Area(width, height, record, template);
        // 81B9 uses a dword ROL, not a byte shift. Participants >= 8 clear no low-byte bit.
        byte keep = (byte)BitOperations.RotateLeft(0xfffffffeu, record[3]);
        for (int y = area.Top; y < area.Top + area.Height; y++)
        for (int x = area.Left; x < area.Left + area.Width; x++) map[y * width + x] &= keep;
        return area;
    }

    public static bool IsExplored(byte cell, byte participant) => (cell & ParticipantBit(participant)) == 0;
    public static byte ParticipantBit(byte participant) => unchecked((byte)(1u << (participant & 31)));

    /// <summary>121C0..121E1, after the scenario condition succeeds. Original SHL of FE clears
    /// all low bits up to the participant, unlike the ordinary ROL writer. The event dispatcher is outside this helper.</summary>
    public static void RevealAllFromScenario(Span<byte> map, byte participant)
    {
        byte keep = unchecked((byte)(0xfeu << (participant & 31)));
        for (int i = 0; i < map.Length; i++) map[i] &= keep;
    }

    /// <summary>A019..A05B: each fog corner needs its three neighbouring cells to remain unexplored.
    /// Neighbours are row-major NW,N,NE,W,E,SW,S,SE. Bit order: NE,NW,SW,SE.
    /// A revealed centre never receives a fog tile.</summary>
    public static byte FogCorners(byte centre, ReadOnlySpan<byte> neighbours, byte participant)
    {
        if (neighbours.Length != 8) throw new ArgumentException("Eight neighbouring cells are required.", nameof(neighbours));
        byte bit = ParticipantBit(participant);
        if ((centre & bit) == 0) return 0;
        byte mask = 0;
        if ((neighbours[1] & neighbours[2] & neighbours[4] & bit) != 0) mask |= 1;
        if ((neighbours[0] & neighbours[1] & neighbours[3] & bit) != 0) mask |= 2;
        if ((neighbours[3] & neighbours[5] & neighbours[6] & bit) != 0) mask |= 4;
        if ((neighbours[4] & neighbours[6] & neighbours[7] & bit) != 0) mask |= 8;
        return mask;
    }

    /// <summary>A063..A075: WORK raw 32x32 mask; 0 means no shading, 15 means a solid black tile.
    /// Parity is (world cell X + Y) &amp; 1, independent of camera movement.</summary>
    public static int FogSprite(byte corners, int cellX, int cellY)
    {
        if (corners is 0 or 15) return -1;
        if (corners > 15) throw new ArgumentOutOfRangeException(nameof(corners));
        return 164 + 2 * corners + ((cellX + cellY) & 1);
    }

    /// <summary>A266..A31F: minimap indices, one byte per cell. Unexplored cells are black.
    /// Plane 2 overrides plane 1; plane 0 is ignored. The special owner [1971] uses terrain colour.</summary>
    public static byte[] Minimap(ReadOnlySpan<byte> map, ReadOnlySpan<ushort> tiles, ReadOnlySpan<byte> terrainColors,
        ReadOnlySpan<uint> plane1, ReadOnlySpan<uint> plane2, Func<uint, byte> ownerOf,
        byte participant, byte terrainOwner, ReadOnlySpan<byte> ownerColors)
    {
        if (tiles.Length != map.Length || plane1.Length != map.Length || plane2.Length != map.Length || ownerColors.Length != 16)
            throw new ArgumentException("Minimap inputs have inconsistent dimensions.");
        var pixels = new byte[map.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            if (!IsExplored(map[i], participant)) continue;
            uint reference = plane2[i] != 0 ? plane2[i] : plane1[i];
            if (reference != 0)
            {
                byte owner = ownerOf(reference);
                if (owner != terrainOwner) { pixels[i] = ownerColors[owner & 15]; continue; }
            }
            pixels[i] = terrainColors[tiles[i]];
        }
        return pixels;
    }

    /// <summary>A2C8..A2FB: a 14x14 outline, colour index 1, drawn after the minimap.</summary>
    public static void DrawCameraOutline(Span<byte> pixels, int width, int height, int left, int top)
    {
        if (pixels.Length != width * height || left < 0 || top < 0 || left + 14 > width || top + 14 > height)
            throw new ArgumentOutOfRangeException(nameof(left));
        for (int i = 0; i < 14; i++)
        {
            pixels[top * width + left + i] = 1;
            pixels[(top + 13) * width + left + i] = 1;
            pixels[(top + i) * width + left] = 1;
            pixels[(top + i) * width + left + 13] = 1;
        }
    }
}
