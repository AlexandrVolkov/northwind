using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/ai-workers.json (tools/verify_ai_workers.py): worker squad states 9 (1F476) and 10 (1F653).</summary>
    private void OriginalAiWorkerChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/ai-workers.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"AI worker template {item.Name} matches bundled DAT");
        var random = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json").RandomValues.ToArray();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const int S = OriginalAiSquads.Size;
        int count = 0, firstMismatch = -1, jobs = 0, trees = 0, deliveries = 0, mines = 0, mills = 0, groves = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            byte p = (byte)c.GetProperty("participant").GetInt32(); int salt = c.GetProperty("salt").GetInt32();
            var ai = new byte[0x1a35]; ai[0] = p;
            int li = 0;
            foreach (var l in c.GetProperty("lists").EnumerateArray())
            {
                int at = (li < 20 ? 0x1ab : 0x4f3) + li % 20 * 0x15;
                for (int i = 0; i < 0x15; i++) ai[at + i] = unchecked((byte)(i * 5 + li));
                ai[at] = (byte)l[0].GetInt32(); ai[at + 1] = (byte)l[1].GetInt32(); li++;
            }
            var enemies = c.GetProperty("enemies"); int ownBase = 0x100 + 0x40 * Math.Max(3, enemies.GetArrayLength());
            var squads = c.GetProperty("squads"); ai[OriginalAiSquads.List] = (byte)squads.GetArrayLength();
            int k = 0;
            foreach (var s in squads.EnumerateArray())
            {
                int r = OriginalAiSquads.Record(k);
                for (int i = 0; i < S; i++) ai[r + i] = unchecked((byte)(i * 7 + k * 31 + salt));
                byte G(string name) => (byte)s.GetProperty(name).GetInt32();
                void Bytes(int at, string name) { int i = 0; foreach (var v in s.GetProperty(name).EnumerateArray()) ai[r + at + i++] = (byte)v.GetInt32(); }
                void Word(int at, string name) => BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(r + at), s.GetProperty(name).GetUInt32());
                ai[r] = p; ai[r + 1] = G("id"); ai[r + 2] = G("state"); ai[r + 4] = G("priority"); ai[r + 5] = G("stage");
                int mine = s.GetProperty("mine").GetInt32();
                BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(r + 0x0f), mine == -1 ? 0 : mine == -2 ? 0x100u : (uint)(ownBase + 0x40 * mine));
                Bytes(0x13, "target"); Word(0x1b, "timer"); ai[r + 0x1f] = G("saved"); Bytes(0x20, "centre"); Word(0x22, "count");
                ai[r + 0x3a] = G("planes"); ai[r + 0x4a] = G("step"); ai[r + 0x4b] = G("next");
                k++;
            }
            int seed = c.GetProperty("explored_seed").GetInt32(); uint terrain = c.GetProperty("terrain_seed").GetUInt32();
            var world = new StateProbe(random, 0, (byte)c.GetProperty("random")[0].GetInt32(), (byte)c.GetProperty("random")[1].GetInt32(),
                Enumerable.Range(0, 1024).Select(x => (byte)((x * 167 + seed) ^ (x >> 3) ^ (seed >> 8))).ToArray())
            {
                Property = cell =>
                {
                    uint h = unchecked((uint)cell * 2654435761u + terrain * 97u);
                    return new uint[] { 1, 1, 1, 0, 2, 3, 1, 5 }[(h >> 29) & 7] | ((h & 0x1f) < 6 ? (h >> 8) & 0xf00 : 0);
                }
            };
            world.Workers = new OriginalAiWorkers(world);
            int q = 0; foreach (var h in c.GetProperty("hostile").EnumerateArray()) world.Hostile[q++] = h.GetUInt32();
            uint at2 = 0x100;
            byte[] Make(int t, byte owner, byte hp, uint x, uint y)
            {
                var unit = CombatUnit((byte)t, owner, hp, 0, x, y, 1); var rec = new byte[0x40]; unit.CopyTo(rec, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x10), 0x40); return rec;
            }
            foreach (var o in enemies.EnumerateArray())
            {
                int t = o[0].GetInt32(), cell = o[4].GetInt32();
                var rec = Make(t, (byte)o[1].GetInt32(), (byte)o[3].GetInt32(), (uint)((cell & 31) * 32 + 17), (uint)((cell >> 5) * 32 + 3));
                rec[0x20] = (byte)o[2].GetInt32();
                world.Add(p, at2, rec, Dat(t), o[5].GetInt32(), cell, own: false); at2 += 0x40;
            }
            while (at2 < 0x100 + 0x40 * 3) { world.Add(p, at2, Make(0, 7, 0, 40, 40), Dat(0), -1, 0, own: false); at2 += 0x40; }
            var own = new List<byte[]>();
            foreach (var o in c.GetProperty("own").EnumerateArray())
            {
                int t = o[0].GetInt32(), cell = o[5].GetInt32();
                var rec = Make(t, p, (byte)o[6].GetInt32(), (uint)((cell & 31) * 32 + 5), (uint)((cell >> 5) * 32 + 9));
                rec[0x1b] = (byte)o[1].GetInt32(); rec[0x0f] = (byte)o[2].GetInt32(); rec[0x14] = (byte)o[3].GetInt32(); rec[0x15] = (byte)o[4].GetInt32();
                BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x24), o[7].GetUInt32());
                world.Add(p, at2, rec, Dat(t), -1, cell, own: true); own.Add(rec); at2 += 0x40;
            }
            var before = own.Select(r => r[0x14]).ToArray();
            var machine = new OriginalAiSquadStates(world, new byte[256], new byte[60]);
            for (int tick = 0; tick < c.GetProperty("ticks").GetInt32(); tick++) machine.Run(ai);
            jobs += own.Where((r, i) => r[0x14] != before[i]).Count();
            trees += own.Where((r, i) => r[0x14] != before[i] && (r[0x14] & 0xf8) == 0x58).Count();
            mines += own.Count(r => (r[0x14] & 0xf8) == 0x60 && BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(0x1c)) >= 0x100 && BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(0x1c)) < 0x1000);
            deliveries += own.Where((r, i) => r[0x14] != before[i] && (r[0x14] & 0xf8) == 0x60).Count();
            for (int i = 0; i < 20; i++) if (ai[0x4f3 + i * 0x15] == 4 && ai[0x4f3 + i * 0x15 + 3] == 0x0f) mills++;
            for (int i = 0; i < ai[OriginalAiSquads.List]; i++) if (ai[OriginalAiSquads.Record(i) + 2] == 9 && ai[OriginalAiSquads.Record(i) + 5] == 1 && ai[OriginalAiSquads.Record(i) + 0x4b] == 2) groves++;
            var masked = new List<byte>();
            foreach (var (list, lo, hi) in new[] { (0x1ab, 8, 0x0c), (0x4f3, 4, 8) })
                for (int i = 0; i < 20; i++) { var r = ai.AsSpan(list + i * 0x15, 0x15).ToArray(); Array.Clear(r, lo, hi - lo); masked.AddRange(r); }
            byte[] output = [.. ai.AsSpan(OriginalAiSquads.List, 1 + S * ai[OriginalAiSquads.List]), .. masked, .. own.SelectMany(r => r),
                world.RandomIndex, world.RandomIndex2];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0)
            { firstMismatch = count; GD.Print("MISMATCH " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"AI workers: {jobs} member commands ({trees} trees, {deliveries} 60h, {mines} at mines), {mills} lumbermill requests, {groves} groves; first mismatch {firstMismatch}");
        var result = root.GetProperty("results").GetProperty("workers");
        Check(trees > 30 && deliveries > 30 && mills > 10 && groves > 30, $"AI worker cases give members jobs ({jobs})");
        Check(count == result.GetProperty("case_count").GetInt32(), $"AI worker reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(), "AI worker states 1F476/1F653 match original x86");
    }
}
