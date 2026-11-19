using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private void OriginalCursorChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/cursor-animation.json");
        var root = fixture.RootElement;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        foreach (var frame in root.GetProperty("frames").EnumerateArray())
        foreach (var previous in root.GetProperty("previous").EnumerateArray())
        foreach (var timer in root.GetProperty("timers").EnumerateArray())
        foreach (var range in root.GetProperty("ranges").EnumerateArray())
        {
            var next = OriginalCursorRules.Step(new(frame.GetUInt32(), previous.GetByte()), timer.GetByte(), range[0].GetUInt16(), range[1].GetUInt16());
            var output = new byte[5]; BinaryPrimitives.WriteUInt32LittleEndian(output, next.Frame); output[4] = next.LastTimer;
            hash.AppendData(output); count++;
        }
        Check(count == root.GetProperty("case_count").GetInt32() && Convert.ToHexStringLower(hash.GetHashAndReset()) == root.GetProperty("sha256").GetString(),
            "Cursor frame/timer output matches original 3DB5, including byte wrap and dword increment");
    }

    private async Task CursorChecks()
    {
        OriginalCursorChecks();
        var previousMode = Input.MouseMode;
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        scene.SetProcess(false); scene.Hud.SetProcess(false);
        uint initial = scene.Hud.CursorFrame;
        for (int i = 0; i < 20; i++) scene.Hud._Process(1 / OriginalCursorRules.TimerHz);
        Check(scene.Hud.CursorFrame < 15 && scene.Hud.CursorFrame != initial, "Bundled arrow animates in the original inclusive 0..14 range");
        uint beforeHitch = scene.Hud.CursorFrame;
        scene.Hud._Process(1);
        Check(scene.Hud.CursorFrame == (beforeHitch + 1) % 15, "Cursor advances at most once per display update after a frame hitch");
        scene.TogglePause(); uint pausedFrame = scene.Hud.CursorFrame;
        scene.Hud._Process(2);
        Check(scene.Hud.CursorFrame == pausedFrame, "Pause freezes cursor animation while its clock continues");
        scene.TogglePause();
        if (DisplayServer.GetName() != "headless")
        {
            Check(Input.MouseMode == Input.MouseModeEnum.Hidden, "Software cursor replaces the system pointer inside the game window");
            Input.WarpMouse(scene.MapScreenRect.GetCenter());
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            scene.QueueRedraw(); scene.Hud.QueueRedraw();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            GetViewport().GetTexture().GetImage().SavePng("res://build/original-cursor.png");
        }
        RemoveChild(scene); scene.Free();
        Check(Input.MouseMode == previousMode, "Leaving the scene restores the previous system mouse mode");
    }
}
