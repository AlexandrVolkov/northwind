using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalTechChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/tech.json");
        var root = reference.RootElement;
        var data = GameData.Read<OriginalTechData>("res://data/original_tech.json");
        var trees = data.Armies.Select(a => a.Build()).ToArray();
        var hashes = root.GetProperty("tree_sha256").EnumerateArray().Select(x => x.GetString()).ToArray();
        for (int k = 0; k < hashes.Length; k++) Check(Convert.ToHexStringLower(SHA256.HashData(trees[k])) == hashes[k], $"TRE {k} record area rebuilds from data/original_tech.json");
        var dat = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates
            .Select(t => Convert.FromHexString(t.RawRecordHex)).ToArray();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, opened = 0, completed = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int army = c.GetProperty("army").GetInt32();
            var tree = (byte[])trees[army].Clone(); var records = data.Armies[army].Records;
            int r = 0;
            foreach (var m in c.GetProperty("mutations").EnumerateArray())
            {
                if (m[0].GetInt32() != 0) tree[records[r].Offset] ^= 0x80;
                if (m[1].ValueKind == JsonValueKind.Number) tree[records[r].Offset + 2] = (byte)m[1].GetInt32();
                r++;
            }
            var result = new List<string>();
            foreach (var op in c.GetProperty("ops").EnumerateArray())
            {
                switch (op[0].GetString())
                {
                    case "available":
                        bool open = OriginalTechRules.Available(tree, (byte)op[1].GetInt32()); if (open) opened++;
                        result.Add(open ? "1" : "0"); break;
                    case "complete":
                        var before = (byte[])tree.Clone();
                        OriginalTechRules.Complete(tree, (byte)op[1].GetInt32(), (byte)op[2].GetInt32());
                        if (!before.SequenceEqual(tree)) completed++;
                        break;
                    case "researchable":
                        var items = OriginalTechRules.Researchable(tree);
                        result.Add($"[{items.Count}, \"{string.Concat(items.Select(at => Convert.ToHexStringLower(tree.AsSpan(at, 16))))}\"]"); break;
                    default:
                        var list = OriginalTechRules.Buildable(dat, tree, op[1].GetInt32() != 0, op[2].GetUInt32(), op[3].GetUInt32());
                        result.Add("[" + string.Join(", ", list.Select(x => x.ToString()).Append("255")) + "]"); break;
                }
            }
            digest.AppendData(tree); digest.AppendData(System.Text.Encoding.UTF8.GetBytes("[" + string.Join(", ", result) + "]"));
            count++;
        }
        var res = root.GetProperty("results").GetProperty("tech");
        Check(count == res.GetProperty("case_count").GetInt32() && opened > 100 && completed > 500, $"TRE reference coverage ({count}, open {opened}, changed {completed})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == res.GetProperty("sha256").GetString(), "TRE routines A673/A6AE/A716/223B4 match original x86");
    }
}
