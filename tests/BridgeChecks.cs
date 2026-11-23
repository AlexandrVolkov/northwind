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
        // The AI repairs its damaged buildings by itself, paying from its stores.
        var barracks = battle.Actors.First(x => x.Team == 1 && x.Alive && x.Source.TemplateName == "Казарма" && x.ConstructionRemaining <= 0);
        int fullB = barracks.Template.InitialCondition; barracks.Record[0x19] = (byte)(fullB - 25);
        var aiBefore = battle.AiResources(3);
        Run(90);
        Check(barracks.Record[0x19] >= fullB && !barracks.Repairing, $"The AI repairs a damaged barracks at once ({barracks.Record[0x19]} of {fullB})");
        var aiAfter = battle.AiResources(3);
        Check(aiAfter.First != aiBefore.First || aiAfter.Second != aiBefore.Second || aiAfter.Third != aiBefore.Third, "The AI's repair is paid from its stores");
        // Collapse: the construction frames run backwards, the building then vanishes for good.
        Check(battle.Demolish(factory) && battle.Ruins.Count > 0 && !factory.Alive, "A demolished building starts to fall apart");
        Run(2);
        Check(battle.Ruins.Count > 0 && battle.Stones.Count > 0, "The falling building raises dust");
        Run(Skirmish.CollapseTicks / 18.2 + 1);
        Check(battle.Ruins.Count > 0, "The ruin stays for a while after the collapse");
        Run(Skirmish.RuinTicks / 18.2 + 6);
        Check(battle.Ruins.Count == 0, "The ruin is gone after its time");
    }
}

