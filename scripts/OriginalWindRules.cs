namespace Northwind;

/// <summary>Wind state of LE:1 9E7D: vector [1C8E]/[1C92] (drifts smoke and carries fire), acceleration [1C97]/[1C9B]
/// and the 16-way direction [1C96].</summary>
public struct WindState
{
    public int X, Y, AccelerationX, AccelerationY;
    public byte Direction;
}

/// <summary>LE:1 9E7D, called on every running timer tick (9BF9): the vector gains the acceleration (each axis within
/// ±1000), its direction is recomputed, and when the timer byte [19E8] is 0 (every 256 ticks) a new acceleration
/// ±(random &amp; 3) per axis is drawn from 9E41 and 9E55 (sign from bit 7). See docs/combat.md, «Ветер».</summary>
public static class OriginalWindRules
{
    public static void Step(ref WindState wind, byte timer, Func<byte> random, Func<byte> secondary)
    {
        wind.X = Math.Clamp(unchecked(wind.AccelerationX + wind.X), -1000, 1000);
        wind.Y = Math.Clamp(unchecked(wind.AccelerationY + wind.Y), -1000, 1000);
        int eax = wind.X, ebx = -wind.Y;
        uint magnitudeY = (uint)Math.Abs(ebx), magnitudeX = (uint)Math.Abs(eax);
        byte bias = 0;
        if (magnitudeX >= magnitudeY) { (eax, ebx) = (ebx, -eax); bias = 4; }
        if (ebx < 0) bias = unchecked((byte)(bias - 8));
        eax >>= 2;
        sbyte divisor = (sbyte)(ebx >> 8);
        if (divisor != 0)
        {
            byte quotient = (byte)(sbyte)((short)eax / divisor);
            wind.Direction = (byte)((byte)((byte)((byte)(quotient + 0x40) >> 5) - bias - 2) & 0x0f);
        }
        if (timer != 0) return;
        static int Draw(byte r) => (r & 0x80) != 0 ? -(r & 3) : r & 3;
        wind.AccelerationX = Draw(random());
        wind.AccelerationY = Draw(secondary());
    }
}
