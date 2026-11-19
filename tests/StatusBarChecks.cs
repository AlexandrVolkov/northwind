using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private void OriginalStatusBarChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/status-bars.json");
        var root = fixture.RootElement; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); int count = 0;
        foreach (var dimensions in root.GetProperty("dimensions").EnumerateArray())
        foreach (var limit in root.GetProperty("maxima").EnumerateArray())
        for (int value = 0; value <= limit.GetInt32(); value++)
        {
            byte maximum = limit.GetByte(), current = (byte)value;
            byte health = OriginalStatusBarRules.HealthColor(current, maximum); hash.AppendData([health]);
            foreach (byte color in new byte[] { health, 0x94 })
            { hash.AppendData(OriginalStatusBarRules.Pixels(dimensions[0].GetInt32(), dimensions[1].GetInt32(), current, maximum, color)); count++; }
        }
        Check(count == root.GetProperty("case_count").GetInt32() && Convert.ToHexStringLower(hash.GetHashAndReset()) == root.GetProperty("sha256").GetString(),
            "Gauge pixels and health colors match original 6DEF/63CB across source limits and dimensions");
    }

    private async Task StatusBarChecks()
    {
        OriginalStatusBarChecks();
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        scene.SetProcess(false); scene.Hud.SetProcess(false);
        var archer = scene.Battle.Actors.First(a => a.Team == 0 && a.Source.TemplateName == "Лучник");
        scene.Selection.Clear(); scene.Selection.Add(archer.Id); archer.Health = 5; archer.Record[0x16] = 64;
        if (DisplayServer.GetName() != "headless")
        {
            var palette = InterfaceData.Load().UiColors;
            void Pixel(Image image, Vector2 at, int index)
            {
                var p = scene.ClassicToScreen(at + Vector2.One * .5f);
                var rgb = palette[index]; var expected = new Color(rgb[0] / 255f, rgb[1] / 255f, rgb[2] / 255f);
                Check(image.GetPixel((int)p.X, (int)p.Y).IsEqualApprox(expected), $"HUD gauge pixel {at} uses native palette index {index:X}");
            }
            scene.Hud.QueueRedraw(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var image = GetViewport().GetTexture().GetImage();
            Pixel(image, new Vector2(572, 200), 0x44);
            Pixel(image, new Vector2(574, 186), 0x51); Pixel(image, new Vector2(578, 206), 0);
            Pixel(image, new Vector2(589, 201), 0x94); Pixel(image, new Vector2(593, 201), 0);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build")); image.SavePng("res://build/hud-status-bars.png");
            archer.Health = 2; archer.Record[0x16] = 127;
            scene.Hud.QueueRedraw(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            image = GetViewport().GetTexture().GetImage();
            Pixel(image, new Vector2(573, 201), 0x22); Pixel(image, new Vector2(574, 201), 0);
            Pixel(image, new Vector2(594, 201), 0x94);
        }
        RemoveChild(scene); scene.Free();
    }
}
