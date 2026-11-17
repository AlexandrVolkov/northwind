namespace Northwind;

public readonly record struct LumbermillProcessing(byte Countdown, byte QueuedLoads, uint Stock, uint Statistics);
public readonly record struct TimberHandoff(ushort MillFlags, byte QueuedLoads, ushort WorkerFlags, byte Cargo, bool Accepted);

/// <summary>Two bounded ARMY__02.WRK lumbermill operations; timing and worker routing are external.</summary>
public static class OriginalLumbermillRules
{
    // WRK 3E4..412, including real FS:6D and FS:1B7 calls in the reference execution.
    // Credits owner+1C and statistics+1A10 by ten when a queued load's countdown expires.
    public static LumbermillProcessing Process(LumbermillProcessing state, byte reset)
    {
        if (state.QueuedLoads == 0) return state;
        byte countdown = unchecked((byte)(state.Countdown - 1));
        if (countdown != 0) return state with { Countdown = countdown };
        return new(reset, unchecked((byte)(state.QueuedLoads - 1)), unchecked(state.Stock + 10), unchecked(state.Statistics + 10));
    }

    // WRK 41C..44D, entered only after the caller located an interacting worker.
    public static TimberHandoff Receive(ushort millFlags, byte queuedLoads, ushort workerFlags, byte cargo, uint workerTemplateFlags)
    {
        bool accepted = (millFlags & 0x40) != 0 && (workerTemplateFlags & 0x20000) != 0 && cargo == 3;
        if (accepted) { workerFlags &= 0xffbf; cargo = 0; queuedLoads = unchecked((byte)(queuedLoads + 1)); }
        else workerFlags |= 0x20;
        return new((ushort)(millFlags & 0xffbf), queuedLoads, workerFlags, cargo, accepted);
    }
}
