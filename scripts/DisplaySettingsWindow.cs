using Godot;
namespace Northwind;

/// <summary>Screen settings drawn in the game's own style: the scroll window HISTORY.SPR 51 (250×200), MENU2/FONT_01 text and the
/// gold of the menu sliders. A port addition; the original had no such window. Layout in 640×480 coordinates, centred.</summary>
public partial class DisplaySettingsWindow : Node2D
{
    public static readonly Vector2 Size = new(640, 480);
    public static readonly Rect2 Window = new(195, 140, 250, 200);
    private static readonly Color Gold = new(186 / 255f, 146 / 255f, 12 / 255f);
    private Texture2D _scroll = null!, _cursor = null!;
    private InterfaceAtlas? _cursorAtlas;
    private OriginalFont _font = null!;
    private float _scale = 1; private Vector2 _origin;
    private readonly List<(Rect2 Hit, string Text, bool Current, Action Do)> _items = [];

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest; ProcessMode = ProcessModeEnum.Always;
        var data = InterfaceData.Load();
        _scroll = GD.Load<Texture2D>(InterfaceData.Root + "window_scroll.png");
        _font = OriginalFont.Load(data, "FONT_01.FNT");
        _cursorAtlas = data.WorkCursors; if (_cursorAtlas != null) _cursor = GD.Load<Texture2D>(InterfaceData.Root + _cursorAtlas.Atlas);
    }

    /// <summary>Menu entries with their hit areas (640×480 coordinates).</summary>
    public IReadOnlyList<(Rect2 Hit, string Text, bool Current, Action Do)> Items()
    {
        _items.Clear();
        var r = DisplaySettings.Resolutions;
        for (int k = 0; k < r.Length; k++)
        {
            var res = r[k]; int column = k / 4, row = k % 4;
            var at = new Vector2(Window.Position.X + 28 + column * 112, Window.Position.Y + 48 + row * 16);
            Add(at, $"{res.X} на {res.Y}", !DisplaySettings.Fullscreen && DisplaySettings.Resolution == res, () => DisplaySettings.Set(resolution: res, fullscreen: false));
        }
        Add(new(Window.Position.X + 28, Window.Position.Y + 120), DisplaySettings.Fullscreen ? "Полный экран: да" : "Полный экран: нет", false,
            () => DisplaySettings.Set(fullscreen: !DisplaySettings.Fullscreen));
        Add(new(Window.Position.X + 28, Window.Position.Y + 136), DisplaySettings.ClassicLayout ? "Рамка: оригинал 4:3" : "Рамка: широкий экран", false,
            () => DisplaySettings.Set(classic: !DisplaySettings.ClassicLayout));
        string back = "Назад";
        Add(new(Window.GetCenter().X - _font.Width(back) / 2f, Window.End.Y - 30), back, false, () => Visible = false);
        return _items;
    }

    private void Add(Vector2 at, string text, bool current, Action action) => _items.Add((new Rect2(at - new Vector2(2, 2), _font.Width(text) + 4, 16), text, current, action));

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var size = GetViewportRect().Size;
        _scale = Math.Max(.25f, Math.Min(size.X / Size.X, size.Y / Size.Y)); _origin = ((size - Size * _scale) / 2).Floor();
        QueueRedraw();
    }

    private Vector2 Mouse => (GetViewport().GetMousePosition() - _origin) / _scale;

    public override void _Draw()
    {
        DrawSetTransform(_origin, 0, Vector2.One * _scale);
        DrawTexture(_scroll, Window.Position);
        string title = "Экран";
        _font.Draw(this, new Vector2(Window.GetCenter().X - _font.Width(title) / 2f, Window.Position.Y + 6), title);
        _font.Draw(this, new Vector2(Window.Position.X + 28, Window.Position.Y + 30), "16:9");
        _font.Draw(this, new Vector2(Window.Position.X + 140, Window.Position.Y + 30), "16:10");
        var mouse = Mouse;
        foreach (var (hit, text, current, _) in Items())
        {
            _font.Draw(this, hit.Position + new Vector2(2, 2), text);
            if (current) DrawRect(new Rect2(hit.Position.X - 6, hit.Position.Y + 6, 4, 4), Gold);
            if (hit.HasPoint(mouse)) DrawRect(new Rect2(hit.Position.X + 2, hit.End.Y - 2, hit.Size.X - 4, 1), Gold);
        }
        if (_cursorAtlas != null && DisplayServer.GetName() != "headless")
            DrawTextureRectRegion(_cursor, new Rect2(mouse, 32, 32), _cursorAtlas.Region(0));
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b) Click((b.Position - _origin) / _scale);
        else if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) Visible = false;
        if (e is InputEventMouse or InputEventKey { Keycode: not Key.F9 and not Key.F11 }) GetViewport().SetInputAsHandled();
    }

    /// <summary>Click in 640×480 coordinates (tests).</summary>
    public void Click(Vector2 point)
    {
        foreach (var (hit, _, _, action) in Items()) if (hit.HasPoint(point)) { action(); break; }
        QueueRedraw();
    }
}
