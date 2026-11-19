using Godot;

namespace Northwind;

/// <summary>Prepared FX catalog. Caller supplies the original random byte and triggers playback.</summary>
public sealed class OriginalSoundBank
{
    public const string Root = "res://assets/imported/sound/";
    public int FormatVersion { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitsPerSample { get; set; }
    public string PcmEncoding { get; set; } = "";
    public SoundBankEntry[] Banks { get; set; } = [];

    public static OriginalSoundBank Load() => GameData.Read<OriginalSoundBank>(Root + "manifest.json");

    public SoundVariant? Choose(string bank, int sound, byte randomByte)
    {
        var source = Banks.First(b => string.Equals(b.Source, bank, StringComparison.OrdinalIgnoreCase));
        if (sound < 0 || sound >= source.Sounds.Length) return null;
        var variants = source.Sounds[sound].Variants;
        return variants[OriginalSoundRules.Variant(randomByte, variants.Length)];
    }

    public static AudioStreamWav Stream(SoundVariant sample) => GD.Load<AudioStreamWav>(Root + sample.File)
        ?? throw new InvalidOperationException($"Missing prepared sound: {sample.File}");
}

public sealed class SoundBankEntry
{
    public string Source { get; set; } = "";
    public string SourceSha256 { get; set; } = "";
    public int SourceBytes { get; set; }
    public int SoundCount { get; set; }
    public int SampleCount { get; set; }
    public SoundEntry[] Sounds { get; set; } = [];
}

public sealed class SoundEntry
{
    public int Sound { get; set; }
    public int GroupOffset { get; set; }
    public SoundVariant[] Variants { get; set; } = [];
}

public sealed class SoundVariant
{
    public int Variant { get; set; }
    public string File { get; set; } = "";
    public int HeaderOffset { get; set; }
    public int PcmOffset { get; set; }
    public uint RawSizeWord { get; set; }
    public byte RawHighByte { get; set; }
    public byte? MeanLevel { get; set; }
    public int Frames { get; set; }
    public double DurationSeconds { get; set; }
    public string PcmSha256 { get; set; } = "";
    public string WavSha256 { get; set; } = "";
}
