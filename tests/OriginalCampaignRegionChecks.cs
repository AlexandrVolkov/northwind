using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class RegionReplay(JsonElement c, List<byte> log) : ICampaignRegionWorld
    {
        public (uint X, uint Y)? FindObject(byte army, uint mask, uint value)
        {
            log.Add((byte)'S'); log.Add(army); log.AddRange(BitConverter.GetBytes(mask)); log.AddRange(BitConverter.GetBytes(value));
            return c.GetProperty("found").GetInt32() != 0 ? ((uint)c.GetProperty("obj")[0].GetInt32(), (uint)c.GetProperty("obj")[1].GetInt32()) : null;
        }
    }

    // the generator of tools/verify_campaign_regions.py
    private static byte[] CampaignFill(long seed, int count)
    {
        uint x = (uint)seed; var bytes = new byte[count];
        for (int i = 0; i < count; i++) { x = unchecked(x * 1103515245 + 12345); byte b = (byte)(x >> 16); bytes[i] = b < 192 ? (byte)(b & 7) : b; }
        return bytes;
    }

    /// <summary>Replays tests/fixtures/campaign-regions.json (tools/verify_campaign_regions.py): LE:1 10BD4, 10CA0 and 110C8.</summary>
    private void OriginalCampaignRegionChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/campaign-regions.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, grown = 0, won = 0; var functions = new HashSet<string>();
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var d) ? d : -1;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int regions = c.GetProperty("regions").GetInt32(), armies = c.GetProperty("armies").GetInt32(); string fn = c.GetProperty("fn").GetString()!; long seed = c.GetProperty("seed").GetInt64();
            var table = CampaignFill(seed, regions * OriginalCampaignRegions.Region); var v17 = c.GetProperty("v17"); var v11 = c.GetProperty("v11");
            for (int r = 0; r < regions; r++)
                for (int a = 0; a < armies; a++)
                {
                    int rec = r * OriginalCampaignRegions.Region + 1 + a * OriginalCampaignRegions.Record; table[rec] = (byte)a;
                    BitConverter.GetBytes((uint)v17[r * 4 + a].GetInt64()).CopyTo(table, rec + 0x17); BitConverter.GetBytes((uint)v11[r * 4 + a].GetInt64()).CopyTo(table, rec + 0x11);
                    if (c.GetProperty("force").GetInt32() != 0 && fn == "end") { table[rec + 0x59] = 0; table[rec + 1 + c.GetProperty("player").GetInt32()] = 3; }
                    if (c.GetProperty("force").GetInt32() != 0 && fn == "end" && a == armies - 1) table[r * OriginalCampaignRegions.Region] |= 1;
                }
            byte state = (byte)seed; uint counter = (uint)c.GetProperty("counter").GetInt64(); var log = new List<byte>(); var cur = c.GetProperty("current");
            switch (fn)
            {
                case "tick": OriginalCampaignRegions.Tick(table, regions, armies, ref counter, c.GetProperty("campaign").GetInt32() != 0); break;
                case "snapshot":
                    {
                        // the harness region table is larger than the cases' (the index of the current region can exceed it): the table is padded as the original memory is
                        var padded = new byte[Math.Max(table.Length, 0x20000)]; table.CopyTo(padded, 0);
                        OriginalCampaignRegions.Snapshot(padded, CampaignFill(seed + 1, armies * OriginalCampaignRegions.State), CampaignFill(seed + 2, armies * OriginalCampaignRegions.Participant), armies,
                            (byte)cur[0].GetInt32(), (byte)cur[1].GetInt32(), (byte)cur[2].GetInt32(), new RegionReplay(c, log));
                        table = padded; break;
                    }
                default: state = OriginalCampaignRegions.End(table, regions, armies, (byte)c.GetProperty("player").GetInt32(), state); break;
            }
            // the harness compares exactly the bytes of the case's table
            byte[] output = [.. table.AsSpan(0, regions * OriginalCampaignRegions.Region).ToArray(), state, .. BitConverter.GetBytes(counter), .. log];
            digest.AppendData(output); functions.Add(fn);
            string hex = Convert.ToHexStringLower(SHA256.HashData(output))[..10];
            if (fn == "tick" && (counter & 0x3ff) == 0) grown++; if (fn == "end" && state == 3) won++;
            if (hex != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Regions: first mismatch {firstMismatch}; {grown} ticks of growth, {won} wins");
        var summary = root.GetProperty("results").GetProperty("regions");
        Check(count == summary.GetProperty("case_count").GetInt32() && functions.Count == 3 && grown > 10, $"Campaign regions reference coverage ({count} cases, {grown} growth ticks, {won} wins)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Campaign regions LE:1 10BD4/10CA0/110C8 match original x86");
    }
}
