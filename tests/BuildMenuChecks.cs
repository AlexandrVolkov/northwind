using Godot;
using Northwind;

public partial class TestRunner
{
    private async Task BuildMenuChecks()
    {
        GetTree().Root.Size = new Vector2I(1280, 800);
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var castle = scene.Battle.PlayerCastle;
        scene.Selection.Clear(); scene.Selection.Add(castle.Id); scene.FocusWorld(castle.Position);
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var commands = scene.Hud.Commands();
        int eye = Array.FindIndex(commands, c => c is { Present: true, Sprite: 874 });
        Check(eye >= 0, $"The castle panel has the «eye» button (WORK sprite 874) at slot {eye}");
        scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.Slot(eye).GetCenter()), MouseButton.Left);
        Check(scene.BuildMenuVisible && scene.CastleBuildItems().Length == 5, "The «eye» opens the castle build list: barracks, sawmill, mine, farm, worker");
        Check(scene.Hud.BuildItemAt(scene.Hud.BuildItemRect(0).GetCenter())?.TemplateIndex == 3, "The first list item is the barracks at x = 380, y = 105");
        var price = Skirmish.Price(scene.Battle.BarracksTemplate);
        Check(price is { First: 50, Second: 80, Third: 150, Fourth: 2 }, "Barracks price matches the original hint: metal 50, wood 80, gold 150, people 2");
        var items = scene.CastleBuildItems();
        int pick = Array.FindIndex(items, i => i.TemplateIndex != 10 && i.TemplateIndex != 6 && scene.Battle.CanAfford(i.Template));
        Check(pick >= 0, $"An affordable building is in the list (free people {scene.Battle.FreePeople})");
        var chosen = items[pick].Template; var cost = Skirmish.Price(chosen);
        var before = scene.Battle.Resources;
        scene.Hud.Click(scene.Hud.BuildItemRect(pick).GetCenter(), MouseButton.Left);
        Check(scene.PlacingTemplate == chosen, $"Clicking «{chosen.Name}» starts placement");
        Vector2I? site = null;
        for (int r = 3; r < 12 && site == null; r++)
            for (int dy = -r; dy <= r && site == null; dy++)
                for (int dx = -r; dx <= r && site == null; dx++)
                {
                    var cell = new Vector2I((int)(castle.Position.X / 32) + dx, (int)(castle.Position.Y / 32) + dy);
                    if ((chosen == scene.Battle.MillTemplate ? scene.Battle.ProbeMill(cell) : chosen == scene.Battle.FarmTemplate ? scene.Battle.ProbeFarm(cell) : scene.Battle.ProbeBarracks(cell)).Status == ConstructionPlacementStatus.Clear) site = cell;
                }
        Check(site != null, "A clear site exists near the castle");
        scene.FocusWorld((Vector2)(site!.Value * 32) + Vector2.One * 16);
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var screen = scene.GetViewport().GetCanvasTransform() * ((Vector2)(site.Value * 32) + Vector2.One * 16);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var after = scene.Battle.Resources;
        Check(scene.PlacingTemplate == null && after.First == before.First - cost.First && after.Third == before.Third - cost.Third, "Placing pays the price at once");
        Check(scene.Selection.Contains(castle.Id) && scene.BuildMenuVisible, "After placing, the castle stays selected with its build list open (as the original)");
        Check(scene.Battle.Actors.Count(a => a.Team == 0 && a.Source.TemplateIndex == chosen.TemplateIndex) >= 1, "The construction site exists");
        float zoom = scene.Camera.Zoom.X;
        var center = scene.MapScreenRect.GetCenter();
        scene._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = center });
        Check(scene.Camera.Zoom.X > zoom, "The mouse wheel zooms in");
        for (int i = 0; i < 30; i++) scene._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = center });
        Check(Math.Abs(scene.Camera.Zoom.X - scene.HudScale * SkirmishMain.MinZoom) < 1e-3, "Zoom stops at half the original scale");
        if (DisplayServer.GetName() != "headless")
        {
            scene.Camera.Zoom = Vector2.One * scene.HudScale; scene.FocusWorld(castle.Position + new Vector2(0, -120));
            Input.WarpMouse(scene.HudOrigin + scene.Hud.BuildItemRect(0).GetCenter() * scene.HudScale);
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(scene.CursorFamily == OriginalOrderCursor.Select, "The sickle cursor over the build list");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            GetViewport().GetTexture().GetImage().SavePng("res://build/build-menu.png");
        }
        RemoveChild(scene); scene.Free();
    }
}
