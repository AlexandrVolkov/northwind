using Godot;
namespace Northwind;

/// <summary>Screens before a mission, drawn in the original 640×480 coordinates and pillarboxed on wide screens
/// (docs/interface.md «Экраны до миссии»): scrolling history, main menu, save chest with hero name entry, campaign map, loading.</summary>
public partial class OriginalMenus : Node2D
{
    public enum Screen { History, Main, Slots, Campaign, Loading }
    public static readonly Vector2 Size = new(640, 480);
    public const int Slots = 8;
    // Measured on DOSBox screenshots (docs/reference): sign hit areas, chest rows, the ball «Выход», campaign buttons.
    public static readonly Rect2 SignGame = new(288, 149, 105, 33), SignExit = new(238, 177, 105, 31), SignAuthors = new(260, 268, 117, 43);
    public static Rect2 SlotRow(int k) => new(152, 175 + 25 * k, 334, 24);
    public static readonly Rect2 Ball = new(196, 40, 97, 89);
    /// <summary>Port addition: «Экран» on the main menu (screen settings).</summary>
    public static readonly Rect2 ScreenButton = new(504, 444, 128, 28);
    public static readonly Rect2 CampaignStart = new(0, 0, 90, 25), CampaignExit = new(0, 30, 90, 25);
    public const double LoadingSeconds = 0.8;

    public Screen Current { get; private set; } = Screen.History;
    public string Message { get; private set; } = "";
    public string TypedName { get; private set; } = "";
    public int EditingSlot { get; private set; } = -1;
    public double Ticks => _ticks;
    public IReadOnlyList<string> HeroNames => _data.HeroNames;
    public float Scale { get; private set; } = 1;
    public Vector2 Origin { get; private set; }
    /// <summary>Scene opened by «Старт»; tests replace it.</summary>
    public string MissionScene { get; set; } = "res://scenes/skirmish.tscn";

