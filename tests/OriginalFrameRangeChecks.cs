using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/frame-range.json (tools/verify_frame_range.py): the playback of a frame range, state 5 of LE:1 9210.</summary>
    private void OriginalFrameRangeChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/frame-range.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, finished = 0, backwards = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var r = new byte[0x40]; r[0x22] = (byte)c.GetProperty("first").GetInt32(); r[0x23] = (byte)c.GetProperty("last").GetInt32(); r[0x2d] = (byte)c.GetProperty("state").GetInt32();
            var template = new byte[0xab]; BitConverter.GetBytes((ushort)c.GetProperty("base").GetInt32()).CopyTo(template, 0x48);
            var frames = new List<byte>(); var log = new List<byte>(); bool playing = OriginalFrameRange.Begin(r, template);
            frames.AddRange([r[1], r[2]]);
            bool finishedAtStart = !playing; if (!playing) log.Add((byte)'F');
            for (int step = 0; step < c.GetProperty("steps").GetInt32() && playing; step++) { playing = OriginalFrameRange.Step(r, template); if (!playing) log.Add((byte)'F'); else frames.AddRange([r[1], r[2]]); }
            // the harness reads the frame after every call, also after the finishing one
            if (log.Count > 0 && !finishedAtStart) frames.AddRange([r[1], r[2]]);
            if (log.Count > 0) finished++; if (c.GetProperty("last").GetInt32() < c.GetProperty("first").GetInt32()) backwards++;
            byte[] output = [.. r, .. frames, .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; GD.Print($"RANGE MISMATCH {count}"); }
            count++;
        }
        GD.Print($"Range: first mismatch {firstMismatch}; finished {finished}, backwards {backwards}");
        var summary = root.GetProperty("results").GetProperty("range");
        Check(count == summary.GetProperty("case_count").GetInt32() && finished > 500 && backwards > 300, $"Frame range reference coverage ({count}: {finished} finished, {backwards} backwards)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Frame-range playback LE:1 9210 state 5 matches original x86");
    }
}
