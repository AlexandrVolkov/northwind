using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/neighbors.json (tools/verify_neighbors.py): the neighbour rule of lines BC2E.</summary>
    private void OriginalLineNeighborChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/neighbors.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Neighbour template {item.Name} matches bundled DAT");
        var rules = OriginalLineNeighbors.From(GameData.Read<LineNeighborData>("res://data/line_neighbors.json"));
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const int W = 32, H = 32;
        int count = 0, firstMismatch = -1, allowed = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int t = c.GetProperty("t").GetInt32(); var p = c.GetProperty("p").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            int variant = Dat(t)[0x77] & 3;
            var cells = new Dictionary<(int, int), byte[]>();
            foreach (var o in c.GetProperty("objects").EnumerateArray())
            {
                var a = o.EnumerateArray().Select(v => v.GetInt32()).ToArray();
                var rec = new byte[0x40]; rec[0] = 2; rec[0x0e] = (byte)a[2]; rec[0x0d] = (byte)variant;
                BitConverter.GetBytes((uint)(a[0] * 32 + 16)).CopyTo(rec, 4); BitConverter.GetBytes((uint)(a[1] * 32 + 16)).CopyTo(rec, 8);
                cells[(variant, a[1] * W + a[0])] = rec;
            }
            bool ok = rules.Allowed(W, H, (plane, cell) => cells.GetValueOrDefault((plane, cell)), index => Dat(index), (byte)t, (uint)(p[0] * 32 + 16), (uint)(p[1] * 32 + 16));
            if (ok) allowed++;
            byte[] output = [(byte)(ok ? 1 : 0)];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print($"NEIGHBOR MISMATCH {count} t{t}"); }
            count++;
        }
        GD.Print($"Line neighbours: {allowed} of {count} allowed; first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("neighbors");
        Check(count == summary.GetProperty("case_count").GetInt32() && allowed > 500 && allowed < count - 500, $"Line neighbour reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Line neighbour rule BC2E matches original x86");
    }
}
