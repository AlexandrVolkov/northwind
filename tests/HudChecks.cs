using Godot;
using Northwind;
using System.Text.Json;

public partial class TestRunner
{
    private void OriginalCommandPanelChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/command-panel.json");
        foreach (var c in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            int mask = 0;
            for (int slot = 0; slot < 6; slot++) if (OriginalCommandPanel.Enabled(c[1].GetByte(), slot)) mask |= 1 << slot;
            Check(mask == c[2].GetInt32(), "Command visibility matches original LE:1 6EC1..6F17");
        }
        var templates = InterfaceData.Load().Armies.Single(a => a.Army == 2).Templates;
        foreach (var c in fixture.RootElement.GetProperty("groups").EnumerateArray())
        {
            var members = c.GetProperty("members").EnumerateArray().Select(m => new CommandPanelMember(m[1].GetByte(), templates[m[0].GetInt32()])).ToArray();
            var commands = OriginalCommandPanel.Compose(members);
            int mask = 0;
            for (int k = 0; k < 6; k++) if (commands[k] != null) mask |= 1 << k;
            Check(mask == c.GetProperty("mask").GetInt32(), "Group command intersection matches original B72D..B762");
        }
    }
    private async Task HudChecks()
    {
        OriginalCommandPanelChecks();
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>();
        AddChild(scene);
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        scene.SetProcess(false); // Keep orders and camera assertions independent of frame time.
        void LeftClick(Vector2 hudPoint) => scene._UnhandledInput(new InputEventMouseButton
        { ButtonIndex = MouseButton.Left, Pressed = true, Position = scene.HudOrigin + hudPoint * scene.HudScale });
        void Press(Key physical, bool ctrl = false, bool echo = false) => scene._UnhandledInput(new InputEventKey
        { PhysicalKeycode = physical, Keycode = (Key)'И', Pressed = true, CtrlPressed = ctrl, Echo = echo });
        async Task CameraSettles()
        { for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        var archer = scene.Battle.Actors.First(a => a.Team == 0 && a.Source.TemplateName == "Лучник");
        for (int count = 0; count < 3; count++)
        {
            scene.Selection.Clear();
            if (count > 0) scene.Selection.Add(archer.Id);
            if (count > 1) scene.Selection.Add(scene.Battle.Hero.Id);
            scene.FocusHero(); await CameraSettles();
            var selection = scene.Selection.Order().ToArray();
            var local = new Vector2(80 + count * 12, 90);
            var expected = local / OriginalHud.Minimap.Size * new Vector2(scene.Battle.Map.Width, scene.Battle.Map.Height) * 32;
            LeftClick(OriginalHud.Minimap.Position + local); await CameraSettles();
            Check(scene.VisibleWorld().GetCenter().DistanceTo(expected) < 2,
                $"Scaled minimap click centers the map with {count} selected units");
            Check(scene.Selection.Order().SequenceEqual(selection) && scene.PendingOrder == 0,
                "Minimap navigation preserves selection and does not arm an order");
        }
        scene.Selection.Clear(); scene.Selection.Add(archer.Id);
        var camera = scene.Camera.Position;
        LeftClick(OriginalHud.MenuButton.GetCenter());
        LeftClick(OriginalHud.Minimap.GetCenter());
        Check(scene.Hud.MenuOpen && scene.Camera.Position == camera, "Menu panel consumes clicks over the hidden minimap");
        scene._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });
        Check(!scene.Hud.MenuOpen && scene.Selection.Contains(archer.Id), "Escape closes the panel without clearing selection");
        LeftClick(OriginalHud.Slot(1).GetCenter());
        Check(scene.PendingOrder == 2, "Archer arrows button arms movement through scaled HUD input");
        scene._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });
        Check(scene.PendingOrder == 0 && scene.Selection.Contains(archer.Id), "Escape cancels targeting and preserves the unit selection");
        LeftClick(OriginalHud.Slot(1).GetCenter());
        var goals = scene.Battle.Actors.Select(a => a.Goal).ToArray();
        scene._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = scene.MapScreenRect.GetCenter() });
        Check(scene.PendingOrder == 0 && scene.Battle.Actors.Select(a => a.Goal).SequenceEqual(goals),
            "Right click cancels targeting without sending a contextual world order");
        LeftClick(OriginalHud.Slot(1).GetCenter()); LeftClick(OriginalHud.MenuButton.GetCenter());
        scene.Restart();
        Check(scene.PendingOrder == 0 && !scene.Hud.MenuOpen, "Restart clears targeting and the open panel");
        scene.SelectArmy();
        Check(scene.Hud.Commands().Count(c => c is { Present: true }) == 3, "Mixed hero and soldier selection exposes three common commands");
        LeftClick(OriginalHud.Slot(1).GetCenter());
        Check(scene.PendingOrder == 2, "Group movement can be armed from the original panel");
        LeftClick(OriginalHud.Slot(0).GetCenter());
        Check(scene.PendingOrder == 0 && scene.Selection.Count == 5, "Group stop preserves the whole selection");
        Press(Key.B);
        Check(scene.PendingOrder == 2, "Physical B uses the original DAT hotkey even with a Cyrillic logical key");
        Press(Key.C);
        Check(scene.PendingOrder == 0 && scene.Selection.Count == 5, "Original C stop takes precedence over the legacy castle shortcut");
        var beforeAttackKey = scene.Camera.Position;
        Press(Key.F);
        Check(scene.PendingOrder == 3 && scene.Camera.Position == beforeAttackKey, "Original F attack takes precedence over the legacy focus shortcut");
        Press(Key.Escape);
        Press(Key.B, ctrl: true); Press(Key.B, echo: true);
        Check(scene.PendingOrder == 0, "Modifier shortcuts and key repeat do not activate DAT commands");
        scene.Hud.MenuOpen = true; Press(Key.B);
        Check(scene.PendingOrder == 0, "Menu panel prevents command hotkeys");
        scene.Hud.MenuOpen = false;
        Press(Key.Home);
        Check(scene.Selection.SetEquals([scene.Battle.PlayerCastle.Id]), "Home always selects the castle despite original C binding");
        scene.SelectArmy();
        if (DisplayServer.GetName() != "headless")
        {
            scene.FocusHero(); await CameraSettles(); scene.QueueRedraw(); scene.Hud.QueueRedraw();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            GetViewport().GetTexture().GetImage().SavePng("res://build/hud-group.png");
        }
        scene.Selection.Clear(); scene.Selection.Add(scene.Battle.Hero.Id);
        var heroTemplate = scene.Hud.Template(scene.Battle.Hero)!;
        Check(heroTemplate.Slots.Where((s, k) => s.Present && OriginalCommandPanel.Enabled(scene.Battle.Hero.Record[15], k)).Count() == 3,
            "Original hero WorkFlags exposes three commands instead of all six template icons");
        LeftClick(OriginalHud.Slot(3).GetCenter());
        Check(scene.PendingOrder == 0, "Hidden duplicate hero command does not react to clicks");

        var farm = scene.Battle.BuildFarm(FarmSite(scene.Battle)) ?? throw new InvalidOperationException("Farm failed");
        for (int i = 0; i < 5000 && scene.Battle.FreePeople == 0; i++) scene.Battle.Advance(1.0 / scene.Battle.Rules.TicksPerSecond);
        var worker = Recruit(scene.Battle, true) ?? throw new InvalidOperationException("Worker failed");
        scene.Selection.Clear(); scene.Selection.Add(worker.Id);
        var workerTemplate = scene.Hud.Template(worker)!;
        Check(workerTemplate.Slots[0].Label == "Стоять" && workerTemplate.Slots[1].Label == "Идти", "Worker commands retain decoded DAT labels");
        Check(OriginalCommandKeys.Hint(scene.Hud.CommandAt(OriginalHud.Slot(3).GetCenter())!) == "Рубить лес [H]", "Hovered worker button exposes its source label and scan-code key");
        Press(Key.H); Check(scene.PendingOrder == 4, "Original H hotkey selects forestry");
        Press(Key.J); Check(scene.PendingOrder == 8, "Original J hotkey selects the mine command");
        if (DisplayServer.GetName() != "headless")
        {
            scene.FocusWorld(worker.Position); await CameraSettles();
            Input.WarpMouse(scene.HudOrigin + OriginalHud.Slot(4).GetCenter() * scene.HudScale);
            await CameraSettles(); scene.QueueRedraw(); scene.Hud.QueueRedraw();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng("res://build/hud-command-hint.png");
        }
        LeftClick(OriginalHud.Slot(1).GetCenter());
        Check(scene.PendingOrder == 2, "Worker move command works despite having a different sprite from the soldier");
        LeftClick(OriginalHud.Slot(0).GetCenter());
        Check(scene.PendingOrder == 0 && worker.Goal == null, "Worker stop command cancels targeting");
        LeftClick(OriginalHud.Slot(3).GetCenter());
        Check(scene.PendingOrder == 4, "Worker forestry command arms forest targeting");
        scene.FocusWorld(worker.Position); await CameraSettles();
        var worldPoint = GetViewport().GetCanvasTransform() * worker.Position;
        scene._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = worldPoint });
        Check(scene.PendingOrder == 4 && worker.GatherTree == null, "Forestry targeting rejects a non-forest click without moving the worker");
        var forest = (from y in Enumerable.Range(1, scene.Battle.Map.Height - 2)
                      from x in Enumerable.Range(1, scene.Battle.Map.Width - 2)
                      let cell = new Vector2I(x, y)
                      where scene.Battle.Harvestable(cell)
                      orderby cell.DistanceSquaredTo(worker.Cell)
                      select cell).First();
        scene.FocusWorld((Vector2)(forest * 32)); await CameraSettles();
        worldPoint = GetViewport().GetCanvasTransform() * (Vector2)(forest * 32 + Vector2I.One * 16);
        scene._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = worldPoint });
        Check(scene.PendingOrder == 0 && worker.GatherTree == forest, "Forestry button and forest click start the existing harvest cycle");
        scene.Battle.Stop(scene.Selection);
        LeftClick(OriginalHud.Slot(4).GetCenter());
        Check(scene.PendingOrder == 8, "Worker mine command replaces forest targeting");
        scene.Battle.Paused = true;
        LeftClick(OriginalHud.Slot(1).GetCenter());
        Check(scene.PendingOrder == 8, "Paused battle does not accept a new panel command");
        scene.Battle.Paused = false;
        for (int i = 0; i < 9000 && scene.Battle.FreePeople < 4; i++) scene.Battle.Advance(1.0 / scene.Battle.Rules.TicksPerSecond);
        var mineSite = (from y in Enumerable.Range(1, scene.Battle.Map.Height - 2)
                        from x in Enumerable.Range(1, scene.Battle.Map.Width - 2)
                        let cell = new Vector2I(x, y)
                        where scene.Battle.ProbeMine(cell).Status == ConstructionPlacementStatus.Clear
                        orderby cell.DistanceSquaredTo(scene.Battle.PlayerCastle.Cell)
                        select cell).First();
        var mine = scene.Battle.BuildMine(mineSite) ?? throw new InvalidOperationException("Mine failed");
        scene.FocusWorld(mine.Position); await CameraSettles();
        worldPoint = GetViewport().GetCanvasTransform() * mine.Position;
        scene._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = worldPoint });
        Check(scene.PendingOrder == 0 && worker.MineJob, "Mine button and mine click start the existing mining cycle");
        scene.Restart();
        if (DisplayServer.GetName() != "headless")
        {
            await CameraSettles(); scene.QueueRedraw(); scene.Hud.QueueRedraw();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            GetViewport().GetTexture().GetImage().SavePng("res://build/hud-navigation.png");
        }
        RemoveChild(scene); scene.Free();
    }
}
