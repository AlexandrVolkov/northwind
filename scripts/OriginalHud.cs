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
    private static readonly int[] CounterX = [66, 159, 260, 365];
    private readonly Dictionary<int, Color> _uiColors = [];
    private static readonly Rect2 HealthBar = new(572, 176, 9, 50), ExperienceBar = new(587, 176, 9, 50);

    public SkirmishMain Game { get; set; } = null!;
    public bool MenuOpen { get; set; }
    /// <summary>Order button slots: two columns, 71 px apart, rows 43 px apart from (485, 313); rows 1–2 measured, row 3 assumed.</summary>
    public static Rect2 Slot(int index) => new(485 + index % 2 * 71, 313 + index / 2 * 43, 69, 35);
    public static readonly Vector2 PortraitAt = new(482, 176);
    public InterfaceArmy Army { get; private set; } = null!;
    private InterfaceAtlas _heroes = null!;
    private Texture2D _portraits = null!, _buttons = null!, _heroAtlas = null!;
    private Texture2D _frame = null!, _menuPanel = null!;
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
        _cursor = data.ArrowCursor;
        if (_cursor != null)
        {
            _cursorTexture = GD.Load<Texture2D>(InterfaceData.Root + _cursor.Atlas);
            if (DisplayServer.GetName() != "headless")
            { _previousMouseMode = Input.MouseMode; Input.MouseMode = Input.MouseModeEnum.Hidden; }
        }
        _frame = InterfaceData.Screen("game_frame"); _menuPanel = InterfaceData.Screen("panel_menu");
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
                (ushort)_cursor.FirstSprite, (ushort)(_cursor.FirstSprite + _cursor.Sprites.Count - 1));
        QueueRedraw();
    }

    public override void _ExitTree()
    {
        if (_previousMouseMode is { } previous && Input.MouseMode == Input.MouseModeEnum.Hidden) Input.MouseMode = previous;
    }

    public override void _Draw()
    {
        // Letterbox: everything outside 640×480 stays black.
        DrawRect(new Rect2(-4000, -4000, 8640, 4000), Colors.Black); DrawRect(new Rect2(-4000, 480, 8640, 4000), Colors.Black);
        DrawRect(new Rect2(-4000, 0, 4000, 480), Colors.Black); DrawRect(new Rect2(640, 0, 4000, 480), Colors.Black);
        DrawTexture(_frame, Vector2.Zero);
        if (Game?.Battle == null) return;
        var b = Game.Battle;
        _font.Draw(this, new Vector2(CounterX[0], 0), b.Metal.ToString());
        _font.Draw(this, new Vector2(CounterX[1], 0), b.Gold.ToString());
        _font.Draw(this, new Vector2(CounterX[2], 0), b.Wood.ToString());
        _red.Draw(this, new Vector2(CounterX[3], 0), b.FreePeople.ToString());
        if (MenuOpen) DrawTexture(_menuPanel, new Vector2(464, 0));
        else { DrawMinimap(); DrawSelection(); }
        if (b.Paused)
        {
            DrawRect(new Rect2(161, 224, 158, 24), new Color(0, 0, 0, .85f));
            _font.Draw(this, new Vector2(169, 228), "ПАУЗА (Esc)");
        }
        if (_cursor != null && _cursorTexture != null && GetViewport().GetVisibleRect().HasPoint(GetViewport().GetMousePosition()))
            DrawTextureRectRegion(_cursorTexture, new Rect2(GetLocalMousePosition(), new Vector2(32, 32)),
                _cursor.Region((int)CursorFrame - _cursor.FirstSprite));
    }

    private void DrawMinimap()
    {
        if (Game.MinimapTexture != null) DrawTextureRect(Game.MinimapTexture, Minimap, false);
        var world = new Vector2(Game.Battle.Map.Width, Game.Battle.Map.Height) * 32;
        foreach (var actor in Game.Battle.Actors.Where(a => a.Alive))
            DrawRect(new Rect2(Minimap.Position + (actor.Position / world * Minimap.Size).Floor(), Vector2.One),
                actor.Team == 0 ? new Color("63e3bd") : new Color("f37769"));
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
        var hover = CommandAt(GetLocalMousePosition());
        string hint = hover != null ? OriginalCommandKeys.Hint(hover) : Game.PendingOrder switch
        {
            2 => "Укажите место", 3 => "Укажите врага", 4 => "Укажите лес", 8 => "Укажите шахту", _ => "",
        };
        DrawHint(hint);
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
        int filled = OriginalStatusBarRules.FilledColumns((int)rect.Size.X, current, maximum);
        DrawRect(rect, _uiColors[0]);
        if (filled > 0) DrawRect(new Rect2(rect.Position + Vector2.One, new Vector2(filled, rect.Size.Y - 2)), _uiColors[color]);
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
        if (MapWindow.HasPoint(point)) return false;
        if (button != MouseButton.Left) return true;
        if (MenuOpen)
        {
            if (BackButton.HasPoint(point)) MenuOpen = false;
            else if (PauseButton.HasPoint(point)) Game.TogglePause();
            else if (ExitButton.HasPoint(point)) Game.ExitMission();
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
