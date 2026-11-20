using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Observation: a second AI settlement (slot 1) in the east fights the red one; both run the original AI.</summary>
    private void SkirmishRivalChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.RivalSlot = 1;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        void Run(double seconds) { for (int t = 0; t < (int)(seconds * 18.2); t++) battle.Advance(1 / 18.2); }
        var rivalCastle = battle.Actors.FirstOrDefault(a => a.Team == 2 && a.Source.TemplateName == "Замок");
        Check(rivalCastle != null && rivalCastle.Cell.X > 95 && rivalCastle.Record[3] == 1, "The rival settlement stands in the east (slot 1)");
        Check(battle.Diplomacy.AtWar(3, 1) && !battle.Diplomacy.AtWar(0, 1), "The rival is at war with the red settlement only");
        bool marched = false;
        for (int second = 0; second < 35 * 60; second += 10)
        {
            Run(10);
            marched |= battle.Actors.Count(a => a.Team == 1 && a.Alive && !a.Structure && !a.Worker && a.Cell.X > 90) >= 5;
            if (marched && battle.Corpses.Count(c => c[1] == 2 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(7)) > 85 * 32) >= 3) break;
        }
        Check(battle.Actors.Any(a => a.Team == 2 && a.Source.TemplateName == "Казарма") && battle.Actors.Any(a => a.Team == 1 && a.Source.TemplateName == "Казарма"),
            "Both AI settlements build up (barracks)");
        Check(marched, "The red AI marches its army on the rival");
        Check(battle.Corpses.Count(c => c[1] == 2 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(7)) > 85 * 32) >= 3, "They fight in the east (corpses)");
        GD.Print($"Rival: red {battle.Actors.Count(a => a.Team == 1 && a.Alive)}, rival {battle.Actors.Count(a => a.Team == 2 && a.Alive)}, after {battle.Elapsed / 60:0} min");
    }
}
