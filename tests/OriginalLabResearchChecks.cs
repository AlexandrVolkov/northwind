using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/lab.json (tools/verify_lab.py): the laboratory's research executor CCCB.</summary>
    private void OriginalLabResearchChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/lab.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, finished = 0, started = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int owner = c.GetProperty("owner").GetInt32();
            var items = c.GetProperty("items").EnumerateArray().Select(i => ((byte)i.GetProperty("id").GetInt32(), i.GetProperty("cost").GetUInt32())).ToList();
            var log = new List<byte>(); var world = new LabWorld(items, (byte)c.GetProperty("random").GetInt32(), log);
            var rate = c.GetProperty("rate").EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
            var lab = new byte[0x40 + 0x80 + 0x40]; lab = new byte[0xc0];
            CombatUnit(0, (byte)owner, 10, 0, 100, 100, 1).CopyTo(lab, 0); lab[0] = 2; lab[3] = (byte)owner; lab[0x15] = (byte)(c.GetProperty("running").GetBoolean() ? 1 : 0);
            lab[0x22] = rate[0]; lab[0x23] = rate[1]; lab[0x28] = (byte)c.GetProperty("item").GetInt32();
            BitConverter.GetBytes(c.GetProperty("remaining").GetUInt32()).CopyTo(lab, 0x24); BitConverter.GetBytes(c.GetProperty("counter_lab").GetUInt32()).CopyTo(lab, 0x73);
            var participant = new byte[0xf5]; BitConverter.GetBytes(c.GetProperty("gold").GetUInt32()).CopyTo(participant, 0x20);
            var settlement = new byte[0x1a35]; BitConverter.GetBytes(c.GetProperty("counter").GetUInt32()).CopyTo(settlement, 0x73);
            if (lab[0x15] == 0) started++;
            OriginalLabResearch.Tick(world, lab, participant, settlement, (byte)c.GetProperty("tick").GetInt32());
            if (world.Done) finished++;
            byte[] output = [.. lab.AsSpan(0, 0x80), .. participant.AsSpan(0x18, 0x10), .. settlement.AsSpan(0x70, 8), .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print($"LAB MISMATCH {count} " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Laboratory: {started} started, {finished} finished; first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("lab");
        Check(count == summary.GetProperty("case_count").GetInt32() && finished > 100 && started > 500, $"Laboratory reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Laboratory research CCCB matches original x86");
    }

    private sealed class LabWorld(List<(byte, uint)> items, byte random, List<byte> log) : ILabResearchWorld
    {
        public bool Done;
        public IReadOnlyList<(byte Id, uint Cost)> Researchable(byte owner, byte army) { log.Add((byte)'P'); log.AddRange(BitConverter.GetBytes((ushort)(owner | army << 8))); log.AddRange(BitConverter.GetBytes((ushort)items.Count)); return items.Select(i => (i.Item1, i.Item2)).ToList(); }
        public byte Random() => random;
        public void Complete(byte item) { Done = true; log.Add((byte)'T'); log.AddRange(BitConverter.GetBytes((ushort)item)); }
    }
}
