using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/fire.json (tools/verify_fire.py): fire shot BAB (type 11h) and smoke trail C8A (12h).</summary>
    private void OriginalFireChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/fire.json");
        var root = reference.RootElement; var results = root.GetProperty("results");
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Fire reference template {item.Name} matches bundled DAT");
        var constants = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json");
        var properties = Convert.FromHexString(GameData.Read<MapData>("res://assets/imported/map.json").SegmentPropertiesHex);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const byte W = 16, H = 16;
        int count = 0; var outcomes = new int[4];
        void Finish(string name)
        {
            var expected = results.GetProperty(name);
            Check(count == expected.GetProperty("case_count").GetInt32(), $"Fire {name} reference coverage ({count})");
            Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), $"Fire {name} matches original x86");
            count = 0;
        }
        static byte[] Effect(uint x, uint y, byte kind = 0x11)
        {
            var raw = Enumerable.Range(0, 0x22).Select(i => unchecked((byte)(i * 5 + 9))).ToArray();
            raw[0] = kind; raw[1] = 0; raw[5] = 1; raw[0x11] = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(7), x); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0xc), y);
            return raw;
        }
        IEnumerable<JsonElement> Items(JsonElement spec, string name) => spec.GetProperty(name).EnumerateArray();

        var spec = root.GetProperty("create");
        foreach (var origin in Items(spec, "origins")) foreach (var target in Items(spec, "targets")) foreach (var experience in Items(spec, "experience"))
        foreach (var indexElement in Items(spec, "random_indices")) foreach (var has in Items(spec, "shooter"))
        {
            uint ax = origin[0].GetUInt32(), ay = origin[1].GetUInt32(); byte index = (byte)indexElement.GetInt32();
            var shooter = CombatUnit(17, 3, 10, 0, ax, ay, 1); shooter[0x16] = (byte)experience.GetInt32();
            var effect = Effect(ax + 8, ay - 8);
            var random = new OriginalTerrainCatalog(properties, constants) { RandomIndex = index, SecondaryIndex = unchecked((byte)(index * 7)) };
            int sound = OriginalFireRules.Create(effect, has.GetInt32() != 0 ? shooter : ReadOnlySpan<byte>.Empty, target[0].GetUInt32(), target[1].GetUInt32(),
                W * 32, H * 32, random.NextRandomByte, random.NextSecondaryByte);
            digest.AppendData(effect); digest.AppendData([random.RandomIndex, random.SecondaryIndex]); digest.AppendData(Encoding.UTF8.GetBytes($"[{sound}]")); count++;
        }
        Finish("create");

        spec = root.GetProperty("step");
        var cells = Enumerable.Range(0, W * H).Select(i => (uint)i).ToArray();
        foreach (var position in Items(spec, "positions")) foreach (var remaining in Items(spec, "remaining")) foreach (var velocity in Items(spec, "velocities"))
        foreach (var obj in Items(spec, "objects")) foreach (var forest in Items(spec, "forest")) foreach (var indexElement in Items(spec, "random_indices"))
        {
            uint x = position[0].GetUInt32(), y = position[1].GetUInt32();
            var effect = Effect(x, y); effect[2] = 3;
            BinaryPrimitives.WriteInt32LittleEndian(effect.AsSpan(0x12), remaining.GetInt32());
            BinaryPrimitives.WriteInt32LittleEndian(effect.AsSpan(0x18), velocity[0].GetInt32()); BinaryPrimitives.WriteInt32LittleEndian(effect.AsSpan(0x1c), velocity[1].GetInt32());
            var props = new byte[W * H * 8]; for (int i = 0; i < W * H; i++) props[i * 8] = (byte)forest.GetInt32();
            var grid = new uint[3 * W * H]; byte[]? record = null; byte[]? template = null;
            if (obj.ValueKind != JsonValueKind.Null)
            {
                int t = obj[0].GetInt32(); record = CombatUnit((byte)t, (byte)obj[1].GetInt32(), (byte)obj[2].GetInt32(), 0x1f, x, y, 1); template = Dat(t);
                grid[W * H + Math.Min(H - 1, (int)(y >> 5)) * W + Math.Min(W - 1, (int)(x >> 5))] = 0x100;
            }
            var random = new OriginalTerrainCatalog(properties, constants) { RandomIndex = (byte)indexElement.GetInt32() };
            var spawns = new List<string>();
            var (outcome, sound) = OriginalFireRules.Step(effect, (sx, sy) => spawns.Add($"[0, 274, {sx}, {sy}]"),
                (px, py) =>
                {
                    var found = OriginalCombatRules.FindStruck(px, py, effect[5], W, cells, props, (p, c) => grid[p * W * H + c], r => r == 0x100 ? (record!, template!) : null);
                    return (found.Reference != 0 ? (record!, template!) : null, found.TerrainClass);
                }, W * 32, H * 32, random.NextRandomByte);
            outcomes[(int)outcome]++;
            string exit = outcome switch { FireOutcome.Ignite => "[\"CE3\", 0]", FireOutcome.Deflect => "[\"FA4\", 0]", _ => "null" };
            digest.AppendData(effect); if (record != null) digest.AppendData(record.AsSpan(0, 0x36)); digest.AppendData([random.RandomIndex]);
            digest.AppendData(Encoding.UTF8.GetBytes($"[[{(sound is { } v ? v.ToString() : "")}], [{string.Join(", ", spawns)}], {exit}]")); count++;
        }
        Check(outcomes.All(n => n > 30), $"Fire step outcomes covered ({string.Join(' ', outcomes)})");
        Finish("step");

        spec = root.GetProperty("trail");
        foreach (var drift in Items(spec, "drift")) foreach (var state in Items(spec, "states"))
            for (int ah = 0; ah < 2; ah++)
            {
                var effect = Effect(300, 200, 0x12);
                BinaryPrimitives.WriteUInt16LittleEndian(effect.AsSpan(0x10), (ushort)state[0].GetInt32()); BinaryPrimitives.WriteUInt16LittleEndian(effect.AsSpan(0x12), 0);
                BinaryPrimitives.WriteUInt16LittleEndian(effect.AsSpan(0x20), (ushort)state[1].GetInt32());
                effect[0x16] = (byte)state[2].GetInt32(); effect[0x17] = (byte)state[3].GetInt32();
                if (ah == 0) OriginalFireRules.CreateTrail(effect); else OriginalFireRules.StepTrail(effect, drift[0].GetInt32(), drift[1].GetInt32());
                digest.AppendData(effect); count++;
            }
        Finish("trail");
    }
}
