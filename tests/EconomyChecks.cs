using Godot;
using Northwind;

public partial class TestRunner
{
    private static Vector2I EconomyTree(Skirmish battle, SkirmishActor worker)
    {
        var trees = new List<Vector2I>();
        for (int y = 0; y < battle.Map.Height; y++) for (int x = 0; x < battle.Map.Width; x++)
        {
            var cell = new Vector2I(x, y);
            if (battle.Harvestable(cell) && ((Vector2)(cell * 32)).DistanceTo(battle.PlayerCastle.Position) < 500) trees.Add(cell);
        }
        var map = GameData.Read<MapData>("res://assets/imported/map.json");
        var properties = Convert.FromHexString(map.SegmentPropertiesHex);
        var constants = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json");
        trees.RemoveAll(tree => {
            var cells = (uint[])battle.Map.Cells.Clone();
            return new OriginalTerrainCatalog(properties, constants).ClearTimber(cells, (byte)map.Width, (byte)map.Height, tree.X, tree.Y) == 0 ||
                OriginalForestryRules.IsTimber(OriginalForestryRules.Property(properties, cells[tree.Y * map.Width + tree.X]));
        });
        foreach (var tree in trees.OrderBy(c => ((Vector2)c).DistanceSquaredTo(worker.Cell)))
        foreach (var d in new[] { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right })
        {
            var cell = tree + d;
            if (battle.CanStand(worker, cell) && battle.Move([worker.Id], cell) == 1 && worker.Goal == cell) return tree;
        }
        throw new InvalidOperationException("No reachable forest near the source castle");
    }
    private static Vector2I EconomyMillCell(Skirmish battle, Vector2I tree)
    {
        var cells = new List<Vector2I>();
        for (int y = -12; y <= 12; y++) for (int x = -12; x <= 12; x++) cells.Add(battle.PlayerCastle.Cell + new Vector2I(x, y));
        var p = battle.MillTemplate.Construction;
        return cells.OrderBy(c => ((Vector2)c).DistanceSquaredTo(tree)).First(c =>
            !new Rect2I(c - new Vector2I(p.FootprintWidth / 2, p.FootprintHeight / 2), new(p.FootprintWidth, p.FootprintHeight)).HasPoint(tree) &&
            battle.ProbeMill(c).Status == ConstructionPlacementStatus.Clear);
    }
    private async Task EconomyChecks()
    {
        OriginalLumbermillChecks(); OriginalForestryMapChecks();
        using (var battle = CreateSkirmish())
        {
            uint initial = battle.Wood;
            var worker = battle.TrainWorker() ?? throw new InvalidOperationException("Could not hire initial worker");
            Check(worker.Worker && !worker.Structure && worker.Record.Length == 54 && battle.Wood == initial - battle.Rules.Economy.WorkerPrice,
                "Castle hires a mobile 54-byte worker and debits the configured wood price");
            var tree = EconomyTree(battle, worker);
            Check(battle.GatherTimber([battle.Hero.Id], tree) == 0 && battle.GatherTimber([worker.Id], tree) == 1,
                "Only workers accept a timber order");
            for (int i = 0; i < 2700 && worker.Record[0x2d] != 0x30; i++) battle.Advance(1.0 / battle.Rules.TicksPerSecond);
            Check(worker.Record[0x2d] == 0x30 && worker.Record[0x2f] == worker.Parameters[0x75] && (worker.Record[0x34] & 31) == 19,
                "Swing request retains the work gate until the animation impact");
            battle.Advance(1.0 / battle.Rules.TicksPerSecond);
            ushort swingFrame = (ushort)(worker.Template.InitialFrame + 8 * worker.Parameters[0x4a] + (worker.Record[0x25] & 7) * worker.Parameters[0x4b]);
            Check(worker.Record[0x2d] == 0x33 && worker.Frame == swingFrame && worker.Record[0x24] == 0,
                "Requested swing enters its original direction-specific frame block");
            battle.Paused = true; var frozenStrike = worker.Record.ToArray(); battle.Advance(10);
            Check(worker.Record.SequenceEqual(frozenStrike), "Pause freezes the entire strike record"); battle.Paused = false;
            for (int i = 0; i < 5; i++) battle.Advance(1.0 / battle.Rules.TicksPerSecond);
            Check(worker.Frame == swingFrame + 5 && worker.Record[0x2f] == worker.Parameters[0x75] && battle.Effects.Count == 0,
                "Pre-impact frames retain the work gate and emit no chopping effect");
            battle.Advance(1.0 / battle.Rules.TicksPerSecond);
            Check(worker.Frame == swingFrame + 6 && worker.Record[0x2f] == 0 && battle.Effects.Count == 1 && worker.Record[0x31] == 0,
                "Native impact frame resets the gate and emits one effect before cargo is granted");
            for (int i = 0; i < 900 && worker.Record[0x31] != 3; i++) battle.Advance(.1);
            Check(worker.Record[0x31] == 3 && worker.Source.TemplateIndex == 11 && battle.Harvestable(tree) && worker.Record[0x34] == 0x20 && worker.Record[0x16] == 5,
                "Worker uses native work counters, retains the grove after one load and switches to loaded template");
            uint before = battle.Wood; battle.Advance(5);
            Check(battle.Wood == before && worker.Record[0x31] == 3 && battle.ProducedWood == 0,
                "Chopping and waiting without a sawmill do not credit the stock");
            var cell = EconomyMillCell(battle, tree);
            var mill = battle.BuildMill(cell) ?? throw new InvalidOperationException("Could not build sawmill");
            Check(mill.ConstructionRemaining > 0 && battle.Wood == before - battle.Rules.Economy.MillPrice &&
                battle.ProbeMill(cell).Status == ConstructionPlacementStatus.Occupied,
                "Construction debits wood and immediately reserves the source footprint");
            before = battle.Wood;
            Check(battle.BuildMill(cell) == null && battle.BuildMill(new(-1, -1)) == null && battle.Wood == before,
                "Occupied and outside placements do not spend tutorial resources");
            battle.Paused = true; double remaining = mill.ConstructionRemaining; battle.Advance(10);
            Check(mill.ConstructionRemaining == remaining && battle.TrainWorker() == null && battle.BuildMill(cell) == null && battle.GatherTimber([worker.Id], tree) == 0,
                "Pause freezes construction and all economy orders");
            battle.Paused = false;
            for (int i = 0; i < 3600 && battle.ProducedWood < 30; i++) battle.Advance(.1);
            Check(battle.ProducedWood == 30 && battle.Wood == initial - battle.Rules.Economy.WorkerPrice - battle.Rules.Economy.MillPrice + 30 &&
                worker.Record[0x31] == 0 && mill.Record[0x24] == 0 && battle.Harvestable(tree),
                $"Three loads travel through the native handoff and processing queue exactly once: wood={battle.Wood}, produced={battle.ProducedWood}, cargo={worker.Record[0x31]}, job={worker.JobText}, worker={worker.Position}, mill={mill.Position}");
            Check(mill.ConstructionRemaining == 0 && mill.Record[0x19] == mill.Template.InitialCondition && mill.Record[0x14] == 0 && mill.Record[0x0f] == 0x20,
                "Native C80D condition increments and completed-action transition finish the building");
            for (int i = 0; i < 3600 && battle.ProducedWood < 70; i++) battle.Advance(.1);
            Check(battle.ProducedWood == 70 && battle.TerrainRevision > 0 && battle.ClearedGroves.Contains(tree) && !battle.Harvestable(tree),
                $"Seventh load applies native terrain blending: produced={battle.ProducedWood}, job={worker.JobText}");
            Check(worker.GatherTree is { } next && next != tree && battle.Harvestable(next), "Worker automatically finds another grove after source terrain replacement");
            for (int i = 0; i < 2700 && (worker.Record[0x2d] & 15) != 3; i++) battle.Advance(1.0 / battle.Rules.TicksPerSecond);
            Check((worker.Record[0x2d] & 15) == 3, "Automatic next-grove task starts another native swing");
            battle.Stop([worker.Id]); before = battle.Wood; battle.Advance(5);
            Check(!OriginalWorkerAnimation.Pending(worker.Record) && worker.GatherTree == null && worker.Record[0x31] == 0,
                "Stop lets the current swing finish, then stays idle without granting cargo");
            Check(battle.Wood == before && battle.ProducedWood == 70, "Stopping the worker prevents further loads");
            var second = EconomyMillCell(battle, tree);
            Check(battle.BuildMill(second) != null && battle.Wood == before - battle.Rules.Economy.MillPrice,
                "Delivered wood participates in a subsequent construction transaction");
        }
        GetTree().Root.Size = new Vector2I(1280, 800);
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        scene.SelectCastle(); scene.HireWorker();
        var hired = scene.Battle.Find(scene.Selection.Single())!;
        Check(hired.Worker, "Castle UI hires and selects the new worker");
        var forest = EconomyTree(scene.Battle, hired);
        scene.IssueOrder((Vector2)(forest * 32) + Vector2.One * 16);
        Check(hired.GatherTree == forest, "Context right-click on forest assigns the selected worker");
        scene.SelectCastle(); scene.BeginMillPlacement();
        Check(scene.PlacingMill, "Castle UI enters placement mode");
        var site = EconomyMillCell(scene.Battle, forest); scene.Camera.Position = (Vector2)(site * 32);
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var screen = scene.GetViewport().GetCanvasTransform() * ((Vector2)(site * 32) + Vector2.One * 16);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(!scene.PlacingMill && scene.Battle.Actors.Any(a => a.Source.TemplateName == "Лесопилка"), "World placement click creates the sawmill");
        scene.TogglePause(); scene.Selection.Clear(); scene.Selection.Add(hired.Id);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            GetViewport().GetTexture().GetImage().SavePng("res://build/economy.png");
        }
        scene.TogglePause();
        for (int i = 0; i < 3000 && ((hired.Record[0x2d] & 15) != 3 || hired.Record[0x24] != 5); i++) scene.Battle.Advance(1.0 / scene.Battle.Rules.TicksPerSecond);
        Check((hired.Record[0x2d] & 15) == 3 && hired.Record[0x24] == 5, "UI worker reaches a source chopping frame");
        scene.TogglePause(); scene.Camera.Position = hired.Position; scene._Process(0);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng("res://build/worker-strike.png");
        }
        scene.TogglePause();
        for (int i = 0; i < 3600 && scene.Battle.ProducedWood < 70; i++) scene.Battle.Advance(.1);
        Check(scene.Battle.ProducedWood >= 70 && scene.Battle.TerrainRevision > 0, "UI-issued hiring, gathering and placement complete seven loads and change terrain");
        scene.TogglePause(); scene._Process(0);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng("res://build/economy-delivery.png");
        }
        RemoveChild(scene); scene.Free();
    }
}
