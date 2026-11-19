using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private void OriginalSoundChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/sound.json");
        var root = fixture.RootElement;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        foreach (var variants in root.GetProperty("selection").GetProperty("variant_counts").EnumerateArray())
        for (int randomByte = 0; randomByte < 256; randomByte++)
        {
            hash.AppendData([(byte)OriginalSoundRules.Variant((byte)randomByte, variants.GetInt32())]); count++;
        }
        CheckHash("selection", count, "FX variant selection matches LE:1 1418D for all random bytes");
        count = 0;
        for (int sample = 0; sample < 256; sample++)
        for (int gain = 0; gain < 256; gain++)
        {
            hash.AppendData([OriginalSoundRules.ScaleSample((byte)sample, (byte)gain)]); count++;
        }
        CheckHash("gain", count, "Unsigned PCM gain matches every native 13F50 input pair");
        count = 0;
        foreach (var length in root.GetProperty("mean_level").GetProperty("lengths").EnumerateArray())
        foreach (var kind in root.GetProperty("mean_level").GetProperty("kinds").EnumerateArray())
        {
            byte[] pcm = Enumerable.Range(0, length.GetInt32()).Select(i => kind.GetString() switch
            { "silence" => (byte)128, "zero" => (byte)0, "full" => (byte)255, "ramp" => (byte)(i % 256), _ => throw new InvalidDataException() }).ToArray();
            hash.AppendData([OriginalSoundRules.MeanLevel(pcm)]); count++;
        }
        CheckHash("mean_level", count, "Amplitude cache matches original 140B0, including folded silence");
        foreach (var defaults in root.GetProperty("driver_defaults").EnumerateArray())
            Check(defaults.GetProperty("sample_rate").GetInt32() == OriginalSoundRules.SampleRate, "All three native driver paths set 11025 Hz");

        var catalog = OriginalSoundBank.Load();
        Check(catalog is { FormatVersion: 1, SampleRate: 11025, Channels: 1, BitsPerSample: 8, PcmEncoding: "unsigned" }, "Manifest preserves source PCM format");
        foreach (var bank in root.GetProperty("banks").EnumerateArray())
        {
            var source = catalog.Banks.Single(b => b.Source == bank.GetProperty("source").GetString());
            Check(source.SourceSha256 == bank.GetProperty("source_sha256").GetString(), "Prepared bank matches oracle source hash");
            count = 0;
            foreach (var sound in source.Sounds)
            for (int randomByte = 0; randomByte < 256; randomByte++)
            {
                var selected = catalog.Choose(source.Source, sound.Sound, (byte)randomByte)!;
                byte[] bytes = new byte[8];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)selected.PcmOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), selected.RawSizeWord);
                hash.AppendData(bytes); count++;
            }
            Check(count == bank.GetProperty("case_count").GetInt32() && Convert.ToHexStringLower(hash.GetHashAndReset()) == bank.GetProperty("sha256").GetString(),
                $"{source.Source}: every prepared selection points to the native PCM and packed size");
            Check(catalog.Choose(source.Source, -1, 0) == null && catalog.Choose(source.Source, source.SoundCount, 0) == null, "Invalid sound indices do not play a different sample");
        }

        void CheckHash(string section, int cases, string message)
        {
            var expected = root.GetProperty(section);
            Check(cases == expected.GetProperty("case_count").GetInt32() && Convert.ToHexStringLower(hash.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), message);
        }
    }

    private async Task SoundChecks()
    {
        OriginalSoundChecks();
        var catalog = OriginalSoundBank.Load();
        var player = new AudioStreamPlayer(); AddChild(player);
        int total = 0;
        foreach (var bank in catalog.Banks)
        {
            Check(bank.SoundCount == bank.Sounds.Length && bank.SampleCount == bank.Sounds.Sum(s => s.Variants.Length), "Bank counts agree with manifest records");
            foreach (var sample in bank.Sounds.SelectMany(s => s.Variants))
            {
                using var file = Godot.FileAccess.Open(OriginalSoundBank.Root + sample.File, Godot.FileAccess.ModeFlags.Read);
                var bytes = file.GetBuffer((long)file.GetLength());
                Check(Convert.ToHexStringLower(SHA256.HashData(bytes)) == sample.WavSha256, "Bundled WAV hash matches converter output");
                using var stream = OriginalSoundBank.Stream(sample);
                Check(stream.Format == AudioStreamWav.FormatEnum.Format8Bits, "Godot imports original PCM without lossy QOA/ADPCM compression");
                byte[] unsignedPcm = stream.Data.Select(b => (byte)(b ^ 128)).ToArray();
                Check(Convert.ToHexStringLower(SHA256.HashData(unsignedPcm)) == sample.PcmSha256, "Godot PCM equals every original sample after signed 8-bit conversion");
                Check(!stream.Stereo && stream.MixRate == catalog.SampleRate && Math.Abs(stream.GetLength() - sample.DurationSeconds) < 1.0 / catalog.SampleRate,
                    $"{sample.File}: Godot loads original duration/rate/mono layout");
                player.Stream = stream; player.Play();
                Check(player.Playing, $"{sample.File}: AudioStreamPlayer accepts prepared WAV");
                await ToSignal(GetTree().CreateTimer(.015), SceneTreeTimer.SignalName.Timeout);
                player.Stop(); player.Stream = null; total++;
            }
        }
        Check(total == 166, "All 166 original FX variants are bundled");
        using (var selection = OriginalSoundBank.Stream(catalog.Choose("WORK.FX", 0, 0)!))
        {
            player.Stream = selection; player.Play();
            await ToSignal(GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
            Check(player.GetPlaybackPosition() > 0, "Audio mixing advances through the device");
            player.Stop(); player.Stream = null;
            Check(!player.Playing, "Stopping the preview releases playback");
        }
        RemoveChild(player); player.Free();
        // Stop queues playback retirement on the audio thread; let its next mix complete before engine shutdown.
        await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
    }
}
