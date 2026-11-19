namespace Northwind;

/// <summary>FX selection 1418D, PCM gain 13F50, amplitude preprocessing 140B0.</summary>
public static class OriginalSoundRules
{
    public const int SampleRate = 11025;

    public static int Variant(byte randomByte, int count)
    {
        if (count is < 1 or > 255) throw new ArgumentOutOfRangeException(nameof(count));
        return (randomByte * count) >> 8;
    }

    public static byte ScaleSample(byte sample, byte gain) =>
        (byte)Math.Clamp(128 + (sample - 128) * gain / 128, 0, 255);

    public static byte MeanLevel(ReadOnlySpan<byte> pcm)
    {
        if (pcm.IsEmpty) throw new ArgumentException("Native level preprocessing requires a nonempty sample", nameof(pcm));
        ulong sum = 0;
        foreach (byte sample in pcm) sum += (uint)((sample >= 128 ? sample : sample ^ 255) & 127);
        return (byte)(sum / (ulong)pcm.Length);
    }
}
