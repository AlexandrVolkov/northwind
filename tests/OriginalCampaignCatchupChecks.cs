using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class CatchupReplay(List<byte> log) : ICampaignCatchupWorld
    {
        public void Casualties(uint loss, int army) { log.Add((byte)'L'); log.AddRange(BitConverter.GetBytes(loss)); log.AddRange(BitConverter.GetBytes((uint)(army * OriginalCampaignRegions.State))); }
        public void ResetMission() => log.Add((byte)'R');
        public void System(byte army, byte mode) { log.Add((byte)'Y'); log.Add(army); log.Add(mode); }
        public void RunWorld(uint updates) { log.Add((byte)'W'); log.AddRange(BitConverter.GetBytes(updates)); }
    }

    /// <summary>Replays tests/fixtures/campaign-catchup.json (tools/verify_campaign_catchup.py): LE:1 10E19 and 10ECE.</summary>
    private void OriginalCampaignCatchupChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/campaign-catchup.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, losses = 0;
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var d) ? d : -1;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int regions = c.GetProperty("regions").GetInt32(), armies = c.GetProperty("armies").GetInt32(); long seed = c.GetProperty("seed").GetInt64(); var v11 = c.GetProperty("v11"); var cur = c.GetProperty("current");
            var table = new byte[Math.Max(regions * OriginalCampaignRegions.Region, 0x20000)]; CampaignFill(seed, regions * OriginalCampaignRegions.Region).CopyTo(table, 0);
            for (int r = 0; r < regions; r++)
            {
                if (r == cur[0].GetInt32() + cur[2].GetInt32()) table[r * OriginalCampaignRegions.Region] |= (byte)c.GetProperty("flag").GetInt32();
                for (int a = 0; a < armies; a++) BitConverter.GetBytes((uint)v11[(r * 4 + a) % 16].GetInt64()).CopyTo(table, r * OriginalCampaignRegions.Region + 1 + a * OriginalCampaignRegions.Record + 0x11);
            }
            var states = CampaignFill(seed + 1, armies * OriginalCampaignRegions.State);
            for (int a = 0; a < armies; a++) BitConverter.GetBytes((uint)v11[(a * 5 + 3) % 16].GetInt64()).CopyTo(states, a * OriginalCampaignRegions.State + 0x19e9);
            var participants = CampaignFill(seed + 2, armies * OriginalCampaignRegions.Participant);
            uint budget = (uint)c.GetProperty("budget").GetInt64(), time = (uint)c.GetProperty("tick").GetInt64(); var log = new List<byte>(); var world = new CatchupReplay(log);
            if (c.GetProperty("fn").GetString() == "away")
                OriginalCampaignCatchup.Away(table, states, participants, armies, ref budget, ref time, (byte)cur[0].GetInt32(), 1, (byte)cur[2].GetInt32(), (byte)c.GetProperty("player").GetInt32(), world);
            else OriginalCampaignCatchup.Budget(ref budget, world);
            byte[] output = [.. participants, .. BitConverter.GetBytes(budget), .. BitConverter.GetBytes(time), .. log];
            digest.AppendData(output); if (log.Contains((byte)'L')) losses++;
            string hex = Convert.ToHexStringLower(SHA256.HashData(output))[..10];
            if (hex != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Catch-up: first mismatch {firstMismatch}; {losses} with casualties");
        var summary = root.GetProperty("results").GetProperty("catchup");
        Check(count == summary.GetProperty("case_count").GetInt32() && losses > 10, $"Campaign catch-up reference coverage ({count} cases, {losses} with casualties)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Campaign catch-up LE:1 10E19/10ECE match original x86");
    }
}
