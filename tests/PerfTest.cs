using Godot;
using Northwind;
public partial class TestRunner
{
    private void Perf()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.RivalSlot = 1; rules.HordeSlot = 4; rules.Fauna = true;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        for (int minute = 1; minute <= 25; minute++)
        {
            battle.ProfileOn = minute >= 20;
            var sw = System.Diagnostics.Stopwatch.StartNew(); int steps = (int)(60 * 18.2);
            for (int t = 0; t < steps; t++) battle.Advance(1 / 18.2);
            if (minute % 5 == 0) GD.Print($"PERF min {minute}: {sw.Elapsed.TotalMilliseconds / steps:0.00} ms/tick, units {battle.Actors.Count(a => a.Alive && !a.Structure)}, actors {battle.Actors.Count}");
        }
        foreach (var (name, peak) in battle.ProfilePeak.OrderByDescending(p => p.Value).Take(8)) GD.Print($"PEAK {name,-14} {peak:0.0} ms");
        foreach (var (name, v) in battle.Profile.OrderByDescending(p => p.Value.Ms)) GD.Print($"PROF {name,-14} {v.Ms / v.Calls:0.000} ms/call × {v.Calls} = {v.Ms / (5 * 60 * 18.2):0.00} ms/tick");
    }
}
