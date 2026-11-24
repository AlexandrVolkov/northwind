using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/campaign-losses.json (tools/verify_campaign_losses.py): LE:1 10FDE.</summary>
    private void OriginalCampaignLossChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/campaign-losses.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, weakened = 0;
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var d) ? d : -1;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var state = new byte[0x1a35]; BitConverter.GetBytes((uint)c.GetProperty("population").GetInt64()).CopyTo(state, 0x19e9); state[0x5c] = (byte)c.GetProperty("strength").GetInt32();
            var templates = c.GetProperty("templates").EnumerateArray().Select(t =>
            {
                var raw = new byte[0xab]; BitConverter.GetBytes((uint)t.GetProperty("flags").GetInt64()).CopyTo(raw, 0x40); raw[0x46] = (byte)t.GetProperty("minimum").GetInt32(); raw[0x76] = (byte)t.GetProperty("weight").GetInt32(); return raw;
            }).ToArray();
            var units = c.GetProperty("units").EnumerateArray().Select(u => { var rec = new byte[0x40]; rec[0x0e] = (byte)u.GetProperty("tmpl").GetInt32(); rec[0x10] = (byte)u.GetProperty("link").GetInt32(); rec[0x19] = (byte)u.GetProperty("cond").GetInt32(); return rec; }).ToList();
            var before = units.Select(u => u[0x19]).ToArray();
            OriginalCampaignLosses.Apply(state, (uint)c.GetProperty("loss").GetInt64(), units.Select(u => (u, templates[u[0x0e]])));
            byte[] output = [.. units.SelectMany(u => u).ToArray(), .. (units.Count == 0 ? new byte[0x40] : []), .. state.AsSpan(0x19e9, 4).ToArray()];
            digest.AppendData(output);
            for (int i = 0; i < units.Count; i++) if (units[i][0x19] != before[i]) { weakened++; break; }
            string hex = Convert.ToHexStringLower(SHA256.HashData(output))[..10];
            if (hex != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Losses: first mismatch {firstMismatch}; {weakened} cases weakened units");
        var summary = root.GetProperty("results").GetProperty("losses");
        Check(count == summary.GetProperty("case_count").GetInt32() && weakened > 100, $"Campaign losses reference coverage ({count} cases, {weakened} weakening)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Campaign casualties LE:1 10FDE match original x86");
    }
}
