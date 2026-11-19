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
    private static readonly Color BarBorder = new(146 / 255f, 85 / 255f, 0), BarFill = new(65 / 255f, 93 / 255f, 48 / 255f);
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

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        var data = InterfaceData.Load();
        _frame = InterfaceData.Screen("game_frame"); _menuPanel = InterfaceData.Screen("panel_menu");
        Army = data.Armies.Single(a => a.Army == 2); _heroes = data.HeroPortraits;
        _portraits = GD.Load<Texture2D>(InterfaceData.Root + Army.Portraits.Atlas); _buttons = GD.Load<Texture2D>(InterfaceData.Root + Army.Buttons.Atlas);
        _heroAtlas = GD.Load<Texture2D>(InterfaceData.Root + _heroes.Atlas);
        _font = OriginalFont.Load(data, "FONT_01.FNT"); _red = OriginalFont.Load(data, "FONT_01.FNT", "font_font_01_red.png");
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
        if (MenuOpen) { DrawTexture(_menuPanel, new Vector2(464, 0)); return; }
        DrawMinimap();
        DrawSelection();
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
        if (SingleUnit() is not { } unit) return;
        _font.Draw(this, new Vector2(482, 160), unit.Hero ? Game.HeroName : unit.Source.TemplateName);
        if (unit.Hero && _heroes.Known.TryGetValue(Game.HeroName, out int hero))
            DrawTextureRectRegion(_heroAtlas, new Rect2(PortraitAt, 64, 70), _heroes.Region(hero - _heroes.FirstSprite));
        else if (Template(unit) is { } template)
            DrawTextureRectRegion(_portraits, new Rect2(PortraitAt, 80, 50), Army.Portraits.Region(template.Portrait));
        if (Template(unit) is { } t && unit.Team == 0)
            for (int k = 0; k < t.Slots.Count; k++)
                if (t.Slots[k].Button is { } button)
                    DrawTextureRectRegion(_buttons, Slot(k), Army.Buttons.Region(button));
        DrawBar(HealthBar, unit.MaxHealth > 0 ? Math.Clamp(unit.Health / (float)unit.MaxHealth, 0, 1) : 0);
        DrawBar(ExperienceBar, 0);
    }

    /// <summary>Order slots of the unit's DAT template (ARMY__02 only for now).</summary>
    public InterfaceTemplate? Template(SkirmishActor unit) =>
        unit.Source.TemplateFile == "ARMY__02.DAT" && unit.Source.TemplateIndex < Army.Templates.Count ? Army.Templates[unit.Source.TemplateIndex] : null;

    private void DrawBar(Rect2 rect, float fill)
    {
        DrawRect(rect, Colors.Black);
        int height = (int)MathF.Round((rect.Size.Y - 2) * fill);
        if (height > 0) DrawRect(new Rect2(rect.Position.X + 1, rect.End.Y - 1 - height, rect.Size.X - 2, height), BarFill);
        DrawRect(new Rect2(rect.Position + new Vector2(.5f, .5f), rect.Size - Vector2.One), BarBorder, false, 1);
    }

    /// <summary>Handles a click in screen coordinates of the 640×480 frame; returns true when the interface consumed it.</summary>
    public bool Click(Vector2 point, MouseButton button)
    {
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
        else if (SingleUnit() is { } unit && Template(unit) is { } template)
        {
            for (int k = 0; k < template.Slots.Count; k++)
                if (template.Slots[k].Sprite != 0 && Slot(k).HasPoint(point)) Game.Order(template.Slots[k].Sprite);
        }
        QueueRedraw();
        return true;
    }
}
