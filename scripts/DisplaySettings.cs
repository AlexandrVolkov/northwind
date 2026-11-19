using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;
namespace Northwind;

/// <summary>Screen settings of the port (not part of the original game, which ran at 640×480): window resolution from 720p to 4K
/// in 16:9 and 16:10, fullscreen, and the classic 4:3 layout. Autoload «Display»; saved in user://display.json.
/// F11 or Alt+Enter toggles fullscreen; F9, «Экран» on the menu panel or the main menu opens <see cref="DisplaySettingsWindow"/>.</summary>
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

    private DisplaySettingsWindow _window = null!;
    public static DisplaySettings? Instance { get; private set; }
    public static bool WindowOpen => Instance?._window.Visible == true;
    /// <summary>Opens the settings scroll (F9, «Экран» on the menu panel or the main menu).</summary>
    public static void Open() { if (Instance != null) Instance._window.Visible = true; }
    public static void Close() { if (Instance != null) Instance._window.Visible = false; }
    public static DisplaySettingsWindow? Window => Instance?._window;

    public override void _Ready()
    {
        Layer = 100; ProcessMode = ProcessModeEnum.Always; Instance = this;
        Load();
        _window = new DisplaySettingsWindow { Visible = false }; AddChild(_window);
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
        if (key.Keycode == Key.F11 || (key.Keycode == Key.Enter && key.AltPressed)) { Set(fullscreen: !Fullscreen); GetViewport().SetInputAsHandled(); }
        else if (key.Keycode == Key.F9) { _window.Visible = !_window.Visible; GetViewport().SetInputAsHandled(); }
    }
}
