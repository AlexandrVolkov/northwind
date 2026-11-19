using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/ai-squads.json (tools/verify_ai_squads.py): squad registry, census and group orders.</summary>
    private void OriginalAiSquadChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/ai-squads.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"AI squad template {item.Name} matches bundled DAT");
        var digests = new Dictionary<string, IncrementalHash>(); var counts = new Dictionary<string, int>();
        const int S = OriginalAiSquads.Size;
                static byte[] U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); return b; }
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            byte p = (byte)c.GetProperty("participant").GetInt32(); int salt = c.GetProperty("salt").GetInt32();
            var ai = new byte[0x1a35]; ai[0] = p;
            foreach (var u in c.GetProperty("used").EnumerateArray()) ai[OriginalAiSquads.Ids + u.GetInt32()] = 1;
            var squads = c.GetProperty("squads"); ai[OriginalAiSquads.List] = (byte)squads.GetArrayLength();
            int k = 0;
            foreach (var s in squads.EnumerateArray())
            {
                int r = OriginalAiSquads.Record(k);
                for (int i = 0; i < S; i++) ai[r + i] = unchecked((byte)(i * 7 + k * 31 + salt));
                ai[r] = p; ai[r + 1] = (byte)s[0].GetInt32(); ai[r + 2] = (byte)s[1].GetInt32(); ai[r + 4] = (byte)s[2].GetInt32();
                BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(r + 0x0f), s[3].GetUInt32());
                for (int i = 0; i < 4; i++) { ai[r + 0x13 + i] = (byte)s[4][i].GetInt32(); ai[r + 0x17 + i] = (byte)s[5][i].GetInt32(); }
                BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(r + 0x36), s[6].GetUInt32());
                k++;
            }
            var chain = new List<(byte[] Record, byte[] Template)>();
            foreach (var o in c.GetProperty("objects").EnumerateArray())
            {
                int t = o[0].GetInt32(), cx = o[5].GetInt32(), cy = o[6].GetInt32();
                var unit = CombatUnit((byte)t, p, 10, (ushort)o[4].GetInt32(), (uint)((cx & 31) * 32 + (cx >> 5)), (uint)((cy & 31) * 32 + (cy >> 5)), 1);
                var rec = new byte[0x40]; unit.CopyTo(rec, 0);
                rec[0x1b] = (byte)o[1].GetInt32(); rec[0x0f] = (byte)o[2].GetInt32(); rec[0x14] = (byte)o[3].GetInt32();
                BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x10), 0x40);
                chain.Add((rec, Dat(t)));
            }
            var records = chain.Select(x => x.Record).ToList();
            byte[] Objects() => records.SelectMany(r => r).ToArray();
            byte[] Squads() => [.. ai.AsSpan(OriginalAiSquads.Ids, 256), .. ai.AsSpan(OriginalAiSquads.List, 1 + 50 * S)];
            byte[] Found(int at) => at < 0 ? [1] : [0, .. U32((uint)at)];
            int pick = OriginalAiSquads.Record(c.GetProperty("pick").GetInt32());
            var point = c.GetProperty("point"); byte px = (byte)point[0].GetInt32(), py = (byte)point[1].GetInt32();
            byte command = (byte)c.GetProperty("command").GetInt32(), squad = (byte)c.GetProperty("squad").GetInt32();
            uint value = c.GetProperty("value").GetUInt32();
            var limits = c.GetProperty("limits"); uint lx = limits[0].GetUInt32(), ly = limits[1].GetUInt32();
            byte[] output;
            string op = c.GetProperty("op").GetString()!;
            switch (op)
            {
                case "census": OriginalAiSquads.Census(ai.AsSpan(pick, S), ai[pick] == p ? chain : []); output = ai.AsSpan(pick, S).ToArray(); break;
                case "create": { int at = OriginalAiSquads.Create(ai, records); output = [.. (at < 0 ? new byte[] { 0 } : [1, .. U32((uint)at)]), .. Squads(), .. Objects()]; break; }
                case "disband": OriginalAiSquads.Disband(ai, records, squads.GetArrayLength() > 0 ? ai[pick + 1] : squad); output = [.. Squads(), .. Objects()]; break;
                case "transfer": OriginalAiSquads.Transfer(ai, records, squads.GetArrayLength() > 0 ? ai[pick + 1] : squad, (byte)value); output = [.. Squads(), .. Objects()]; break;
                case "find": output = Found(OriginalAiSquads.Find(ai, squad)); break;
                case "find_area": output = Found(OriginalAiSquads.FindInArea(ai, px, py)); break;
                case "strength_area": output = U32(OriginalAiSquads.StrengthInArea(ai, px, py)); break;
                case "find_state": output = Found(OriginalAiSquads.FindByState(ai, (byte)c.GetProperty("state").GetInt32(), c.GetProperty("target").GetUInt32())); break;
                case "allocate": { byte id = OriginalAiSquads.Allocate(ai); output = [(byte)(id == 0 ? 1 : 0), id, .. ai.AsSpan(OriginalAiSquads.Ids, 256)]; break; }
                case "order": OriginalAiSquads.Order(records, command, squad, px, py); output = Objects(); break;
                case "order_formation": OriginalAiSquads.OrderFormation(records, command, squad, px, py, lx, ly, 32, 32); output = Objects(); break;
                case "order_parameter": OriginalAiSquads.OrderParameter(records, command, squad, value); output = Objects(); break;
                case "command": OriginalAiSquads.Command(records, command, squad); output = Objects(); break;
                case "order_point": OriginalAiSquads.OrderPoint(records, command, squad, px, py); output = Objects(); break;
                case "clear": OriginalAiSquads.Clear(records, squad); output = Objects(); break;
                case "bounds":
                    {
                        var (a, b, w, h) = OriginalAiSquads.Bounds(records, squad, lx, ly);
                        output = [.. Objects(), .. U32(a), .. U32(b), .. U32(w), .. U32(h)]; break;
                    }
                default: throw new InvalidDataException("Unknown squad operation");
            }
            if (!digests.TryGetValue(op, out var digest)) digests[op] = digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            digest.AppendData(output); counts[op] = counts.GetValueOrDefault(op) + 1;
        }
        foreach (var item in root.GetProperty("results").EnumerateObject())
        {
            Check(counts.GetValueOrDefault(item.Name) == item.Value.GetProperty("case_count").GetInt32(), $"AI squad {item.Name} coverage ({counts.GetValueOrDefault(item.Name)})");
            Check(digests.TryGetValue(item.Name, out var d) && Convert.ToHexStringLower(d.GetHashAndReset()) == item.Value.GetProperty("sha256").GetString(), $"AI squad {item.Name} matches original x86");
        }
    }
}
