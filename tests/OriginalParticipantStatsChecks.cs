using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class StatsReplay(JsonElement c, List<byte> log) : IParticipantStatsWorld
    {
        public (ushort Center, ushort Size) FitTerritory(uint center, uint size)
        {
            log.Add((byte)'A'); log.AddRange(BitConverter.GetBytes(center)); log.AddRange(BitConverter.GetBytes(size));
            var city = c.GetProperty("city");
            return ((ushort)(city[0].GetInt32() | (city[1].GetInt32() << 8)), (ushort)(city[2].GetInt32() | (city[3].GetInt32() << 8)));
        }
        public void Notice() => log.Add((byte)'E');
        public void Pulse() => log.Add((byte)'G');
    }

    /// <summary>Replays tests/fixtures/participant-stats.json (tools/verify_participant_stats.py): LE:1 D3E5, D40C and D453.</summary>
    private void OriginalParticipantStatsChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/participant-stats.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, boxes = 0, grown = 0;
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var d) ? d : -1;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var templates = c.GetProperty("templates").EnumerateArray().Select(t =>
            {
                var raw = new byte[0xab]; BitConverter.GetBytes((uint)t.GetProperty("flags").GetInt64()).CopyTo(raw, 0x40); raw[0x46] = (byte)t.GetProperty("minimum").GetInt32();
                BitConverter.GetBytes((uint)t.GetProperty("v78").GetInt64()).CopyTo(raw, 0x78); BitConverter.GetBytes((uint)t.GetProperty("v88").GetInt64()).CopyTo(raw, 0x88); return raw;
            }).ToArray();
            int at = 0x100; var parts = new List<byte[]>(); var units = new List<List<(byte[], byte[])>>();
            foreach (var p in c.GetProperty("participants").EnumerateArray())
            {
                var part = new byte[OriginalParticipantStats.Participant];
                BitConverter.GetBytes((uint)p.GetProperty("strength").GetInt64()).CopyTo(part, 0x28); BitConverter.GetBytes((uint)p.GetProperty("weight").GetInt64()).CopyTo(part, 0x2c);
                BitConverter.GetBytes((uint)p.GetProperty("growth").GetInt64()).CopyTo(part, 0x24); BitConverter.GetBytes((uint)p.GetProperty("limit").GetInt64()).CopyTo(part, 0x30);
                int unitCount = p.GetProperty("units").GetArrayLength(); BitConverter.GetBytes((uint)at).CopyTo(part, 0x0e); at += 0x40 * unitCount; BitConverter.GetBytes((uint)at).CopyTo(part, 0x09);
                parts.Add(part);
                units.Add(p.GetProperty("units").EnumerateArray().Select(u =>
                {
                    var rec = new byte[0x40]; rec[0x0e] = (byte)u.GetProperty("tmpl").GetInt32(); rec[0x19] = (byte)u.GetProperty("cond").GetInt32();
                    BitConverter.GetBytes((uint)u.GetProperty("x").GetInt32()).CopyTo(rec, 4); BitConverter.GetBytes((uint)u.GetProperty("y").GetInt32()).CopyTo(rec, 8); return (rec, templates[rec[0x0e]]);
                }).ToList());
            }
            var log = new List<byte>(); var world = new StatsReplay(c, log); var box = new OriginalParticipantStats.Box { MinX = 0, MinY = 0 };
            // the globals 1C6E.. are zero at the start of a case
            var array = parts.ToArray(); uint tick = (uint)c.GetProperty("tick").GetInt64();
            Func<int, IEnumerable<(byte[], byte[])>> objects = i => units[i];
            switch (c.GetProperty("fn").GetString())
            {
                case "all": OriginalParticipantStats.All(array, c.GetProperty("campaign").GetInt32() != 0, (byte)c.GetProperty("g19e9").GetInt32(), tick, objects, box, world); break;
                case "recompute": OriginalParticipantStats.Recompute(array, objects, box, world); break;
                default: OriginalParticipantStats.Grow(array, tick); break;
            }
            byte[] output = [.. array.SelectMany(p => p).ToArray(), .. BitConverter.GetBytes(box.MaxX), .. BitConverter.GetBytes(box.MaxY), .. BitConverter.GetBytes(box.MinX), .. BitConverter.GetBytes(box.MinY), .. log];
            digest.AppendData(output); if (log.Contains((byte)'A')) boxes++; if (c.GetProperty("fn").GetString() != "recompute") grown++;
            string hex = Convert.ToHexStringLower(SHA256.HashData(output))[..10];
            if (hex != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Stats: first mismatch {firstMismatch}; {boxes} territories");
        var summary = root.GetProperty("results").GetProperty("stats");
        Check(count == summary.GetProperty("case_count").GetInt32() && boxes > 50, $"Participant statistics reference coverage ({count} cases, {boxes} territories)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Participant statistics LE:1 D3E5/D40C/D453 match original x86");
    }
}
