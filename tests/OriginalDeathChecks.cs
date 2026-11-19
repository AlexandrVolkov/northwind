using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalDeathChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/death.json");
        var root = reference.RootElement; var results = root.GetProperty("results");
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Death reference template {item.Name} matches bundled DAT");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        void Finish(string name)
        {
            var expected = results.GetProperty(name);
            Check(count == expected.GetProperty("case_count").GetInt32(), $"Death {name} reference coverage ({count})");
            Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), $"Death {name} matches original x86");
            count = 0;
        }
        static int[] Ints(JsonElement e) => e.EnumerateArray().Select(v => v.GetInt32()).ToArray();

        var spec = root.GetProperty("dispatch");
        foreach (int flags in Ints(spec.GetProperty("flags")))
        foreach (int hp in Ints(spec.GetProperty("health")))
        foreach (int marks in Ints(spec.GetProperty("marks")))
        {
            var template = Dat(0); BinaryPrimitives.WriteUInt32LittleEndian(template.AsSpan(0x40), 0x1800u | (uint)flags);
            BinaryPrimitives.WriteUInt32LittleEndian(template.AsSpan(0xa7), 0x5678);
            var unit = CombatUnit(0, 0, (byte)hp, (ushort)marks, 80, 80, 1);
            var (special, voice) = OriginalDeathRules.Dispatch(unit, template);
            var calls = new List<string>();
            if (special) calls.Add("[\"12D08\"]");
            else { if (voice is { } v) calls.Add($"[\"A416\", {v}]"); calls.Add("[\"A4DF\"]"); }
            digest.AppendData(unit); digest.AppendData([0xff]); digest.AppendData(Encoding.UTF8.GetBytes("[" + string.Join(", ", calls) + "]")); count++;
        }
        Finish("dispatch");

        spec = root.GetProperty("death");
        foreach (int index in Ints(spec.GetProperty("templates")))
        foreach (int hp in Ints(spec.GetProperty("health")))
        foreach (int marks in Ints(spec.GetProperty("marks")))
        foreach (int counter in Ints(spec.GetProperty("counters")))
        foreach (int parity in Ints(spec.GetProperty("parity")))
        foreach (int heading in Ints(spec.GetProperty("headings")))
        foreach (var cell in spec.GetProperty("cells").EnumerateArray())
        foreach (var corpses in spec.GetProperty("corpses").EnumerateArray())
        foreach (var kinds in spec.GetProperty("kinds").EnumerateArray())
        {
            var template = Dat(index);
            Ints(kinds).Select(v => (byte)v).ToArray().CopyTo(template, 0x4f); Ints(corpses).Select(v => (byte)v).ToArray().CopyTo(template, 0x53);
            int cx = cell[0].GetInt32(), cy = cell[1].GetInt32();
            var unit = CombatUnit((byte)index, 0, (byte)hp, (ushort)marks, (uint)(cx * 32 + 16), (uint)(cy * 32 + 16), 1);
            unit[0x15] = (byte)counter; unit[0x2e] = (byte)parity; unit[0x25] = (byte)heading;
            var world = new DeathProbe((byte)((cy * 8 + cx) % 8));
            OriginalDeathRules.Step(unit, template, world);
            digest.AppendData(unit); digest.AppendData(Encoding.UTF8.GetBytes("[" + string.Join(", ", world.Calls) + "]")); count++;
        }
        Finish("death");
    }
    private sealed class DeathProbe(byte terrain) : IDeathWorld
    {
        public List<string> Calls { get; } = [];
        public void ClearOccupancy() { Calls.Add("[\"9A8E\"]"); Calls.Add("[\"43B0\", 0]"); }
        public void Corpse(byte army, byte code, uint x, uint y) => Calls.Add($"[\"9727\", {army}, {code}, {x}, {y}]");
        public void Remove() => Calls.Add("[\"98DB\"]");
        public byte TerrainClass() => terrain;
    }
}
