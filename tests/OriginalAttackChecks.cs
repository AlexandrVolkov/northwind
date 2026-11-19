using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalAttackChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/attack.json");
        var root = reference.RootElement; var results = root.GetProperty("results");
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Attack reference template {item.Name} matches bundled DAT");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        void Finish(string name)
        {
            var expected = results.GetProperty(name);
            Check(count == expected.GetProperty("case_count").GetInt32(), $"Attack {name} reference coverage ({count})");
            Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), $"Attack {name} matches original x86");
            count = 0;
        }
        static byte[] Bytes(JsonElement e) => e.EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();

        var spec = root.GetProperty("range");
        foreach (byte attackerIndex in Bytes(spec.GetProperty("attacker")))
        foreach (byte targetIndex in Bytes(spec.GetProperty("targets")))
        foreach (byte pierce in Bytes(spec.GetProperty("pierce")))
        foreach (byte maximum in Bytes(spec.GetProperty("maximum")))
        foreach (byte minimum in Bytes(spec.GetProperty("minimum")))
        foreach (byte mask in Bytes(spec.GetProperty("masks")))
        foreach (byte layer in Bytes(spec.GetProperty("layers")))
        foreach (var offset in spec.GetProperty("offsets").EnumerateArray())
        foreach (byte water in Bytes(spec.GetProperty("water")))
        {
            // The reference keeps the attacker's DAT at template 0 and the record template byte at 0.
            var attackerTemplate = Dat(0); attackerTemplate[0x71] = pierce; attackerTemplate[0x72] = maximum; attackerTemplate[0x73] = minimum;
            BinaryPrimitives.WriteUInt32LittleEndian(attackerTemplate.AsSpan(0x74), mask);
            int dx = offset[0].GetInt32(), dy = offset[1].GetInt32();
            uint tx = (uint)(48 + dx + (water != 0 && dx < 200 ? 352 : 0));
            var attacker = CombatUnit(0, 0, 10, 0, 48, 80, 1);
            var target = CombatUnit(targetIndex, 3, 10, 0, tx, (uint)(80 + dy), layer);
            var targetTemplate = targetIndex == 0 ? attackerTemplate : Dat(targetIndex);
            byte terrain = (byte)((tx >> 5) >= 12 ? 1 : 0);
            byte reach = OriginalCombatRules.Reach(attacker, attackerTemplate, target, targetTemplate, terrain);
            digest.AppendData([reach, (byte)(reach == 0 ? 1 : 0)]); count++;
        }
        Finish("range");

        spec = root.GetProperty("gate");
        foreach (byte delay in Bytes(spec.GetProperty("delays")))
        foreach (byte experience in Bytes(spec.GetProperty("experience")))
        foreach (byte counter in Bytes(spec.GetProperty("counters")))
        {
            var template = Dat(0); template[0x75] = delay;
            var unit = CombatUnit(0, 0, 10, 0, 80, 80, 1); unit[0x16] = experience; unit[0x2f] = counter;
            bool ready = OriginalCombatRules.SwingReady(unit, template);
            digest.AppendData(unit); digest.AppendData([(byte)(ready ? 1 : 0)]); count++;
        }
        Finish("gate");

        spec = root.GetProperty("reward");
        foreach (byte condition in Bytes(spec.GetProperty("conditions")))
        foreach (byte threshold in Bytes(spec.GetProperty("thresholds")))
        foreach (byte experience in Bytes(spec.GetProperty("experience")))
        foreach (byte reward in Bytes(spec.GetProperty("rewards")))
        foreach (byte present in Bytes(spec.GetProperty("present")))
        {
            var template = Dat(2); template[0x46] = threshold; template[0x76] = reward;
            var unit = CombatUnit(0, 0, 10, 0, 80, 80, 1); unit[0x16] = experience;
            BinaryPrimitives.WriteUInt32LittleEndian(unit.AsSpan(0x1c), present != 0 ? 0x100u : 0);
            var target = CombatUnit(2, 3, condition, 0, 80, 80, 1);
            bool alive = OriginalCombatRules.TargetAlive(unit, target, template);
            digest.AppendData(unit); digest.AppendData([(byte)(alive ? 0 : 1)]); count++;
        }
        Finish("reward");

        spec = root.GetProperty("swing");
        foreach (byte heading in Bytes(spec.GetProperty("headings")))
        foreach (var position in spec.GetProperty("positions").EnumerateArray())
        foreach (byte layer in Bytes(spec.GetProperty("layers")))
        {
            var unit = CombatUnit(0, 0, 10, 0, 80, 80, 1); unit[0x22] = 0xff; unit[0x25] = heading;
            BinaryPrimitives.WriteUInt32LittleEndian(unit.AsSpan(0x1c), 0x100);
            // The reference enters 93AD directly; here the same branch is reached through a pending state-3 request.
            unit[0x2d] = 0x30;
            var target = CombatUnit(2, 3, 10, 0, position[0].GetUInt32(), position[1].GetUInt32(), layer);
            OriginalWorkerAnimation.Step(unit, Dat(0), target);
            digest.AppendData(unit); count++;
        }
        Finish("swing");

        using var tail = GameData.Read<JsonDocument>("res://tests/fixtures/update-tail.json");
        var t = tail.RootElement.GetProperty("tail");
        foreach (byte hp in Bytes(t.GetProperty("health")))
        foreach (byte previous in Bytes(t.GetProperty("previous")))
        foreach (byte hasTarget in Bytes(t.GetProperty("targets")))
        foreach (var tflags in t.GetProperty("target_flags").EnumerateArray())
        foreach (byte thp in Bytes(t.GetProperty("target_health")))
        foreach (var flags in t.GetProperty("flags").EnumerateArray())
        foreach (byte tick in Bytes(t.GetProperty("ticks")))
        {
            var unit = CombatUnit(0, 0, hp, (ushort)flags.GetInt32(), 80, 80, 1); unit[0x1a] = previous;
            BinaryPrimitives.WriteUInt32LittleEndian(unit.AsSpan(0x1c), hasTarget != 0 ? 0x100u : 0);
            var target = CombatUnit(0, 3, thp, (ushort)tflags.GetInt32(), 80, 80, 1);
            OriginalCombatRules.AfterUpdate(unit, target, tick);
            digest.AppendData(unit); count++;
        }
        var expected = tail.RootElement.GetProperty("results").GetProperty("tail");
        Check(count == expected.GetProperty("case_count").GetInt32() && Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(),
            "Object update tail 434D..4384 matches original x86");
        count = 0;
    }
}
