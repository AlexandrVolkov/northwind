using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>The player's bridge (docs/bridges.md): a line across water is paid per segment and laid as bridge objects.</summary>

    private static void BuildTestFactory(Skirmish battle)
    {
        battle.GrantPeople(8);
        for (int r = 4; r < 14; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var cell = battle.PlayerCastle.Cell + new Vector2I(dx, dy);
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || battle.ProbeFactory(cell).Status != ConstructionPlacementStatus.Clear) continue;
                    var factory = battle.BuildFactory(cell);
                    if (factory == null) continue;
                    for (int t = 0; t < (int)(300 * 18.2) && factory.ConstructionRemaining > 0; t++) battle.Advance(1 / 18.2);
                    return;
                }
    }
    private void BridgeChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json");
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        var hero = battle.Hero;
        // The first water row stretch (cells the hero cannot stand on, but a bridge can) with free cells at both ends of a horizontal line.
        Vector2I? start = null, end = null;
        for (int y = 2; y < battle.Map.Height - 2 && start == null; y++)
            for (int x = 2; x < battle.Map.Width - 8 && start == null; x++)
            {
                int run = 0; while (x + run < battle.Map.Width && battle.BridgeCellFree(new(x + run, y)) && !battle.CanStand(hero, new(x + run, y))) run++;
                if (run >= 3 && run <= 6) { start = new(x, y); end = new(x + run - 1, y); }
            }
        Check(start != null, "A river stretch for a bridge exists on the map");
        Check(battle.BuildBridge(start!.Value, end!.Value) == 0, "Without a factory no bridge is laid (they are built through the factory)");
        BuildTestFactory(battle);
        Check(battle.Actors.Any(Skirmish.IsFactory), "The player builds a factory");
        var before = battle.Resources; int actors = battle.Actors.Count;
        int laid = battle.BuildBridge(start!.Value, end!.Value);
        var cells = battle.BridgeCells(start.Value, end.Value);
        Check(laid > 0 && laid == cells.Count, $"The bridge lays one segment per step of the line ({laid} of {cells.Count})");
        Check(battle.Actors.Skip(actors).All(a => a.Source.TemplateIndex == 7 && a.Team == 0 && a.Record[3] == 0), "The segments are bridge objects of the player");
        var price = Skirmish.Price(battle.BridgeTemplate);
        Check(before.First - battle.Resources.First == price.First * (uint)laid && before.Second - battle.Resources.Second == price.Second * (uint)laid, "The wallet is charged per segment");
        Check(battle.BuildBridge(new(0, 0), new(3, 0)) == 0 && battle.Resources.First == before.First - price.First * (uint)laid, "A refused first segment costs nothing");
    }
}
