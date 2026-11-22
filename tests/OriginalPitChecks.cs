using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class PitWorld(uint found, uint other, uint area, List<byte> log) : IPitWorld
    {
        public uint Found = found, Other = other, AreaCount = area;
        public (uint Found, uint Other) Search(uint corner, uint size, byte[] record)
        {
            log.Add((byte)'F'); log.AddRange(BitConverter.GetBytes(corner)); log.AddRange(BitConverter.GetBytes(size)); log.AddRange(BitConverter.GetBytes(0x100u));
            return (Found, Other);
        }
        public uint Area(uint reference, uint replaced, byte crush, byte kind, byte[] record)
        {
            log.Add((byte)'A'); log.AddRange(BitConverter.GetBytes(reference)); log.AddRange(BitConverter.GetBytes(replaced));
            log.AddRange(BitConverter.GetBytes((uint)crush | (uint)kind << 8)); log.AddRange(BitConverter.GetBytes(0x100u)); log.Add(record[0x0d]);
            return AreaCount;
        }
        public void Service(byte[] record) => log.Add((byte)'S');
    }

    /// <summary>Replays tests/fixtures/pit.json (tools/verify_pit.py): the pit trap ARMY__03.WRK 7F0.</summary>
    private void OriginalPitChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/pit.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, opened = 0, closed = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var log = new List<byte>();
            var record = CombatUnit(12, 6, 100, 0, c.GetProperty("x").GetUInt32(), c.GetProperty("y").GetUInt32(), (byte)c.GetProperty("layer").GetInt32());
            record[0] = 4; record[0x15] = (byte)c.GetProperty("state").GetInt32();
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1), (ushort)c.GetProperty("frame").GetInt32());
            record[0x2a] = (byte)c.GetProperty("count").GetInt32(); record[0x14] = (byte)c.GetProperty("flags").GetInt32();
            byte counter = (byte)c.GetProperty("counter").GetInt32();
            if (c.GetProperty("x").GetUInt32() % 5 == 0) OriginalPit.Initialize(record);
            var world = new PitWorld(0, 0, 0, log);
            foreach (var t in c.GetProperty("ticks").EnumerateArray())
            {
                counter = (byte)(counter + t.GetProperty("counter_step").GetInt32());
                world.Found = (uint)t.GetProperty("found").GetInt32(); world.Other = (uint)t.GetProperty("other").GetInt32(); world.AreaCount = (uint)t.GetProperty("area").GetInt32();
                byte before = record[0x15];
                OriginalPit.Step(world, record, counter, 0x100);
                if (before == 0 && record[0x15] == 1) opened++; if (before == 3 && record[0x15] == 0) closed++;
                log.Add((byte)'T');
            }
            byte[] output = [.. record.AsSpan(0, 0x2b), .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print($"PIT MISMATCH {count}"); }
            count++;
        }
        GD.Print($"Pit: {opened} opened, {closed} closed; first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("pit");
        Check(count == summary.GetProperty("case_count").GetInt32() && opened > 30 && closed > 20, $"Pit reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Pit trap ARMY__03.WRK 7F0 matches original x86");
    }
}
