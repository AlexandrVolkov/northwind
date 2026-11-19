namespace Northwind;

/// <summary>Input of one call of the scroll step: pointer in screen coordinates of the frame and the arrow keys.</summary>
public readonly record struct ScrollInput(float X, float Y, float Width, float Height, bool Left, bool Right, bool Up, bool Down);
public readonly record struct ScrollStep(int Dx, int Dy, ushort CursorFirst, ushort CursorLast, byte LastTimer);

/// <summary>LE:1 3FA1..40C3: map scrolling by whole cells. The view moves one cell when the byte timer [19E4] advanced at least
/// 10h − «Панорама» ([908F], 1..16, default 0Dh) ticks since the last step; the pointer must touch the screen edge
/// (x &lt; 1, x ≥ 27Fh, y &lt; 1, y ≥ 1DDh in 640×480) or an arrow key (scan codes E04B/E04D/E048/E050) be held.
/// The horizontal side is tested first (left before right), then the vertical one (up before down); each match selects its WORK.SPR
/// cursor frames, so a vertical match overrides the horizontal cursor.</summary>
public static class OriginalScrollRules
{
    public const byte DefaultPan = 0x0d, MinPan = 1, MaxPan = 0x10;
    public static readonly (ushort First, ushort Last) Arrow = (0, 0x0e), Left = (0x2d, 0x36), Right = (0x19, 0x22), Up = (0x0f, 0x18), Down = (0x23, 0x2c);

    public static ScrollStep Step(ScrollInput input, byte timer, byte last, byte pan)
    {
        int move = 0;
        byte interval = unchecked((byte)(0x10 - pan));
        if (unchecked((byte)(timer - last)) >= interval) { move = 1; last = timer; }
        // 640×480: right edge 27Fh, bottom 1DDh; a wider frame keeps the right edge at its last column.
        float right = input.Width - 1, bottom = input.Height - 3;
        int dx = 0, dy = 0; var cursor = Arrow; bool any = false;
        // Left skips the right test and up skips the down test (403D, 4084). Only at the map's left or top border, where the
        // subtraction clamps to 0, the original falls through to the opposite test; the port clamps the camera instead.
        if (input.Left || input.X < 1) { cursor = Left; dx -= move; any = true; }
        else if (input.Right || input.X >= right) { cursor = Right; dx += move; any = true; }
        if (input.Up || input.Y < 1) { cursor = Up; dy -= move; any = true; }
        else if (input.Down || input.Y >= bottom) { cursor = Down; dy += move; any = true; }
        return new ScrollStep(dx, dy, any ? cursor.First : Arrow.First, any ? cursor.Last : Arrow.Last, last);
    }

    /// <summary>LE:1 1132F: «Панорама» −/+ changes [908F] by one within 1..16.</summary>
    public static byte ChangePan(byte pan, int step) { int next = pan + step; return next is >= MinPan and <= MaxPan ? (byte)next : pan; }
}
