namespace Northwind;

public readonly record struct MenuHourglassTick(uint Frame, byte LastTimer, byte LaunchFlag,
                                                ushort ScanCode, uint MouseButtons);

/// <summary>LE:1 19185..1923A, AL=1 only; the subsequent 1CC1B is external.</summary>
public static class OriginalMenuHourglassRules
{
    public static MenuHourglassTick Tick(byte flags, byte phase, uint baseFrame, byte count,
                                         uint frame, byte previous, byte timer,
                                         ushort scanCode, uint mouseButtons)
    {
        if (phase >= 8 || (flags & (1 << phase)) == 0)
            return new(baseFrame, previous, 0, scanCode, mouseButtons);
        MenuFrameStep step = OriginalMenuAnimationRules.Step(true, frame, previous, timer,
                                                              unchecked(baseFrame + count), baseFrame, 5);
        if (!step.Wrapped)
            return new(step.Frame, step.LastTimer, 0, scanCode, mouseButtons);
        return new(unchecked(baseFrame + count - 1u), step.LastTimer, 1, 0, 0);
    }
}
