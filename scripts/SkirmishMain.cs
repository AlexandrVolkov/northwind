using Godot;
using System.Buffers.Binary;
namespace Northwind;

public sealed class TerrainColorsData { public int[][] Colors { get; set; } = []; }
public sealed class BattleFramesData { public int[] TeamTables { get; set; } = []; public BattleFrame[] Frames { get; set; } = []; }
public sealed class BattleFrame
{
    public string TemplateFile { get; set; } = "";
    public int TemplateIndex { get; set; }
    public int Frame { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Slot in the owner-colour sheets (battle_frames_team_NN.png, 64×64 cells); −1 when the frame has no owner colour.</summary>
    public int TeamSlot { get; set; } = -1;
}

public partial class SkirmishMain : Node2D
{
    /// <summary>Name shown for the hero; the original only accepts the heroes Олег, Игорь and Эйрик.</summary>
    public string HeroName { get; set; } = GameSession.HeroName;
    public OriginalHud Hud { get; } = new();
    private readonly CanvasLayer _hudLayer = new() { Layer = 2 };
    public Texture2D? MinimapTexture => _minimapTexture;
    /// <summary>«Панорама» [908F] (1..16).</summary>
    public byte Pan { get; private set; } = OriginalScrollRules.DefaultPan;
    public (ushort First, ushort Last) CursorFamily { get; private set; } = OriginalScrollRules.Arrow;
    public int CursorHotSpot { get; private set; }
    /// <summary>WASD scrolling speed in original pixels per second (port convenience).</summary>
    public const float WasdSpeed = 560;
    /// <summary>Wheel zoom limits relative to one original pixel per interface pixel (port convenience).</summary>
    public const float MinZoom = .5f, MaxZoom = 2.5f;
    private double _scrollTicks; private byte _scrollLast;
    public Skirmish Battle { get; private set; } = null!;
    public HashSet<uint> Selection { get; } = [];
    public Camera2D Camera { get; } = new();
    public float Speed { get; private set; } = 1;
    private Texture2D _terrain = null!, _sprites = null!;
    private readonly Dictionary<(string, int, int), BattleFrame> _frames = [];
    private readonly Dictionary<int, Texture2D> _teamSheets = [];
    private Label _status = null!, _selection = null!, _events = null!;
    private Label _economy = null!, _recruitment = null!;
    private Button _cancelRecruitment = null!;
    private Button _hire = null!, _buildMill = null!, _hireMilitia = null!, _buildFarm = null!, _buildBarracks = null!, _buildMine = null!;
    public bool PlacingBarracks { get; private set; }
    public bool PlacingMine { get; private set; }
    public bool PlacingMill { get; private set; }
    public bool PlacingFarm { get; private set; }
    public bool PlacingBuilding => PlacingMill || PlacingFarm || PlacingBarracks || PlacingMine;
    private Button _pause = null!, _speed = null!;
    private TextureRect _minimap = null!;
    private Image _minimapImage = null!;
    private ImageTexture _minimapTexture = null!;
    private int[][] _terrainColors = [];
    private int _terrainRevision = -1;
    private Control _markers = null!;
    private CanvasLayer _ui = null!;
    private bool _ready, _drag, _pan;
    public bool ReadyToPlay => _ready;
    private Vector2 _dragStart, _dragEnd;
    private Vector2? _orderMark;
    private double _markUntil;
    private int _hover;
    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        AddChild(Camera); Camera.Enabled = true;
        BuildUi(); _ui.Visible = false;   // the former debug panel: F1
        AddChild(_hudLayer); Hud.Game = this; _hudLayer.AddChild(Hud); Layout(); Camera.Zoom = Vector2.One * HudScale;
        try
        {
            _terrain = GD.Load<Texture2D>("res://assets/imported/terrain.png");
            _sprites = GD.Load<Texture2D>("res://assets/imported/battle_frames.png");
            if (_terrain == null || _sprites == null) throw new InvalidDataException("Bundled textures are missing");
            var framesData = GameData.Read<BattleFramesData>("res://assets/imported/battle_frames.json");
            foreach (var frame in framesData.Frames) _frames[(frame.TemplateFile, frame.TemplateIndex, frame.Frame)] = frame;
            foreach (int table in framesData.TeamTables) _teamSheets[table] = GD.Load<Texture2D>($"res://assets/imported/battle_frames_team_{table:00}.png");
            _terrainColors = GameData.Read<TerrainColorsData>("res://assets/imported/terrain_colors.json").Colors;
            _minimapImage = GD.Load<Texture2D>("res://assets/imported/minimap.png").GetImage();
            _minimapTexture = ImageTexture.CreateFromImage(_minimapImage); _minimap.Texture = _minimapTexture;
            Restart(); _ready = true;
        }
        catch (Exception error) { GD.PushError(error.ToString()); _status.Text = "Не удалось загрузить схватку.\nСкачайте проект целиком."; }
    }
    public override void _ExitTree() { if (Battle != null) Battle.Dispose(); }
    public void Restart()
    {
        if (Battle != null) Battle.Dispose();
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json");
        rules.RivalSlot = 1;   // observation: a second AI settlement in the east, at war with the red one
        Battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"),
            GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        Selection.Clear(); Selection.Add(Battle.Hero.Id); _orderMark = null; _drag = _pan = PlacingMill = PlacingFarm = PlacingBarracks = PlacingMine = false;
        PendingOrder = 0; Hud.MenuOpen = false;
        // DOSBox screenshots: the idle castle shows «Выбор» and «Разрушить» (WorkFlags bits 2 and 5 = 24h) although its OBE record holds 0;
        // the WRK handler that sets them is not ported, so the observed idle value is applied here.
        if (Battle.PlayerCastle.Record[0x0f] == 0) Battle.PlayerCastle.Record[0x0f] = 0x24;
        _terrainRevision = -1; RefreshTerrain(); FocusHero(); RefreshUi(); QueueRedraw();
    }
    public void SelectArmy()
    {
        Selection.Clear();
        foreach (var actor in Battle.Actors.Where(a => a.Team == 0 && a.Alive && !a.Structure && !a.Worker)) Selection.Add(actor.Id);
        RefreshUi();
    }
    public void SelectCastle()
    {
        if (!Battle.PlayerCastle.Alive) return;
        Selection.Clear(); Selection.Add(Battle.PlayerCastle.Id); PlacingMill = PlacingFarm = PlacingBarracks = PlacingMine = false;
        Focus(Battle.PlayerCastle.Position); RefreshUi();
    }
    /// <summary>Castle build list opened by the «eye» button (WORK sprite 874) as on DOSBox screenshots: barracks, sawmill, mine,
    /// farm, worker. The original greys the mine with full resources for a reason not found yet; the port greys unaffordable items.</summary>
    public bool BuildMenuOpen { get; set; }
    public sealed record BuildItem(TemplateEntry Template, int TemplateIndex, Action Start);
    public BuildItem[] CastleBuildItems() =>
    [
        new(Battle.BarracksTemplate, 3, BeginBarracksPlacement), new(Battle.MillTemplate, 4, BeginMillPlacement),
        new(Battle.MineTemplate, 6, BeginMinePlacement), new(Battle.FarmTemplate, 9, BeginFarmPlacement), new(Battle.WorkerTemplate, 10, HireWorker),
    ];
    public bool BuildMenuVisible => BuildMenuOpen && Selection.Count == 1 && Selection.Contains(Battle.PlayerCastle.Id) && Battle.PlayerCastle.Alive;
    public TemplateEntry? PlacingTemplate => PlacingBarracks ? Battle.BarracksTemplate : PlacingMill ? Battle.MillTemplate : PlacingMine ? Battle.MineTemplate : PlacingFarm ? Battle.FarmTemplate : null;
    public void HireWorker()
    {
        if (!Selection.Contains(Battle.PlayerCastle.Id)) return;
        Battle.QueueWorker();
        RefreshUi();
    }
    public void HireMilitia()
    {
        if (Selection.Count != 1 || Battle.Find(Selection.Single()) is not { } barracks || !Skirmish.IsBarracks(barracks) || barracks.Team != 0) return;
        Battle.QueueBarracksMilitia(barracks);
        RefreshUi();
    }
    public void CancelRecruitment()
    {
        if (Selection.Contains(Battle.PlayerCastle.Id)) Battle.CancelRecruitment();
        else if (Selection.Count == 1 && Battle.Find(Selection.Single()) is { } barracks && Skirmish.IsBarracks(barracks) && barracks.Team == 0) Battle.CancelBarracksRecruitment(barracks);
        RefreshUi();
    }
    public void BeginBarracksPlacement()
    {
        if (Battle.Paused || Battle.Outcome != "" || !Selection.Contains(Battle.PlayerCastle.Id) || !Battle.PlayerCastle.Alive) return;
        PlacingBarracks = true; PlacingMill = PlacingFarm = PlacingMine = false; RefreshUi();
    }
    public void BeginMinePlacement()
    {
        if (Battle.Paused || Battle.Outcome != "" || !Selection.Contains(Battle.PlayerCastle.Id) || !Battle.PlayerCastle.Alive) return;
        PlacingMine = true; PlacingMill = PlacingFarm = PlacingBarracks = false; RefreshUi();
    }
    public void BeginFarmPlacement()
    {
        if (Battle.Paused || Battle.Outcome != "" || !Selection.Contains(Battle.PlayerCastle.Id) || !Battle.PlayerCastle.Alive) return;
        PlacingFarm = true; PlacingMill = PlacingBarracks = PlacingMine = false; RefreshUi();
    }
    public void BeginMillPlacement()
    {
        if (Battle.Paused || Battle.Outcome != "" || !Selection.Contains(Battle.PlayerCastle.Id) || !Battle.PlayerCastle.Alive) return;
        PlacingMill = true; PlacingFarm = PlacingBarracks = PlacingMine = false; RefreshUi();
    }
    private Vector2 _pausePointer;
    public void TogglePause()
    {
        if (!_ready || Battle.Outcome != "") return;
        Battle.Paused = !Battle.Paused;
        if (Battle.Paused) { _drag = _pan = false; _pausePointer = GetViewport().GetMousePosition(); }
        RefreshUi(); Hud.QueueRedraw();
    }
    public void ChangeSpeed() { Speed = Speed == 1 ? 2 : Speed == 2 ? 4 : 1; RefreshUi(); }
    public void FocusHero() => Focus(Battle.Hero.Position - new Vector2(0, 100));
    public void FocusObjective()
    {
        var target = Battle.Objective.Standing ? Battle.Objective : Battle.Actors.FirstOrDefault(a => a.Team == 1 && a.Alive && !a.Structure);
        Focus(target?.Position ?? Battle.Objective.Position);
    }
    private void Focus(Vector2 point) => FocusWorld(point);
    /// <summary>Scale and origin of the 640×480 interface in the window.</summary>
    public float HudScale { get; private set; } = 1;
    public Vector2 HudOrigin { get; private set; }
    private void Layout()
    {
        var size = GetViewportRect().Size;
        // 4:3 keeps the original 640 width; 16:10 gives 768, 16:9 about 853; wider screens are pillarboxed at 16:9.
        float width = DisplaySettings.ClassicLayout ? 640 : Mathf.Clamp(MathF.Floor(480 * size.X / size.Y), 640, MathF.Floor(480 * 16 / 9f));
        Hud.Width = width;
        float old = HudScale;
        HudScale = Math.Max(.25f, Math.Min(size.X / width, size.Y / OriginalHud.Size.Y));
        HudOrigin = ((size - new Vector2(width, OriginalHud.Size.Y) * HudScale) / 2).Floor();
        _hudLayer.Transform = new Transform2D(0, Vector2.One * HudScale, 0, HudOrigin);
        if (Camera.Zoom.X == old && old != HudScale) Camera.Zoom = Vector2.One * HudScale;   // window resized: keep 1 original pixel per HUD pixel
    }
    /// <summary>Screen rectangle of the map window.</summary>
    public Rect2 MapScreenRect => new(HudOrigin + Hud.MapRect.Position * HudScale, Hud.MapRect.Size * HudScale);
    /// <summary>Screen point of a point given in the original 640×480 frame (panel points follow the panel to the right edge).</summary>
    public Vector2 ClassicToScreen(Vector2 point) => HudOrigin + Hud.FromClassic(point) * HudScale;
    public Vector2 ToHud(Vector2 screen) => (screen - HudOrigin) / HudScale;
    /// <summary>World rectangle visible through the map window.</summary>
    public Rect2 VisibleWorld()
    {
        var inverse = GetViewport().GetCanvasTransform().AffineInverse(); var rect = MapScreenRect;
        var a = inverse * rect.Position; return new Rect2(a, inverse * rect.End - a);
    }
    /// <summary>Center the map window on a world point.</summary>
    public void FocusWorld(Vector2 point)
    {
        Camera.Position = point - (MapScreenRect.GetCenter() - GetViewportRect().Size / 2) / Camera.Zoom; ClampCamera();
    }
    private void ClampCamera()
    {
        if (Battle == null) return;
        var shift = (MapScreenRect.GetCenter() - GetViewportRect().Size / 2) / Camera.Zoom;
        var half = MapScreenRect.Size / Camera.Zoom / 2;
        var size = new Vector2(Battle.Map.Width, Battle.Map.Height) * 32;
        var center = Camera.Position + shift;
        center = new Vector2(Mathf.Clamp(center.X, half.X, Math.Max(half.X, size.X - half.X)), Mathf.Clamp(center.Y, half.Y, Math.Max(half.Y, size.Y - half.Y)));
        Camera.Position = center - shift;
    }
    /// <summary>«Игра» [908E]: F0h..FEh, default FDh; the world steps every FFh − [908E] timer ticks (A10B), so the multiplier of the
    /// default pace (2 ticks) is 2 / (FFh − [908E]). −/+ change it by one (LE:1 11364).</summary>
    public byte GameSpeed { get; private set; } = 0xfd;
    public void ChangeSpeed(int step)
    {
        int next = GameSpeed + step;
        if (next is >= 0xf0 and <= 0xfe) GameSpeed = (byte)next;
        Speed = 2f / (0xff - GameSpeed); RefreshUi();
    }
    public void ChangeScrollSpeed(int step) => Pan = OriginalScrollRules.ChangePan(Pan, step);
    /// <summary>Pending targeting: 2 move, 3 attack, 4 forestry, 8 mine; UI adapter to the supported world commands.</summary>
    public int PendingOrder { get; private set; }
    public void CancelOrder() => PendingOrder = 0;
    /// <summary>Use the decoded DAT mode/group pair, shared by differently drawn worker and soldier buttons.</summary>
    public void OrderSlot(SkirmishActor unit, InterfaceSlot slot)
    {
        if (Battle.Paused || Battle.Outcome != "" || unit.Team != 0 || !unit.Alive || !Selection.Contains(unit.Id)) return;
        switch (slot.Mode, slot.RawGroup)
        {
            case (0, 1): Battle.Stop(Selection); PendingOrder = 0; break;
            case (1, 2): PendingOrder = 2; break;
            case (2, 3): PendingOrder = 3; break;
            case (4, 4) when unit.Worker: PendingOrder = 4; break;
            case (8, 7) when unit.Worker: PendingOrder = 8; break;
            case var _ when slot.Sprite == 874 && unit.Id == Battle.PlayerCastle.Id: BuildMenuOpen = !BuildMenuOpen; break;
            default: Battle.Log($"Приказ «{slot.Label}» ещё не перенесён."); break;
        }
    }
    private void ApplyPanelOrder(Vector2 point)
    {
        if (Battle.Paused || Battle.Outcome != "") return;
        var cell = new Vector2I((int)MathF.Floor(point.X / 32), (int)MathF.Floor(point.Y / 32));
        var target = Battle.At(point);
        bool accepted = PendingOrder switch
        {
            2 => Battle.Move(Selection, cell) > 0,
            3 => target is { Team: 1 } && Battle.Attack(Selection, target.Id),
            4 => Battle.GatherTimber(Selection, cell) > 0,
            8 => target?.Source.TemplateName == "Шахта" && Battle.WorkMine(Selection, target.Id),
            _ => false,
        };
        if (accepted) { PendingOrder = 0; _orderMark = point; _markUntil = Battle.Elapsed + 2; }
    }
    /// <summary>LE:1 3FA1 (OriginalScrollRules): whole-cell steps at the screen edge or with the arrow keys.</summary>
    public void ScrollMap(double delta)
    {
        _scrollTicks += Math.Max(0, delta) * OriginalCursorRules.TimerHz;
        bool keys = !Input.IsKeyPressed(Key.Ctrl);
        var mouse = ToHud(GetViewport().GetMousePosition());
        bool pointer = EdgeScrolling && DisplayServer.GetName() != "headless" && DisplayServer.WindowIsFocused();
        var input = new ScrollInput(pointer ? Math.Clamp(mouse.X, 0, Hud.Width - 1) : Hud.Width / 2, pointer ? Math.Clamp(mouse.Y, 0, 479) : 240, Hud.Width, 480,
            keys && Input.IsKeyPressed(Key.Left), keys && Input.IsKeyPressed(Key.Right), keys && Input.IsKeyPressed(Key.Up), keys && Input.IsKeyPressed(Key.Down));
        var step = OriginalScrollRules.Step(input, unchecked((byte)(ulong)_scrollTicks), _scrollLast, Pan);
        _scrollLast = step.LastTimer; CursorFamily = (step.CursorFirst, step.CursorLast); CursorHotSpot = 0;
        var logical = ToHud(GetViewport().GetMousePosition());
        if (CursorFamily == OriginalScrollRules.Arrow && Hud.BuildItemAt(logical) != null) CursorFamily = OriginalOrderCursor.Select;   // sickle over the list
        else if (CursorFamily == OriginalScrollRules.Arrow && PlacingBuilding && MapScreenRect.HasPoint(GetViewport().GetMousePosition()))
        { CursorFamily = OriginalOrderCursor.Place; CursorHotSpot = OriginalOrderCursor.HotSpot; }
        else if (CursorFamily == OriginalScrollRules.Arrow && PendingOrder is 2 or 3 && MapScreenRect.HasPoint(GetViewport().GetMousePosition()))
        {
            // Port pending orders 2 (move) and 3 (attack) correspond to the original modes 1 and 2; gather/mine cursors are not confirmed.
            var hovered = Battle.At(GetGlobalMousePosition());
            byte? cls = hovered == null ? null : OriginalOrderCursor.ObjectClass(BinaryPrimitives.ReadUInt32LittleEndian(hovered.Parameters.AsSpan(0x40)),
                hovered.Record[3], (byte)Battle.Rules.PlayerSlot, 0xff, hovered.Record[3] != Battle.Rules.PlayerSlot && Battle.Diplomacy.AtWar(Battle.Rules.PlayerSlot, hovered.Record[3]));
            CursorFamily = OriginalOrderCursor.For(PendingOrder == 2 ? 1 : 2, cls); CursorHotSpot = OriginalOrderCursor.HotSpot;
        }
        if (step.Dx != 0 || step.Dy != 0) { Camera.Position += new Vector2(step.Dx, step.Dy) * 32; ClampCamera(); }
        // Port convenience (requested): WASD scrolls smoothly in addition to the original arrow keys.
        var wasd = Vector2.Zero;
        if (keys)
        {
            if (Input.IsPhysicalKeyPressed(Key.A)) wasd.X--;
            if (Input.IsPhysicalKeyPressed(Key.D)) wasd.X++;
            if (Input.IsPhysicalKeyPressed(Key.W)) wasd.Y--;
            if (Input.IsPhysicalKeyPressed(Key.S)) wasd.Y++;
        }
        if (wasd != Vector2.Zero) { Camera.Position += wasd.Normalized() * (float)delta * WasdSpeed / Camera.Zoom.X * HudScale; ClampCamera(); }
    }
    /// <summary>Pointer scrolling at the screen edge (can be turned off for tests).</summary>
    public bool EdgeScrolling { get; set; } = true;
    public void ExitMission() => GetTree().ChangeSceneToFile("res://scenes/menus.tscn");
    /// <summary>Colours of the sides: the player green, the red settlement red, the observed rival blue.</summary>
    public static Color TeamColor(int team) => new(team switch { 0 => "63e3bd", 2 => "6fa8f5", _ => "f37769" });
    private OriginalSoundBank? _soundBank;
    private readonly List<(AudioStreamPlayer2D Player, string File)> _voices = [];
    /// <summary>Plays the simulation's sounds (A416/A47E) from the original banks. As the mixer does (1414D), a sample that
    /// already plays on more than one channel is not started again.</summary>
    private void PlaySounds()
    {
        if (Battle.Sounds.Count == 0) return;
        _soundBank ??= OriginalSoundBank.Load();
        foreach (var (player, _) in _voices.Where(v => !v.Player.Playing).ToArray()) { _voices.RemoveAll(v => v.Player == player); player.QueueFree(); }
        foreach (var sound in Battle.Sounds)
        {
            if (_soundBank.Choose(sound.Bank, sound.Id, sound.Random) is not { } variant) continue;
            if (_voices.Count(v => v.File == variant.File) > 1 || _voices.Count >= 32) continue;
            var player = new AudioStreamPlayer2D { Stream = OriginalSoundBank.Stream(variant), Position = sound.Position, MaxDistance = 1600 };
            AddChild(player); player.Play(); _voices.Add((player, variant.File));
        }
        Battle.Sounds.Clear();
    }
    public override void _Process(double delta)
    {
        if (!_ready) return;
        Layout();
        if (Battle.Paused) { RefreshUi(); QueueRedraw(); _markers.QueueRedraw(); Hud.QueueRedraw(); return; }
        Battle.Advance(delta * Speed); RefreshTerrain(); PlaySounds();
        Selection.RemoveWhere(id => Battle.Find(id) == null);
        ScrollMap(delta);
        // While a building is being placed the castle also shows slot 4 «Построить» (bit 4), as on the screenshot.
        if (Battle.PlayerCastle.Alive) Battle.PlayerCastle.Record[0x0f] = (byte)(PlacingBuilding ? Battle.PlayerCastle.Record[0x0f] | 0x10 : Battle.PlayerCastle.Record[0x0f] & ~0x10);
        _hover = (int)(Battle.At(GetGlobalMousePosition())?.Id ?? 0);
        RefreshUi(); QueueRedraw(); _markers.QueueRedraw(); Hud.QueueRedraw();
    }
    public void IssueOrder(Vector2 point)
    {
        if (Battle.Paused || Battle.Outcome != "") return;
        if (Selection.Count == 0) { Battle.Log("Сначала выделите отряды: рамкой или Ctrl+A."); return; }
        var target = Battle.At(point);
        var cell = new Vector2I((int)MathF.Floor(point.X / 32), (int)MathF.Floor(point.Y / 32));
        if (target?.Team == 1) Battle.Attack(Selection, target.Id);
        else if (target?.Source.TemplateName == "Лесопилка" && Battle.DeliverTimber(Selection, target.Id)) { }
        else if (target?.Source.TemplateName == "Шахта" && Battle.WorkMine(Selection, target.Id)) { }
        else if (Battle.Harvestable(cell) && Selection.Any(id => Battle.Find(id)?.Worker == true)) Battle.GatherTimber(Selection, cell);
        else Battle.Move(Selection, cell);
        _orderMark = point; _markUntil = Battle.Elapsed + 2;
    }
    public override void _Input(InputEvent e)
    {
        if (!_ready) return;
        if (Battle.Paused)
        {
            if (e is InputEventMouseMotion motion && DisplayServer.GetName() != "headless"
                && GetViewportRect().HasPoint(motion.Position) && motion.Position.DistanceSquaredTo(_pausePointer) > 1)
                Input.WarpMouse(_pausePointer);
            return;
        }
        // Release a drag even when the pointer finishes over a UI control.
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: false }) _pan = false;
        if (_drag && e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } button)
        {
            _dragEnd = ClampToMap(button.Position);
            if (!Input.IsKeyPressed(Key.Shift)) Selection.Clear();
            if (_dragStart.DistanceTo(_dragEnd) < 6)
            {
                var point = GetViewport().GetCanvasTransform().AffineInverse() * button.Position;
                if (Battle.At(point) is { } actor && (actor.Team == 0 || !_showFog || Seen(actor.Cell)))
                {
                    // An enemy or foreign object can be selected alone, to look at it; orders ignore it.
                    if (actor.Team != 0) { Selection.Clear(); Selection.Add(actor.Id); }
                    else if (!Selection.Add(actor.Id)) Selection.Remove(actor.Id);
                }
            }
            else
            {
                var box = new Rect2(_dragStart, _dragEnd - _dragStart).Abs();
                foreach (var actor in Battle.Actors.Where(a => a.Team == 0 && a.Alive && !a.Structure))
                    if (box.HasPoint(GetViewport().GetCanvasTransform() * actor.Position)) Selection.Add(actor.Id);
            }
            _drag = false; RefreshUi();
        }
    }
    public override void _UnhandledInput(InputEvent e)
    {
        if (!_ready) return;
        if (Battle.Paused)
        {
            if (e is InputEventKey { Pressed: true, Echo: false } resume && resume.PhysicalKeycode is Key.Escape or Key.Space) TogglePause();
            return;
        }
        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (!key.CtrlPressed && !key.AltPressed && !key.MetaPressed && !key.ShiftPressed && Hud.Hotkey(key.PhysicalKeycode)) return;
            if (key.CtrlPressed && key.PhysicalKeycode == Key.A) SelectArmy();
            else switch (key.PhysicalKeycode)
            {
                case Key.Space: TogglePause(); break;
                case Key.F: FocusHero(); break;
                case Key.End: FocusHero(); break;
                case Key.T: FocusObjective(); break;
                case Key.C: SelectCastle(); break;
                case Key.Home: SelectCastle(); break;
                case Key.R: Restart(); break;
                case Key.X: Battle.Stop(Selection); break;
                case Key.Escape:
                    if (PendingOrder != 0) PendingOrder = 0;
                    else if (PlacingBuilding) { PlacingMill = false; PlacingFarm = PlacingBarracks = PlacingMine = false; }
                    else if (Hud.MenuOpen) Hud.MenuOpen = false;
                    else Selection.Clear();
                    break;
                case Key.F10: GetTree().ChangeSceneToFile("res://scenes/main.tscn"); break;
                case Key.F1: _ui.Visible = !_ui.Visible; break;
                case Key.F4: _showFog = !_showFog; QueueRedraw(); break;
            }
        }
        if (e is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Middle) _pan = button.Pressed && MapScreenRect.HasPoint(button.Position);
            if (!button.Pressed) return;
            if (button.ButtonIndex is MouseButton.Left or MouseButton.Right && Hud.Click(ToHud(button.Position), button.ButtonIndex)) return;
            if (!MapScreenRect.HasPoint(button.Position)) return;
            if (button.ButtonIndex == MouseButton.Right && PendingOrder != 0) { PendingOrder = 0; return; }
            if (PlacingBuilding && button.ButtonIndex == MouseButton.Right) { PlacingMill = PlacingFarm = PlacingBarracks = PlacingMine = false; RefreshUi(); return; }
            if (PlacingBuilding && button.ButtonIndex == MouseButton.Left)
            {
                var point = GetViewport().GetCanvasTransform().AffineInverse() * button.Position;
                var cell = new Vector2I((int)MathF.Floor(point.X / 32), (int)MathF.Floor(point.Y / 32));
                var mill = PlacingFarm ? Battle.BuildFarm(cell) : PlacingBarracks ? Battle.BuildBarracks(cell) : PlacingMine ? Battle.BuildMine(cell) : Battle.BuildMill(cell);
                // The original keeps the castle selected and its build list open after placing (DOSBox screenshots).
                if (mill != null) { PlacingMill = PlacingFarm = PlacingBarracks = PlacingMine = false; if (!BuildMenuVisible) { Selection.Clear(); Selection.Add(mill.Id); } }
                RefreshUi(); return;
            }
            if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                // Port convenience (requested): zoom around the pointer; the original had a fixed scale.
                var before = GetGlobalMousePosition();
                float factor = Mathf.Clamp(Camera.Zoom.X / HudScale * (button.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f), MinZoom, MaxZoom);
                Camera.Zoom = Vector2.One * HudScale * factor; Camera.ForceUpdateScroll();
                Camera.Position += before - GetGlobalMousePosition(); ClampCamera(); return;
            }
            if (button.ButtonIndex == MouseButton.Left && PendingOrder != 0)
                ApplyPanelOrder(GetViewport().GetCanvasTransform().AffineInverse() * button.Position);
            else if (button.ButtonIndex == MouseButton.Left && !Battle.Paused && Battle.Outcome == "")
            { _drag = true; _dragStart = _dragEnd = button.Position; }
            else if (button.ButtonIndex == MouseButton.Right) IssueOrder(GetViewport().GetCanvasTransform().AffineInverse() * button.Position);
        }
        else if (e is InputEventMouseMotion motion)
        {
            if (_drag) _dragEnd = ClampToMap(motion.Position);
            if (_pan) { Camera.Position -= motion.Relative / Camera.Zoom; ClampCamera(); }
        }
    }
    private bool Seen(Vector2I cell) { int i = cell.Y * Battle.Map.Width + cell.X; return i < 0 || i >= Battle.FogMap.Length || (Battle.FogMap[i] & (1 << Battle.Rules.PlayerSlot)) == 0; }
    private Vector2 ClampToMap(Vector2 point) { var r = MapScreenRect; return point.Clamp(r.Position, r.End); }
    private bool _showFog;   // adapter: a plain dark overlay of the cells the player has not seen ([18DA]); the original's edge drawing 9F38 is not ported
    public override void _Draw()
    {
        if (!_ready) return;
        var half = GetViewportRect().Size / Camera.Zoom / 2;
        var first = (Vector2I)((Camera.Position - half) / 32).Floor();
        var last = (Vector2I)((Camera.Position + half) / 32).Ceil();
        for (int y = Math.Max(0, first.Y); y < Math.Min(Battle.Map.Height, last.Y); y++)
        for (int x = Math.Max(0, first.X); x < Math.Min(Battle.Map.Width, last.X); x++)
        {
            int tile = (int)(Battle.Map.Cells[y * Battle.Map.Width + x] & 65535);
            DrawTextureRectRegion(_terrain, new Rect2(x * 32, y * 32, 32, 32), new Rect2(tile % 32 * 32, tile / 32 * 32, 32, 32));
        }
        foreach (var (type, firstFrame, at, left) in Battle.Sparks)
            if (_frames.TryGetValue(("ARMY__00.EFF", type, firstFrame + (8 - left)), out var spark))
                DrawTextureRectRegion(_sprites, new Rect2(at - new Vector2(spark.Width / 2f, spark.Height / 2f), new Vector2(spark.Width, spark.Height)), new Rect2(spark.X, spark.Y, spark.Width, spark.Height));
        foreach (var actor in Battle.Actors.Where(a => a.Alive || a.Dying).OrderBy(a => a.Position.Y)) if (!_showFog || actor.Team == 0 || Seen(actor.Cell)) DrawActor(actor);
        if (_showFog)
            for (int y = Math.Max(0, first.Y); y < Math.Min(Battle.Map.Height, last.Y); y++)
            for (int x = Math.Max(0, first.X); x < Math.Min(Battle.Map.Width, last.X); x++)
                if (!Seen(new Vector2I(x, y))) DrawRect(new Rect2(x * 32, y * 32, 32, 32), new Color(0, 0, 0, 0.85f));
        var producer = Selection.Count == 1 && Battle.Find(Selection.Single()) is { } barracks && Skirmish.IsBarracks(barracks) && barracks.Team == 0
            ? barracks : Selection.Contains(Battle.PlayerCastle.Id) ? Battle.PlayerCastle : null;
        if (_ui.Visible && !PlacingBuilding && producer is { Alive: true })
        {
            var template = producer == Battle.PlayerCastle ? Battle.RecruitmentTemplate ?? Battle.WorkerTemplate : Battle.MilitiaTemplate;
            var exit = Battle.RecruitExit(producer, template);
            var point = new Vector2(exit.X, exit.Y); var color = new Color(producer == Battle.PlayerCastle ? Battle.RecruitmentBlocked ? "ef8272" : "e7cd82" : Battle.BarracksRecruitmentBlocked(producer) ? "ef8272" : "e7cd82");
            DrawArc(point, 10, 0, Mathf.Tau, 24, color, 2);
            DrawString(ThemeDB.FallbackFont, point + new Vector2(-22, 22), "Выход", HorizontalAlignment.Left, -1, 12, color);
        }
        if (_ui.Visible && !PlacingBuilding && Selection.Any(id => Battle.Find(id)?.Worker == true))
        {
            var point = GetGlobalMousePosition(); var cell = new Vector2I((int)MathF.Floor(point.X / 32), (int)MathF.Floor(point.Y / 32));
            if (Battle.Harvestable(cell))
            {
                DrawRect(new Rect2((Vector2)(cell * 32), Vector2.One * 32), new Color("a0dfa0"), false, 2);
                DrawString(ThemeDB.FallbackFont, (Vector2)(cell * 32) - new Vector2(0, 4), "Лес · ПКМ — рубить", HorizontalAlignment.Left, -1, 12, new Color("d9e8b5"));
            }
        }
        if (PlacingBuilding)
        {
            var point = GetGlobalMousePosition(); var cell = new Vector2I((int)MathF.Floor(point.X / 32), (int)MathF.Floor(point.Y / 32));
            var parameters = (PlacingFarm ? Battle.FarmTemplate : PlacingBarracks ? Battle.BarracksTemplate : PlacingMine ? Battle.MineTemplate : Battle.MillTemplate).Construction;
            var rect = new Rect2((Vector2)((cell - new Vector2I(parameters.FootprintWidth / 2, parameters.FootprintHeight / 2)) * 32), new Vector2(parameters.FootprintWidth * 32, parameters.FootprintHeight * 32));
            bool clear = !Battle.Paused && Battle.CanAfford(PlacingFarm ? Battle.FarmTemplate : PlacingBarracks ? Battle.BarracksTemplate : PlacingMine ? Battle.MineTemplate : Battle.MillTemplate) && (PlacingFarm ? Battle.ProbeFarm(cell) : PlacingBarracks ? Battle.ProbeBarracks(cell) : PlacingMine ? Battle.ProbeMine(cell) : Battle.ProbeMill(cell)).Status == ConstructionPlacementStatus.Clear;
            if (_ui.Visible) { var color = clear ? new Color("8de5b8") : new Color("ef8272"); DrawRect(rect, new Color(color, .2f)); DrawRect(rect, color, false, 2); }
            else
            {
                // Original placement outline: 1-px blue (40,28,223) with cut corners; the colour of a refused place is an assumption.
                var c = clear ? new Color(40 / 255f, 28 / 255f, 223 / 255f) : new Color(200 / 255f, 30 / 255f, 30 / 255f);
                var r = rect.Grow(-1);
                DrawRect(new Rect2(r.Position.X + 3, r.Position.Y, r.Size.X - 6, 1), c); DrawRect(new Rect2(r.Position.X + 3, r.End.Y - 1, r.Size.X - 6, 1), c);
                DrawRect(new Rect2(r.Position.X, r.Position.Y + 3, 1, r.Size.Y - 6), c); DrawRect(new Rect2(r.End.X - 1, r.Position.Y + 3, 1, r.Size.Y - 6), c);
                foreach (var (cx, cy, dx, dy) in new[] { (r.Position.X, r.Position.Y, 1, 1), (r.End.X - 1, r.Position.Y, -1, 1), (r.Position.X, r.End.Y - 1, 1, -1), (r.End.X - 1, r.End.Y - 1, -1, -1) })
                { DrawRect(new Rect2(cx + dx, cy + dy * 2, 1, 1), c); DrawRect(new Rect2(cx + dx * 2, cy + dy, 1, 1), c); }
            }
            if (PlacingMine && cell.X >= 0 && cell.Y >= 0 && cell.X < Battle.Map.Width && cell.Y < Battle.Map.Height)
            {
                var deposit = Battle.MineDepositAt(cell);
                DrawString(ThemeDB.FallbackFont, rect.Position - new Vector2(0, 5), $"Руда: {deposit.Count} клеток · тип {deposit.Type}",
                    HorizontalAlignment.Left, -1, 12, deposit.Count > 0 ? new Color("e7cd82") : new Color("ef8272"));
            }
            var entry = Battle.MillEntry(cell);
            if (PlacingMill && entry.Valid)
            {
                var pointOfEntry = new Vector2(entry.X, entry.Y);
                DrawLine(rect.GetCenter(), pointOfEntry, new Color("e7cd82"), 2);
                DrawCircle(pointOfEntry, 6, new Color("e7cd82"));
                DrawString(ThemeDB.FallbackFont, pointOfEntry + new Vector2(-24, -10), "Приём груза", HorizontalAlignment.Left, -1, 12, new Color("e7cd82"));
            }
        }
        foreach (var corpse in Battle.Corpses)
        {
            var at = new Vector2(BinaryPrimitives.ReadUInt32LittleEndian(corpse.AsSpan(7)), BinaryPrimitives.ReadUInt32LittleEndian(corpse.AsSpan(0xc)));
            ushort frameNumber = BinaryPrimitives.ReadUInt16LittleEndian(corpse.AsSpan(3));
            BattleFrame? sprite = null;
            if (corpse[1] == 2)
            {
                // The unit's own last death frame first, then the corpse frames of ARMY__02 effect type 1.
                _frames.TryGetValue(("ARMY__02.DAT", corpse[0x0e], frameNumber), out sprite);
                if (sprite == null) _frames.TryGetValue(("ARMY__02.EFF", 1, frameNumber), out sprite);
                if (sprite == null) { DrawRect(new Rect2(at - new Vector2(9, 4), new Vector2(18, 8)), new Color(.25f, .18f, .12f, .35f + corpse[0x17] * .1f)); continue; }
            }
            else if (!_frames.TryGetValue(("ARMY__00.EFF", 3, frameNumber), out sprite)) { DrawCircle(at, 3, new Color(.5f, .05f, .05f, .5f)); continue; }
            var spriteSize = new Vector2(sprite.Width, sprite.Height);
            var spriteAt = corpse[1] == 2 ? at - new Vector2(spriteSize.X / 2, spriteSize.Y - 16) : at - spriteSize / 2;
            if (corpse[1] == 2 && sprite.TeamSlot >= 0 && _teamSheets.TryGetValue(Battle.ColorTable(corpse[2]), out var corpseSheet))
                DrawTextureRectRegion(corpseSheet, new Rect2(spriteAt, spriteSize), new Rect2(sprite.TeamSlot % 64 * 64, sprite.TeamSlot / 64 * 64, sprite.Width, sprite.Height));
            else DrawTextureRectRegion(_sprites, new Rect2(spriteAt, spriteSize), new Rect2(sprite.X, sprite.Y, sprite.Width, sprite.Height));
        }
        foreach (var arrow in Battle.Arrows)
        {
            // Effect record: position +7/+0C, 24.8 velocity +18/+1C, height above ground +10 (8.8).
            var at = new Vector2(BinaryPrimitives.ReadUInt32LittleEndian(arrow.AsSpan(7)), BinaryPrimitives.ReadUInt32LittleEndian(arrow.AsSpan(0xc)) - BinaryPrimitives.ReadUInt16LittleEndian(arrow.AsSpan(0x10)) / 256f);
            var velocity = new Vector2(BinaryPrimitives.ReadInt32LittleEndian(arrow.AsSpan(0x18)), BinaryPrimitives.ReadInt32LittleEndian(arrow.AsSpan(0x1c))).Normalized();
            DrawLine(at - velocity * 9 - new Vector2(0, 16), at - new Vector2(0, 16), new Color("e8dcc0"), 2);
        }
        foreach (var stone in Battle.Stones)
        {
            var at = new Vector2(BinaryPrimitives.ReadUInt32LittleEndian(stone.AsSpan(7)), BinaryPrimitives.ReadUInt32LittleEndian(stone.AsSpan(0xc)) - BinaryPrimitives.ReadUInt16LittleEndian(stone.AsSpan(0x10)) / 256f);
            if (stone[0] == OriginalCatapultRules.Stone) DrawCircle(at - new Vector2(0, 16), 4, new Color("8a8178"));
            else if (stone[0] == OriginalCatapultRules.Bolt) DrawCircle(at - new Vector2(0, 16), 3, new Color("c9b38a"));
            else if (stone[0] == OriginalCatapultRules.BigExplosion) DrawCircle(at - new Vector2(0, 16), 10 + (0x12 - stone[0x17]), new Color(1f, .6f, .25f, stone[0x17] / 30f));
            else if (stone[0] == OriginalFireRules.Shot) DrawCircle(at - new Vector2(0, 16), 3, new Color("ff9a2e"));
            else if (stone[0] == OriginalFireRules.Trail) DrawCircle(at - new Vector2(0, 16), 3, new Color(.5f, .5f, .5f, stone[0x17] / 22f));
            else if (stone[0] is OriginalBurnRules.Small or OriginalBurnRules.Medium or OriginalBurnRules.Large)
                DrawCircle(at - new Vector2(0, 12), 5 + 4 * (stone[0] - OriginalBurnRules.Small), new Color(1f, .45f, .1f, .75f));
            else if (stone[0] == OriginalBurnRules.Embers) DrawCircle(at - new Vector2(0, 16), 2, new Color("ffcf5a"));
            else if (stone[0] == OriginalBurnRules.Column) DrawCircle(at - new Vector2(0, 20), 7, new Color(.35f, .35f, .35f, stone[0x17] / 28f));
            else DrawCircle(at - new Vector2(0, 16), 6 + (0x12 - stone[0x17]), new Color(.55f, .5f, .45f, stone[0x17] / 36f));
        }
        foreach (var effect in Battle.Effects)
        {
            DrawLine(effect.From - new Vector2(0, 16), effect.To - new Vector2(0, 16), new Color(effect.Team == 0 ? "f0d88a" : "f57862"), 2);
            DrawCircle(effect.To - new Vector2(0, 16), 6, new Color(1, .6f, .2f, .5f));
        }
        foreach (uint id in _ui.Visible ? Selection : [])
        {
            var actor = Battle.Find(id); if (actor == null) continue;
            var point = actor.Position;
            foreach (var cell in actor.Route)
            { var next = new Vector2(cell.X * 32 + 16, cell.Y * 32 + 16); DrawLine(point, next, new Color(.4f, 1, .7f, .45f), 1); point = next; }
        }
        if (_ui.Visible && _orderMark is { } mark && Battle.Elapsed < _markUntil)
        { DrawArc(mark, 15, 0, Mathf.Tau, 32, new Color("f7d67e"), 2); DrawCircle(mark, 3, Colors.White); }
        if (_drag)
        {
            var inverse = GetViewport().GetCanvasTransform().AffineInverse();
            var rect = new Rect2(inverse * _dragStart, inverse * _dragEnd - inverse * _dragStart).Abs();
            DrawRect(rect, new Color(.3f, .9f, .7f, .12f)); DrawRect(rect, new Color("88e7bf"), false, 1);
        }
    }
    private void DrawActor(SkirmishActor actor)
    {
        var point = actor.Position; var color = TeamColor(actor.Team);
        bool debug = _ui.Visible;
        if (debug && !actor.Structure && !actor.Dying)
        {
            DrawSetTransform(point, 0, new Vector2(1, .45f)); DrawCircle(Vector2.Zero, 14, new Color(color, .22f));
            DrawArc(Vector2.Zero, Selection.Contains(actor.Id) ? 19 : 14, 0, Mathf.Tau, 32, color, Selection.Contains(actor.Id) ? 3 : 1);
            DrawSetTransform(Vector2.Zero);
        }
        int frameIndex = actor.Frame;
        if (actor.Stepping && !actor.Turning) frameIndex += (int)(Battle.Elapsed * 8) % Math.Max(1, (int)actor.Parameters[0x4a]);
        if (!_frames.TryGetValue((actor.Source.TemplateFile, actor.Source.TemplateIndex, frameIndex), out var frame))
            _frames.TryGetValue((actor.Source.TemplateFile, actor.Source.TemplateIndex, actor.Frame), out frame);
        if (frame != null)
        {
            var size = new Vector2(frame.Width, frame.Height);
            // The owner's colour (COLOR_NN.TAB of the owner's slot) only changes the sprite indices 240..255.
            if (frame.TeamSlot >= 0 && _teamSheets.TryGetValue(Battle.ColorTable(actor.Record[3]), out var sheet))
                DrawTextureRectRegion(sheet, new Rect2(point - new Vector2(size.X / 2, size.Y - 16), size), new Rect2(frame.TeamSlot % 64 * 64, frame.TeamSlot / 64 * 64, frame.Width, frame.Height));
            else
                DrawTextureRectRegion(_sprites, new Rect2(point - new Vector2(size.X / 2, size.Y - 16), size), new Rect2(frame.X, frame.Y, frame.Width, frame.Height));
        }
        else if (!actor.Dying) DrawCircle(point - Vector2.Down * 12, 9, color);
        if (actor.Dying) return; // death frames only, no ring or health bar
        if (Selection.Contains(actor.Id)) DrawCorners(actor.Structure ? Battle.Bounds(actor) : new Rect2(point - Vector2.One * 16, Vector2.One * 32));
        if (!debug) return;   // labels, rings and bars below belong to the debug view (F1), the original has none
        float width = actor.Structure ? 72 : 30;
        if (actor.Worker && actor.Record[0x31] == 3) DrawCircle(point + new Vector2(12, -24), 5, new Color("d3ac64"));
        var bar = new Rect2(point + new Vector2(-width / 2, 20), new Vector2(width, 5));
        DrawRect(bar, new Color("18211d")); DrawRect(new Rect2(bar.Position + Vector2.One, new Vector2((width - 2) * actor.Health / actor.MaxHealth, 3)), color);
        if (actor.ConstructionRemaining > 0)
            DrawRect(new Rect2(bar.Position + new Vector2(0, 8), new Vector2(width * (float)(1 - actor.ConstructionRemaining / actor.ConstructionDuration), 4)), new Color("e7cd82"));
        if (actor.Hero || actor.Structure || Selection.Contains(actor.Id) || actor.Id == _hover)
            DrawString(ThemeDB.FallbackFont, point + new Vector2(-40, actor.Structure ? -(frame?.Height ?? actor.Template.Preview.Height) + 12 : -42), actor.Hero ? "ПРЕДВОДИТЕЛЬ" : actor.Source.TemplateName, HorizontalAlignment.Left, -1, 11, color);
        if (actor.Id == Battle.Objective.Id)
        { var box = Battle.Bounds(actor).Grow(4); DrawRect(box, new Color(color, .7f), false, 1); }
    }
    /// <summary>Selection mark of the original: four green corners, 4 px arms, around the object (measured on screenshots).</summary>
    private void DrawCorners(Rect2 box)
    {
        var c = new Color(121 / 255f, 186 / 255f, 36 / 255f); var a = box.Position; var b = box.End - Vector2.One;
        foreach (var (corner, dx, dy) in new[] { (a, 1, 1), (new Vector2(b.X, a.Y), -1, 1), (new Vector2(a.X, b.Y), 1, -1), (b, -1, -1) })
        {
            DrawRect(new Rect2(corner.X + (dx < 0 ? -3 : 0), corner.Y, 4, 1), c);
            DrawRect(new Rect2(corner.X, corner.Y + (dy < 0 ? -3 : 0), 1, 4), c);
        }
    }
    private void BuildUi()
    {
        _ui = new CanvasLayer(); AddChild(_ui);
        var panel = new PanelContainer(); panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.LeftWide); panel.OffsetRight = 280;
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("15231f"), BorderColor = new Color("4e6855"), BorderWidthRight = 2,
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 16, ContentMarginBottom = 16 }); _ui.AddChild(panel);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; panel.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; column.AddThemeConstantOverride("separation", 6); scroll.AddChild(column);
        Label Text(string text, int size = 13)
        { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }; label.AddThemeFontSizeOverride("font_size", size); column.AddChild(label); return label; }
        void Button(string text, Action action)
        { var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None }; button.Pressed += action; column.AddChild(button); }
        (Button, Button) Row(string left, Action first, string right, Action second)
        {
            var row = new HBoxContainer(); column.AddChild(row);
            Button Make(string text, Action action)
            {
                var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                button.AddThemeFontSizeOverride("font_size", 12); button.Pressed += action; row.AddChild(button); return button;
            }
            return (Make(left, first), Make(right, second));
        }
        Text("СЕВЕРНЫЙ ВЕТЕР", 21); Text("ПЕРВАЯ СХВАТКА", 12);
        Text("Разбейте красный замок и его защитников. Предводитель должен выжить.", 14);
        _minimap = new TextureRect { CustomMinimumSize = new Vector2(240, 132), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        column.AddChild(_minimap);
        _markers = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; _markers.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _minimap.AddChild(_markers);
        _markers.Draw += DrawMinimap;
        _minimap.GuiInput += e =>
        {
            if (!_ready || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b) return;
            var rect = MiniRect(); if (rect.HasPoint(b.Position)) Focus((b.Position - rect.Position) / rect.Size * new Vector2(Battle.Map.Width, Battle.Map.Height) * 32);
        };
        Row("ПРЕДВОДИТЕЛЬ [End]", FocusHero, "ЦЕЛЬ [T]", FocusObjective);
        _status = Text("Загрузка…", 16); _selection = Text("", 13);
        _economy = Text("", 13);
        (_hire, _hireMilitia) = Row("", HireWorker, "", HireMilitia);
        (_buildMill, _buildFarm) = Row("", BeginMillPlacement, "", BeginFarmPlacement);
        _buildBarracks = new Button { FocusMode = Control.FocusModeEnum.None }; _buildBarracks.Pressed += BeginBarracksPlacement; column.AddChild(_buildBarracks);
        _buildMine = new Button { FocusMode = Control.FocusModeEnum.None }; _buildMine.Pressed += BeginMinePlacement; column.AddChild(_buildMine);
        _recruitment = Text("", 12);
        _cancelRecruitment = new Button { Text = "ОЧИСТИТЬ ОЧЕРЕДЬ", FocusMode = Control.FocusModeEnum.None };
        _cancelRecruitment.Pressed += CancelRecruitment; column.AddChild(_cancelRecruitment);
        Row("ВОЙСКО [Ctrl+A]", SelectArmy, "СТОП [X]", () => Battle.Stop(Selection));
        (_pause, _speed) = Row("ПАУЗА [ПРОБЕЛ]", TogglePause, "СКОРОСТЬ ×1", ChangeSpeed);
        Button("НАЧАТЬ ЗАНОВО  [R]", Restart);
        Text("ЛКМ / рамка — выделить\nПКМ — идти / атаковать / рубить\nShift — добавить · Home — замок\nWASD, стрелки, СКМ — камера", 12);
        _events = Text("", 12);
        Button("ЛАБОРАТОРИЯ  [F10]", () => GetTree().ChangeSceneToFile("res://scenes/main.tscn"));
        Text("Ранняя версия: бой и ИИ временные. Кампания ещё в работе.", 11);
    }
    private void RefreshTerrain()
    {
        if (_terrainRevision == Battle.TerrainRevision) return;
        for (int y = 0; y < Battle.Map.Height; y++)
        for (int x = 0; x < Battle.Map.Width; x++)
        {
            var rgb = _terrainColors[Battle.Map.Cells[y * Battle.Map.Width + x] & 65535];
            _minimapImage.SetPixel(x, y, new Color(rgb[0] / 255f, rgb[1] / 255f, rgb[2] / 255f));
        }
        _minimapTexture.Update(_minimapImage); _terrainRevision = Battle.TerrainRevision;
    }
    private Rect2 MiniRect()
    { float side = Math.Min(_minimap.Size.X, _minimap.Size.Y); return new((_minimap.Size - Vector2.One * side) / 2, Vector2.One * side); }
    private void DrawMinimap()
    {
        if (!_ready) return;
        var rect = MiniRect(); var world = new Vector2(Battle.Map.Width, Battle.Map.Height) * 32;
        foreach (var actor in Battle.Actors.Where(a => a.Alive))
            _markers.DrawCircle(rect.Position + actor.Position / world * rect.Size, actor.Structure ? 3 : 2, TeamColor(actor.Team));
        var view = GetViewportRect().Size / Camera.Zoom;
        _markers.DrawRect(new Rect2(rect.Position + (Camera.Position - view / 2) / world * rect.Size, view / world * rect.Size), new Color("f1da93"), false);
    }
    private void RefreshUi()
    {
        if (Battle == null) return;
        _status.Text = Battle.Outcome != "" ? Battle.Outcome + "\nR — повторить" : Battle.Paused ? "ПАУЗА · Esc — продолжить" :
            !Battle.Objective.Standing ? $"Замок разрушен\nОсталось защитников: {Battle.Actors.Count(a => a.Team == 1 && !a.Structure && a.Alive)} · T — найти" :
            $"Время {TimeSpan.FromSeconds(Battle.Elapsed):mm\\:ss} · ×{Speed}";
        var units = Selection.Select(Battle.Find).OfType<SkirmishActor>().ToArray();
        _selection.Text = units.Length == 1 ? $"{units[0].Source.TemplateName}{(units[0].Team != 0 ? " (чужой)" : "")}\nЗдоровье {units[0].Health}/{units[0].MaxHealth}"
            : $"Выбрано отрядов: {units.Length}\nЖивых союзников: {Battle.Actors.Count(a => a.Team == 0 && a.Alive && !a.Structure)}";
        if (units is [{ Worker: true } worker]) _selection.Text += $"\n{worker.JobText}";
        if (units is [var selectedFarm] && selectedFarm.Source.TemplateName == "Ферма")
        {
            uint support = (sbyte)selectedFarm.Record[0x19] > (sbyte)selectedFarm.Parameters[0x46] ? BinaryPrimitives.ReadUInt32LittleEndian(selectedFarm.Parameters.AsSpan(0x78)) : 0;
            _selection.Text += selectedFarm.ConstructionRemaining > 0 ? $"\nСтроится: {selectedFarm.ConstructionRemaining:0} с" : $"\nПоддержка поселения: {support}";
        }
        if (units is [var selectedMill] && selectedMill.Source.TemplateName == "Лесопилка")
            _selection.Text += selectedMill.ConstructionRemaining > 0 ? $"\nСтроится: {selectedMill.ConstructionRemaining:0} с" : $"\nГрузов в очереди: {selectedMill.Record[0x24]}";
        if (units is [var selectedMine] && selectedMine.Source.TemplateName == "Шахта")
            _selection.Text += selectedMine.ConstructionRemaining > 0 ? $"\nСтроится: {selectedMine.ConstructionRemaining:0} с" :
                $"\nРуда: {Skirmish.OreName(selectedMine.Record[0x23])} · мешков: {(BinaryPrimitives.ReadUInt32LittleEndian(selectedMine.Record.AsSpan(0x24)) + 99) / 100}\nПКМ рабочими — добывать";
        _economy.Text = $"Металл: {Battle.Metal} · Дерево: {Battle.Wood}\nЗолото: {Battle.Gold} · Люди: {Battle.FreePeople}\nПоддержка ферм: {Battle.Population.FarmSupport} · Нагрузка: {Battle.Population.Used}";
        if (Battle.FreePeople == 0) _economy.Text += Battle.PopulationGrowthSeconds is { } seconds ? $"\nПополнение через {Math.Ceiling(seconds)} с" : "\nДля прироста нужна ферма";
        bool castle = units is [var selected] && selected.Id == Battle.PlayerCastle.Id && selected.Alive;
        SkirmishActor? selectedBarracks = units is [var selectedB] && Skirmish.IsBarracks(selectedB) && selectedB.Team == 0 ? selectedB : null;
        bool barracks = selectedBarracks != null;
        _hire.Visible = _buildMill.Visible = _buildFarm.Visible = _buildBarracks.Visible = _buildMine.Visible = castle;
        _hireMilitia.Visible = barracks;
        _hire.Text = $"РАБОЧИЙ\n{Skirmish.PriceText(Battle.WorkerTemplate)}";
        _hireMilitia.Text = $"ОПОЛЧЕНЕЦ\n{Skirmish.PriceText(Battle.MilitiaTemplate)}";
        _buildMill.Text = $"ЛЕСОПИЛКА\n{Skirmish.PriceText(Battle.MillTemplate)}";
        _buildFarm.Text = $"ФЕРМА\n{Skirmish.PriceText(Battle.FarmTemplate)}";
        _buildBarracks.Text = $"КАЗАРМА · {Skirmish.PriceText(Battle.BarracksTemplate).Replace("\n", " · ")}";
        _buildMine.Text = $"ШАХТА · {Skirmish.PriceText(Battle.MineTemplate).Replace("\n", " · ")}";
        bool blocked = Battle.Paused || Battle.Outcome != "";
        _hire.Disabled = blocked || Battle.RecruitmentCount == 5;
        _hireMilitia.Disabled = blocked || selectedBarracks?.ConstructionRemaining > 0 || (selectedBarracks != null && Battle.BarracksRecruitmentCountFor(selectedBarracks) == 5);
        _buildMill.Disabled = blocked || !Battle.CanAfford(Battle.MillTemplate);
        _buildFarm.Disabled = blocked || !Battle.CanAfford(Battle.FarmTemplate);
        _buildBarracks.Disabled = blocked || !Battle.CanAfford(Battle.BarracksTemplate);
        _buildMine.Disabled = blocked || !Battle.CanAfford(Battle.MineTemplate);
        _recruitment.Visible = _cancelRecruitment.Visible = castle || barracks;
        _recruitment.Text = selectedBarracks != null ? Battle.BarracksRecruitmentTextFor(selectedBarracks) : Battle.RecruitmentText;
        _cancelRecruitment.Disabled = blocked || (selectedBarracks != null ? Battle.BarracksRecruitmentCountFor(selectedBarracks) == 0 : Battle.RecruitmentCount == 0);
        _cancelRecruitment.TooltipText = "Снимает все заказы. Оплаченный заказ возвращает людей целиком, материалы — пропорционально накопленному прогрессу по формуле оригинала.";
        _hire.TooltipText = "Добавить в очередь; оплата при начале подготовки. " + Battle.MissingResources(Battle.WorkerTemplate);
        _hireMilitia.TooltipText = "Заказ в казарме; оплата при начале подготовки. " + Battle.MissingResources(Battle.MilitiaTemplate);
        _buildMill.TooltipText = Battle.MissingResources(Battle.MillTemplate);
        _buildFarm.TooltipText = Battle.MissingResources(Battle.FarmTemplate);
        _buildBarracks.TooltipText = Battle.MissingResources(Battle.BarracksTemplate);
        _buildMine.TooltipText = Battle.MissingResources(Battle.MineTemplate);
        if (PlacingMine) _status.Text = "Место для шахты\nЛКМ — строить · ПКМ/Esc — отмена";
        if (PlacingBarracks) _status.Text = "Место для казармы\nЛКМ — строить · ПКМ/Esc — отмена";
        if (PlacingFarm) _status.Text = "Место для фермы\nЛКМ — строить · ПКМ/Esc — отмена";
        if (PlacingMill) _status.Text = "Место для лесопилки\nОставьте проход к жёлтой точке.\nЛКМ — строить · ПКМ/Esc — отмена";
        _pause.Text = Battle.Paused ? "ПРОДОЛЖИТЬ" : "ПАУЗА [ПРОБЕЛ]"; _speed.Text = $"СКОРОСТЬ ×{Speed}";
        _events.Text = string.Join('\n', Battle.Events.TakeLast(2));
    }
}
