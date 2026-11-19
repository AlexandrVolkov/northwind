using Godot;
using Northwind;

public partial class TestRunner
{
    private async Task MenuChecks()
    {
        GetTree().Root.Size = new Vector2I(1280, 960);
        var menus = GD.Load<PackedScene>("res://scenes/menus.tscn").Instantiate<OriginalMenus>(); menus.MissionScene = ""; AddChild(menus);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(menus.Current == OriginalMenus.Screen.History, "The game starts with the scrolling history");
        Check(menus.HeroNames.SequenceEqual(["Олег", "Игорь", "Эйрик"]), "Hero names come from the EXE");
        int before = menus.HistoryOffset(); menus.AdvanceTicks(70);
        Check(menus.HistoryOffset() - before == 10, "History moves one pixel per 7 timer ticks (1B1B7)");
        menus.AdvanceTicks((500 - 278) * 7 - 70);   // first line at y = 278, the moment of docs/reference/original-history.png
        if (DisplayServer.GetName() != "headless") { await Shot(menus, "menus-history"); Compare("menus-history", "original-history", 12); }
        menus.AdvanceTicks(1360 * 7 + 30 * 7 + 7);
        Check(menus.Current == OriginalMenus.Screen.Main, "The history ends after its last line leaves the screen");
        if (DisplayServer.GetName() != "headless") { await Shot(menus, "menus-main"); Compare("menus-main", "original-main-menu", 12); }
        menus.Click(OriginalMenus.SignGame.GetCenter());
        Check(menus.Current == OriginalMenus.Screen.Slots, "«Игра» opens the save chest");
        if (DisplayServer.GetName() != "headless") { await Shot(menus, "menus-slots"); Compare("menus-slots", "original-save-slots", 12); }
        menus.Click(OriginalMenus.SlotRow(0).GetCenter());
        foreach (char c in "Алекс") menus.Key(Key.None, c);
        menus.Key(Key.Enter, '\0');
        Check(menus.Current == OriginalMenus.Screen.Slots && menus.Message.StartsWith("Такого персонажа"), "An unknown name is refused like in the original");
        for (int i = 0; i < 5; i++) menus.Key(Key.Backspace, '\0');
        foreach (char c in "олег") menus.Key(Key.None, c);
        menus.Key(Key.Enter, '\0');
        Check(menus.Current == OriginalMenus.Screen.Campaign && GameSession.HeroName == "Олег", "A hero name opens the campaign map");
        if (DisplayServer.GetName() != "headless") { await Shot(menus, "menus-campaign"); Compare("menus-campaign", "original-campaign-map", 12); }
        menus.Click(OriginalMenus.CampaignStart.GetCenter());
        Check(menus.Current == OriginalMenus.Screen.Loading, "«Старт» shows the loading screen");
        menus.Key(Key.Escape, '\0');
        RemoveChild(menus); menus.Free();
        GetTree().Root.Size = new Vector2I(1280, 800);
    }

    private async Task Shot(OriginalMenus menus, string name)
    {
        menus.QueueRedraw();
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
        var image = GetViewport().GetTexture().GetImage();
        float scale = Math.Min(image.GetWidth() / 640f, image.GetHeight() / 480f);
        var origin = new Vector2I((int)((image.GetWidth() - 640 * scale) / 2), (int)((image.GetHeight() - 480 * scale) / 2));
        var frame = image.GetRegion(new Rect2I(origin, new Vector2I((int)(640 * scale), (int)(480 * scale))));
        frame.Resize(640, 480, Image.Interpolation.Nearest); frame.SavePng($"res://build/{name}.png");
    }

    /// <summary>Mean colour difference against a DOSBox screenshot of the original, ignoring the cursor corner and animated areas' noise.</summary>
    private void Compare(string port, string original, double limit)
    {
        var a = Image.LoadFromFile(ProjectSettings.GlobalizePath($"res://build/{port}.png"));
        var b = Image.LoadFromFile(ProjectSettings.GlobalizePath($"res://docs/reference/{original}.png"));
        double sum = 0; int n = 0;
        for (int y = 0; y < 480; y += 2)
        for (int x = 0; x < 640; x += 2)
        {
            var p = a.GetPixel(x, y); var q = b.GetPixel(x, y);
            sum += Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B); n++;
        }
        double mean = sum / n * 255;
        Check(mean < limit, $"{port} matches the original screenshot (mean difference {mean:0.0} of 765)");
    }
}
