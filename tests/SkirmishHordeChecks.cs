using System.Buffers.Binary;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>The horde of Teymur (docs/horde.md): the camps of slot 4 count down, found a «Шатер» of ARMY__01 and wake the AI with the character 4.</summary>
    private void SkirmishHordeChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.HordeSlot = 4;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        void Run(double seconds) { for (int t = 0; t < (int)(seconds * 18.2); t++) battle.Advance(1 / 18.2); }
        Check(battle.Camps.Count == 4 && battle.Camps.All(c => c[3] == 4 && c[0x15] == 0 && BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(0x23)) >= 0x2710), "Four camps of the horde wait with the timer 2710h+9E41");
        Run(30);
        Check(!battle.Actors.Any(a => a.Team == 3), "Before the timer runs out the horde has no settlement");
        BinaryPrimitives.WriteUInt32LittleEndian(battle.Camps[0].AsSpan(0x23), 3);
        Run(5);
        var tent = battle.Actors.FirstOrDefault(a => a.Team == 3 && a.Source.TemplateFile == "ARMY__01.DAT");
        Check(tent != null && tent.Source.TemplateName == "Шатер" && tent.Record[3] == 4, "The camp founds a «Шатер» of ARMY__01 for slot 4");
        Check(battle.Camps[0][0x15] == 1, "The camp goes to state 1 (feeding)");
        Check(battle.AiBlock(4)[0x5c] != 0, "The horde's AI block counts its building (+5C)");
        for (int minute = 0; minute < 6; minute++)
        {
            Run(60);
            GD.Print($"Horde +{minute + 1} min: tent done {tent!.ConstructionRemaining <= 0}, units {string.Join(",", battle.Actors.Where(a => a.Team == 3 && a.Alive && !a.Structure).GroupBy(a => a.Source.TemplateName).Select(g => g.Key + g.Count()))}, res {battle.AiResources(4)}");
        }
        var names = battle.Actors.Where(a => a.Team == 3 && !a.Structure && a.Source.TemplateFile == "ARMY__01.DAT").Select(a => a.Source.TemplateName).ToHashSet();
        Check(names.Count >= 4 && names.Contains("Мечник") && names.Contains("Всадник"), $"The Шатер produces ARMY__01 units ({string.Join(", ", names)})");
        GD.Print($"Horde: tent {tent?.Cell}, camps {string.Join(",", battle.Camps.Select(c => c[0x15]))}");
    }
}
