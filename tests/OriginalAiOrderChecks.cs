using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/ai-orders.json (tools/verify_ai_orders.py): order executor 227C7.</summary>
    private void OriginalAiOrderChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/ai-orders.json");
        var root = reference.RootElement;
        var dat = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates
            .Select(t => Convert.FromHexString(t.RawRecordHex)).ToArray();
        var tree = GameData.Read<OriginalTechData>("res://data/original_tech.json").Armies[2].Build();
        Check(Convert.ToHexStringLower(SHA256.HashData(tree)) == root.GetProperty("tree_sha256").GetString(), "AI orders tree matches ARMY__02.TRE");
        var random = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json").RandomValues.ToArray();
        var classes = Convert.FromHexString(root.GetProperty("gather_classes_hex").GetString()!);
        var fort = Convert.FromHexString(root.GetProperty("fortification_hex").GetString()!);
        uint patternAt = BinaryPrimitives.ReadUInt32LittleEndian(fort) - 0xf0f0; var pattern = fort[(int)patternAt..];
        using var producers = GameData.Read<JsonDocument>("res://data/original_producers.json");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const int S = OriginalAiSquads.Size, Mil = OriginalAiMilitary.List, Ord = OriginalAiMilitary.Orders;
        int count = 0, firstMismatch = -1; var modes = new int[11];
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            byte p = (byte)c.GetProperty("participant").GetInt32(); int n = c.GetProperty("count").GetInt32();
            var rnd = c.GetProperty("random");
            var world = new MilitaryProbe(dat, tree, random, (byte)rnd[0].GetInt32(), (byte)n) { RandomIndex2 = (byte)rnd[1].GetInt32(), Seed = (byte)c.GetProperty("seed").GetInt32(), Pattern = pattern };
            foreach (var pr in producers.RootElement.GetProperty("producers").EnumerateObject())
                world.Producers[byte.Parse(pr.Name)] = pr.Value.GetProperty("makes").EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
            var tr = c.GetProperty("terrain"); int orient = tr[0].GetInt32(), tat = tr[1].GetInt32(), twidth = tr[2].GetInt32(), tseed = tr[3].GetInt32();
            world.Terrain = cellIndex =>
            {
                int x = cellIndex % 32, y = cellIndex / 32, along = orient == 0 ? x : y;
                if (twidth != 0 && along >= tat && along < tat + twidth) return 2;
                return (byte)((x * 7 + y * 13 + tseed) % 41 == 0 ? 1 : (x + y + tseed) % 5 == 0 ? 3 : 0);
            };
            var hostile = c.GetProperty("hostile");
            for (int q = 0; q < 8; q++)
            {
                var rec = new byte[0xf5]; rec[0] = 2;
                if (q < n)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x12), hostile[q].GetUInt32());
                    int ri = 0; foreach (var v in c.GetProperty("resources").EnumerateArray()) BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x18 + 4 * ri++), v.GetUInt32());
                    int i = 0; foreach (var v in c.GetProperty("relations").GetProperty(q.ToString()).EnumerateArray()) rec[0xb9 + i++] = (byte)v.GetInt32();
                }
                world.Participants[q] = rec;
            }
            for (int q = 0; q < n; q++)
            {
                var b = new byte[0x1a35]; var v = c.GetProperty("blocks").GetProperty(q.ToString()); b[0] = (byte)q;
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x19e9), v.GetProperty("strength").GetUInt32());
                b[0x61] = (byte)v.GetProperty("home")[0].GetInt32(); b[0x62] = (byte)v.GetProperty("home")[1].GetInt32();
                byte lx = (byte)v.GetProperty("land")[0].GetInt32(), ly = (byte)v.GetProperty("land")[1].GetInt32();
                b[0x63] = lx; b[0x64] = ly; b[0x65] = (byte)(lx + 8); b[0x66] = (byte)(ly + 8);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x45), v.GetProperty("45").GetUInt32());
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x13), v.GetProperty("13").GetUInt32());
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x0e), v.GetProperty("0e").GetUInt32());
                b[0x52] = (byte)v.GetProperty("52").GetInt32(); b[0x1a01] = (byte)v.GetProperty("1a01").GetInt32();
                b[0x2a] = (byte)v.GetProperty("2a").GetInt32(); b[0x19e4] = (byte)v.GetProperty("flags").GetInt32();
                b[0x1a15] = (byte)c.GetProperty("enabled").GetInt32(); b[0x5c] = (byte)v.GetProperty("52").GetInt32();
                for (int k = 0; k < 20; k++) { b[Mil + k * 0x15] = 0xff; b[Ord + k * 0x15] = 0xff; }
                world.Blocks[q] = b;
            }
            var ai = world.Blocks[p];
            var objectList = c.GetProperty("objects").EnumerateArray().ToList();
            uint at = 0x100; var refs = new uint[objectList.Count];
            for (int q = 0; q < n; q++)
                for (int i = 0; i < objectList.Count; i++)
                {
                    var ob = objectList[i];
                    if (ob.GetProperty("owner").GetInt32() != q) continue;
                    int t = ob.GetProperty("t").GetInt32(), x = ob.GetProperty("cell")[0].GetInt32(), y = ob.GetProperty("cell")[1].GetInt32();
                    var unit = CombatUnit((byte)t, (byte)q, (byte)ob.GetProperty("hp").GetInt32(), 0, (uint)(x * 32 + 9), (uint)(y * 32 + 13), 1);
                    var rec = new byte[0x40]; unit.CopyTo(rec, 0);
                    rec[0x1b] = (byte)ob.GetProperty("squad").GetInt32(); rec[0x23] = (byte)ob.GetProperty("kind").GetInt32();
                    rec[0x0f] = ob.TryGetProperty("flags", out var fl) ? (byte)fl.GetInt32() : (byte)0; rec[0x14] = ob.TryGetProperty("cmd", out var cm) ? (byte)cm.GetInt32() : (byte)5;
                    BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x24), ob.GetProperty("stock").GetUInt32());
                    BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x10), 0x40);
                    if (q == p) { rec[0x22] = 0; Array.Clear(rec, 0x29, 8); }
                    world.Add((byte)q, at, rec, dat[t], y * 32 + x); refs[i] = at; at += 0x40;
                }
            var squads = c.GetProperty("squads"); ai[OriginalAiSquads.List] = (byte)squads.GetArrayLength();
            int sk = 0;
            foreach (var s in squads.EnumerateArray())
            {
                int r = OriginalAiSquads.Record(sk);
                for (int i = 0; i < S; i++) ai[r + i] = unchecked((byte)(i * 3 + sk * 17));
                ai[r] = p; ai[r + 1] = (byte)s[0].GetInt32(); ai[r + 2] = (byte)s[1].GetInt32(); ai[r + 5] = (byte)s[2].GetInt32(); ai[r + 4] = (byte)s[3].GetInt32();
                ai[r + 0x20] = (byte)s[4][0].GetInt32(); ai[r + 0x21] = (byte)s[4][1].GetInt32();
                BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(r + 0x36), s[5].GetUInt32());
                ai[OriginalAiSquads.Ids + ai[r + 1]] = 1; sk++;
            }
            int rk = 0;
            foreach (var r in c.GetProperty("requests").EnumerateArray())
            {
                int a = Ord + rk * 0x15;
                for (int i = 0; i < 0x15; i++) ai[a + i] = unchecked((byte)(i * 11 + rk));
                int mode = r.GetProperty("mode").GetInt32(); if (mode <= 10) modes[mode]++;
                ai[a] = (byte)mode; ai[a + 1] = (byte)r.GetProperty("first").GetInt32(); ai[a + 2] = (byte)r.GetProperty("stage").GetInt32(); ai[a + 3] = (byte)r.GetProperty("prio").GetInt32();
                ai[a + 4] = (byte)r.GetProperty("b4").GetInt32(); ai[a + 5] = (byte)r.GetProperty("b5").GetInt32(); ai[a + 6] = (byte)r.GetProperty("b6").GetInt32(); ai[a + 7] = (byte)r.GetProperty("b7").GetInt32();
                uint param = r.GetProperty("param").GetUInt32();
                BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(a + 8), param);
                ai[a + 0x0c] = (byte)r.GetProperty("squad").GetInt32(); BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(a + 0x10), r.GetProperty("age").GetUInt32());
                ai[a + 0x14] = (byte)r.GetProperty("tries").GetInt32();
                rk++;
            }
            var military = new OriginalAiMilitary(world, classes) { ClassMask = c.GetProperty("class")[0].GetUInt32(), ClassValue = c.GetProperty("class")[1].GetUInt32() };
            var orders = new OriginalAiOrders(world, military);
            byte fault = 0;
            try { orders.Run(ai); } catch (Exception e) when (e is OverflowException or DivideByZeroException) { fault = 1; }
            byte[] output;
            if (fault != 0) output = [1];
            else
            {
                var final = (byte[])ai.Clone();
                for (int k = 0; k < 20; k++)
                {
                    int a = Mil + k * 0x15; final[a + 7] = 0;
                    if (final[a] != 8) Array.Clear(final, a + 5, 2);
                    if (final[a] == 6) Array.Clear(final, a + 8, 4);
                    a = Ord + k * 0x15; Array.Clear(final, a + 4, 4);
                }
                output = [0, .. final.AsSpan(0x7b, 256), .. final.AsSpan(OriginalAiSquads.List, 1 + S * final[OriginalAiSquads.List]),
                    .. final.AsSpan(Mil, 0x15 * 20), .. final.AsSpan(Ord, 0x15 * 20), .. world.AllRecords(), world.RandomIndex, world.RandomIndex2,
                    .. BitConverter.GetBytes(military.ClassMask), .. BitConverter.GetBytes(military.ClassValue), .. world.PlacementLog];
            }
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0)
            { firstMismatch = count; GD.Print("MISMATCH " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"AI orders: orders per mode {string.Join(' ', modes)}; first mismatch {firstMismatch}");
        var result = root.GetProperty("results").GetProperty("orders");
        Check(count == result.GetProperty("case_count").GetInt32(), $"AI orders reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(), "AI order executor 227C7 matches original x86");
    }
}
