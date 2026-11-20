using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/ai-formations.json (tools/verify_ai_formations.py): squad step 4, march in formation.</summary>
    private void OriginalAiFormationChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/ai-formations.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"AI formation template {item.Name} matches bundled DAT");
        var data = GameData.Read<OriginalAiData>("res://data/original_ai.json");
        Check(data.Formations.Count == 3 && data.SineHex.Length == 128, "Formation table 3D45 and sine table 2A7E are bundled");
        var formations = OriginalAiFormations.From(data);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, orders = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            byte p = (byte)c.GetProperty("participant").GetInt32(); int salt = c.GetProperty("salt").GetInt32();
            var s = new byte[OriginalAiSquads.Size];
            for (int i = 0; i < s.Length; i++) s[i] = unchecked((byte)(i * 13 + salt));
            byte[] Pair(string name) => c.GetProperty(name).EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
            s[0] = p; s[1] = (byte)c.GetProperty("id").GetInt32(); s[8] = (byte)c.GetProperty("kind").GetInt32();
            (s[9], s[0x0a]) = (Pair("goal")[0], Pair("goal")[1]); (s[0x0b], s[0x0c]) = (Pair("from")[0], Pair("from")[1]);
            BinaryPrimitives.WriteUInt32LittleEndian(s.AsSpan(0x1b), c.GetProperty("timer").GetUInt32());
            (s[0x20], s[0x21]) = (Pair("centre")[0], Pair("centre")[1]);
            BinaryPrimitives.WriteUInt32LittleEndian(s.AsSpan(0x22), c.GetProperty("count").GetUInt32());
            s[0x43] = (byte)c.GetProperty("moving").GetInt32(); s[0x4c] = (byte)c.GetProperty("stage").GetInt32();
            var chain = new List<(byte[] Record, byte[] Template)>();
            foreach (var m in c.GetProperty("members").EnumerateArray())
            {
                int t = m[0].GetInt32();
                var unit = CombatUnit((byte)t, p, 10, (ushort)m[3].GetInt32(), (uint)(m[4].GetInt32() * 32 + 7), (uint)(m[5].GetInt32() * 32 + 19), 1);
                var rec = new byte[0x40]; unit.CopyTo(rec, 0);
                rec[0x1b] = (byte)m[1].GetInt32(); rec[0x14] = (byte)m[2].GetInt32(); BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x10), 0x40);
                chain.Add((rec, Dat(t)));
            }
            var log = new List<byte>();
            bool more = formations.Step(s, chain, text => { log.Add((byte)'M'); log.AddRange(BitConverter.GetBytes(text)); }, 32, 32);
            orders += chain.Count(m => (m.Record[0x14] & 0xf8) == 0x48 && m.Record[0x1b] == s[1]);
            byte[] output = [.. s, .. chain.SelectMany(m => m.Record), .. log, more ? (byte)1 : (byte)0];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0)
            { firstMismatch = count; GD.Print("MISMATCH " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"AI formations: {orders} members placed; first mismatch {firstMismatch}");
        var result = root.GetProperty("results").GetProperty("formation");
        Check(count == result.GetProperty("case_count").GetInt32() && orders > 1000, $"AI formation reference coverage ({count}, {orders})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(), "AI step 4 (1EF26 formation march) matches original x86");
    }
}
