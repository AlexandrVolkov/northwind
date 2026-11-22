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
        void Run(double seconds) { for (int t = 0; t < (int)(seconds * 18.2); t++) battle.Advance(1 / 18.2); }
        // Four water columns (the hero cannot stand on any of them) between two land cells: two segments of 2×2 at x+1 and x+3 cover them.
        Vector2I? start = null, end = null; int bx = 0, by = 0;
        for (int y = 3; y < battle.Map.Height - 3 && start == null; y++)
            for (int x = 3; x < battle.Map.Width - 8 && start == null; x++)
            {
                bool water = Enumerable.Range(0, 4).All(k => !battle.CanStand(hero, new(x + k, y)) && !battle.CanStand(hero, new(x + k, y - 1)));
                if (water && battle.CanStand(hero, new(x - 1, y)) && battle.CanStand(hero, new(x + 4, y)) && battle.BridgeCellFree(new(x + 1, y)) && battle.BridgeCellFree(new(x + 3, y)))
                { start = new(x, y - 1); end = new(x + 3, y - 1); bx = x; by = y; }
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
        Run(400);
        var built = battle.Actors.Skip(actors).Where(a => a.Index02 == 7).ToList();
        Check(built.All(a => a.ConstructionRemaining <= 0) && built.All(a => a.Record[0x0d] == 0), "The segments finish in the bridge plane 0");
        Check(cells.Select(c => battle.CanStand(hero, c)).Any(ok => ok) && built.Any(a => battle.CanStand(hero, a.Cell)), "A unit can stand on a finished bridge");
        // The hero walks across: the four columns are water, so he can only get over by the bridge.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(4), (uint)((bx - 1) * 32 + 16)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(8), (uint)(by * 32 + 16));
        GD.Print("BRIDGECELLS " + string.Join(",", Enumerable.Range(-1, 6).Select(k => $"{bx + k}:{battle.CanStand(hero, new(bx + k, by))}")) + " segs " + string.Join(" ", built.Select(a => $"{a.Cell}/{a.Record[0x0d]}/{a.Health}/{a.Parameters[0x46]}")));
        battle.Move([hero.Id], new Vector2I(bx + 4, by)); Run(30);
        Check(hero.Cell.X >= bx + 3, $"The hero crosses the river by the bridge ({hero.Cell}, wanted {bx + 4},{by})");
        Check(battle.BuildBridge(new(0, 0), new(3, 0)) == 0 && battle.Resources.First == before.First - price.First * (uint)laid, "A refused first segment costs nothing");
    }
}

public partial class TestRunner
{
    /// <summary>The fence (ARMY__02 5, built through the factory like the bridge): a line of one-cell segments whose ends take their own shapes and which units cannot walk through.</summary>
    private void FenceChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json");
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        var hero = battle.Hero;
        BuildTestFactory(battle);
        battle.LineIndex = 5;
        Vector2I? start = null;
        for (int r = 6; r < 16 && start == null; r++)
            for (int dx = -r; dx <= r && start == null; dx++)
            {
                var c = battle.PlayerCastle.Cell + new Vector2I(dx, r);
                if (Enumerable.Range(0, 5).All(k => battle.BridgeCellFree(c + new Vector2I(k, 0)) && battle.CanStand(hero, c + new Vector2I(k, 0)))) start = c;
            }
        Check(start != null, "A free stretch for a fence exists");
        int laid = battle.BuildBridge(start!.Value, start.Value + new Vector2I(4, 0));
        Check(laid == 5, $"The fence lays one segment per cell ({laid})");
        for (int t = 0; t < (int)(200 * 18.2); t++) battle.Advance(1 / 18.2);
        var segments = battle.Actors.Where(a => a.Index02 == 5).OrderBy(a => a.Cell.X).ToList();
        Check(segments.Count == 5 && segments.All(a => a.Alive && a.ConstructionRemaining <= 0), "The fence segments are built");
        // A branch hanging from the middle makes a T: a horizontal line is shape 0 everywhere, the branch and the junction take other shapes.
        int branch = battle.BuildBridge(start.Value + new Vector2I(2, 1), start.Value + new Vector2I(2, 3));
        for (int t = 0; t < (int)(200 * 18.2); t++) battle.Advance(1 / 18.2);
        var all = battle.Actors.Where(a => a.Index02 == 5).ToList();
        Check(branch == 3 && all.Select(a => a.Record[0x22]).Distinct().Count() >= 2, $"A branch gives the fence more shapes ({string.Join(",", all.Select(a => a.Record[0x22]))})");
        Check(segments.All(a => !battle.CanStand(hero, a.Cell)), "Units cannot stand on a fence");
    }
}

public partial class TestRunner
{
    /// <summary>Repair (the «Ремонт» button) pays per condition step and restores a damaged building; a destroyed building falls apart and disappears.</summary>
    private void RepairChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json");
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        void Run(double seconds) { for (int t = 0; t < (int)(seconds * 18.2); t++) battle.Advance(1 / 18.2); }
        BuildTestFactory(battle);
        var factory = battle.Actors.First(Skirmish.IsFactory);
        int full = factory.Template.InitialCondition;
        factory.Record[0x19] = (byte)(full - 30);
        var before = battle.Resources;
        Check(battle.StartRepair(factory) && factory.Repairing, "The repair of a damaged building starts");
        Run(120);
        var after = battle.Resources;
        Check(factory.Record[0x19] >= full && !factory.Repairing, $"The repair restores the condition ({factory.Record[0x19]} of {full})");
        Check(after.First < before.First || after.Second < before.Second || after.Third < before.Third, "The repair is paid from the stores");
        Check(!battle.StartRepair(factory), "A sound building needs no repair");
        // Collapse: the construction frames run backwards, the building then vanishes for good.
        Check(battle.Demolish(factory) && battle.Ruins.Count > 0 && !factory.Alive, "A demolished building starts to fall apart");
        Run(2);
        Check(battle.Ruins.Count > 0 && battle.Stones.Count > 0, "The falling building raises dust");
        Run(Skirmish.CollapseTicks / 18.2 + 1);
        Check(battle.Ruins.Count == 0, "Nothing is left of the building after the collapse");
    }
}
