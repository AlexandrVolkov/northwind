using Godot;
using Northwind;
public partial class TestRunner
{
    private void PerfProbe()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.RivalSlot = 1;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        var wall = System.Diagnostics.Stopwatch.StartNew(); double last = 0;
        for (int minute = 1; minute <= 30; minute++)
        {
            for (int t = 0; t < 60 * 18; t++) battle.Advance(1 / 18.2);
            if (minute % 10 == 0) { double now = wall.Elapsed.TotalSeconds; GD.Print($"min {minute}: actors {battle.Actors.Count(a => a.Alive)}  {(now - last) / (600 * 18.2) * 1000:0.00} ms per tick (budget 54.9)  wall {now:0.0}s"); last = now; }
        }
    }
}
