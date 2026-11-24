using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class ObjectReplay(JsonElement c, byte[] random, List<byte> log) : IObjectScriptWorld
    {
        private byte _i = (byte)c.GetProperty("seed").GetInt32(), _j = (byte)(c.GetProperty("seed").GetInt32() ^ 0x5a);
        private readonly Queue<(uint, uint)> _area = new(c.GetProperty("area").EnumerateArray().Select(a => ((uint)a[0].GetInt32(), (uint)a[1].GetInt32())));
        public byte I => _i; public byte J => _j;
        public byte Random() => random[_i++];
        public byte Random2() => random[_j++];
        public (uint Count, uint Hostile) Area(uint center, uint size) { log.Add((byte)'A'); log.AddRange(BitConverter.GetBytes(center)); log.AddRange(BitConverter.GetBytes(size)); return _area.Count > 0 ? _area.Dequeue() : (0, 0); }
        public void Effect(byte[] record, byte type, byte layer, uint x, uint y) { log.Add((byte)'E'); log.Add(type); log.Add(layer); log.AddRange(BitConverter.GetBytes(0x800u)); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y)); }
        public void Sound(uint id) { log.Add((byte)'S'); log.AddRange(BitConverter.GetBytes(id)); }
    }

    private void ObjectScriptReplay(string file, string key, string label, Action<IObjectScriptWorld, byte[], int> script)
    {
        using var reference = GameData.Read<JsonDocument>($"res://tests/fixtures/{file}.json");
        var root = reference.RootElement;
        var random = root.GetProperty("random_values").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, effects = 0;
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var d) ? d : -1;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var r = new byte[0x50]; BitConverter.GetBytes((uint)c.GetProperty("x").GetInt32()).CopyTo(r, 4); BitConverter.GetBytes((uint)c.GetProperty("y").GetInt32()).CopyTo(r, 8);
            r[0x15] = (byte)c.GetProperty("f15").GetInt32(); BitConverter.GetBytes((uint)c.GetProperty("f26").GetInt64()).CopyTo(r, 0x26); r[0x2a] = (byte)c.GetProperty("f2a").GetInt32();
            BitConverter.GetBytes((ushort)c.GetProperty("w1").GetInt32()).CopyTo(r, 1); BitConverter.GetBytes((uint)c.GetProperty("f22").GetInt64()).CopyTo(r, 0x22);
            var log = new List<byte>(); var world = new ObjectReplay(c, random, log);
            script(world, r, c.GetProperty("ah").GetInt32());
            byte[] output = [.. r, world.I, world.J, .. log];
            digest.AppendData(output); if (log.Contains((byte)'E')) effects++;
            string hex = Convert.ToHexStringLower(SHA256.HashData(output))[..10];
            if (hex != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"{key}: first mismatch {firstMismatch}; {effects} effects");
        var summary = root.GetProperty("results").GetProperty(key);
        Check(count == summary.GetProperty("case_count").GetInt32() && effects > 20, $"{key} reference coverage ({count} cases, {effects} effects)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), $"{label} matches original x86");
    }

    private void OriginalObjectScriptChecks()
    {
        ObjectScriptReplay("obj-rockfall", "rockfall", "The rockfall script ARMY__03.WRK 189", OriginalObjectScripts.Rockfall);
        ObjectScriptReplay("obj-face", "face", "The stone face script ARMY__03.WRK 2B9", OriginalObjectScripts.Face);
    }
}
