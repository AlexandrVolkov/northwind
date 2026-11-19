namespace Northwind;

public readonly record struct CursorAnimation(uint Frame, byte LastTimer);

/// <summary>LE:1 3DB5..3DF1, including byte timer wrap and full dword frame increment.</summary>
public static class OriginalCursorRules
{
    public const double TimerHz = 1193182.0 / 16384;
    public static CursorAnimation Step(CursorAnimation state, byte timer, ushort first, ushort last)
    {
        uint frame = state.Frame; byte previous = state.LastTimer;
        if (unchecked((byte)(timer - previous)) >= 4) { frame = unchecked(frame + 1); previous = timer; }
        ushort low = (ushort)frame;
        if (low < first || low > last) frame = (frame & 0xffff0000) | first;
        return new(frame, previous);
    }
}
