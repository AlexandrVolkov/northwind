using Godot;
using Northwind;

public partial class TestRunner
{
    private async Task HudChecks()
    {
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>();
        AddChild(scene);
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        scene.SetProcess(false); // Keep orders and camera assertions independent of frame time.
        void LeftClick(Vector2 hudPoint) => scene._UnhandledInput(new InputEventMouseButton
        { ButtonIndex = MouseButton.Left, Pressed = true, Position = scene.HudOrigin + hudPoint * scene.HudScale });
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
        if (DisplayServer.GetName() != "headless")
        {
            scene.Hud.QueueRedraw(); await CameraSettles();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            GetViewport().GetTexture().GetImage().SavePng("res://build/hud-navigation.png");
        }
        RemoveChild(scene); scene.Free();
    }
}
