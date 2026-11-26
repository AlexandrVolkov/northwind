using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class VolcanoReplay(List<byte> log) : IVolcanoWorld
    {
        public uint A, B; public int Effects;
        public uint RandomA() => A;
        public uint RandomB() => B;
        public void Effect(byte army, byte type, byte layer, uint x, uint y)
        {
            Effects++; log.Add((byte)'E'); log.Add(army); log.Add(type); log.Add(layer); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y));
        }
    }

    /// <summary>Replays tests/fixtures/volcano.json (tools/verify_volcano.py): the volcano ARMY__04.WRK 0..9F.</summary>
    private void OriginalVolcanoChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/volcano.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, effects = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var log = new List<byte>(); var world = new VolcanoReplay(log);
            var record = CombatUnit(0, 0, 100, 0, c.GetProperty("x").GetUInt32(), c.GetProperty("y").GetUInt32(), 1);
            record[0] = (byte)c.GetProperty("army").GetInt32(); record[0x15] = (byte)c.GetProperty("state").GetInt32();
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x22), c.GetProperty("count").GetUInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), c.GetProperty("x").GetUInt32()); BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), c.GetProperty("y").GetUInt32());
            var ticks = c.GetProperty("ticks");
            if (c.GetProperty("init").GetInt32() != 0) { world.A = ticks[0].GetProperty("a").GetUInt32(); world.B = 0; OriginalVolcano.Script(world, 0, record); log.Add((byte)'I'); }
            foreach (var t in ticks.EnumerateArray())
            {
                world.A = t.GetProperty("a").GetUInt32(); world.B = t.GetProperty("b").GetUInt32();
                OriginalVolcano.Script(world, 1, record); log.Add((byte)'T');
            }
            effects += world.Effects;
            byte[] output = [.. record.AsSpan(0, 0x30), .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print($"VOLCANO MISMATCH {count}"); }
            count++;
        }
        GD.Print($"Volcano: {effects} effects; first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("volcano");
        Check(count == summary.GetProperty("case_count").GetInt32() && effects > 200, $"Volcano reference coverage ({count} cases, {effects} effects)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Volcano ARMY__04.WRK 0..9F matches original x86");
    }
}
