using Godot;
namespace Northwind;

/// <summary>Game screen of the original in its own 640×480 coordinates (docs/interface.md): frame SCREENS.SCR #0 with the
/// map window 16..463, resource counters, minimap 128×128 at (488, 32), selection name and bars, «Меню» and the menu panel
/// SCREENS.SCR #2. Positions were measured on DOSBox screenshots of the original; hit areas of buttons are approximate.</summary>
public partial class OriginalHud : Node2D
{
    public static readonly Vector2 Size = new(640, 480);
    public static readonly Rect2 MapWindow = new(16, 16, 448, 448);
    public static readonly Rect2 Minimap = new(488, 32, 128, 128);
    public static readonly Rect2 MenuButton = new(486, 0, 130, 22);
    public static readonly Rect2 DiplomacyButton = new(480, 446, 146, 30);
    // Menu panel (placed at x = 464): Назад, Панорама −/+, Игра −/+, Пауза, Выход.
    public static readonly Rect2 BackButton = new(490, 0, 126, 22);
    public static readonly Rect2 ScrollSlower = new(470, 78, 18, 20), ScrollFaster = new(624, 78, 18, 20);
    public static readonly Rect2 GameSlower = new(470, 124, 18, 20), GameFaster = new(624, 124, 18, 20);
    public static readonly Rect2 PauseButton = new(494, 402, 124, 24), ExitButton = new(494, 428, 124, 24);
    /// <summary>Port addition: «Экран» (screen settings) on the empty wood between «Тест» and «Пауза».</summary>
    public static readonly Rect2 ScreenButton = new(488, 340, 128, 28);
    private static readonly int[] CounterX = [66, 159, 260, 365];
    private readonly Dictionary<int, Color> _uiColors = [];
    private static readonly Rect2 HealthBar = new(572, 176, 9, 50), ExperienceBar = new(587, 176, 9, 50);

