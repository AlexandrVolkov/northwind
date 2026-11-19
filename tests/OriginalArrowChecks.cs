using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalArrowChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/arrow.json");
        var root = reference.RootElement; var results = root.GetProperty("results");
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Arrow reference template {item.Name} matches bundled DAT");
        var constants = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json");
        var properties = Convert.FromHexString(GameData.Read<MapData>("res://assets/imported/map.json").SegmentPropertiesHex);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        void Finish(string name)
        {
            var expected = results.GetProperty(name);
            Check(count == expected.GetProperty("case_count").GetInt32(), $"Arrow {name} reference coverage ({count})");
            Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), $"Arrow {name} matches original x86");
            count = 0;
        }
        static byte[] Effect(uint x, uint y, byte layer)
        {
            var raw = Enumerable.Range(0, 0x22).Select(i => unchecked((byte)(i * 5 + 9))).ToArray();
            raw[0] = 2; raw[1] = 0; raw[5] = layer; raw[0x11] = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(7), x); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0xc), y);
            return raw;
        }

        var spec = root.GetProperty("create");
        foreach (var origin in spec.GetProperty("origins").EnumerateArray())
        foreach (var target in spec.GetProperty("targets").EnumerateArray())
        foreach (var experience in spec.GetProperty("experience").EnumerateArray())
        foreach (var indexElement in spec.GetProperty("random_indices").EnumerateArray())
        {
            uint ax = origin[0].GetUInt32(), ay = origin[1].GetUInt32(); byte index = (byte)indexElement.GetInt32();
            var shooter = CombatUnit(1, 0, 10, 0, ax, ay, 1); shooter[0x16] = (byte)experience.GetInt32();
            var effect = Effect(ax + 8, ay - 8, 1);
            var random = new OriginalTerrainCatalog(properties, constants) { RandomIndex = index, SecondaryIndex = unchecked((byte)(index * 7)) };
            int sound = OriginalArrowRules.Create(effect, shooter, target[0].GetUInt32(), target[1].GetUInt32(), 512, 512, random.NextRandomByte, random.NextSecondaryByte);
            digest.AppendData(effect); digest.AppendData([random.RandomIndex, random.SecondaryIndex]); digest.AppendData(Encoding.UTF8.GetBytes($"[{sound}]")); count++;
        }
        Finish("create");

        spec = root.GetProperty("step");
        foreach (var remaining in spec.GetProperty("remaining").EnumerateArray())
        foreach (var velocity in spec.GetProperty("velocities").EnumerateArray())
        foreach (var present in spec.GetProperty("present").EnumerateArray())
        foreach (var flags in spec.GetProperty("flags").EnumerateArray())
        foreach (var owner in spec.GetProperty("owners").EnumerateArray())
        foreach (var health in spec.GetProperty("health").EnumerateArray())
        foreach (var defence in spec.GetProperty("defences").EnumerateArray())
        foreach (var indexElement in spec.GetProperty("random_indices").EnumerateArray())
        {
            var template = Dat(2); template[3] = 1; template[4] = 1; template[0x44] = (byte)defence.GetInt32();
            BinaryPrimitives.WriteUInt32LittleEndian(template.AsSpan(0x40), flags.GetUInt32());
            var effect = Effect(200, 200, 1); effect[2] = 0; effect[0x17] = 0x0a;
            BinaryPrimitives.WriteUInt32LittleEndian(effect.AsSpan(0x12), remaining.GetUInt32());
            BinaryPrimitives.WriteInt32LittleEndian(effect.AsSpan(0x18), velocity[0].GetInt32()); BinaryPrimitives.WriteInt32LittleEndian(effect.AsSpan(0x1c), velocity[1].GetInt32());
            var victim = CombatUnit(2, (byte)owner.GetInt32(), (byte)health.GetInt32(), 0, 208, 208, 1);
            var random = new OriginalTerrainCatalog(properties, constants) { RandomIndex = (byte)indexElement.GetInt32() };
            var (state, sound, argument) = OriginalArrowRules.Step(effect, victim, template, present.GetInt32() != 0, 512, 512, random.NextRandomByte);
            string exit = state switch { ArrowState.Dropped => $"[\"7FE\", {argument}]", ArrowState.Stuck => $"[\"AEB\", {argument}]", _ => "null" };
            digest.AppendData(effect); digest.AppendData(victim); digest.AppendData([random.RandomIndex]);
            digest.AppendData(Encoding.UTF8.GetBytes($"[{(sound is { } s ? $"[{s}]" : "[]")}, {exit}]")); count++;
        }
        Finish("step");
    }
}
