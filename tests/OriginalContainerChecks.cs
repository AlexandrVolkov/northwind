using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class ContainerLog(List<byte> log) : IContainerServices
    {
        public void Redraw() => log.Add((byte)'D'); public void Remove() => log.Add((byte)'R');
        public void Effect(byte army, byte type, byte layer, uint x, uint y) { log.Add((byte)'E'); log.AddRange([army, type, layer]); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y)); }
    }

    /// <summary>Replays tests/fixtures/container.json (tools/verify_container.py): the scripts of jugs and chests of ARMY__03.WRK.</summary>
    private void OriginalContainerChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/container.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, removals = 0, items = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            string kindName = c.GetProperty("kind").GetString()!;
            var kind = kindName switch { "jug" => OriginalContainer.Kind.Jug, "chest" => OriginalContainer.Kind.Chest, "chest10" => OriginalContainer.Kind.Chest10, _ => OriginalContainer.Kind.Chest11 };
            bool chest = kind != OriginalContainer.Kind.Jug;
            var record = new byte[50]; record[0] = (byte)c.GetProperty("army").GetInt32(); record[0x14] = (byte)c.GetProperty("state").GetInt32(); record[0x15] = (byte)c.GetProperty("timer").GetInt32();
            record[0x19] = (byte)c.GetProperty("condition").GetInt32(); BitConverter.GetBytes((ushort)c.GetProperty("frame").GetInt32()).CopyTo(record, 1); record[0x31] = (byte)c.GetProperty("item").GetInt32();
            BitConverter.GetBytes(c.GetProperty("x").GetUInt32()).CopyTo(record, 4); BitConverter.GetBytes(c.GetProperty("y").GetUInt32()).CopyTo(record, 8);
            ushort frameBase = (ushort)c.GetProperty("base").GetInt32();
            var log = new List<byte>(); var services = new ContainerLog(log); var results = new List<byte>();
            if (c.GetProperty("x").GetUInt32() % 4 == 0) OriginalContainer.Initialize(record, kind);
            foreach (var step in c.GetProperty("steps").EnumerateArray())
            {
                string s = step.GetString()!;
                if (s == 'v'.ToString()) { var (al, zf) = OriginalContainer.Visit(record, chest); results.AddRange([al, (byte)(zf ? 1 : 0)]); if (zf && al != 0) items++; }
                else OriginalContainer.Step(record, frameBase, chest, services);
                log.Add((byte)'|');
            }
            removals += log.Count(b => b == (byte)'R');
            byte[] output = [.. record, .. results, .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print($"CONTAINER MISMATCH {count} {kindName}"); }
            count++;
        }
        GD.Print($"Containers: first mismatch {firstMismatch}; removals {removals}, items {items}");
        var summary = root.GetProperty("results").GetProperty("container");
        Check(count == summary.GetProperty("case_count").GetInt32() && removals > 50 && items > 50, $"Container reference coverage ({count}, {removals} removals, {items} items)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Jug and chest scripts ARMY__03.WRK E4/71A match original x86");
    }
}
