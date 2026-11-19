using Godot;
using Northwind;

public partial class TestRunner
{
    private async Task PauseChecks()
    {
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        scene.SetProcess(false);
        void Press(Key key, bool ctrl = false) => scene._UnhandledInput(new InputEventKey
        { PhysicalKeycode = key, Pressed = true, CtrlPressed = ctrl });
        var selected = scene.Selection.ToArray();
        Press(Key.B);
        Check(scene.PendingOrder == 2, "Pause sample starts while choosing a move target");
        scene._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true, Position = scene.MapScreenRect.GetCenter() });
        scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.MenuButton.GetCenter()), MouseButton.Left);
        scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.PauseButton.GetCenter()), MouseButton.Left);
        Check(scene.Battle.Paused && scene.Hud.MenuOpen, "Original menu pause button stops the game");
        var battle = scene.Battle; var camera = scene.Camera.Position; var speed = scene.Speed;
        var positions = battle.Actors.Select(a => a.Position).ToArray(); var resources = battle.Resources; double elapsed = battle.Elapsed;
        Press(Key.Home); Press(Key.F); Press(Key.C); Press(Key.R); Press(Key.A, ctrl: true);
        scene._UnhandledInput(new InputEventMouseMotion { Position = scene.MapScreenRect.GetCenter(), Relative = new Vector2(100, 50) });
        scene._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = scene.MapScreenRect.GetCenter() });
        scene._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = scene.MapScreenRect.GetCenter() });
        scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.Minimap.GetCenter()), MouseButton.Left);
        scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.BackButton.GetCenter()), MouseButton.Left);
        scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.GameFaster.GetCenter()), MouseButton.Left);
        scene._Process(10);
        Check(ReferenceEquals(scene.Battle, battle) && scene.Selection.SetEquals(selected) && scene.PendingOrder == 2,
            "Pause ignores selection changes, orders, target clicks and restart shortcuts");
        Check(scene.Camera.Position == camera && scene.Speed == speed && scene.Hud.MenuOpen,
            "Pause freezes camera, minimap and menu controls");
        Check(battle.Elapsed == elapsed && battle.Resources == resources && battle.Actors.Select(a => a.Position).SequenceEqual(positions),
            "Pause preserves time, economy and all actor positions");
        if (DisplayServer.GetName() != "headless")
        {
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            scene.QueueRedraw(); scene.Hud.QueueRedraw();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            GetViewport().GetTexture().GetImage().SavePng("res://build/hud-pause.png");
        }
        Press(Key.Escape);
        Check(!battle.Paused && scene.Selection.SetEquals(selected) && scene.PendingOrder == 2, "Escape resumes with selection and targeting preserved");
        Press(Key.Space); Check(battle.Paused, "Space remains an additional pause shortcut");
        Press(Key.Space); Check(!battle.Paused, "Space can resume the additional shortcut pause");
        RemoveChild(scene); scene.Free();
    }
}
