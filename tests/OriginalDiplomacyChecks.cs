using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalDiplomacyChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/diplomacy.json");
        var root = reference.RootElement;
        var data = GameData.Read<OriginalDiplomacyData>("res://data/original_diplomacy.json");
        Check(data.Types.Count == 30 && data.Reactions.Count == 72 && data.Types[3].Handler == OriginalDiplomacyRules.FirstContactHandler,
            "Diplomacy tables are bundled");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int n = c.GetProperty("count").GetInt32();
            var state = new DiplomacyState(n) { Time = c.GetProperty("time").GetUInt32() };
            for (int p = 0; p < n; p++)
            {
                state.Hostile[p] = c.GetProperty("hostile")[p].GetUInt32();
                for (int q = 0; q < n; q++) state.Relations[p, q] = (byte)c.GetProperty("relations")[p][q].GetInt32();
                state.Dead[p] = c.GetProperty("dead")[p].GetInt32() != 0;
                state.Reputation[p] = c.GetProperty("reputation")[p].GetInt32();
                int slot = 0;
                foreach (var q in c.GetProperty("queued")[p].EnumerateArray())
                { state.Queue[p, slot].Type = (byte)q[0].GetInt32(); state.Queue[p, slot].Other = (byte)q[1].GetInt32(); slot++; }
            }
            var calls = new List<string>();
            var rules = new OriginalDiplomacyRules(data, 0x8ddb)
            { Unhandled = (handler, bx, record) => calls.Add($"[\"{handler:X}\", {bx}, \"{Convert.ToHexStringLower(record.Encode())}\"]") };
            byte other = (byte)c.GetProperty("other").GetInt32();
            bool sent = rules.Send(state, (byte)c.GetProperty("type").GetInt32(), other, (byte)c.GetProperty("sender").GetInt32(), other, c.GetProperty("param").GetUInt32());
            for (int p = 0; p < n; p++)
            {
                digest.AppendData(BitConverter.GetBytes(state.Hostile[p]));
                var relations = new byte[16]; for (int q = 0; q < 16; q++) relations[q] = state.Relations[p, q]; digest.AppendData(relations);
                for (int s = 0; s < 8; s++) digest.AppendData(state.Queue[p, s].Encode());
                digest.AppendData(BitConverter.GetBytes(state.Reputation[p]));
            }
            digest.AppendData(Encoding.UTF8.GetBytes($"[{(sent ? 0 : 255)}, [{string.Join(", ", calls)}]]")); count++;
        }
        var expected = root.GetProperty("results").GetProperty("send");
        Check(count == expected.GetProperty("case_count").GetInt32(), $"Diplomacy reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), "Message kernel EDD5/F250/F000/EF3F matches original x86");
    }
}
