using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/visibility.json (tools/verify_visibility.py): the fog-of-war reveal 813E.</summary>
    private void OriginalVisibilityChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/visibility.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Visibility template {item.Name} matches bundled DAT");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, opened = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int w = c.GetProperty("w").GetInt32(), h = c.GetProperty("h").GetInt32(), seed = c.GetProperty("seed").GetInt32();
            var fog = Enumerable.Range(0, w * h).Select(i => (byte)((((i * 59 + seed) ^ (i >> 1) ^ (seed >> 8)) & 255) | (i % 7 == 0 ? 0xff : 0))).ToArray();
            var before = (byte[])fog.Clone();
            OriginalVisibilityRules.Reveal(fog, (byte)w, (byte)h, (byte)c.GetProperty("owner").GetInt32(), c.GetProperty("x").GetUInt32(), c.GetProperty("y").GetUInt32(),
                Dat(c.GetProperty("t").GetInt32())[0x47], (byte)c.GetProperty("experience").GetInt32());
            opened += fog.Where((b, i) => b != before[i]).Count();
            digest.AppendData(fog);
            if (Convert.ToHexStringLower(SHA256.HashData(fog))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print($"VISIBILITY MISMATCH {count}"); }
            count++;
        }
        var result = root.GetProperty("results").GetProperty("reveal");
        Check(count == result.GetProperty("case_count").GetInt32() && opened > 5000, $"Visibility reference coverage ({count}, {opened} cells opened)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(), "Fog-of-war reveal 813E matches original x86");
    }
}
