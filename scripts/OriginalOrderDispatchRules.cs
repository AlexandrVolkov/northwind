namespace Northwind;

public readonly record struct OrderButtonActivation(byte QueuedCommand, byte Mode, int OriginalTarget);

/// <summary>LE:1 61C3..622F, entered after a command button was clicked. Selection and event filtering
/// happen before this boundary; destination handlers are reported, not executed by this helper.</summary>
public static class OriginalOrderDispatchRules
{
    public static int Target(byte mode) => mode switch
    {
        0 => 0x622f,
        1 => 0x4d41,
        2 => 0x4e27,
        3 => 0x4fce,
        4 => 0x50e5,
        5 => 0x51ac,
        6 => 0x5297,
        7 => 0x538d,
        8 => 0x53c4,
        9 => 0x54b4,
        11 => 0x5593,
        _ => 0,
    };

    public static OrderButtonActivation Activate(Span<byte> widget, byte mode)
    {
        // The earlier port already verifies 61C7..61D3; reuse its six-slot encoding.
        byte command = OriginalBuildingLifecycle.QueueDirectCommand(0, widget[2]);
        widget[0x1a] = 1;
        widget[0x1b] = mode;
        return new(command, mode, Target(mode));
    }
}
