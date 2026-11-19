using Godot;
using Northwind;

public partial class HelpPreview : Control
{
    private readonly OriginalHelpCatalog _catalog = OriginalHelpCatalog.Load();
    private readonly TextureRect _page = new() { CustomMinimumSize = new Vector2(448, 448), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
    private readonly Label _title = new();
    private readonly Button _previous = new() { Text = "← Назад" };
    private readonly Button _next = new() { Text = "Вперёд →" };
    private Texture2D? _texture;
    public byte CurrentPage { get; private set; }

    public override void _Ready()
    {
        var panel = new VBoxContainer { OffsetLeft = 12, OffsetTop = 12, OffsetRight = -12, OffsetBottom = -12 };
        panel.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(panel);
        panel.AddChild(_title);
        var center = new CenterContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; panel.AddChild(center); center.AddChild(_page);
        var buttons = new HBoxContainer(); panel.AddChild(buttons);
        buttons.AddChild(_previous); buttons.AddChild(_next);
        buttons.AddChild(new Label { Text = "Стрелки ←/↑ и →/↓ также переключают страницы" });
        _previous.Pressed += () => Navigate(0xe04b); _next.Pressed += () => Navigate(0xe04d);
        ShowPage(0);
    }

    public void Navigate(ushort key)
    {
        var next = OriginalHelpRules.Step(CurrentPage, key, 0);
        if (next.Redraw) ShowPage(next.Page);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key) return;
        ushort scan = (key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode) switch
        { Key.Left => 0xe04b, Key.Up => 0xe048, Key.Right => 0xe04d, Key.Down => 0xe050, _ => 0 };
        if (scan != 0) { Navigate(scan); GetViewport().SetInputAsHandled(); }
    }

    private void ShowPage(byte number)
    {
        _page.Texture = null; _texture?.Dispose();
        CurrentPage = number; _texture = OriginalHelpCatalog.Texture(_catalog.Pages[number]); _page.Texture = _texture;
        _title.Text = $"Справка «Орды» — страница {number + 1} из 32";
        _previous.Disabled = number == 0; _next.Disabled = number == 31;
    }

    public override void _ExitTree()
    {
        _page.Texture = null; _texture?.Dispose(); _texture = null;
    }
}
