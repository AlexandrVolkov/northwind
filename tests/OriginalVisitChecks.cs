using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class VisitTestWorld((byte Al, bool Zf) answer, List<byte> log) : IVisitWorld
    {
        public (byte Al, bool Zf) Service() { log.Add((byte)'W'); log.Add(8); log.AddRange(BitConverter.GetBytes(0x200u)); log.AddRange(BitConverter.GetBytes(0x100u)); return answer; }
        public void Pickup(byte item, byte owner) { log.Add((byte)'P'); log.Add(item); log.Add(owner); }
    }

    /// <summary>Replays tests/fixtures/visit.json (tools/verify_visit.py): the visit of an object LE:1 902F with 9B86, 8C1E, 960B and 12406.</summary>
    private void OriginalVisitChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/visit.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, serviced = 0, picked = 0, sent = 0, turned = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var templates = c.GetProperty("templates").EnumerateArray().Select(army => army.EnumerateArray().Select(t =>
            {
                var raw = new byte[0xab]; raw[3] = (byte)t.GetProperty("w").GetInt32(); raw[4] = (byte)t.GetProperty("h").GetInt32();
                BitConverter.GetBytes(t.GetProperty("flags").GetUInt32()).CopyTo(raw, 0x40); BitConverter.GetBytes((ushort)t.GetProperty("base").GetInt32()).CopyTo(raw, 0x48); raw[0x4a] = (byte)t.GetProperty("frames").GetInt32();
                return raw;
            }).ToArray()).ToArray();
            var v = c.GetProperty("visitor"); var t = c.GetProperty("target");
            var visitor = new byte[0x40]; visitor[0] = (byte)v.GetProperty("army").GetInt32(); visitor[0x0e] = (byte)v.GetProperty("template").GetInt32();
            uint x = v.GetProperty("x").GetUInt32(), y = v.GetProperty("y").GetUInt32();
            BitConverter.GetBytes(x).CopyTo(visitor, 4); BitConverter.GetBytes(y).CopyTo(visitor, 8); visitor[0x2d] = (byte)v.GetProperty("state").GetInt32();
            visitor[0x25] = (byte)v.GetProperty("facing").GetInt32(); visitor[3] = (byte)v.GetProperty("owner").GetInt32(); BitConverter.GetBytes((ushort)v.GetProperty("frame").GetInt32()).CopyTo(visitor, 1);
            var target = new byte[0x40]; target[0] = (byte)t.GetProperty("army").GetInt32(); target[0x0e] = (byte)t.GetProperty("template").GetInt32(); target[0x19] = (byte)t.GetProperty("condition").GetInt32();
            BitConverter.GetBytes(unchecked((uint)((int)x + t.GetProperty("dx").GetInt32()))).CopyTo(target, 4); BitConverter.GetBytes(unchecked((uint)((int)y + t.GetProperty("dy").GetInt32()))).CopyTo(target, 8);
            var log = new List<byte>(); var wrk = c.GetProperty("wrk");
            var before = (byte[])visitor.Clone();
            OriginalVisit.Visit(visitor, templates[visitor[0]][visitor[0x0e]], target, templates[target[0]][target[0x0e]], 0x200, new VisitTestWorld(((byte)wrk.GetProperty("al").GetInt32(), wrk.GetProperty("zf").GetInt32() != 0), log));
            if (log.Contains((byte)'W')) serviced++; if (log.Contains((byte)'P')) picked++;
            if (BitConverter.ToUInt32(visitor, 0x1c) == 0x200) sent++; if (visitor[0x25] != before[0x25]) turned++;
            byte[] output = [.. visitor, .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print($"VISIT MISMATCH {count}"); }
            count++;
        }
        GD.Print($"Visit: first mismatch {firstMismatch}; serviced {serviced}, picked {picked}, sent {sent}, turned {turned}");
        var summary = root.GetProperty("results").GetProperty("visit");
        Check(count == summary.GetProperty("case_count").GetInt32() && serviced > 100 && picked > 30 && sent > 30 && turned > 100, $"Visit reference coverage ({count}: {serviced} serviced, {picked} picked, {sent} sent, {turned} turned)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Visit LE:1 902F/9B86/8C1E/960B/12406 matches original x86");
    }
}
