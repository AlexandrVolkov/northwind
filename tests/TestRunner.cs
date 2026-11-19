using Godot;
using Northwind;

public partial class TestRunner : Node
{
    private int _checks;
    private void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }
    public override async void _Ready()
    {
        try
        {
            GD.Print($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            string mode = OS.GetCmdlineUserArgs().FirstOrDefault() ?? "simulation";
            switch (mode)
            {
                case "scene": await SceneChecks(); break;
                case "hud": await HudChecks(); break;
                case "pause": await PauseChecks(); break;
                case "display": await DisplayChecks(); break;
                case "menus": await MenuChecks(); break;
                case "cursor": await CursorChecks(); break;
                case "status-bars": await StatusBarChecks(); break;
                case "skirmish": await SkirmishChecks(); break;
                case "original-route-search": OriginalRouteSearchChecks(); RouteSearchIntegrationChecks(); break;
                case "original-route-pool": OriginalRoutePoolChecks(); PackedRouteIntegrationChecks(); break;
                case "original-movement-admission": OriginalMovementAdmissionChecks(); MovementBridgeChecks(); break;
                case "original-worker-approach": OriginalWorkerApproachChecks(); break;
                case "original-worker-delivery": OriginalWorkerDeliveryChecks(); break;
                case "original-worker-animation": OriginalWorkerAnimationChecks(); break;
                case "original-forestry": OriginalForestryChecks(); OriginalForestryMapChecks(); break;
                case "production": await ProductionChecks(); break;
                case "mine": await MineChecks(); break;
                case "population": await PopulationChecks(); break;
                case "economy": await EconomyChecks(); break;
                case "simulation": SimulationChecks(); OriginalCommandPanelChecks(); OriginalCursorChecks(); OriginalStatusBarChecks(); OriginalBuildingChecks(); OriginalLifecycleChecks(); OriginalConstructionChecks(); OriginalOccupancyChecks(); OriginalSpatialChecks(); OriginalMotionPreparationChecks(); OriginalLumbermillChecks(); OriginalForestryChecks(); OriginalWorkerAnimationChecks(); OriginalWorkerDeliveryChecks(); OriginalWorkerApproachChecks(); OriginalMovementAdmissionChecks(); MovementBridgeChecks(); OriginalRoutePoolChecks(); PackedRouteIntegrationChecks(); OriginalRouteSearchChecks(); RouteSearchIntegrationChecks(); OriginalPopulationChecks(); OriginalResourceChecks(); OriginalProductionChecks(); OriginalOreChecks(); OriginalCombatChecks(); OriginalAttackChecks(); OriginalArrowChecks(); OriginalDeathChecks(); OriginalTargetChecks(); OriginalDiplomacyChecks(); OriginalAiChecks(); OriginalAiInitChecks(); OriginalAiTurnChecks(); OriginalAiPlanChecks(); OriginalAiUpkeepChecks(); OriginalTechChecks(); OriginalAiResearchChecks(); break;
                case "original-buildings": OriginalBuildingChecks(); OriginalLifecycleChecks(); OriginalConstructionChecks(); OriginalOccupancyChecks(); break;
                case "imported-occupancy": ImportedOccupancyChecks(); break;
                case "original-spatial": OriginalSpatialChecks(); break;
                case "original-motion": OriginalMotionPreparationChecks(); break;
                case "original-lumbermill": OriginalLumbermillChecks(); break;
                case "original-ore": OriginalOreChecks(); break;
                case "original-combat": OriginalCombatChecks(); OriginalAttackChecks(); OriginalArrowChecks(); OriginalDeathChecks(); OriginalTargetChecks(); break;
                case "original-diplomacy": OriginalDiplomacyChecks(); break;
                case "original-tech": OriginalTechChecks(); break;
                case "original-ai": OriginalAiChecks(); OriginalAiInitChecks(); OriginalAiTurnChecks(); OriginalAiPlanChecks(); OriginalAiUpkeepChecks(); OriginalAiResearchChecks(); break;
                case "missing-assets": MissingAssetsChecks(); break;
                default: throw new ArgumentException($"Unknown test mode: {mode}");
            }
            GD.Print($"C# {mode}: PASS ({_checks} checks)"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private void SimulationChecks()
    {
        var rules = GameData.Read<GameRules>("res://data/prototype_rules.json");
        rules.SpawnCenter = [3, 3];
        rules.Units = [new Spawn { Id = 1, Team = 0, Offset = [0, 0] }, new Spawn { Id = 2, Team = 1, Offset = [2, 0] }];
        var map = new MapData { Width = 7, Height = 7, Cells = new uint[49], Terrain = ["ground", "water", "forest"] };
        map.Cells[2 * 7 + 3] = 1; map.Cells[4 * 7 + 3] = 2;
        using var model = new Simulation(); model.Setup(map, rules);
        Check(model.Units.Count == 2, "Two units spawn"); var unit = model.GetUnit(1)!;
        Check(model.MoveUnit(1, new(3, 2)).Count == 0, "Water blocks movement");
        Check(model.MoveUnit(1, new(5, 3)).Count == 0, "Occupied target blocked");
        Check(model.MoveUnit(1, new(7, 3)).Count == 0, "Outside target blocked");
        Check(!model.Attack(1, 2), "Attack at range rejected");
        Check(unit.Moves == 6 && unit.Cell == new Vector2I(3, 3), "Rejected actions leave state unchanged");
        Check(model.RouteCost(model.PathFor(unit, new(3, 4))) == 2, "Forest movement cost");
        Check(model.MoveUnit(1, new(4, 3)).Count == 2 && unit.Moves == 5, "Movement and budget");
        Check(model.Attack(1, 2), "Adjacent attack");
        Check(model.GetUnit(2)!.Hp == 7 && unit.Hp == 9, "Damage and retaliation");
        Check(!model.Attack(1, 2) && unit.Moves == 0, "No double attack");
        Check(model.MoveUnit(1, new(4, 4)).Count == 0, "Exhausted unit cannot move");
        model.EndTurn();
        Check(model.Turn == 2 && unit.Moves == 6 && !unit.Attacked, "Friendly turn resets");
        Check(unit.Hp == 6, "Enemy retaliates on its turn");
        model.GetUnit(2)!.Hp = 0;
        Check(model.UnitAt(new(5, 3)) == null, "Dead unit does not occupy cell");
        Check(model.Outcome() != "", "Victory detected");
        model.EndTurn(); Check(model.Turn == 2, "Finished battle cannot advance");
        model.Setup(map, rules);
        for (int y = 0; y < 7; y++) model.Grid.SetPointSolid(new(4, y), true);
        Check(model.PathFor(model.GetUnit(1), new(6, 6)).Count == 0, "Unreachable path");
        model.GetUnit(2)!.Team = 0; Check(!model.Attack(1, 2), "Friendly fire rejected");
        // Lethal attacks must not trigger retaliation.
        model.Setup(map, rules); model.GetUnit(1)!.Cell = new(4, 3); model.GetUnit(2)!.Hp = 3;
        model.Attack(1, 2); Check(model.GetUnit(1)!.Hp == 10 && model.GetUnit(2) == null, "No retaliation after death");
        // Enemy movement must remain within budget and not overlap friendly units.
        model.Setup(map, rules); model.EndTurn();
        Check(model.Units.Select(u => u.Cell).Distinct().Count() == 2, "AI does not overlap units");
        Check(model.Grid.GetPointWeightScale(new(3, 4)) == 2, "Weighted pathfinding retained");
    }
    private void MissingAssetsChecks()
    {
        Check(!Godot.FileAccess.FileExists("res://assets/imported/map.json"), "Fixture must not contain imported assets");
        var scene = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        AddChild(scene);
        Check(!scene.ReadyToPlay && scene.EndButton.Disabled, "Missing resource fixture shows recovery instructions and disables battle");
        RemoveChild(scene); scene.Free();
    }
    private async Task Click(Main scene, Vector2I cell, MouseButton button)
    {
        var point = scene.GetViewport().GetCanvasTransform() * Main.CenterOf(cell);
        Input.WarpMouse(point); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var e = new InputEventMouseButton { Position = point, ButtonIndex = button, Pressed = true };
        Input.ParseInputEvent(e); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventMouseButton { Position = point, ButtonIndex = button, Pressed = false });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task SceneChecks()
    {
        GetTree().Root.Size = new Vector2I(1280, 800);
        var scene = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>(); AddChild(scene);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(scene.ReadyToPlay, "Imported assets load"); Check(scene.Model.Units.Count == 4, "Four units spawn");
        var unit = scene.Model.GetUnit(2)!; await Click(scene, unit.Cell, MouseButton.Left);
        Check(scene.Selected == 2, "Mouse selection");
        var target = new[] { Vector2I.Right, Vector2I.Down, Vector2I.Left, Vector2I.Up }.Select(d => unit.Cell + d)
            .First(c => { var p = scene.Model.PathFor(unit, c); return p.Count > 1 && scene.Model.RouteCost(p) <= unit.Moves; });
        await Click(scene, target, MouseButton.Right); Check(unit.Cell == target, "Right-click movement");
        for (int i = 0; i < 10; i++) scene._Process(1);
        Check(scene.Animations.Count == 0, "Animation completes");
        Check(scene.Positions[2].DistanceTo(Main.CenterOf(target)) < .1f, "Sprite arrives");
        scene.EndButton.EmitSignal(Button.SignalName.Pressed); Check(scene.Model.Turn == 2, "End-turn button");
        for (int i = 0; i < 100; i++) scene._Process(1);
        scene._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.R, Pressed = true });
        Check(scene.Model.Turn == 1 && scene.Animations.Count == 0, "Reset key");
        Check(scene.OriginalObjects.Count > 0, "Original OBE records imported");
        Check(scene.OriginalObjects.All(o => o.Record.TemplateName.Length > 0 && o.Record.TemplateFile.EndsWith(".DAT")), "OBE objects link to named DAT templates");
        var castle = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates[2];
        Check(castle is { RuntimeRecordBytes: 49, InitialCondition: 127, InitialFrame: 876 }, "Original creation parameters import from DAT");
        Check(castle.Commands.Length == 6 && castle.Commands[1] is { Present: true, Slot: 1, Label: "Ремонт", Mode: 0, HotkeyScanCode: 0x23 },
            "Original repair command imports with its slot, mode and scan code");
        Check(OriginalBuildingLifecycle.QueueDirectCommand(0, castle.Commands[1]) == 0x48,
            "Imported repair command queues the original action state");
        bool rejectedMenuMode = false;
        try { OriginalBuildingLifecycle.QueueDirectCommand(0, castle.Commands[3]); }
        catch (ArgumentException) { rejectedMenuMode = true; }
        Check(rejectedMenuMode, "A placement/menu command cannot silently become a direct order");
        var barracks = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates[3].Construction;
        Check(barracks.FootprintWidth == 4 && barracks.FootprintHeight == 2 && barracks.UpfrontCosts.SequenceEqual(new uint[] { 50, 80, 150, 2 }),
            "Original footprint and four upfront costs import from DAT");
        var costs = barracks.UpfrontCosts;
        var payment = OriginalConstructionRules.TryDebitUpfront(new(50, 80, 150, 2), new(costs[0], costs[1], costs[2], costs[3]));
        Check(payment.Accepted && payment.Balances == new ResourceQuad(), "Imported construction price debits exact balances");
        var sourceMap = GameData.Read<MapData>("res://assets/imported/map.json");
        var properties = Convert.FromHexString(sourceMap.SegmentPropertiesHex);
        var occupancy = new uint[sourceMap.Width * sourceMap.Height * 3]; // Controlled empty fixture, not reconstructed live occupancy.
        var mask = castle.Construction.TerrainMask.ToArray();
        var place = OriginalConstructionRules.ProbePlacement((byte)sourceMap.Width, (byte)sourceMap.Height,
            sourceMap.Cells, properties, occupancy, castle.Construction.FootprintWidth, castle.Construction.FootprintHeight, mask, 608, 720);
        Check(place.Status == ConstructionPlacementStatus.Clear, "SEG properties permit the known castle footprint on a controlled empty map");
        occupancy[21 * sourceMap.Width + 17] = 0x10;
        place = OriginalConstructionRules.ProbePlacement((byte)sourceMap.Width, (byte)sourceMap.Height,
            sourceMap.Cells, properties, occupancy, castle.Construction.FootprintWidth, castle.Construction.FootprintHeight, mask, 608, 720);
        Check(place == new ConstructionPlacement(ConstructionPlacementStatus.Occupied, 0x10), "Controlled occupancy blocks the imported footprint");
        Check(scene.OriginalOccupancy is { MatchesReference: true }, "Main loads the source-backed initial occupancy snapshot");
        var live = new OriginalOccupancyMap((byte)sourceMap.Width, (byte)sourceMap.Height);
        live.LoadCells(scene.OriginalOccupancy!.Grid.Cells);
        place = OriginalConstructionRules.ProbePlacement(live.Width, live.Height, sourceMap.Cells, properties, live.Cells,
            castle.Construction.FootprintWidth, castle.Construction.FootprintHeight, mask, 608, 720);
        Check(place == new ConstructionPlacement(ConstructionPlacementStatus.Occupied, 1) && scene.OriginalOccupancy.References[1].TemplateName == "Замок",
            "Imported castle occupies its true footprint with nonzero runtime reference 1");
        live.WriteFootprint(4, 3, 1, 608, 720, 0);
        place = OriginalConstructionRules.ProbePlacement(live.Width, live.Height, sourceMap.Cells, properties, live.Cells, 4, 3, mask, 608, 720);
        Check(place.Status == ConstructionPlacementStatus.Clear, "Clearing the original footprint changes placement immediately");
        var fresh = OriginalBuildingLifecycle.CreateFreshCastleRecord(2, 0, 2, 608, 720, 1, castle.InitialFrame, castle.InitialCondition);
        OriginalBuildingLifecycle.MarkConstruction(fresh);
        uint appendedReference = 1 + (uint)GameData.Read<ScenarioData>("res://assets/imported/scenario.json").Slots[0].ObjectBytes;
        live.WriteFootprint(4, 3, fresh[0x0d], 608, 720, appendedReference);
        place = OriginalConstructionRules.ProbePlacement(live.Width, live.Height, sourceMap.Cells, properties, live.Cells, 4, 3, mask, 608, 720);
        Check(place == new ConstructionPlacement(ConstructionPlacementStatus.Occupied, appendedReference),
            "A newly created castle record registers in the same mutable occupancy map");
        ImportedSpatialChecks(scene.OriginalOccupancy, sourceMap, castle);
        var before = scene.Model.Units.Select(u => (u.Id, u.Cell, u.Hp, u.Moves)).ToArray();
        scene.ToggleOriginalView();
        Check(scene.OriginalView && scene.EndButton.Disabled, "Original view disables battle");
        scene.FinishTurn();
        scene._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = new Vector2(600, 400) });
        Check(scene.Model.Turn == 1 && before.SequenceEqual(scene.Model.Units.Select(u => (u.Id, u.Cell, u.Hp, u.Moves))), "Viewing original records cannot mutate demo simulation");
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var panel = scene.GetChildren().OfType<CanvasLayer>().Single().GetChildren().OfType<PanelContainer>().Single();
        Check(panel.GetCombinedMinimumSize().Y <= GetTree().Root.Size.Y, "OBE panel fits the viewport");
        if (DisplayServer.GetName() != "headless")
        {
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute("res://build");
            GetTree().Root.GetTexture().GetImage().SavePng("res://build/objects-view.png");
        }
        scene.ToggleOriginalView(); Check(!scene.OriginalView, "Can return to demo");
        scene.OpenObservation();
        Check(scene.Observation!.Visible && scene.Observation.ImageLoaded && scene.Observation.SampleCount == 2, "Verified observations load");
        scene.Observation.ShowSample(1); Check(scene.Observation.ImageLoaded, "Castle body observation loads");
        if (DisplayServer.GetName() != "headless")
        {
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetTree().Root.GetTexture().GetImage().SavePng("res://build/castle-observation.png");
        }
        scene.Observation.Hide();
        scene.OpenGallery();
        Check(scene.Gallery!.Visible && scene.Gallery.AtlasLoaded, "Template gallery opens with a loaded atlas");
        Check(scene.Gallery.EntryCount == 123 && scene.Gallery.CandidateCount > 0 && scene.Gallery.CandidateCount < scene.Gallery.EntryCount, "Gallery reports decoded and unresolved templates");
        if (DisplayServer.GetName() != "headless")
        {
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetTree().Root.GetTexture().GetImage().SavePng("res://build/template-gallery.png");
        }
        scene.Gallery.Hide();
        scene.OpenScenario(); Check(scene.ScenarioDialog!.Visible && scene.ScenarioText!.Text.Length > 0, "Scenario inspector"); scene.ScenarioDialog.Hide();
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute("res://build");
            Check(GetTree().Root.GetTexture().GetImage().SavePng("res://build/dotnet-smoke.png") == Error.Ok, "Rendered screenshot");
        }
        scene.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