    private InterfaceData _data = null!;
    private readonly Dictionary<string, Texture2D> _textures = [];
    private OriginalFont _introFont = null!, _menuFont = null!, _gameFont = null!, _hudFont = null!;
    private double _ticks, _screenTicks;
    private Vector2 _mouse;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        _data = InterfaceData.Load();
        // Fonts identified on screenshots (docs/interface.md): SCROLL.FNT (loaded by LE:1 18A50) for the history, MENU.FNT on the chest,
        // MENU2.FNT for the hero name on the campaign map; all with one pixel between glyphs.
        _introFont = OriginalFont.Load(_data, "SCROLL.FNT");
        _menuFont = OriginalFont.Load(_data, "MENU.FNT", "font_menu_menu2.png"); _gameFont = OriginalFont.Load(_data, "MENU2.FNT", "font_menu2_color1.png");
        _hudFont = OriginalFont.Load(_data, "FONT_01.FNT");
        if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Hidden;
    }

    public override void _ExitTree() { if (Input.MouseMode == Input.MouseModeEnum.Hidden) Input.MouseMode = Input.MouseModeEnum.Visible; }

    private Texture2D Tex(string file) => _textures.TryGetValue(file, out var t) ? t : _textures[file] = GD.Load<Texture2D>(InterfaceData.Root + file);

    public void Show(Screen screen)
    {
        Current = screen; _screenTicks = 0; Message = ""; EditingSlot = -1; TypedName = ""; QueueRedraw();
    }

    public override void _Process(double delta)
    {
        var size = GetViewportRect().Size;
        Scale = Math.Max(.25f, Math.Min(size.X / Size.X, size.Y / Size.Y)); Origin = ((size - Size * Scale) / 2).Floor();
        double step = Math.Max(0, delta) * _data.IntroText.TimerHz;
        _ticks += step; _screenTicks += step;
        _mouse = (GetViewport().GetMousePosition() - Origin) / Scale;
        if (Current == Screen.History && HistoryFinished()) Show(Screen.Main);
        if (Current == Screen.Loading && _screenTicks >= LoadingSeconds * _data.IntroText.TimerHz) StartMission();
        QueueRedraw();
    }

    /// <summary>LE:1 1B1B7: every 7 timer ticks every line moves up one pixel.</summary>
    public int HistoryOffset() => (int)(_screenTicks / _data.IntroText.StepTicks);
    /// <summary>1B1F3..1B242: the history ends once the last line is 30 px above the top.</summary>
    public bool HistoryFinished() => _data.IntroText.Lines[^1].Y - HistoryOffset() + 0x1e < 0;

    /// <summary>Advance the timer by original ticks (tests).</summary>
    public void AdvanceTicks(double ticks) { _ticks += ticks; _screenTicks += ticks; if (Current == Screen.History && HistoryFinished()) Show(Screen.Main); }

    public override void _Draw()
    {
        DrawSetTransform(Origin, 0, Vector2.One * Scale);
        DrawRect(new Rect2(-4000, -4000, 8640, 8480), Colors.Black);
        switch (Current)
        {
            case Screen.History:
                DrawTexture(InterfaceData.Screen("intro_map"), Vector2.Zero);
                int offset = HistoryOffset();
                foreach (var line in _data.IntroText.Lines)
                {
                    int y = line.Y - offset;
                    if (y >= 0 && y < _data.IntroText.ClipBottom) _introFont.Draw(this, new Vector2(line.X, y), line.Text);
                }
                // Lines are drawn while y < 1D0h but no pixel below row 463 is shown (checked on the screenshot).
                int clip = _data.IntroText.ClipBottom;
                DrawTextureRectRegion(InterfaceData.Screen("intro_map"), new Rect2(0, clip, 640, 480 - clip), new Rect2(0, clip, 640, 480 - clip));
                break;
            case Screen.Main:
                DrawTexture(InterfaceData.Screen("main_menu"), Vector2.Zero);
                DrawGroup("menu_bush", PingPong(_ticks / 3, Group("menu_bush").Frames));
                DrawGroup("menu_owl", 0);
                if (SignGame.HasPoint(_mouse)) DrawGroup("menu_sign_game", 1);
                else if (SignExit.HasPoint(_mouse)) DrawGroup("menu_sign_exit", 1);
                else if (SignAuthors.HasPoint(_mouse)) DrawGroup("menu_sign_authors", 1);
                DrawTexture(Tex("button_blank.png"), ScreenButton.Position);
                _hudFont.Draw(this, new Vector2(ScreenButton.GetCenter().X - _hudFont.Width("Экран") / 2f, ScreenButton.Position.Y + 7), "Экран");
                break;
            case Screen.Slots:
                DrawTexture(InterfaceData.Screen("save_slots"), Vector2.Zero);
                DrawGroup("menu_candles", (int)(_ticks / 3) % Group("menu_candles").Frames);
                DrawGroup("menu_ball", 1);
                if (EditingSlot >= 0) { _menuFont.Draw(this, new Vector2(156, 148), "Введите имя"); _menuFont.Draw(this, new Vector2(326, 148), "Один игрок"); }
                for (int k = 0; k < Slots; k++)
                {
                    bool hot = EditingSlot == k || (EditingSlot < 0 && SlotRow(k).HasPoint(_mouse));
                    if (hot) DrawRect(SlotRow(k), new Color(1, .8f, .3f, .12f));   // highlight colour not recovered, approximation
                    _menuFont.Draw(this, new Vector2(156, 173 + 25 * k), "Новая игра");
                    if (EditingSlot == k) _menuFont.Draw(this, new Vector2(326, 173 + 25 * k), TypedName + (((int)(_ticks / 18) & 1) == 0 ? "_" : ""));
                }
                if (Message != "") _menuFont.Draw(this, new Vector2(100, 20), Message);
                break;
            case Screen.Campaign:
                DrawTexture(InterfaceData.Screen("campaign_map"), Vector2.Zero);
                DrawCampaignButton("campaign_start", CampaignStart); DrawCampaignButton("campaign_exit", CampaignExit);
                var flags = _data.CampaignSprites["campaign_flags"];
                DrawTextureRectRegion(Tex(flags.Atlas), new Rect2(106, 30, 16, 16), flags.Region(32 - flags.FirstSprite));
                _gameFont.Draw(this, new Vector2(109, 50), GameSession.HeroName);
                break;
            case Screen.Loading:
                DrawTexture(InterfaceData.Screen("loading"), Vector2.Zero);
                _hudFont.Draw(this, new Vector2(6, 464), "Загрузка эпизода_");   // FONT_01, matched on the screenshot (colour error 4)
                break;
        }
        var cursor = Group("menu_cursor");
        if (Current != Screen.History && !DisplaySettings.WindowOpen && DisplayServer.GetName() != "headless")
            DrawTextureRectRegion(Tex(cursor.Atlas), new Rect2(_mouse, 32, 32), cursor.Region(0));
    }

    private InterfaceSpriteGroup Group(string name) => _data.MenuSprites[name];
    private static int PingPong(double t, int frames) { int period = 2 * (frames - 1); int p = (int)t % period; return p < frames ? p : period - p; }

    private void DrawGroup(string name, int frame)
    {
        var g = Group(name);
        DrawTextureRectRegion(Tex(g.Atlas), new Rect2(g.Position![0], g.Position[1], g.Cell[0], g.Cell[1]), g.Region(frame));
    }

    private void DrawCampaignButton(string name, Rect2 at)
    {
        var g = _data.CampaignSprites[name];
        DrawTextureRectRegion(Tex(g.Atlas), at, g.Region(at.HasPoint(_mouse) ? 1 : 0));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b) Click((b.Position - Origin) / Scale);
        else if (e is InputEventKey { Pressed: true } key) Key(key.Keycode, (char)key.Unicode);
    }

    /// <summary>Click in 640×480 coordinates.</summary>
    public void Click(Vector2 p)
    {
        switch (Current)
        {
            case Screen.History: Show(Screen.Main); break;
            case Screen.Main:
                if (SignGame.HasPoint(p)) Show(Screen.Slots);
                else if (SignExit.HasPoint(p)) GetTree().Quit();
                else if (SignAuthors.HasPoint(p)) { }
                else if (ScreenButton.HasPoint(p)) DisplaySettings.Open();   // «От авторов» (SCREENS.SCR #9, FROM.SMK) is not ported yet
                break;
            case Screen.Slots:
                if (Ball.HasPoint(p)) { Show(Screen.Main); break; }
                for (int k = 0; k < Slots; k++)
                    if (SlotRow(k).HasPoint(p)) { EditingSlot = k; TypedName = ""; Message = ""; }
                break;
            case Screen.Campaign:
                if (CampaignStart.HasPoint(p)) Show(Screen.Loading);
                else if (CampaignExit.HasPoint(p)) Show(Screen.Slots);
                break;
        }
        QueueRedraw();
    }

    public void Key(Key key, char ch)
    {
        if (Current == Screen.History) { if (key is Godot.Key.Escape or Godot.Key.Space or Godot.Key.Enter) Show(Screen.Main); return; }
        if (key == Godot.Key.Escape)
        {
            if (Current == Screen.Slots && EditingSlot >= 0) { EditingSlot = -1; TypedName = ""; }
            else if (Current is Screen.Slots) Show(Screen.Main);
            else if (Current is Screen.Campaign) Show(Screen.Slots);
            return;
        }
        if (Current != Screen.Slots || EditingSlot < 0) return;
        if (key == Godot.Key.Backspace) { if (TypedName.Length > 0) TypedName = TypedName[..^1]; }
        else if (key is Godot.Key.Enter or Godot.Key.KpEnter) SubmitName();
        else if (ch >= ' ' && TypedName.Length < 12) TypedName += ch;
        QueueRedraw();
    }

    /// <summary>The original checks the name against the heroes stored in the EXE; other names get this message.</summary>
    public void SubmitName()
    {
        var hero = _data.HeroNames.FirstOrDefault(n => string.Equals(n, TypedName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (hero == null) { Message = "Такого персонажа в игре нет, смотри от авторов"; return; }
        GameSession.HeroName = hero; GameSession.Slot = EditingSlot; Show(Screen.Campaign);
    }

    private void StartMission()
    {
        Current = Screen.Campaign;   // do not restart while the scene changes
        if (MissionScene != "") GetTree().ChangeSceneToFile(MissionScene);
    }
}
