using Godot;
using Northwind;

public partial class TestRunner
{
    private async Task DisplayChecks()
    {
        Check(SkirmishMain.EdgeDirection(new Vector2(0, 300), new Vector2(1280, 720), 6) == new Vector2(-1, 0), "Left edge scrolls left");
        Check(SkirmishMain.EdgeDirection(new Vector2(1279, 719), new Vector2(1280, 720), 6) == new Vector2(1, 1), "Bottom-right corner scrolls diagonally");
        Check(SkirmishMain.EdgeDirection(new Vector2(640, 360), new Vector2(1280, 720), 6) == Vector2.Zero, "The middle of the screen does not scroll");
        Check(SkirmishMain.EdgeDirection(new Vector2(-5, 300), new Vector2(1280, 720), 6) == Vector2.Zero, "A pointer outside the window does not scroll");
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
        RemoveChild(scene); scene.Free();
        GetTree().Root.Size = new Vector2I(1280, 800);
    }
}
