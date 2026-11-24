using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class MotionReplay(JsonElement c, List<byte> log) : IMotionWorld
    {
        private readonly JsonElement _s = c.GetProperty("stubs");
        public byte PlayerOwner => (byte)c.GetProperty("player").GetInt32();
        public byte MapWidth => (byte)c.GetProperty("map")[0].GetInt32();
        public byte MapHeight => (byte)c.GetProperty("map")[1].GetInt32();
        public byte WalkStep(uint request) { log.Add((byte)'W'); log.AddRange(BitConverter.GetBytes(request)); return (byte)_s.GetProperty("walk").GetInt32(); }
        public byte MoveStep(uint request) { log.Add((byte)'M'); log.AddRange(BitConverter.GetBytes(request)); return (byte)_s.GetProperty("step4").GetInt32(); }
        public bool TurnStep(byte target) { log.Add((byte)'T'); log.Add(target); return _s.GetProperty("turn_zf").GetInt32() != 0; }
        public byte BorderEvents() { log.Add((byte)'B'); return (byte)_s.GetProperty("border").GetInt32(); }
        public bool DeathCheck() { log.Add((byte)'K'); return _s.GetProperty("death_zf").GetInt32() != 0; }
        public void Effect(byte army, byte type, byte layer, uint x, uint y, uint selfReference, uint targetX, uint targetY)
        { log.Add((byte)'E'); log.Add(army); log.Add(type); log.Add(layer); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y)); log.AddRange(BitConverter.GetBytes(selfReference)); log.AddRange(BitConverter.GetBytes(targetX)); log.AddRange(BitConverter.GetBytes(targetY)); }
        public void Die() { log.Add((byte)'X'); }
        public byte[]? Resolve(uint reference)
        {
            var t = new byte[0x40]; BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(4), (uint)(c.GetProperty("x").GetInt32() ^ 0x55)); BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(8), (uint)(c.GetProperty("y").GetInt32() ^ 0xaa)); t[0x0d] = (byte)c.GetProperty("layer").GetInt32(); return t;
        }
    }

    /// <summary>Replays tests/fixtures/unit-motion.json (tools/verify_unit_motion.py): the unit motion machine LE:1 9210..95D3.</summary>
    private void OriginalUnitMotionChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/unit-motion.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1; var states = new HashSet<int>();
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var d) ? d : -1;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var r = new byte[0x40]; int cur = c.GetProperty("cur").GetInt32(); r[0x2d] = (byte)(cur | (c.GetProperty("req").GetInt32() << 4));
            r[0x17] = (byte)c.GetProperty("flag17").GetInt32(); r[0x24] = (byte)c.GetProperty("f24").GetInt32(); r[0x25] = (byte)c.GetProperty("f25").GetInt32(); r[0x26] = (byte)c.GetProperty("f26").GetInt32();
            r[0x27] = (byte)c.GetProperty("f27").GetInt32(); r[0x2c] = (byte)c.GetProperty("f2c").GetInt32(); r[0x2e] = (byte)c.GetProperty("f2e").GetInt32(); r[0x31] = (byte)c.GetProperty("f31").GetInt32();
            r[0x22] = (byte)c.GetProperty("f22").GetInt32(); r[0x23] = (byte)c.GetProperty("f23").GetInt32(); BitConverter.GetBytes((ushort)c.GetProperty("f28").GetInt32()).CopyTo(r, 0x28);
            BitConverter.GetBytes((ushort)c.GetProperty("f2a").GetInt32()).CopyTo(r, 0x2a); r[0x2f] = (byte)c.GetProperty("f2f").GetInt32(); BitConverter.GetBytes((uint)c.GetProperty("target").GetInt32()).CopyTo(r, 0x1c);
            r[3] = (byte)c.GetProperty("owner").GetInt32(); BitConverter.GetBytes((uint)c.GetProperty("x").GetInt32()).CopyTo(r, 4); BitConverter.GetBytes((uint)c.GetProperty("y").GetInt32()).CopyTo(r, 8); r[0x0d] = (byte)c.GetProperty("layer").GetInt32();
            var t = c.GetProperty("template"); var template = new byte[0xab];
            BitConverter.GetBytes((uint)t.GetProperty("flags").GetInt32()).CopyTo(template, 0x40); BitConverter.GetBytes((ushort)t.GetProperty("base").GetInt32()).CopyTo(template, 0x48);
            template[0x4a] = (byte)t.GetProperty("n4a").GetInt32(); template[0x4b] = (byte)t.GetProperty("n4b").GetInt32(); template[0x4c] = (byte)t.GetProperty("n4c").GetInt32(); template[0x4d] = (byte)t.GetProperty("n4d").GetInt32();
            template[0x4e] = (byte)t.GetProperty("n4e").GetInt32(); int k = 0; foreach (var s in t.GetProperty("sum").EnumerateArray()) template[0x4f + k++] = (byte)s.GetInt32();
            k = 0; foreach (var s in t.GetProperty("muzzle").EnumerateArray()) template[0x60 + k++] = (byte)s.GetInt32(); template[0x70] = (byte)t.GetProperty("f70").GetInt32(); template[0x78] = (byte)t.GetProperty("f78").GetInt32();
            var log = new List<byte>(); var motion = new OriginalUnitMotion(new MotionReplay(c, log));
            motion.Step(r, template, 0x800);
            byte[] output = [.. r, motion.Al, motion.Bl, .. log];
            digest.AppendData(output); states.Add(cur);
            string hex = Convert.ToHexStringLower(SHA256.HashData(output))[..10];
            if (hex != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Motion: first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("motion");
        Check(count == summary.GetProperty("case_count").GetInt32() && states.Count == 8, $"Unit motion reference coverage ({count} cases, {states.Count} states)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Unit motion machine LE:1 9210 matches original x86");
    }
}
