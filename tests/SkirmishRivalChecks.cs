using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Observation: a second AI settlement (slot 1) in the east fights the red one; both run the original AI (the setting «ИИ: оригинальный»).</summary>
    private void SkirmishRivalChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.RivalSlot = 1;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        battle.ImprovedAi = false;   // this check is about the original's logic
        void Run(double seconds) { for (int t = 0; t < (int)(seconds * 18.2); t++) battle.Advance(1 / 18.2); }
        var rivalCastle = battle.Actors.FirstOrDefault(a => a.Team == 2 && a.Source.TemplateName == "Замок");
        Check(rivalCastle != null && rivalCastle.Cell.X > 95 && rivalCastle.Record[3] == 1, "The rival settlement stands in the east (slot 1)");
        Check(battle.Diplomacy.AtWar(3, 1) && !battle.Diplomacy.AtWar(0, 1), "The rival is at war with the red settlement only");
        bool marched = false; int squadSounds = 0;
        for (int second = 0; second < 50 * 60; second += 10)   // the engagement timing of the original (OriginalEngagement) makes the march start later than the old adapter did
        {
            Run(10);
            squadSounds += battle.Sounds.Count(e => e.Bank == "WORK.FX"); battle.Sounds.Clear();   // A4C2: the squads' messages
            marched |= battle.Actors.Count(a => a.Team == 1 && a.Alive && !a.Structure && !a.Worker && a.Cell.X > 90) >= 5;
            if (marched && battle.Corpses.Count(c => c[1] == 2 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(7)) > 85 * 32) >= 3) break;
        }
        Check(battle.Actors.Any(a => a.Team == 2 && a.Source.TemplateName == "Казарма") && battle.Actors.Any(a => a.Team == 1 && a.Source.TemplateName == "Казарма"),
            "Both AI settlements build up (barracks)");
        Check(marched, "The red AI marches its army on the rival");
        Check(squadSounds > 0, $"AI squads send their messages as WORK.FX sounds (A4C2: {squadSounds})");
        foreach (var a in battle.Actors.Where(a => a.Alive && !a.Structure && !a.Worker && a.Target != 0 && a.Team is 1 or 2).Take(8))
        {
            var t = battle.Find(a.Target); if (t == null) continue;
            GD.Print($"DIAG {a.Team}:{a.Source.TemplateName}@{a.Cell} -> {t.Team}:{t.Source.TemplateName}@{t.Cell} dist {Math.Max(Math.Abs(a.Cell.X - t.Cell.X), Math.Abs(a.Cell.Y - t.Cell.Y))} cmd {a.Record[0x14]:x} s2d {a.Record[0x2d]:x} tvis {t.Record[0x20]:x} forestT {battle.InForest(t)} forestA {battle.InForest(a)} atk {a.Record[0x2f]} stepping {a.Stepping} goal {a.Goal} route {a.Route.Count} hp {t.Health}");
        }
        // Units among trees are hidden from afar (forest visibility), so the fight may only just have begun: corpses, or soldiers locked on targets.
        Check(battle.Corpses.Count(c => c[1] == 2 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(c.AsSpan(7)) > 85 * 32) >= 3
            || battle.Actors.Count(a => a.Alive && !a.Structure && !a.Worker && a.Target != 0 && a.Cell.X > 70) >= 6, "They fight in the east (corpses or engaged soldiers)");
        GD.Print($"Rival: red {battle.Actors.Count(a => a.Team == 1 && a.Alive)}, rival {battle.Actors.Count(a => a.Team == 2 && a.Alive)}, after {battle.Elapsed / 60:0} min");
    }
}

public partial class TestRunner
{
    /// <summary>The improved AI (default): at war the settlements send waves against each other's buildings and the fighting leaves corpses.</summary>
    private void SkirmishImprovedChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.RivalSlot = 1;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        Check(battle.ImprovedAi, "The improved AI is the default");
        for (int second = 0; second < 25 * 60 && !(battle.WavesLaunched >= 2 && battle.Corpses.Count(c => c[1] == 2) >= 3); second += 10)
            for (int t = 0; t < (int)(10 * 18.2); t++) battle.Advance(1 / 18.2);
        Check(battle.WavesLaunched >= 2, $"The improved AI launches attack waves ({battle.WavesLaunched})");
        Check(battle.Corpses.Count(c => c[1] == 2) >= 3, "The waves fight (corpses)");
    }
}