    public SkirmishMain Game { get; set; } = null!;
    /// <summary>Logical width of the screen: 640 as in the original (4:3); wider screens widen the map window to the left of the
    /// panel, which keeps the original pixels and stays at the right edge (port extension, docs/interface.md «Широкий экран»).</summary>
    public float Width { get; set; } = 640;
    public float Shift => Width - 640;
    public Rect2 MapRect => new(16, 16, Width - 192, 448);
    /// <summary>Point of the original 640×480 frame in the current logical screen (panel points move right by <see cref="Shift"/>).</summary>
    public Vector2 FromClassic(Vector2 point) => point.X >= 464 ? point + new Vector2(Shift, 0) : point;
    /// <summary>Logical point back to the original frame for hit tests of the panel.</summary>
    public Vector2 ToClassic(Vector2 point) => point.X >= 464 + Shift ? point - new Vector2(Shift, 0) : point;
    public bool MenuOpen { get; set; }
    /// <summary>Order button slots: two columns, 71 px apart, rows 43 px apart from (485, 313); rows 1–2 measured, row 3 assumed.</summary>
    public static Rect2 Slot(int index) => new(485 + index % 2 * 71, 313 + index / 2 * 43, 69, 35);
    public static readonly Vector2 PortraitAt = new(482, 176);
    public InterfaceArmy Army { get; private set; } = null!;
    private InterfaceAtlas _heroes = null!;
    private Texture2D _portraits = null!, _buttons = null!, _heroAtlas = null!;
    private Texture2D _frame = null!, _menuPanel = null!, _blankButton = null!;
    private OriginalFont _font = null!, _red = null!;
    private InterfaceAtlas? _cursor;
    private Texture2D? _cursorTexture;
    private Input.MouseModeEnum? _previousMouseMode;
    private CursorAnimation _cursorAnimation;
    private double _cursorTime;
    public uint CursorFrame => _cursorAnimation.Frame;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        var data = InterfaceData.Load();
        foreach (var (index, rgb) in data.UiColors) _uiColors[index] = new Color(rgb[0] / 255f, rgb[1] / 255f, rgb[2] / 255f);
        _cursor = data.WorkCursors ?? data.ArrowCursor;
        if (_cursor != null)
        {
            _cursorTexture = GD.Load<Texture2D>(InterfaceData.Root + _cursor.Atlas);
            if (DisplayServer.GetName() != "headless")
            { _previousMouseMode = Input.MouseMode; Input.MouseMode = Input.MouseModeEnum.Hidden; }
        }
        _frame = InterfaceData.Screen("game_frame"); _menuPanel = InterfaceData.Screen("panel_menu"); _blankButton = InterfaceData.Screen("button_blank");
        Army = data.Armies.Single(a => a.Army == 2); _heroes = data.HeroPortraits;
        _portraits = GD.Load<Texture2D>(InterfaceData.Root + Army.Portraits.Atlas); _buttons = GD.Load<Texture2D>(InterfaceData.Root + Army.Buttons.Atlas);
        _heroAtlas = GD.Load<Texture2D>(InterfaceData.Root + _heroes.Atlas);
        _font = OriginalFont.Load(data, "FONT_01.FNT"); _red = OriginalFont.Load(data, "FONT_01.FNT", "font_font_01_red.png");
    }

    public override void _Process(double delta)
    {
        if (_cursor == null || Game?.Battle == null) return;
        _cursorTime += Math.Max(0, delta) * OriginalCursorRules.TimerHz;
        if (!Game.Battle.Paused)
            _cursorAnimation = OriginalCursorRules.Step(_cursorAnimation, unchecked((byte)(ulong)_cursorTime),
                Game.CursorFamily.First, Game.CursorFamily.Last);
        QueueRedraw();
    }

    public override void _ExitTree()
    {
        if (_previousMouseMode is { } previous && Input.MouseMode == Input.MouseModeEnum.Hidden) Input.MouseMode = previous;
    }

    public override void _Draw()
    {
        // Letterbox: everything outside the logical screen stays black.
        DrawRect(new Rect2(-4000, -4000, Width + 8000, 4000), Colors.Black); DrawRect(new Rect2(-4000, 480, Width + 8000, 4000), Colors.Black);
        DrawRect(new Rect2(-4000, 0, 4000, 480), Colors.Black); DrawRect(new Rect2(Width, 0, 4000, 480), Colors.Black);
        DrawFrame();
        if (Game?.Battle == null) return;
        var b = Game.Battle;
        _font.Draw(this, new Vector2(CounterX[0], 0), b.Metal.ToString());
        _font.Draw(this, new Vector2(CounterX[1], 0), b.Gold.ToString());
        _font.Draw(this, new Vector2(CounterX[2], 0), b.Wood.ToString());
        _red.Draw(this, new Vector2(CounterX[3], 0), b.FreePeople.ToString());
        DrawSetTransform(new Vector2(Shift, 0));
        if (MenuOpen)
        {
            DrawTexture(_menuPanel, new Vector2(464, 0)); DrawSlider(103, Game.Pan, 16); DrawSlider(151, Game.GameSpeed - 0xef, 15);
            DrawTexture(_blankButton, ScreenButton.Position);
            _font.Draw(this, new Vector2(ScreenButton.GetCenter().X - _font.Width("Экран") / 2f, ScreenButton.Position.Y + 7), "Экран");
        }
        else { DrawMinimap(); DrawSelection(); }
        DrawSetTransform(Vector2.Zero);
        DrawBuildList();
        if (b.Paused)
        {
            var at = new Vector2(MapRect.GetCenter().X - 79, 224);
            DrawRect(new Rect2(at, 158, 24), new Color(0, 0, 0, .85f));
            _font.Draw(this, at + new Vector2(8, 4), "ПАУЗА (Esc)");
        }
        if (!DisplaySettings.WindowOpen && _cursor != null && _cursorTexture != null && GetViewport().GetVisibleRect().HasPoint(GetViewport().GetMousePosition()))
            DrawTextureRectRegion(_cursorTexture, new Rect2(GetLocalMousePosition() - Vector2.One * (Game?.CursorHotSpot ?? 0), new Vector2(32, 32)),
                _cursor.Region((int)CursorFrame - _cursor.FirstSprite));
    }

    /// <summary>Frame SCREENS.SCR #0: its left part 0..463, the right part 464..639 at the right edge, and for wider screens the
    /// gap of the top and bottom borders filled with copies of the plain stretch 384..463 (no icons there).</summary>
    private void DrawFrame()
    {
        DrawTextureRectRegion(_frame, new Rect2(0, 0, 464, 480), new Rect2(0, 0, 464, 480));
        for (float x = 464; x < 464 + Shift; x += 80)
        {
            float w = Math.Min(80, 464 + Shift - x);
            DrawTextureRectRegion(_frame, new Rect2(x, 0, w, 16), new Rect2(384, 0, w, 16));
            DrawTextureRectRegion(_frame, new Rect2(x, 464, w, 16), new Rect2(384, 464, w, 16));
        }
        DrawTextureRectRegion(_frame, new Rect2(464 + Shift, 0, 176, 480), new Rect2(464, 0, 176, 480));
    }

    /// <summary>Slider of the menu panel as seen on DOSBox screenshots: two 1-px lines at y and y + 5 from x = 491 over a 124-px track,
    /// gold (186,146,12) for round(value · 124 / max) pixels, black for the rest (measured; the drawing routine is not found yet).</summary>
    private void DrawSlider(int y, int value, int maximum)
    {
        int lit = (int)Math.Round(Math.Clamp(value, 0, maximum) * 124.0 / maximum, MidpointRounding.AwayFromZero);
        var gold = new Color(186 / 255f, 146 / 255f, 12 / 255f);
        foreach (int row in new[] { y, y + 5 })
        {
            DrawRect(new Rect2(491, row, 124, 1), Colors.Black);
            if (lit > 0) DrawRect(new Rect2(491, row, lit, 1), gold);
        }
    }

    /// <summary>Castle build list: 80×50 portraits at x = 380 (at the right edge of the map window), y = 105 + 50·k (DOSBox screenshots).</summary>
    public Rect2 BuildItemRect(int k) => new(380 + Shift, 105 + 50 * k, 80, 50);
    public SkirmishMain.BuildItem? BuildItemAt(Vector2 logical)
    {
        if (Game?.Battle == null || !Game.BuildMenuVisible || MenuOpen) return null;
        var items = Game.BuildItems();
        for (int k = 0; k < items.Length; k++) if (BuildItemRect(k).HasPoint(logical)) return items[k];
        return null;
    }

    private void DrawBuildList()
    {
        if (!Game.BuildMenuVisible || MenuOpen) return;
        var items = Game.BuildItems();
        for (int k = 0; k < items.Length; k++)
        {
            var tint = Game.Battle.CanAfford(items[k].Template) ? Colors.White : new Color(.45f, .45f, .45f);
            DrawTextureRectRegion(_portraits, BuildItemRect(k), Army.Portraits.Region(Army.Templates[items[k].TemplateIndex].Portrait), tint);
        }
        if (BuildItemAt(GetLocalMousePosition()) is { } hover) DrawPrice(hover.Template);
    }

    /// <summary>Name and price on the bottom border (DOSBox screenshot of the hovered barracks): name at x = 20, the resource icons of the
    /// top bar at 172/243/306/376 with numbers at 210/280/350/420.</summary>
    private void DrawPrice(TemplateEntry template)
    {
        var price = Skirmish.Price(template);
        _font.Draw(this, new Vector2(20, 464), template.Name);
        foreach (var (src, at, text, value) in new[] { (38, 172, 210, price.First), (132, 243, 280, price.Third), (224, 306, 350, price.Second), (326, 376, 420, price.Fourth) })
        {
            int width = src is 38 or 132 ? 28 : 38;
            DrawTextureRectRegion(_frame, new Rect2(at, 464, width, 16), new Rect2(src, 0, width, 16));
            _font.Draw(this, new Vector2(text, 464), value.ToString());
        }
    }

    private void DrawMinimap()
    {
        if (Game.MinimapTexture != null) DrawTextureRect(Game.MinimapTexture, Minimap, false);
        var world = new Vector2(Game.Battle.Map.Width, Game.Battle.Map.Height) * 32;
        foreach (var actor in Game.Battle.Actors.Where(a => a.Alive))
            DrawRect(new Rect2(Minimap.Position + (actor.Position / world * Minimap.Size).Floor(), Vector2.One),
                SkirmishMain.TeamColor(actor.Team));
        var view = Game.VisibleWorld();
        DrawRect(new Rect2(Minimap.Position + view.Position / world * Minimap.Size, view.Size / world * Minimap.Size), Colors.White, false, 1);
    }

    private SkirmishActor? SingleUnit()
    {
        var units = Game.Selection.Select(Game.Battle.Find).OfType<SkirmishActor>().ToArray();
        return units.Length == 1 ? units[0] : null;
    }

    private void DrawSelection()
    {
        if (SingleUnit() is { } unit)
        {
            _font.Draw(this, new Vector2(482, 160), unit.Hero ? Game.HeroName : unit.Source.TemplateName);
            if (unit.Hero && _heroes.Known.TryGetValue(Game.HeroName, out int hero))
                DrawTextureRectRegion(_heroAtlas, new Rect2(PortraitAt, 64, 70), _heroes.Region(hero - _heroes.FirstSprite));
            else if (Template(unit) is { } template)
                DrawTextureRectRegion(_portraits, new Rect2(PortraitAt, 80, 50), Army.Portraits.Region(template.Portrait));
            byte maximum = unit.Parameters[0x45], condition = (byte)Math.Clamp(unit.Health, 0, maximum);
            DrawBar(HealthBar, condition, maximum, OriginalStatusBarRules.HealthColor(condition, maximum));
            // +16 is experience for living units; buildings use it as a maintenance counter.
            DrawBar(ExperienceBar, unit.Structure ? (byte)0 : (byte)Math.Min(127, (int)unit.Record[0x16]), 127, 0x94);
        }
        else if (Game.Selection.Count > 1) _font.Draw(this, new Vector2(482, 160), $"Выбрано: {Game.Selection.Count}");
        var commands = Commands();
        for (int k = 0; k < commands.Length; k++)
            if (commands[k] is { Present: true, Button: { } button } command)
            {
                DrawTextureRectRegion(_buttons, Slot(k), Army.Buttons.Region(button));
                if ((Game.PendingOrder, command.Mode, command.RawGroup) is (2, 1, 2) or (3, 2, 3) or (4, 4, 4) or (8, 8, 7))
                    DrawRect(Slot(k).Grow(-1), new Color("d5b85c"), false, 1);
            }
        var hover = CommandAt(ToClassic(GetLocalMousePosition()));
        string hint = hover != null ? OriginalCommandKeys.Hint(hover) : Game.PendingOrder switch
        {
            2 => "Укажите место", 3 => "Укажите врага", 4 => "Укажите лес", 8 => "Укажите шахту", _ => "",
        };
        if (Game.PlacingTemplate is { } placing)
        {
            // Placement: the chosen building under the castle (name y = 232, portrait y = 248), measured on the screenshot.
            _font.Draw(this, new Vector2(482, 232), placing.Name);
            int index = placing.TemplateIndex;
            if (index >= 0 && index < Army.Templates.Count) DrawTextureRectRegion(_portraits, new Rect2(482, 248, 80, 50), Army.Portraits.Region(Army.Templates[index].Portrait));
        }
        else DrawHint(hint);
    }

    // Placement and pending-order outlines are UI feedback of the port, not a recovered DOS drawing routine.
    private void DrawHint(string hint)
    {
        string line = ""; int y = 256;
        foreach (string word in hint.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (y >= 304) break;
            string next = line.Length == 0 ? word : line + " " + word;
            if (_font.Width(next) > 142 && line.Length != 0)
            { _font.Draw(this, new Vector2(482, y), line); y += 16; line = word; }
            else line = next;
        }
        if (line.Length != 0 && y < 304) _font.Draw(this, new Vector2(482, y), line);
    }

    public InterfaceSlot? CommandAt(Vector2 point)
    {
        if (MenuOpen) return null;
        var commands = Commands();
        for (int k = 0; k < commands.Length; k++)
            if (Slot(k).HasPoint(point) && commands[k] is { Present: true, Button: not null } command) return command;
        return null;
    }

    public bool Hotkey(Key key)
    {
        if (MenuOpen || Game.Battle.Paused || Game.Battle.Outcome != "") return false;
        int scan = OriginalCommandKeys.Scan(key);
        if (scan < 0) return false;
        var command = Commands().FirstOrDefault(c => c is { Present: true, Button: not null } && c.HotkeyScanCode == scan);
        var unit = Game.Battle.Actors.FirstOrDefault(a => Game.Selection.Contains(a.Id));
        if (command == null || unit == null) return false;
        Game.OrderSlot(unit, command); QueueRedraw(); return true;
    }

    /// <summary>Selection order follows the scene's owner/object list, rather than mouse insertion order.</summary>
    public InterfaceSlot?[] Commands()
    {
        var members = new List<CommandPanelMember>();
        foreach (var unit in Game.Battle.Actors.Where(a => Game.Selection.Contains(a.Id)))
        {
            if (!unit.Alive || unit.Team != 0 || Template(unit) is not { } template) return new InterfaceSlot?[6];
            members.Add(new(unit.Record[0x0f], template));
        }
        return OriginalCommandPanel.Compose(members);
    }

    /// <summary>Order slots of the unit's DAT template (ARMY__02 only for now).</summary>
    public InterfaceTemplate? Template(SkirmishActor unit) =>
        unit.Source.TemplateFile == "ARMY__02.DAT" && unit.Source.TemplateIndex < Army.Templates.Count ? Army.Templates[unit.Source.TemplateIndex] : null;

    private void DrawBar(Rect2 rect, byte current, byte maximum, byte color)
    {
        DrawRect(rect, _uiColors[0]);
        if (rect.Size.Y > rect.Size.X)
        {
            // The health and experience gauges beside the portrait stand upright and fill from the bottom (user, 22.11); the length is the height.
            int rows = OriginalStatusBarRules.FilledColumns((int)rect.Size.Y, current, maximum);
            if (rows > 0) DrawRect(new Rect2(rect.Position.X + 1, rect.End.Y - 1 - rows, rect.Size.X - 2, rows), _uiColors[color]);
        }
        else
        {
            int filled = OriginalStatusBarRules.FilledColumns((int)rect.Size.X, current, maximum);
            if (filled > 0) DrawRect(new Rect2(rect.Position + Vector2.One, new Vector2(filled, rect.Size.Y - 2)), _uiColors[color]);
        }
        var border = _uiColors[0x44];
        DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 1)), border);
        DrawRect(new Rect2(rect.Position.X, rect.End.Y - 1, rect.Size.X, 1), border);
        DrawRect(new Rect2(rect.Position, new Vector2(1, rect.Size.Y)), border);
        DrawRect(new Rect2(rect.End.X - 1, rect.Position.Y, 1, rect.Size.Y), border);
    }

    /// <summary>Handles a click in screen coordinates of the 640×480 frame; returns true when the interface consumed it.</summary>
    public bool Click(Vector2 point, MouseButton button)
    {
        if (Game.Battle.Paused) return true;
        if (button == MouseButton.Left && BuildItemAt(point) is { } item) { item.Start(); QueueRedraw(); return true; }
        if (MapRect.HasPoint(point)) return false;
        point = ToClassic(point);
        if (button != MouseButton.Left) return true;
        if (MenuOpen)
        {
            if (BackButton.HasPoint(point)) MenuOpen = false;
            else if (PauseButton.HasPoint(point)) Game.TogglePause();
            else if (ExitButton.HasPoint(point)) Game.ExitMission();
            else if (ScreenButton.HasPoint(point)) DisplaySettings.Open();
            else if (GameSlower.HasPoint(point)) Game.ChangeSpeed(-1);
            else if (GameFaster.HasPoint(point)) Game.ChangeSpeed(+1);
            else if (ScrollSlower.HasPoint(point)) Game.ChangeScrollSpeed(-1);
            else if (ScrollFaster.HasPoint(point)) Game.ChangeScrollSpeed(+1);
        }
        else if (MenuButton.HasPoint(point)) MenuOpen = true;
        else if (Minimap.HasPoint(point))
            Game.FocusWorld((point - Minimap.Position) / Minimap.Size * new Vector2(Game.Battle.Map.Width, Game.Battle.Map.Height) * 32);
        else if (Game.Battle.Actors.FirstOrDefault(a => Game.Selection.Contains(a.Id)) is { } unit)
        {
            if (CommandAt(point) is { } command) Game.OrderSlot(unit, command);
        }
        QueueRedraw();
        return true;
    }
}
