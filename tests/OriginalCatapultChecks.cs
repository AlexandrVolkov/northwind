using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/catapult.json (tools/verify_catapult.py): catapult stone 85A, explosion 986, D6E6, BD0F.</summary>
    private void OriginalCatapultChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/catapult.json");
        var root = reference.RootElement; var results = root.GetProperty("results");
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Catapult reference template {item.Name} matches bundled DAT");
        var constants = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json");
        var properties = Convert.FromHexString(GameData.Read<MapData>("res://assets/imported/map.json").SegmentPropertiesHex);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const byte W = 16, H = 16;
        int count = 0, landings = 0, wounds = 0;
        void Finish(string name)
        {
            var expected = results.GetProperty(name);
            Check(count == expected.GetProperty("case_count").GetInt32(), $"Catapult {name} reference coverage ({count})");
            Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), $"Catapult {name} matches original x86");
            count = 0;
        }
        static byte[] Effect(uint x, uint y)
        {
            var raw = Enumerable.Range(0, 0x22).Select(i => unchecked((byte)(i * 5 + 9))).ToArray();
            raw[0] = 8; raw[1] = 0; raw[5] = 1; raw[0x11] = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(7), x); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0xc), y);
            return raw;
        }
        IEnumerable<JsonElement> Items(JsonElement spec, string name) => spec.GetProperty(name).EnumerateArray();

        var spec = root.GetProperty("create");
        foreach (var origin in Items(spec, "origins")) foreach (var target in Items(spec, "targets")) foreach (var experience in Items(spec, "experience"))
        foreach (var indexElement in Items(spec, "random_indices")) foreach (var lift in Items(spec, "lift"))
        {
            uint ax = origin[0].GetUInt32(), ay = origin[1].GetUInt32(); byte index = (byte)indexElement.GetInt32();
            var shooter = CombatUnit(16, 3, 10, 0, ax, ay, 1); shooter[0x16] = (byte)experience.GetInt32();
            var effect = Effect(ax + 8, ay - 8); effect[0x11] = (byte)lift.GetInt32();
            var random = new OriginalTerrainCatalog(properties, constants) { RandomIndex = index, SecondaryIndex = unchecked((byte)(index * 7)) };
            int sound = OriginalCatapultRules.Create(effect, shooter, target[0].GetUInt32(), target[1].GetUInt32(), W * 32, H * 32, random.NextRandomByte, random.NextSecondaryByte);
            digest.AppendData(effect); digest.AppendData([random.RandomIndex, random.SecondaryIndex]); digest.AppendData(Encoding.UTF8.GetBytes($"[{sound}]")); count++;
        }
        Finish("create");

        spec = root.GetProperty("step");
        foreach (var position in Items(spec, "positions")) foreach (var remaining in Items(spec, "remaining")) foreach (var velocity in Items(spec, "velocities"))
        foreach (var arc in Items(spec, "arc")) foreach (var objects in Items(spec, "objects")) foreach (var shared in Items(spec, "shared"))
        {
            uint x = position[0].GetUInt32(), y = position[1].GetUInt32();
            var effect = Effect(x, y);
            BinaryPrimitives.WriteInt32LittleEndian(effect.AsSpan(0x12), remaining.GetInt32());
            BinaryPrimitives.WriteInt32LittleEndian(effect.AsSpan(0x18), velocity[0].GetInt32()); BinaryPrimitives.WriteInt32LittleEndian(effect.AsSpan(0x1c), velocity[1].GetInt32());
            BinaryPrimitives.WriteUInt16LittleEndian(effect.AsSpan(0x10), (ushort)arc[0].GetInt32()); BinaryPrimitives.WriteUInt16LittleEndian(effect.AsSpan(0x20), (ushort)arc[1].GetInt32());
            int cell = (int)(Math.Min(H - 1, y >> 5) * W + Math.Min(W - 1, x >> 5));
            var grid = new uint[3 * W * H]; var records = new Dictionary<uint, (byte[], byte[])>(); var order = new List<byte[]>(); uint at = 0x100;
            foreach (var o in objects.EnumerateArray())
            {
                int plane = o[0].GetInt32(), t = o[2].GetInt32() == 0 ? 0 : 2;
                var rec = CombatUnit(0, 1, (byte)o[1].GetInt32(), 0x1f, x, y, 1); rec[0x0e] = (byte)t;
                records[at] = (rec, Dat(t)); order.Add(rec); grid[plane * W * H + cell] = at;
                if (shared.GetInt32() != 0) grid[((plane + 1) % 2) * W * H + cell] = at;
                at += 0x40;
            }
            var before = order.Select(r => r[0x19]).ToArray();
            string sounds = "[]", craters = "[]";
            if (OriginalCatapultRules.Step(effect, W * 32, H * 32) is var (cx, cy))
            {
                OriginalCatapultRules.Hit(OriginalCatapultRules.Attack, 2, 3, cx, cy, 1, 1, W, H, (plane, c) => grid[plane * W * H + c], r => records[r]);
                craters = $"[[{cy << 8 | cx}, 768, 257]]";
                sounds = $"[{OriginalCatapultRules.Explode(effect)}]"; landings++;
            }
            wounds += order.Where((r, i) => r[0x19] != before[i]).Count();
            digest.AppendData(effect); foreach (var r in order) digest.AppendData(r.AsSpan(0, 0x36));
            digest.AppendData(Encoding.UTF8.GetBytes($"[{sounds}, {craters}]")); count++;
        }
        Check(landings > 300 && wounds > 100, $"Catapult cases land ({landings}) and wound ({wounds})");
        Finish("step");

        spec = root.GetProperty("smoke");
        foreach (var drift in Items(spec, "drift")) foreach (var frames in Items(spec, "frames"))
        {
            var effect = Effect(300, 200); effect[0] = 10; effect[0x16] = (byte)frames[0].GetInt32(); effect[0x17] = (byte)frames[1].GetInt32();
            OriginalCatapultRules.Smoke(effect, drift[0].GetInt32(), drift[1].GetInt32());
            digest.AppendData(effect); count++;
        }
        Finish("smoke");

        spec = root.GetProperty("bolt_create");
        foreach (var origin in Items(spec, "origins")) foreach (var target in Items(spec, "targets")) foreach (var experience in Items(spec, "experience"))
        foreach (var indexElement in Items(spec, "random_indices"))
        {
            uint ax = origin[0].GetUInt32(), ay = origin[1].GetUInt32(); byte index = (byte)indexElement.GetInt32();
            var shooter = CombatUnit(26, 3, 10, 0, ax, ay, 1); shooter[0x16] = (byte)experience.GetInt32();
            var effect = Effect(ax + 8, ay - 8);
            var random = new OriginalTerrainCatalog(properties, constants) { RandomIndex = index, SecondaryIndex = unchecked((byte)(index * 7)) };
            OriginalCatapultRules.CreateBolt(effect, shooter, target[0].GetUInt32(), target[1].GetUInt32(), W * 32, H * 32, random.NextRandomByte, random.NextSecondaryByte);
            digest.AppendData(effect); digest.AppendData([random.RandomIndex, random.SecondaryIndex]); count++;
        }
        Finish("bolt_create");

        spec = root.GetProperty("bolt_step"); int thrown = 0;
        foreach (var position in Items(spec, "positions")) foreach (var remaining in Items(spec, "remaining")) foreach (var objects in Items(spec, "objects"))
        foreach (var indexElement in Items(spec, "random_indices")) foreach (var refuse in Items(spec, "refuse"))
        {
            uint x = position[0].GetUInt32(), y = position[1].GetUInt32(); byte index = (byte)indexElement.GetInt32();
            var effect = Effect(x, y); effect[0] = 0x1d; effect[2] = 3;
            BinaryPrimitives.WriteInt32LittleEndian(effect.AsSpan(0x12), remaining.GetInt32());
            BinaryPrimitives.WriteInt32LittleEndian(effect.AsSpan(0x18), 0x900); BinaryPrimitives.WriteInt32LittleEndian(effect.AsSpan(0x1c), -0x300);
            BinaryPrimitives.WriteUInt32LittleEndian(effect.AsSpan(0x10), 0); BinaryPrimitives.WriteUInt16LittleEndian(effect.AsSpan(0x20), 0x200);
            int cell = (int)(Math.Min(H - 1, y >> 5) * W + Math.Min(W - 1, x >> 5));
            var grid = new uint[3 * W * H]; var records = new Dictionary<uint, (byte[], byte[])>(); var order = new List<byte[]>(); uint at = 0x100;
            foreach (var o in objects.EnumerateArray())
            {
                int t = o[2].GetInt32() == 0 ? 0 : 2;
                var rec = CombatUnit(0, 1, (byte)o[1].GetInt32(), 0x1f, x, y, 1); rec[0x0e] = (byte)t;
                records[at] = (rec, Dat(t)); order.Add(rec); grid[o[0].GetInt32() * W * H + cell] = at; at += 0x40;
            }
            var random = new OriginalTerrainCatalog(properties, constants) { RandomIndex = index, SecondaryIndex = unchecked((byte)(index * 3 + 1)) };
            var scratch = new byte[0x22]; var spawned = new List<string>(); bool refused = refuse.GetInt32() != 0;
            string sounds = "[]";
            uint bx = BinaryPrimitives.ReadUInt32LittleEndian(effect.AsSpan(7)), by = BinaryPrimitives.ReadUInt32LittleEndian(effect.AsSpan(0xc));
            if (OriginalCatapultRules.StepBolt(effect, W * 32, H * 32, random.NextRandomByte, random.NextSecondaryByte, (tx, ty) =>
                {
                    spawned.Add($"[{tx & 0xff}, 401, {BinaryPrimitives.ReadUInt32LittleEndian(effect.AsSpan(7))}, {BinaryPrimitives.ReadUInt32LittleEndian(effect.AsSpan(0xc))}, 0, {tx}, {ty}]");
                    Array.Clear(scratch); return refused ? null : scratch;
                }) is var (cx, cy))
            {
                OriginalCatapultRules.Hit(OriginalCatapultRules.Attack, 3, 3, cx, cy, 1, 1, W, H, (plane, c) => grid[plane * W * H + c], r => records[r]);
                sounds = $"[{OriginalCatapultRules.CreateBigExplosion(effect)}]";
            }
            thrown += spawned.Count;
            digest.AppendData(effect); foreach (var r in order) digest.AppendData(r.AsSpan(0, 0x36));
            digest.AppendData([scratch[2], random.RandomIndex, random.SecondaryIndex]);
            digest.AppendData(Encoding.UTF8.GetBytes($"[{sounds}, [{string.Join(", ", spawned)}]]")); count++;
        }
        Check(thrown > 100, $"Ballista bolts throw fire shots ({thrown})");
        Finish("bolt_step");

        spec = root.GetProperty("big");
        foreach (var frames in Items(spec, "frames")) foreach (var ah in Items(spec, "ah"))
        {
            var effect = Effect(300, 200); effect[0] = 0x0c; effect[0x16] = (byte)frames[0].GetInt32(); effect[0x17] = (byte)frames[1].GetInt32();
            string sounds = "[]";
            if (ah.GetInt32() == 0) sounds = $"[{OriginalCatapultRules.CreateBigExplosion(effect)}]"; else OriginalCatapultRules.StepBigExplosion(effect);
            digest.AppendData(effect); digest.AppendData(Encoding.UTF8.GetBytes(sounds)); count++;
        }
        Finish("big");
    }
}
