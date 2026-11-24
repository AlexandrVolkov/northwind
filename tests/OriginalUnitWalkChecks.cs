using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class WalkReplay(JsonElement c, List<byte> log) : IWalkWorld
    {
        private readonly Queue<bool> _turns = new(c.GetProperty("turn_zf").EnumerateArray().Select(v => v.GetInt32() != 0));
        public byte MapWidth => (byte)c.GetProperty("width").GetInt32();
        public (byte Heading, byte Code) NextStep() { log.Add((byte)'F'); return ((byte)c.GetProperty("dir").GetInt32(), (byte)c.GetProperty("code").GetInt32()); }
        public bool Turn(byte target) { log.Add((byte)'T'); log.Add(target); return _turns.Count > 0 && _turns.Dequeue(); }
        public void Occupancy(uint step) { log.Add((byte)'B'); log.AddRange(BitConverter.GetBytes(step)); }
        public void Effect(byte army, byte type, uint x, uint y) { log.Add((byte)'E'); log.Add(army); log.Add(type); log.Add(0); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y)); }
        public void RevealFog() => log.Add((byte)'V');
        public void ReleaseRoute() => log.Add((byte)'R');
        public void SearchEnemy(byte mode) { log.Add((byte)'D'); log.Add(mode); }
        public uint OccupancyWord(uint cell) => (uint)c.GetProperty("occ").GetInt64();
        public byte TerrainType(uint cell) => (byte)c.GetProperty("tt").GetInt32();
        public sbyte Delta(int index) => (sbyte)c.GetProperty("signs")[index].GetInt32();
    }

    /// <summary>Replays tests/fixtures/unit-walk.json (tools/verify_unit_walk.py): the walk step LE:1 70B0..72FD.</summary>
    private void OriginalUnitWalkChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/unit-walk.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, moved = 0;
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var d) ? d : -1;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var r = new byte[0x40]; r[0x24] = (byte)c.GetProperty("f24").GetInt32(); r[0x25] = (byte)c.GetProperty("f25").GetInt32(); r[0x26] = (byte)c.GetProperty("f26").GetInt32();
            r[0x27] = (byte)c.GetProperty("f27").GetInt32(); r[0x2c] = (byte)c.GetProperty("f2c").GetInt32(); r[0x16] = (byte)c.GetProperty("f16").GetInt32(); r[0x0d] = (byte)c.GetProperty("f0d").GetInt32();
            BitConverter.GetBytes((uint)c.GetProperty("x").GetInt32()).CopyTo(r, 4); BitConverter.GetBytes((uint)c.GetProperty("y").GetInt32()).CopyTo(r, 8); BitConverter.GetBytes((uint)c.GetProperty("f28").GetInt64()).CopyTo(r, 0x28);
            var tp = c.GetProperty("template"); var t = new byte[0xab];
            t[0x4a] = (byte)tp.GetProperty("n4a").GetInt32(); t[0x57] = (byte)tp.GetProperty("n57").GetInt32(); t[0xa2] = (byte)tp.GetProperty("na2").GetInt32(); BitConverter.GetBytes((ushort)tp.GetProperty("base").GetInt32()).CopyTo(t, 0x48);
            int k = 0; foreach (var v in tp.GetProperty("n58").EnumerateArray()) t[0x58 + k++] = (byte)v.GetInt32();
            var log = new List<byte>(); var walk = new OriginalUnitWalk(new WalkReplay(c, log));
            byte al = (byte)c.GetProperty("al").GetInt32();
            byte flags = walk.Step(r, t, al, (byte)(al ^ 5), (byte)c.GetProperty("bl").GetInt32());
            byte[] output = [.. r, al, (byte)(al ^ 5), walk.Flags, flags, .. log];
            digest.AppendData(output);
            string hex = Convert.ToHexStringLower(SHA256.HashData(output))[..10];
            if (log.Contains((byte)'B')) moved++;
            if (hex != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Walk: first mismatch {firstMismatch}; {moved} with occupancy transfer");
        var summary = root.GetProperty("results").GetProperty("walk");
        Check(count == summary.GetProperty("case_count").GetInt32() && moved > 20, $"Unit walk reference coverage ({count} cases, {moved} transfers)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Unit walk step LE:1 70B0 matches original x86");
    }
}
