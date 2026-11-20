namespace Northwind;

public readonly record struct MenuWidgetTick(uint Frame, byte LastTimer, byte Phase);

/// <summary>Native idle callbacks 19063/190ED; renderer and phase-entry side effects are external.</summary>
public static class OriginalMenuWidgetRules
{
    public static MenuWidgetTick Tick(byte flags, uint baseFrame, byte count, uint frame,
                                      byte previous, byte timer, byte phase, bool owl)
    {
        if (phase >= 8 || (flags & (1 << phase)) == 0)
            return new(frame, previous, phase);
        MenuFrameStep step = OriginalMenuAnimationRules.Step(true, frame, previous, timer,
                                                              unchecked(baseFrame + count), baseFrame, 3);
        if (owl && step.Wrapped) phase = 2;
        return new(step.Frame, step.LastTimer, phase);
    }
}
