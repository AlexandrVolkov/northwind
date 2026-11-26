using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class MoveReplay(JsonElement c, List<byte> log) : IMoveWorld
    {
        public uint LimitX => (uint)c.GetProperty("limit")[0].GetInt32();
        public uint LimitY => (uint)c.GetProperty("limit")[1].GetInt32();
        public uint Angle(uint goalX, uint x, uint goalY, uint y)
        { log.Add((byte)'A'); foreach (var v in new[] { goalX, x, goalY, y }) log.AddRange(BitConverter.GetBytes(v)); return (uint)c.GetProperty("angle").GetInt32(); }
        public void Occupancy(uint reference) { log.Add((byte)'O'); log.AddRange(BitConverter.GetBytes(reference)); }
        public bool Free(byte army, byte template, byte layer, uint x, uint y)
        { log.Add((byte)'P'); log.Add(template); log.Add(army); log.Add(layer); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y)); return c.GetProperty("free_zf").GetInt32() != 0; }
        public void RevealFog() => log.Add((byte)'V');
        public void Finish() => log.Add((byte)'R');
    }

    /// <summary>Replays tests/fixtures/unit-move.json (tools/verify_unit_move.py): the move step LE:1 12930..12AE1.</summary>
    private void OriginalUnitMoveChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/unit-move.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, moved = 0;
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var d) ? d : -1;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var r = new byte[0x40]; r[0] = (byte)c.GetProperty("army").GetInt32(); r[0x24] = (byte)c.GetProperty("f24").GetInt32(); r[0x25] = (byte)c.GetProperty("f25").GetInt32(); r[0x26] = (byte)c.GetProperty("f26").GetInt32();
            r[0x27] = (byte)c.GetProperty("f27").GetInt32(); r[0x2c] = (byte)c.GetProperty("f2c").GetInt32(); r[0x2e] = (byte)c.GetProperty("f2e").GetInt32(); r[0x31] = (byte)c.GetProperty("f31").GetInt32();
            r[0x0d] = (byte)c.GetProperty("f0d").GetInt32(); r[0x0e] = (byte)c.GetProperty("f0e").GetInt32();
            BitConverter.GetBytes((uint)c.GetProperty("x").GetInt32()).CopyTo(r, 4); BitConverter.GetBytes((uint)c.GetProperty("y").GetInt32()).CopyTo(r, 8);
            var tp = c.GetProperty("template"); var t = new byte[0xab];
            t[0x4a] = (byte)tp.GetProperty("n4a").GetInt32(); t[0x4b] = (byte)tp.GetProperty("n4b").GetInt32(); t[0x4f] = (byte)tp.GetProperty("n4f").GetInt32(); t[0x58] = (byte)tp.GetProperty("n58").GetInt32();
            BitConverter.GetBytes((ushort)tp.GetProperty("base").GetInt32()).CopyTo(t, 0x48);
            var log = new List<byte>(); var move = new OriginalUnitMove(new MoveReplay(c, log));
            byte flags = move.Step(r, t, (byte)c.GetProperty("al").GetInt32(), (byte)c.GetProperty("ah").GetInt32(), (byte)c.GetProperty("bl").GetInt32(), 0x800);
            byte[] output = [.. r, move.Flags, flags, .. log];
            digest.AppendData(output);
            string hex = Convert.ToHexStringLower(SHA256.HashData(output))[..10];
            if (log.Contains((byte)'V')) moved++;
            if (hex != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Move: first mismatch {firstMismatch}; {moved} full steps");
        var summary = root.GetProperty("results").GetProperty("move");
        Check(count == summary.GetProperty("case_count").GetInt32() && moved > 20, $"Unit move reference coverage ({count} cases, {moved} steps)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Unit move step LE:1 12930 matches original x86");
    }
}
