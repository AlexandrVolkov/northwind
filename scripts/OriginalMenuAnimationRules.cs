namespace Northwind;

public readonly record struct MenuFrameStep(uint Frame, byte LastTimer, bool Wrapped);

/// <summary>LE:1 18ED4..18F15. One source timer byte drives both directions.</summary>
public static class OriginalMenuAnimationRules
{
    public static MenuFrameStep Step(bool forward, uint frame, byte lastTimer, byte timer,
                                     uint limit, uint reset, byte delay)
    {
        if (unchecked((byte)(timer - lastTimer)) < delay)
            return new(frame, lastTimer, false);
        frame = forward ? unchecked(frame + 1) : unchecked(frame - 1);
        bool wrapped = forward ? frame >= limit : frame <= limit;
        return new(wrapped ? reset : frame, timer, wrapped);
    }
}
