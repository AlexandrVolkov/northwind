using Godot;
using Northwind;

public partial class TestRunner
{
    private async Task DisplayChecks()
    {
        Check(OriginalScrollRules.Step(new ScrollInput(0, 300, 853, 480, false, false, false, false), 3, 0, 13).Dx == -1, "The left screen edge scrolls one cell left");
        Check(OriginalScrollRules.Step(new ScrollInput(852, 300, 853, 480, false, false, false, false), 3, 0, 13).Dx == 1, "The right edge of a wide screen scrolls right");
        Check(OriginalScrollRules.Step(new ScrollInput(400, 240, 853, 480, false, false, false, false), 3, 0, 13) is { Dx: 0, Dy: 0, CursorFirst: 0 }, "The middle of the screen does not scroll");
        Check(DisplaySettings.Resolutions.Length == 8 && DisplaySettings.Resolutions.Min(r => r.Y) == 720 && DisplaySettings.Resolutions.Max(r => r.X) == 3840,
            "Resolutions span 720p to 4K");
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        foreach (var (size, width) in new[] { (new Vector2I(1280, 720), 853), (new Vector2I(1920, 1080), 853), (new Vector2I(2560, 1440), 853),
                     (new Vector2I(3840, 2160), 853), (new Vector2I(1280, 800), 768), (new Vector2I(1920, 1200), 768), (new Vector2I(2560, 1600), 768),
                     (new Vector2I(3840, 2400), 768), (new Vector2I(1024, 768), 640), (new Vector2I(2560, 1080), 853) })
        {
            GetTree().Root.Size = size;
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var view = scene.GetViewportRect().Size;
            bool fits = scene.HudScale * width <= view.X + 1 && scene.HudScale * 480 <= view.Y + 1 &&
                        (Math.Abs(scene.HudScale * 480 - view.Y) < 1 || Math.Abs(scene.HudScale * width - view.X) < 1);
            Check((int)scene.Hud.Width == width && fits && scene.Hud.MapRect.Size == new Vector2(width - 192, 448),
                $"{size.X}×{size.Y}: logical width {width}, the frame fills the screen ({scene.Hud.Width}, scale {scene.HudScale:0.###})");
            var menu = scene.ClassicToScreen(OriginalHud.MenuButton.GetCenter());
            Check(menu.X > scene.MapScreenRect.End.X && scene.Hud.ToClassic(scene.Hud.FromClassic(OriginalHud.Minimap.GetCenter())) == OriginalHud.Minimap.GetCenter(),
                $"{size.X}×{size.Y}: the panel sits right of the map window");
            if (DisplayServer.GetName() != "headless" && size.X is 1920 or 2560 && size.Y is 1080 or 1600)
            {
                scene.FocusHero();
                for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
                GetViewport().GetTexture().GetImage().SavePng($"res://build/display-{size.X}x{size.Y}.png");
            }
        }
        if (DisplayServer.GetName() != "headless" && DisplayServer.WindowIsFocused())
        {
            GetTree().Root.Size = new Vector2I(1280, 720);
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            scene.FocusWorld(new Vector2(2048, 2048)); var before = scene.Camera.Position;
            Input.WarpMouse(new Vector2(0, 360));
            for (int i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(scene.CursorFamily == OriginalScrollRules.Left && scene.Camera.Position.X < before.X, "The pointer at the left screen edge shows the left scroll cursor and scrolls");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng("res://build/display-scroll-left.png");
            Input.WarpMouse(new Vector2(640, 360));
            var archer = scene.Battle.Actors.First(x => x.Team == 0 && x.Source.TemplateName == "Лучник");
            scene.Selection.Clear(); scene.Selection.Add(archer.Id); scene.FocusWorld(archer.Position);
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.Slot(2).GetCenter()), MouseButton.Left);
            Input.WarpMouse(scene.GetViewport().GetCanvasTransform() * (archer.Position + new Vector2(96, 96)));
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(scene.PendingOrder == 3 && scene.CursorFamily == OriginalOrderCursor.Target && scene.CursorHotSpot == 15, "The attack order shows the grey skull over the ground");
            Input.WarpMouse(scene.GetViewport().GetCanvasTransform() * archer.Position);
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(scene.CursorFamily == OriginalOrderCursor.TargetObject, "Over an own unit the skull turns gold");
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng("res://build/display-attack-cursor.png");
            scene.CancelOrder();
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.WarpMouse(scene.ClassicToScreen(OriginalHud.Slot(1).GetCenter()));
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = scene.ClassicToScreen(OriginalHud.Slot(1).GetCenter()) });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = scene.ClassicToScreen(OriginalHud.Slot(1).GetCenter()) });
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Input.WarpMouse(scene.GetViewport().GetCanvasTransform() * (archer.Position + new Vector2(96, 96)));
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"MOVE pending={scene.PendingOrder} family={scene.CursorFamily} sel={scene.Selection.Count}");
            Check(scene.PendingOrder == 2 && scene.CursorFamily == OriginalOrderCursor.Move, "The move button shows the gold cross over the map");
            scene.CancelOrder();
        }
        RemoveChild(scene); scene.Free();
        GetTree().Root.Size = new Vector2I(1280, 800);
    }
}
