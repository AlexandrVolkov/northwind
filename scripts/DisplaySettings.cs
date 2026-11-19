using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;
namespace Northwind;

/// <summary>Screen settings of the port (not part of the original game, which ran at 640×480): window resolution from 720p to 4K
/// in 16:9 and 16:10, fullscreen, and the classic 4:3 layout. Autoload «Display»; saved in user://display.json.
/// F11 or Alt+Enter toggles fullscreen, F9 opens the settings overlay.</summary>
public partial class DisplaySettings : CanvasLayer
{
    public static readonly Vector2I[] Resolutions =
    [
        new(1280, 720), new(1920, 1080), new(2560, 1440), new(3840, 2160),
        new(1280, 800), new(1920, 1200), new(2560, 1600), new(3840, 2400),
    ];
    public static readonly Vector2I Minimum = new(1280, 720);
    private const string File = "user://display.json";

    public static Vector2I Resolution { get; private set; } = Resolutions[0];
    public static bool Fullscreen { get; private set; }
    /// <summary>Show the original 640×480 frame only (pillarboxed) instead of widening the map window.</summary>
    public static bool ClassicLayout { get; private set; }

    private sealed class Saved
    {
        public int Width { get; set; } = 1280;
        public int Height { get; set; } = 720;
        public bool Fullscreen { get; set; }
        public bool ClassicLayout { get; set; }
    }

    private PanelContainer _panel = null!;
    private Label _state = null!;

    public override void _Ready()
    {
        Layer = 100; ProcessMode = ProcessModeEnum.Always;
        Load();
        BuildOverlay();
        if (DisplayServer.GetName() != "headless") Apply();
    }

    private static void Load()
    {
        try
        {
            if (!FileAccess.FileExists(File)) return;
            var saved = JsonSerializer.Deserialize<Saved>(FileAccess.GetFileAsString(File));
            if (saved == null) return;
            var chosen = new Vector2I(saved.Width, saved.Height);
            Resolution = Resolutions.Contains(chosen) ? chosen : Resolutions[0];
            Fullscreen = saved.Fullscreen; ClassicLayout = saved.ClassicLayout;
        }
        catch (Exception error) { GD.PushWarning($"Display settings ignored: {error.Message}"); }
    }

    private static void Save()
    {
        using var file = FileAccess.Open(File, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(new Saved { Width = Resolution.X, Height = Resolution.Y, Fullscreen = Fullscreen, ClassicLayout = ClassicLayout }));
    }

    /// <summary>Applies the settings to the window. Fullscreen uses the monitor's own resolution (borderless);
    /// a windowed size larger than the screen is reduced to fit, never below 1280×720.</summary>
    public static void Apply()
    {
        DisplayServer.WindowSetMinSize(Minimum);
        if (Fullscreen) { DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen); return; }
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        var screen = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
        var size = Resolution;
        if (size.X > screen.Size.X || size.Y > screen.Size.Y)
        {
            float k = Math.Min(screen.Size.X / (float)size.X, screen.Size.Y / (float)size.Y);
            size = new Vector2I(Math.Max(Minimum.X, (int)(size.X * k)), Math.Max(Minimum.Y, (int)(size.Y * k)));
        }
        DisplayServer.WindowSetSize(size);
        DisplayServer.WindowSetPosition(screen.Position + (screen.Size - size) / 2);
    }

    public static void Set(Vector2I? resolution = null, bool? fullscreen = null, bool? classic = null)
    {
        if (resolution is { } r && Resolutions.Contains(r)) Resolution = r;
        if (fullscreen is { } f) Fullscreen = f;
        if (classic is { } c) ClassicLayout = c;
        Save();
        if (DisplayServer.GetName() != "headless") Apply();
    }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.F11 || (key.Keycode == Key.Enter && key.AltPressed)) { Set(fullscreen: !Fullscreen); Refresh(); GetViewport().SetInputAsHandled(); }
        else if (key.Keycode == Key.F9) { _panel.Visible = !_panel.Visible; Refresh(); GetViewport().SetInputAsHandled(); }
        else if (key.Keycode == Key.Escape && _panel.Visible) { _panel.Visible = false; GetViewport().SetInputAsHandled(); }
    }

    private void BuildOverlay()
    {
        _panel = new PanelContainer { Visible = false };
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.08f, 0.05f, 0.03f, 0.95f), BorderColor = new Color("8a5a20"),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12 });
        AddChild(_panel);
        var column = new VBoxContainer(); _panel.AddChild(column);
        column.AddChild(new Label { Text = "Экран (F9) — настройки переноса" });
        var grid = new GridContainer { Columns = 4 }; column.AddChild(grid);
        foreach (var r in Resolutions)
        {
            var button = new Button { Text = $"{r.X}×{r.Y}", FocusMode = Control.FocusModeEnum.None };
            button.Pressed += () => { Set(resolution: r, fullscreen: false); Refresh(); };
            grid.AddChild(button);
        }
        var full = new Button { Text = "Полный экран (F11, Alt+Enter)", FocusMode = Control.FocusModeEnum.None };
        full.Pressed += () => { Set(fullscreen: !Fullscreen); Refresh(); }; column.AddChild(full);
        var classic = new Button { Text = "Рамка оригинала 4:3 / широкий экран", FocusMode = Control.FocusModeEnum.None };
        classic.Pressed += () => { Set(classic: !ClassicLayout); Refresh(); }; column.AddChild(classic);
        _state = new Label(); column.AddChild(_state);
        Refresh();
    }

    private void Refresh()
    {
        if (_state == null) return;
        _state.Text = $"Окно: {Resolution.X}×{Resolution.Y} · {(Fullscreen ? "полный экран" : "в окне")} · {(ClassicLayout ? "4:3" : "широкий")}";
        _panel.ResetSize(); _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
    }
}
