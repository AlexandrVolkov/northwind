using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class BridgeReplay(JsonElement c, List<byte> log, byte[] blocker) : IBridgeBuilderWorld
    {
        public uint Reference => 0x800;
        public (bool Free, bool Blocked, byte[] Blocker) Place(byte template, byte army, byte mode, uint x, uint y)
        {
            log.Add((byte)'P'); log.AddRange([template, army, mode]); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y));
            return (c.GetProperty("zf").GetInt32() != 0, c.GetProperty("cf").GetInt32() != 0, blocker);
        }
        public void Occupancy(uint reference) { log.Add((byte)'O'); log.AddRange(BitConverter.GetBytes(reference)); }
        public bool SearchEnemy(byte mode) { log.Add((byte)'S'); log.Add(mode); return c.GetProperty("search_zf").GetInt32() == 0; }
    }

    /// <summary>Replays tests/fixtures/bridge-builder.json (tools/verify_bridge_builder.py): LE:1 D5B1, D61D and D689.</summary>
    private void OriginalBridgeBuilderChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/bridge-builder.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, done = 0, hurt = 0;
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var d) ? d : -1;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var r = new byte[0x40]; r[0] = (byte)c.GetProperty("army").GetInt32(); r[0x0c] = (byte)c.GetProperty("f0c").GetInt32(); r[0x0d] = (byte)c.GetProperty("f0d").GetInt32(); r[0x0e] = (byte)c.GetProperty("f0e").GetInt32();
            r[0x0f] = (byte)c.GetProperty("f0f").GetInt32(); r[0x15] = (byte)c.GetProperty("f15").GetInt32(); r[0x22] = (byte)c.GetProperty("f22").GetInt32(); r[0x23] = (byte)c.GetProperty("f23").GetInt32();
            BitConverter.GetBytes((ushort)c.GetProperty("f17").GetInt32()).CopyTo(r, 0x17); BitConverter.GetBytes((uint)c.GetProperty("x").GetInt32()).CopyTo(r, 4); BitConverter.GetBytes((uint)c.GetProperty("y").GetInt32()).CopyTo(r, 8);
            var blocker = new byte[0x40]; blocker[0x19] = (byte)c.GetProperty("blocker").GetInt32(); var log = new List<byte>(); var world = new BridgeReplay(c, log, blocker);
            byte bl = c.GetProperty("fn").GetString() switch
            {
                "raise" => OriginalBridgeBuilder.Raise(r, world),
                "lower" => OriginalBridgeBuilder.Lower(r, world),
                _ => OriginalBridgeBuilder.Step(r, world, () => world.SearchEnemy(0)),
            };
            byte[] output = [.. r, .. blocker, bl, .. log];
            digest.AppendData(output); if ((bl & 0x40) != 0) done++; if (blocker[0x19] != (byte)c.GetProperty("blocker").GetInt32()) hurt++;
            string hex = Convert.ToHexStringLower(SHA256.HashData(output))[..10];
            if (hex != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Bridge: first mismatch {firstMismatch}; {done} finished, {hurt} hurt");
        var summary = root.GetProperty("results").GetProperty("bridge");
        Check(count == summary.GetProperty("case_count").GetInt32() && done > 50 && hurt > 10, $"Bridge builder reference coverage ({count} cases, {done} finished, {hurt} hurt)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Bridge builder actions LE:1 D5B1/D61D/D689 match original x86");
    }
}
