using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/corpse.json (tools/verify_corpse.py): corpse 161D (ARMY__02 type 1), blood 6C7.</summary>
    private void OriginalCorpseChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/corpse.json");
        var root = reference.RootElement; var results = root.GetProperty("results");
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Corpse reference template {item.Name} matches bundled DAT");
        var constants = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json");
        var properties = Convert.FromHexString(GameData.Read<MapData>("res://assets/imported/map.json").SegmentPropertiesHex);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const byte W = 16, H = 16; int count = 0, splashes = 0;
        void Finish(string name)
        {
            var expected = results.GetProperty(name);
            Check(count == expected.GetProperty("case_count").GetInt32(), $"Corpse {name} reference coverage ({count})");
            Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), $"Corpse {name} matches original x86");
            count = 0;
        }
        static byte[] Effect(uint x, uint y, byte kind)
        {
            var raw = Enumerable.Range(0, 0x22).Select(i => unchecked((byte)(i * 5 + 9))).ToArray();
            raw[0] = kind; raw[1] = 2; raw[5] = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(7), x); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0xc), y);
            return raw;
        }
        var spec = root.GetProperty("corpse");
        var cells = Enumerable.Range(0, W * H).Select(i => (uint)i).ToArray();
        IEnumerable<JsonElement> Items(string name) => spec.GetProperty(name).EnumerateArray();
        foreach (var terrain in Items("terrain")) foreach (var support in Items("support")) foreach (var flags in Items("flags"))
        foreach (var heading in Items("headings")) foreach (var indexElement in Items("random_indices")) foreach (var position in Items("positions"))
        {
            uint x = position[0].GetUInt32(), y = position[1].GetUInt32();
            var props = new byte[W * H * 8]; for (int i = 0; i < W * H; i++) props[i * 8] = (byte)terrain.GetInt32();
            var grid = new uint[3 * W * H]; byte[]? rec = null; byte[]? tpl = null;
            if (support.ValueKind != JsonValueKind.Null)
            {
                int t = support.GetInt32(); rec = CombatUnit((byte)t, 1, 50, 0, x, y, 0); tpl = Dat(t);
                grid[(y >> 5) * W + (x >> 5)] = 0x100;
            }
            var dead = CombatUnit(0, 3, 0xf1, (ushort)flags.GetInt32(), x, y, 1); dead[0x25] = (byte)heading.GetInt32();
            BinaryPrimitives.WriteUInt16LittleEndian(dead.AsSpan(1), 0x1234);
            var effect = Effect(x, y, 1);
            var found = OriginalCombatRules.FindStruck(x, y, 0, W, cells, props, (p, c) => grid[p * W * H + c], r => r == 0x100 ? (rec!, tpl!) : null);
            bool supported = found.Reference != 0 && (BinaryPrimitives.ReadUInt32LittleEndian(tpl!.AsSpan(0x40)) & 2) != 0;
            var random = new OriginalTerrainCatalog(properties, constants) { RandomIndex = (byte)indexElement.GetInt32() };
            string spawns = "[]";
            if (OriginalCorpseRules.Create(effect, dead, supported, found.TerrainClass, random.NextRandomByte) is var (bx, by))
            { spawns = $"[[2, 131, {bx}, {by}]]"; splashes++; }
            digest.AppendData(effect); digest.AppendData([random.RandomIndex]); digest.AppendData(Encoding.UTF8.GetBytes(spawns)); count++;
        }
        foreach (uint timer in new uint[] { 1, 2, 600 }) foreach (byte stage in new byte[] { 1, 2, 4 }) foreach (byte variant in new byte[] { 0, 3, 7 })
        {
            var effect = Effect(200, 200, 1); BinaryPrimitives.WriteUInt32LittleEndian(effect.AsSpan(0x18), timer); effect[0x17] = stage; effect[6] = variant;
            OriginalCorpseRules.Step(effect); digest.AppendData(effect); count++;
        }
        Check(splashes > 100, $"Corpses throw blood splashes ({splashes})");
        Finish("corpse");
        foreach (int ah in new[] { 0, 1 }) foreach (byte timer in new byte[] { 1, 2, 5, 0xdc }) foreach (byte stage in new byte[] { 1, 6, 7, 11 })
        {
            var effect = Effect(200, 200, 3); effect[0x16] = timer; effect[0x17] = stage;
            if (ah == 0) OriginalCorpseRules.CreateBlood(effect); else OriginalCorpseRules.StepBlood(effect);
            digest.AppendData(effect); count++;
        }
        Finish("blood");
    }
}
