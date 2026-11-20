namespace Northwind;

public readonly record struct MenuPlaqueHover(uint Frame, byte LastTimer, byte PressedState, bool InvokeAction);

public readonly record struct MenuPlaquePress(uint Frame, byte LastTimer, bool InvokeAction);

/// <summary>Pressed state of LE:1 18F16. Hover detection remains an external UI concern.</summary>
public static class OriginalMenuPlaqueRules
{
    /// <summary>AL=1 without a pressed latch. The hit-test result is supplied by the caller.</summary>
    public static MenuPlaqueHover HoverTick(uint baseFrame, byte[] sections, uint frame,
                                             byte previous, byte timer, bool inside,
                                             bool mouseDown, bool hotkeyDown)
    {
        if ((inside && mouseDown) || hotkeyDown)
            return new(frame, previous, 1, false);
        uint hoverStart = unchecked(baseFrame + sections[0]);
        MenuFrameStep step;
        if (inside)
            step = OriginalMenuAnimationRules.Step(true, frame, previous, timer,
                       unchecked(hoverStart + sections[1]), hoverStart, 3);
        else if (frame <= hoverStart)
            step = OriginalMenuAnimationRules.Step(true, frame, previous, timer,
                       hoverStart, baseFrame, 3);
        else
            step = OriginalMenuAnimationRules.Step(false, frame, previous, timer,
                       hoverStart, hoverStart, 3);
        return new(step.Frame, step.LastTimer, 0, false);
    }

    public static MenuPlaquePress PressedTick(uint baseFrame, byte[] sections, uint frame,
                                               byte previous, byte timer)
    {
        uint pressed = unchecked(baseFrame + sections[0] + sections[1] + sections[2]);
        uint end = unchecked(pressed + sections[3]);
        MenuFrameStep step = OriginalMenuAnimationRules.Step(true, frame, previous, timer,
                                                              end, pressed, 3);
        return new(step.Frame, step.LastTimer, step.Wrapped);
    }
}
