namespace Northwind;

/// <summary>Fog of war of the original: the map [18DA] holds one byte per cell, bit n set while participant n has not seen the
/// cell (the whole map starts at FFh, LE:1 24E6). LE:1 813E opens a square around an object: radius DAT[47] (+1 for an
/// experience of 28h or more), clipped to the map; it runs at the end of every movement step (72B3), when an object is placed
/// (81D9) and every fourth update of some objects (12A7B). See docs/fog-of-war.md.</summary>
public static class OriginalVisibilityRules
{
    /// <summary>813E: clears the owner's bit in the square around the cell. All counts are bytes, as in the original: the clip
    /// leaves the width or height of an object far outside the map at zero, and the do-while loops then wrap around 256 times.</summary>
    public static void Reveal(Span<byte> fog, byte mapWidth, byte mapHeight, byte owner, uint rawX, uint rawY, byte sight, byte experience)
    {
        byte radius = sight; if (experience >= 0x28) radius++;
        byte width = (byte)(radius + radius + 1), height = width;
        byte row = (byte)(rawY >> 5);
        byte top = (byte)(row - radius);
        if (row < radius) { top = (byte)-top; height = (byte)(height - top); top = 0; }
        byte bottom = (byte)(top + height);
        if (bottom >= mapHeight) height = (byte)(height - (byte)(bottom - mapHeight));
        byte column = (byte)(rawX >> 5);
        byte left = (byte)(column - radius);
        if (column < radius) { left = (byte)-left; width = (byte)(width - left); left = 0; }
        byte right = (byte)(left + width);
        if (right >= mapWidth) width = (byte)(width - (byte)(right - mapWidth));
        byte mask = owner >= 8 && (owner & 31) >= 8 ? (byte)0xff : (byte)~(1 << (owner & 31));
        int at = top * mapWidth + left;
        byte rows = height;
        do
        {
            byte columns = width;
            do { if (at < fog.Length) fog[at] &= mask; at++; } while (--columns != 0);
            at += (byte)(mapWidth - width);   // SUB DL, BH: the row step wraps as a byte
        } while (--rows != 0);
    }
}