public partial class TestRunner
{
    /// <summary>Flow around the buildings: recruits step off the exit, a blocked exit is cleared one unit at a time, idle units leave the rings of service buildings,
    /// workers retry their last order after a pause, mines and sawmills are built anywhere.</summary>
    private void FlowChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json");
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        void Run(double seconds) { for (int t = 0; t < (int)(seconds * 18.2); t++) battle.Advance(1 / 18.2); }
        battle.GrantPeople(20);
        // A far sawmill: outside the build radius of the castle.
        var far = battle.PlayerCastle.Cell; Vector2I? site = null;
        for (int r = 24; r < 60 && site == null; r += 2)
            for (int dx = -r; dx <= r && site == null; dx += 3)
                foreach (var dy in new[] { -r, r }) { var c = far + new Vector2I(dx, dy); if (battle.Inside(c) && battle.ProbeMill(c).Status == ConstructionPlacementStatus.Clear) { site = c; break; } }
        Check(site != null && (new Vector2(site.Value.X * 32, site.Value.Y * 32) - battle.PlayerCastle.Position).Length() > rules.Economy.BuildRadius, $"A sawmill site beyond the build radius is accepted ({site})");
        // A recruit steps away from the exit; units in the way of the exit are moved.
        battle.QueueWorker();
        Run(60);
        var workers = battle.Actors.Where(a => a.Team == 0 && a.Worker && a.Alive).ToList();
        Check(workers.Count >= 1, $"The castle produces a worker ({workers.Count})");
        var exitCell = battle.PlayerCastle.Cell;
        Check(battle.Actors.Where(a => a.Team == 0 && a.Worker && a.Id == battle.LastRecruitId).All(a => a.Stepping || a.Goal != null || (a.Cell - exitCell).Length() > 3), "The new worker walks away from the exit");
        // An idle soldier standing right beside the castle is moved out of the ring.
        var soldier = battle.Actors.First(a => a.Team == 0 && a.Alive && !a.Structure && !a.Worker && !a.Hero);
        var ring = battle.PlayerCastle.Cell + new Vector2I(-battle.PlayerCastle.Parameters[3] / 2 - 1, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(soldier.Record.AsSpan(4), (uint)(ring.X * 32 + 16)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(soldier.Record.AsSpan(8), (uint)(ring.Y * 32 + 16));
        soldier.Goal = null; soldier.Target = 0; soldier.Route.Clear();
        Check(battle.IsServiceCell(soldier.Cell, 0), "The test cell lies in the ring of the castle");
        Run(6);
        Check(!battle.IsServiceCell(soldier.Cell, 0), $"An idle unit leaves the ring of the castle (now {soldier.Cell})");
        // A worker that lost its job takes up the last order again after a pause.
        var w = workers[0]; var tree = Enumerable.Range(0, 3000).Select(i => new Vector2I(i % battle.Map.Width, i / battle.Map.Width)).FirstOrDefault(c => battle.Harvestable(c));
        battle.GatherTimber([w.Id], tree);
        Check(w.LastJob is { Kind: WorkerJobKind.Timber } && w.GatherTree != null, "A felling order is remembered");
        w.GatherTree = null; w.Goal = null; w.Route.Clear(); w.Stepping = false;   // the job failed
        Run(Skirmish.WorkerRetrySeconds + 3);
        Check(w.GatherTree != null || w.Goal != null || w.Route.Count > 0 || w.Stepping, "An idle worker tries its last order again after a pause");
        battle.Move([w.Id], battle.PlayerCastle.Cell + new Vector2I(0, 8));
        Check(w.LastJob == null, "A move order makes the worker forget the job");
    }
}

public partial class TestRunner
{
    /// <summary>Two workers sent to the same tree both fell: the one that finds it taken goes to a neighbouring free tree instead of waiting.</summary>
    private void ChopNearbyChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json");
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        battle.GrantPeople(10);
        for (int i = 0; i < 3; i++) battle.QueueWorker();
        for (int t = 0; t < (int)(150 * 18.2); t++) battle.Advance(1 / 18.2);
        var workers = battle.Actors.Where(a => a.Team == 0 && a.Worker && a.Alive).Take(2).ToList();
        Check(workers.Count == 2, $"Two workers are available ({workers.Count})");
        // A tree with free land on both sides and more trees around it.
        Vector2I? tree = null;
        for (int y = 3; y < battle.Map.Height - 3 && tree == null; y++)
            for (int x = 3; x < battle.Map.Width - 3 && tree == null; x++)
            {
                var c = new Vector2I(x, y);
                if (battle.Harvestable(c) && battle.CanStand(workers[0], c + Vector2I.Left) && battle.CanStand(workers[1], c + Vector2I.Right)
                    && Enumerable.Range(-2, 5).SelectMany(dy => Enumerable.Range(-2, 5).Select(dx => c + new Vector2I(dx, dy))).Count(battle.Harvestable) >= 4) tree = c;
            }
        Check(tree != null, "A clump of trees with free sides exists");
        foreach (var (w, side) in workers.Zip([Vector2I.Left, Vector2I.Right]))
        {
            var at = tree!.Value + side;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(w.Record.AsSpan(4), (uint)(at.X * 32 + 16)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(w.Record.AsSpan(8), (uint)(at.Y * 32 + 16));
            w.Goal = null; w.Route.Clear(); w.Stepping = false;
        }
        battle.GatherTimber(workers.Select(w => w.Id), tree!.Value);
        var chopped = new HashSet<uint>(); var trees = new HashSet<Vector2I>();
        for (int t = 0; t < 60 * 18; t++)
        {
            battle.Advance(1 / 18.2);
            foreach (var w in workers) if (w.JobText.StartsWith("Рубит")) { chopped.Add(w.Id); if (w.GatherTree is { } g) trees.Add(g); }
        }
        Check(chopped.Count == 2, $"Both workers fell trees ({chopped.Count} of 2, trees {trees.Count})");
        Check(trees.Count >= 2, "The workers fell different trees instead of one waiting for the other");
    }
}

public partial class TestRunner
{
    /// <summary>A felled tree strikes a worker standing in its cell only with a probability (1 % by default).</summary>
    private void TreeFallChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json");
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        Check(battle.TreeKillPercent == 1, "A felled tree kills a worker in its cell with a probability of 1 %");
        battle.GrantPeople(10);
        for (int i = 0; i < 4; i++) battle.QueueWorker();
        for (int t = 0; t < (int)(200 * 18.2); t++) battle.Advance(1 / 18.2);
        var workers = battle.Actors.Where(a => a.Team == 0 && a.Worker && a.Alive).Take(4).ToList();
        Check(workers.Count == 4, $"Four workers for the felling check ({workers.Count})");
        var trees = Enumerable.Range(0, battle.Map.Width * battle.Map.Height).Select(i => new Vector2I(i % battle.Map.Width, i / battle.Map.Width))
            .Where(c => c.X >= 4 && c.Y >= 4 && c.X < battle.Map.Width - 4 && c.Y < battle.Map.Height - 4 && battle.Harvestable(c) && battle.CanStand(workers[0], c + Vector2I.Left)).Take(40).ToList();
        // The victim stands in the tree's cell; with 100 % the next felled load kills it, with 0 % it survives.
        int pair = 0;
        foreach (var (percent, expectDead) in new[] { (0, false), (100, true) })
        {
            var tree = trees[pair * 10]; int victimIndex = pair * 2 + 1, chopperIndex = pair * 2; pair++;
            battle.TreeKillPercent = percent;
            var victim = workers[victimIndex];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(victim.Record.AsSpan(4), (uint)(tree.X * 32 + 16)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(victim.Record.AsSpan(8), (uint)(tree.Y * 32 + 16));
            var chopper = workers[chopperIndex];
            var at = tree + Vector2I.Left;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(chopper.Record.AsSpan(4), (uint)(at.X * 32 + 16)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(chopper.Record.AsSpan(8), (uint)(at.Y * 32 + 16));
            battle.GatherTimber([chopper.Id], tree);
            for (int t = 0; t < 40 * 18 && victim.Alive; t++) battle.Advance(1 / 18.2);
            Check(victim.Alive != expectDead, $"With {percent} % the worker in the felled cell {(expectDead ? "dies" : "survives")}");
        }
    }
}

public partial class TestRunner
{
    /// <summary>The player's development tree decides what the lists offer; a barracks hires the units of its row that the tree allows.</summary>
    private void ProducerChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json");
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        GD.Print("AVAILABLE " + string.Join(" ", new[] { 0, 1, 3, 4, 5, 6, 7, 9, 10, 12, 13, 14, 15, 16, 17, 18, 21, 22, 23, 24, 26 }.Select(i => $"{i}:{(battle.Available(i) ? "y" : "n")}")));
        Check(battle.Available(0) && battle.Available(3), "Militia and the barracks are available at the start");
        battle.GrantPeople(20);
        var barracks = battle.BuildIndex(3, battle.PlayerCastle.Cell + new Vector2I(8, 8)) ?? battle.BuildIndex(3, battle.PlayerCastle.Cell + new Vector2I(-8, 8)) ?? battle.BuildIndex(3, battle.PlayerCastle.Cell + new Vector2I(8, -8));
        Check(barracks != null, "A barracks is placed from the castle list");
        for (int t = 0; t < (int)(300 * 18.2) && barracks!.ConstructionRemaining > 0; t++) battle.Advance(1 / 18.2);
        Check(Skirmish.IsPlayerProducer(barracks!), "The barracks is a finished producer");
        Check(battle.QueueUnit(barracks!, 0), "The barracks queues a militiaman");
        for (int t = 0; t < (int)(120 * 18.2); t++) battle.Advance(1 / 18.2);
        Check(battle.Actors.Count(a => a.Team == 0 && a.Alive && a.Source.TemplateName == "Ополченец") >= 2, "The militiaman comes out of the barracks");
        Check(!battle.Available(15), "The factory needs both the barracks and the sawmill");
        var mill = battle.BuildIndex(4, battle.PlayerCastle.Cell + new Vector2I(-8, -8)) ?? battle.BuildIndex(4, battle.PlayerCastle.Cell + new Vector2I(0, -9)) ?? battle.BuildIndex(4, battle.PlayerCastle.Cell + new Vector2I(9, 0));
        for (int t = 0; t < (int)(300 * 18.2) && mill != null && mill.ConstructionRemaining > 0; t++) battle.Advance(1 / 18.2);
        Check(mill != null && battle.Available(15), "A finished barracks and sawmill open the factory in the player's tree");
        Check(!battle.Available(12) && !battle.Available(22), "The laboratory and the stable are still closed");
        bool archer = battle.Available(1);
        Check(battle.QueueUnit(barracks!, 1) == archer, $"The archer is queued only if the tree allows it ({archer})");
    }
}
