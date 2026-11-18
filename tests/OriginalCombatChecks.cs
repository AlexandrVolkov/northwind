using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalCombatChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/combat.json");
        var root = reference.RootElement;
        Check(root.GetProperty("format_version").GetInt32() == 1, "Combat reference schema");
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Combat reference template {item.Name} matches bundled DAT");
        var constants = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json");
        var map = GameData.Read<MapData>("res://assets/imported/map.json");
        var properties = Convert.FromHexString(map.SegmentPropertiesHex);
        var results = root.GetProperty("results");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        void Finish(string name)
        {
            var expected = results.GetProperty(name);
            Check(count == expected.GetProperty("case_count").GetInt32(), $"Combat {name} reference coverage ({count})");
            Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), $"Combat {name} matches original x86");
            count = 0;
        }
        static byte[] Bytes(JsonElement e) => e.EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
        static uint[] Uints(JsonElement e) => e.EnumerateArray().Select(v => v.GetUInt32()).ToArray();
        OriginalTerrainCatalog Random(byte index, byte secondary = 0) => new(properties, constants) { RandomIndex = index, SecondaryIndex = secondary };
        // 8x8 synthetic map of the reference: cell (x,y) uses tile y*8+x whose descriptor class is tile % 8.
        var cells = Enumerable.Range(0, 64).Select(i => (uint)i).ToArray();
        var classes = Enumerable.Range(0, 64).SelectMany(i => new byte[] { (byte)(i % 8), 0, 0, 0, 0, 0, 0, 0 }).ToArray();

        var spec = root.GetProperty("hit");
        foreach (byte attack in Bytes(spec.GetProperty("attacks")))
        foreach (byte kind in Bytes(spec.GetProperty("kinds")))
        foreach (byte defence in Bytes(spec.GetProperty("defences")))
        foreach (byte hp in Bytes(spec.GetProperty("health")))
        foreach (ushort flags in Uints(spec.GetProperty("flags")))
        foreach (byte index in Bytes(spec.GetProperty("random_indices")))
        {
            var template = Dat(0); template[0x44] = defence;
            var unit = CombatUnit(0, 0, hp, flags, 80, 80, 1); var random = Random(index);
            bool hit = OriginalCombatRules.Hit(unit, template, attack, kind, random.NextRandomByte);
            digest.AppendData(unit); digest.AppendData([random.RandomIndex, (byte)(hit ? 0 : 1)]); count++;
        }
        Finish("hit");

        spec = root.GetProperty("lookup");
        foreach (var point in spec.GetProperty("points").EnumerateArray())
        foreach (var footprint in spec.GetProperty("footprints").EnumerateArray())
        foreach (uint flags in Uints(spec.GetProperty("flags")))
        foreach (byte layer in Bytes(spec.GetProperty("layers")))
        foreach (byte present in Bytes(spec.GetProperty("present")))
        {
            uint px = point[0].GetUInt32(), py = point[1].GetUInt32();
            var template = Dat(0); template[3] = (byte)footprint[0].GetInt32(); template[4] = (byte)footprint[1].GetInt32();
            BinaryPrimitives.WriteUInt32LittleEndian(template.AsSpan(0x40), flags);
            var target = CombatUnit(0, 0, 10, 0, 96, 96, 1);
            int occupied = (int)((py >> 5) * 8 + (px >> 5));
            var found = OriginalCombatRules.FindStruck(px, py, layer, 8, cells, classes,
                (plane, cell) => present != 0 && plane == (layer & 3) && cell == occupied ? 0x100u : 0, _ => (target, template));
            var encoded = new byte[6]; BinaryPrimitives.WriteUInt32LittleEndian(encoded, present != 0 ? 0x100u : 0);
            encoded[4] = found.TerrainClass; encoded[5] = (byte)(found.Reference != 0 ? 0 : 1);
            digest.AppendData(encoded); count++;
        }
        Finish("lookup");

        spec = root.GetProperty("strike");
        foreach (byte experience in Bytes(spec.GetProperty("experience")))
        foreach (byte defence in Bytes(spec.GetProperty("defences")))
        foreach (byte hp in Bytes(spec.GetProperty("health")))
        foreach (uint flags in Uints(spec.GetProperty("flags")))
        foreach (byte present in Bytes(spec.GetProperty("present")))
        foreach (byte index in Bytes(spec.GetProperty("random_indices")))
        {
            var template = Dat(0); template[3] = 1; template[4] = 1; template[0x44] = defence;
            BinaryPrimitives.WriteUInt32LittleEndian(template.AsSpan(0x40), flags);
            var target = CombatUnit(0, 3, hp, 0, 96, 96, 1);
            var attacker = CombatUnit(0, 0, 10, 0, 64, 96, 1); attacker[0x16] = experience;
            var effect = Enumerable.Range(0, 0x22).Select(i => unchecked((byte)(i * 5 + 9))).ToArray();
            effect[0] = 0; effect[1] = 0; effect[5] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(effect.AsSpan(7), 100); BinaryPrimitives.WriteUInt32LittleEndian(effect.AsSpan(0xb), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(effect.AsSpan(0xc), 100);
            var random = Random(index);
            var found = OriginalCombatRules.FindStruck(100, 100, effect[5], 8, cells, classes,
                (plane, cell) => present != 0 && plane == 1 && cell == 27 ? 0x100u : 0, _ => (target, template));
            int? sound = OriginalCombatRules.StrikeEffect(effect, attacker, target, template, found.Reference != 0, random.NextRandomByte);
            digest.AppendData(effect); digest.AppendData(target); digest.AppendData([random.RandomIndex]);
            digest.AppendData(Encoding.UTF8.GetBytes(sound is { } s ? $"[{s}]" : "[]")); count++;
        }
        Finish("strike");

        spec = root.GetProperty("vitals");
        var allowed = new byte[] { 1, 1, 1, 0, 1, 1, 1, 1 };
        foreach (byte hp in Bytes(spec.GetProperty("health")))
        foreach (byte maximum in Bytes(spec.GetProperty("maximum")))
        foreach (byte experience in Bytes(spec.GetProperty("experience")))
        foreach (byte mechanical in Bytes(spec.GetProperty("mechanical")))
        foreach (byte state in Bytes(spec.GetProperty("states")))
        foreach (byte tick in Bytes(spec.GetProperty("ticks")))
        foreach (byte index in Bytes(spec.GetProperty("random_indices")))
        foreach (byte action in Bytes(spec.GetProperty("actions")))
        foreach (ushort flags in Uints(spec.GetProperty("flags")))
        foreach (var cell in spec.GetProperty("cells").EnumerateArray())
        foreach (byte layer in Bytes(spec.GetProperty("layers")))
        {
            int cx = cell[0].GetInt32(), cy = cell[1].GetInt32(); bool bridge = cell[2].GetInt32() != 0;
            var template = Dat(0);
            BinaryPrimitives.WriteUInt32LittleEndian(template.AsSpan(0x40), 0x1800u | (mechanical != 0 ? 0x10u : 0));
            template[0x45] = 12; allowed.CopyTo(template, 0x58);
            BinaryPrimitives.WriteUInt32LittleEndian(template.AsSpan(0xa3), 0x1234); BinaryPrimitives.WriteUInt32LittleEndian(template.AsSpan(0xa7), 0x5678);
            var unit = CombatUnit(0, 0, hp, flags, (uint)(cx * 32 + 16), (uint)(cy * 32 + 16), layer);
            unit[0x1a] = maximum; unit[0x16] = experience; unit[0x2d] = state; unit[0x14] = (byte)((unit[0x14] & 0xf8) | action);
            var random = Random(index, unchecked((byte)(index * 3)));
            var world = new VitalsProbe(bridge ? ((layer - 1) & 3, cy * 8 + cx) : (-1, -1));
            if (OriginalCombatRules.Vitals(unit, template, tick, 8, 8, cells, classes, random, world) == VitalsResult.Dead) world.Calls.Add("[\"95D3\"]");
            digest.AppendData(unit); digest.AppendData([random.RandomIndex, random.SecondaryIndex]);
            digest.AppendData(Encoding.UTF8.GetBytes("[" + string.Join(", ", world.Calls) + "]")); count++;
        }
        Finish("vitals");
    }

    private static byte[] CombatUnit(byte template, byte owner, byte hp, ushort flags, uint x, uint y, byte layer)
    {
        var raw = Enumerable.Range(0, 54).Select(i => unchecked((byte)(i * 13 + 1))).ToArray();
        raw[0] = 2; raw[3] = owner; raw[0x0d] = layer; raw[0x0e] = template; raw[0x19] = hp;
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x17), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), x); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), y);
        return raw;
    }
    private sealed class VitalsProbe((int Plane, int Cell) bridge) : IVitalsWorld
    {
        public List<string> Calls { get; } = [];
        public void Sound(uint reference) => Calls.Add($"[\"A416\", {reference}]");
        public uint Occupant(int plane, int cell) => (plane, cell) == bridge ? 0x300u : 0;
    }
}
