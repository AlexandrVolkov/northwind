namespace Northwind;

public readonly record struct MenuPlaquePress(uint Frame, byte LastTimer, bool InvokeAction);

/// <summary>Pressed state of LE:1 18F16. Hover detection remains an external UI concern.</summary>
public static class OriginalMenuPlaqueRules
{
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
