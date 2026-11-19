namespace Northwind;

/// <summary>Menu loop dispatch 1CBF7 and mono channel end 14449. Scheduling stays with the caller.</summary>
public static class OriginalAudioSceneRules
{
    public readonly record struct MenuRequest(uint LastSound, uint? StartSound);

    public static MenuRequest Menu(uint previous, uint requested) =>
        new(requested, previous == requested ? null : requested);

    public static void EndChannel(Span<byte> channel)
    {
        if (channel.Length != 32) throw new ArgumentException("Expected native 20h-byte sound channel", nameof(channel));
        if (channel[1] == 1) { channel[0] = 0; return; }
        if (channel[1] > 1) channel[1]--;
        channel.Slice(6, 4).CopyTo(channel.Slice(14, 4));
        channel.Slice(2, 4).CopyTo(channel.Slice(10, 4));
    }
}
