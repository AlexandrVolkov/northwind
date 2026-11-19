using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalMusicChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/music.json");
        var root = fixture.RootElement;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        var menu = root.GetProperty("menu");
        foreach (var previous in menu.GetProperty("previous").EnumerateArray())
        foreach (var requested in menu.GetProperty("requested").EnumerateArray())
        {
            var command = OriginalAudioSceneRules.Menu(previous.GetUInt32(), requested.GetUInt32());
            byte[] bytes = new byte[5]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, command.LastSound);
            bytes[4] = command.StartSound.HasValue ? (byte)1 : (byte)0; hash.AppendData(bytes);
            if (command.StartSound is uint sound)
            {
                bytes = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, sound);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 1); hash.AppendData(bytes);
            }
            count++;
        }
        Check(count == menu.GetProperty("case_count").GetInt32() && Convert.ToHexStringLower(hash.GetHashAndReset()) == menu.GetProperty("sha256").GetString(),
            "Menu loops, zero sound and repeated requests match native 1CBF7");
        using var scenes = GameData.Read<JsonDocument>("res://data/original_audio_scenes.json");
        count = 0;
        var catalog = OriginalSoundBank.Load();
        foreach (var channel in scenes.RootElement.GetProperty("ambient_channels").EnumerateArray())
        {
            byte[] bytes = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, channel.GetProperty("sound").GetUInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), channel.GetProperty("mixer_mode").GetUInt32());
            hash.AppendData(bytes); count++;
            Check(channel.GetProperty("loop_forever").GetBoolean() && catalog.Choose(channel.GetProperty("bank").GetString()!, channel.GetProperty("sound").GetInt32(), 0) != null,
                "Every ambient loop refers to bundled PCM");
        }
        var ambient = root.GetProperty("ambient_start");
        Check(count == ambient.GetProperty("case_count").GetInt32() && Convert.ToHexStringLower(hash.GetHashAndReset()) == ambient.GetProperty("sha256").GetString(),
            "All six ambient starts match original 148DD order/mode");
        count = 0;
        foreach (var repeat in root.GetProperty("repeat_end").GetProperty("repeats").EnumerateArray())
        {
            byte[] raw = Enumerable.Range(0, 32).Select(i => (byte)((i * 13 + 7) % 256)).ToArray();
            raw[0] = 1; raw[1] = repeat.GetByte();
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(2), 100);
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(6), 0xabcdef);
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(10), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(14), 0xabcdef + 100);
            OriginalAudioSceneRules.EndChannel(raw); hash.AppendData(raw); count++;
        }
        var repeats = root.GetProperty("repeat_end");
        Check(count == repeats.GetProperty("case_count").GetInt32() && Convert.ToHexStringLower(hash.GetHashAndReset()) == repeats.GetProperty("sha256").GetString(),
            "Finite/infinite repeats preserve the full native channel record at 14449");
    }
}
