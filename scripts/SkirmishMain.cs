using Godot;
using System.Buffers.Binary;
namespace Northwind;

public sealed class TerrainColorsData { public int[][] Colors { get; set; } = []; }
public sealed class BattleFramesData { public BattleFrame[] Frames { get; set; } = []; }
public sealed class BattleFrame
{
    public string TemplateFile { get; set; } = "";
    public int TemplateIndex { get; set; }
    public int Frame { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public partial class SkirmishMain : Node2D
{
    private const int PanelWidth = 280;
    public Skirmish Battle { get; private set; } = null!;
    public HashSet<uint> Selection { get; } = [];
    public Camera2D Camera { get; } = new();
    public float Speed { get; private set; } = 1;
    private Texture2D _terrain = null!, _sprites = null!;
    private readonly Dictionary<(string, int, int), BattleFrame> _frames = [];
    private Label _status = null!, _selection = null!, _events = null!;
    private Label _economy = null!, _recruitment = null!;
    private Button _cancelRecruitment = null!;
    private Button _hire = null!, _buildMill = null!, _hireMilitia = null!, _buildFarm = null!, _buildBarracks = null!;
    public bool PlacingBarracks { get; private set; }
    public bool PlacingMill { get; private set; }
    public bool PlacingFarm { get; private set; }
    public bool PlacingBuilding => PlacingMill || PlacingFarm || PlacingBarracks;
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
        AddChild(Camera); Camera.Enabled = true; Camera.Zoom = Vector2.One * 1.25f;
        BuildUi();
        try
        {
            _terrain = GD.Load<Texture2D>("res://assets/imported/terrain.png");
            _sprites = GD.Load<Texture2D>("res://assets/imported/battle_frames.png");
            if (_terrain == null || _sprites == null) throw new InvalidDataException("Bundled textures are missing");
            foreach (var frame in GameData.Read<BattleFramesData>("res://assets/imported/battle_frames.json").Frames)
                _frames[(frame.TemplateFile, frame.TemplateIndex, frame.Frame)] = frame;
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
        Battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"),
            GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), GameData.Read<SkirmishRules>("res://data/skirmish.json"));
        Selection.Clear(); Selection.Add(Battle.Hero.Id); _orderMark = null; _drag = _pan = PlacingMill = PlacingFarm = PlacingBarracks = false;
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
        Selection.Clear(); Selection.Add(Battle.PlayerCastle.Id); PlacingMill = PlacingFarm = PlacingBarracks = false;
        Focus(Battle.PlayerCastle.Position); RefreshUi();
    }
    public void HireWorker()
    {
        if (!Selection.Contains(Battle.PlayerCastle.Id)) return;
        Battle.QueueWorker();
        RefreshUi();
    }
    public void HireMilitia()
    {
        if (Selection.Count != 1 || Battle.Find(Selection.Single()) is not { } barracks || !Skirmish.IsBarracks(barracks)) return;
        Battle.QueueBarracksMilitia(barracks);
        RefreshUi();
    }
    public void CancelRecruitment()
    {
        if (Selection.Contains(Battle.PlayerCastle.Id)) Battle.CancelRecruitment();
        else if (Selection.Count == 1 && Battle.Find(Selection.Single()) is { } barracks && Skirmish.IsBarracks(barracks)) Battle.CancelBarracksRecruitment(barracks);
        RefreshUi();
    }
    public void BeginBarracksPlacement()
    {
        if (Battle.Paused || Battle.Outcome != "" || !Selection.Contains(Battle.PlayerCastle.Id) || !Battle.PlayerCastle.Alive) return;
        PlacingBarracks = true; PlacingMill = PlacingFarm = false; RefreshUi();
    }
    public void BeginFarmPlacement()
    {
        if (Battle.Paused || Battle.Outcome != "" || !Selection.Contains(Battle.PlayerCastle.Id) || !Battle.PlayerCastle.Alive) return;
        PlacingFarm = true; PlacingMill = PlacingBarracks = false; RefreshUi();
    }
    public void BeginMillPlacement()
    {
        if (Battle.Paused || Battle.Outcome != "" || !Selection.Contains(Battle.PlayerCastle.Id) || !Battle.PlayerCastle.Alive) return;
        PlacingMill = true; PlacingFarm = PlacingBarracks = false; RefreshUi();
    }
    public void TogglePause() { if (!_ready || Battle.Outcome != "") return; Battle.Paused = !Battle.Paused; RefreshUi(); }
    public void ChangeSpeed() { Speed = Speed == 1 ? 2 : Speed == 2 ? 4 : 1; RefreshUi(); }
    public void FocusHero() => Focus(Battle.Hero.Position - new Vector2(0, 100));
    public void FocusObjective()
    {
        var target = Battle.Objective.Alive ? Battle.Objective : Battle.Actors.FirstOrDefault(a => a.Team == 1 && a.Alive && !a.Structure);
        Focus(target?.Position ?? Battle.Objective.Position);
    }
    private void Focus(Vector2 point) { Camera.Position = point - new Vector2(PanelWidth / (2 * Camera.Zoom.X), 0); ClampCamera(); }
    private void ClampCamera()
    {
        if (Battle == null) return;
        var half = GetViewportRect().Size / Camera.Zoom / 2;
        var size = new Vector2(Battle.Map.Width, Battle.Map.Height) * 32;
        Camera.Position = new Vector2(Mathf.Clamp(Camera.Position.X, half.X, size.X - half.X), Mathf.Clamp(Camera.Position.Y, half.Y, size.Y - half.Y));
    }
    public override void _Process(double delta)
    {
        if (!_ready) return;
        Battle.Advance(delta * Speed); RefreshTerrain();
        Selection.RemoveWhere(id => Battle.Find(id) == null);
        Vector2 direction = Vector2.Zero;
        if (!Input.IsKeyPressed(Key.Ctrl))
        {
            if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) direction.X--;
            if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) direction.X++;
            if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) direction.Y--;
            if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) direction.Y++;
        }
        Camera.Position += direction.Normalized() * (float)delta * 700 / Camera.Zoom.X; ClampCamera();
        _hover = (int)(Battle.At(GetGlobalMousePosition())?.Id ?? 0);
        RefreshUi(); QueueRedraw(); _markers.QueueRedraw();
    }
    public void IssueOrder(Vector2 point)
    {
        if (Battle.Paused || Battle.Outcome != "") return;
        if (Selection.Count == 0) { Battle.Log("Сначала выделите отряды: рамкой или Ctrl+A."); return; }
        var target = Battle.At(point);
        var cell = new Vector2I((int)MathF.Floor(point.X / 32), (int)MathF.Floor(point.Y / 32));
        if (target?.Team == 1) Battle.Attack(Selection, target.Id);
        else if (target?.Source.TemplateName == "Лесопилка" && Battle.DeliverTimber(Selection, target.Id)) { }
        else if (Battle.Harvestable(cell) && Selection.Any(id => Battle.Find(id)?.Worker == true)) Battle.GatherTimber(Selection, cell);
        else Battle.Move(Selection, cell);
        _orderMark = point; _markUntil = Battle.Elapsed + 2;
    }
    public override void _Input(InputEvent e)
    {
        if (!_ready) return;
        // Release a drag even when the pointer finishes over a UI control.
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: false }) _pan = false;
        if (_drag && e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } button)
        {
            _dragEnd = button.Position;
            if (!Input.IsKeyPressed(Key.Shift)) Selection.Clear();
            if (_dragStart.DistanceTo(_dragEnd) < 6)
            {
                var point = GetViewport().GetCanvasTransform().AffineInverse() * button.Position;
                if (Battle.At(point) is { Team: 0 } actor)
                { if (!Selection.Add(actor.Id)) Selection.Remove(actor.Id); }
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
        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.CtrlPressed && key.PhysicalKeycode == Key.A) SelectArmy();
            else switch (key.PhysicalKeycode)
            {
                case Key.Space: TogglePause(); break;
                case Key.F: FocusHero(); break;
                case Key.T: FocusObjective(); break;
                case Key.C: SelectCastle(); break;
                case Key.R: Restart(); break;
                case Key.X: Battle.Stop(Selection); break;
                case Key.Escape: if (PlacingBuilding) { PlacingMill = false; PlacingFarm = PlacingBarracks = false; } else Selection.Clear(); break;
                case Key.F10: GetTree().ChangeSceneToFile("res://scenes/main.tscn"); break;
            }
        }
        if (e is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Middle) _pan = button.Pressed;
            if (!button.Pressed) return;
            if (PlacingBuilding && button.ButtonIndex == MouseButton.Right) { PlacingMill = PlacingFarm = PlacingBarracks = false; RefreshUi(); return; }
            if (PlacingBuilding && button.ButtonIndex == MouseButton.Left)
            {
                var point = GetViewport().GetCanvasTransform().AffineInverse() * button.Position;
                var cell = new Vector2I((int)MathF.Floor(point.X / 32), (int)MathF.Floor(point.Y / 32));
                var mill = PlacingFarm ? Battle.BuildFarm(cell) : PlacingBarracks ? Battle.BuildBarracks(cell) : Battle.BuildMill(cell);
                if (mill != null) { PlacingMill = PlacingFarm = PlacingBarracks = false; Selection.Clear(); Selection.Add(mill.Id); }
                RefreshUi(); return;
            }
            if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var before = GetGlobalMousePosition();
                Camera.Zoom = Vector2.One * Mathf.Clamp(Camera.Zoom.X * (button.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f), .55f, 2.5f);
                Camera.Position += before - GetGlobalMousePosition(); ClampCamera();
            }
            else if (button.ButtonIndex == MouseButton.Left && !Battle.Paused && Battle.Outcome == "")
            { _drag = true; _dragStart = _dragEnd = button.Position; }
            else if (button.ButtonIndex == MouseButton.Right) IssueOrder(GetViewport().GetCanvasTransform().AffineInverse() * button.Position);
        }
        else if (e is InputEventMouseMotion motion)
        {
            if (_drag) _dragEnd = motion.Position;
            if (_pan) { Camera.Position -= motion.Relative / Camera.Zoom; ClampCamera(); }
        }
    }
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
        foreach (var actor in Battle.Actors.Where(a => a.Alive).OrderBy(a => a.Position.Y)) DrawActor(actor);
        var producer = Selection.Count == 1 && Battle.Find(Selection.Single()) is { } barracks && Skirmish.IsBarracks(barracks)
            ? barracks : Selection.Contains(Battle.PlayerCastle.Id) ? Battle.PlayerCastle : null;
        if (!PlacingBuilding && producer is { Alive: true })
        {
            var template = producer == Battle.PlayerCastle ? Battle.RecruitmentTemplate ?? Battle.WorkerTemplate : Battle.MilitiaTemplate;
            var exit = Battle.RecruitExit(producer, template);
            var point = new Vector2(exit.X, exit.Y); var color = new Color(producer == Battle.PlayerCastle ? Battle.RecruitmentBlocked ? "ef8272" : "e7cd82" : Battle.BarracksRecruitmentBlocked(producer) ? "ef8272" : "e7cd82");
            DrawArc(point, 10, 0, Mathf.Tau, 24, color, 2);
            DrawString(ThemeDB.FallbackFont, point + new Vector2(-22, 22), "Выход", HorizontalAlignment.Left, -1, 12, color);
        }
        if (!PlacingBuilding && Selection.Any(id => Battle.Find(id)?.Worker == true))
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
            var parameters = (PlacingFarm ? Battle.FarmTemplate : PlacingBarracks ? Battle.BarracksTemplate : Battle.MillTemplate).Construction;
            var rect = new Rect2((Vector2)((cell - new Vector2I(parameters.FootprintWidth / 2, parameters.FootprintHeight / 2)) * 32), new Vector2(parameters.FootprintWidth * 32, parameters.FootprintHeight * 32));
            bool clear = !Battle.Paused && Battle.CanAfford(PlacingFarm ? Battle.FarmTemplate : PlacingBarracks ? Battle.BarracksTemplate : Battle.MillTemplate) && (PlacingFarm ? Battle.ProbeFarm(cell) : PlacingBarracks ? Battle.ProbeBarracks(cell) : Battle.ProbeMill(cell)).Status == ConstructionPlacementStatus.Clear;
            var color = clear ? new Color("8de5b8") : new Color("ef8272"); DrawRect(rect, new Color(color, .2f)); DrawRect(rect, color, false, 2);
            var entry = Battle.MillEntry(cell);
            if (PlacingMill && entry.Valid)
            {
                var pointOfEntry = new Vector2(entry.X, entry.Y);
                DrawLine(rect.GetCenter(), pointOfEntry, new Color("e7cd82"), 2);
                DrawCircle(pointOfEntry, 6, new Color("e7cd82"));
                DrawString(ThemeDB.FallbackFont, pointOfEntry + new Vector2(-24, -10), "Приём груза", HorizontalAlignment.Left, -1, 12, new Color("e7cd82"));
            }
        }
        foreach (var effect in Battle.Effects)
        {
            DrawLine(effect.From - new Vector2(0, 16), effect.To - new Vector2(0, 16), new Color(effect.Team == 0 ? "f0d88a" : "f57862"), 2);
            DrawCircle(effect.To - new Vector2(0, 16), 6, new Color(1, .6f, .2f, .5f));
        }
        foreach (uint id in Selection)
        {
            var actor = Battle.Find(id); if (actor == null) continue;
            var point = actor.Position;
            foreach (var cell in actor.Route)
            { var next = new Vector2(cell.X * 32 + 16, cell.Y * 32 + 16); DrawLine(point, next, new Color(.4f, 1, .7f, .45f), 1); point = next; }
        }
        if (_orderMark is { } mark && Battle.Elapsed < _markUntil)
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
        var point = actor.Position; var color = new Color(actor.Team == 0 ? "63e3bd" : "f37769");
        if (!actor.Structure)
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
            DrawTextureRectRegion(_sprites, new Rect2(point - new Vector2(size.X / 2, size.Y - 16), size), new Rect2(frame.X, frame.Y, frame.Width, frame.Height));
        }
        else DrawCircle(point - Vector2.Down * 12, 9, color);
        float width = actor.Structure ? 72 : 30;
        if (actor.Worker && actor.Record[0x31] == 3) DrawCircle(point + new Vector2(12, -24), 5, new Color("d3ac64"));
        var bar = new Rect2(point + new Vector2(-width / 2, 20), new Vector2(width, 5));
        DrawRect(bar, new Color("18211d")); DrawRect(new Rect2(bar.Position + Vector2.One, new Vector2((width - 2) * actor.Health / actor.Rules.Health, 3)), color);
        if (actor.ConstructionRemaining > 0)
            DrawRect(new Rect2(bar.Position + new Vector2(0, 8), new Vector2(width * (float)(1 - actor.ConstructionRemaining / actor.ConstructionDuration), 4)), new Color("e7cd82"));
        if (actor.Hero || actor.Structure || Selection.Contains(actor.Id) || actor.Id == _hover)
            DrawString(ThemeDB.FallbackFont, point + new Vector2(-40, actor.Structure ? -(frame?.Height ?? actor.Template.Preview.Height) + 12 : -42), actor.Hero ? "ПРЕДВОДИТЕЛЬ" : actor.Source.TemplateName, HorizontalAlignment.Left, -1, 11, color);
        if (actor.Id == Battle.Objective.Id)
        { var box = Battle.Bounds(actor).Grow(4); DrawRect(box, new Color(color, .7f), false, 1); }
    }
    private void BuildUi()
    {
        _ui = new CanvasLayer(); AddChild(_ui);
        var panel = new PanelContainer(); panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.LeftWide); panel.OffsetRight = PanelWidth;
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
        Row("ПРЕДВОДИТЕЛЬ [F]", FocusHero, "ЦЕЛЬ [T]", FocusObjective);
        _status = Text("Загрузка…", 16); _selection = Text("", 13);
        _economy = Text("", 13);
        (_hire, _hireMilitia) = Row("", HireWorker, "", HireMilitia);
        (_buildMill, _buildFarm) = Row("", BeginMillPlacement, "", BeginFarmPlacement);
        _buildBarracks = new Button { FocusMode = Control.FocusModeEnum.None }; _buildBarracks.Pressed += BeginBarracksPlacement; column.AddChild(_buildBarracks);
        _recruitment = Text("", 12);
        _cancelRecruitment = new Button { Text = "ОЧИСТИТЬ ОЧЕРЕДЬ", FocusMode = Control.FocusModeEnum.None };
        _cancelRecruitment.Pressed += CancelRecruitment; column.AddChild(_cancelRecruitment);
        Row("ВОЙСКО [Ctrl+A]", SelectArmy, "СТОП [X]", () => Battle.Stop(Selection));
        (_pause, _speed) = Row("ПАУЗА [ПРОБЕЛ]", TogglePause, "СКОРОСТЬ ×1", ChangeSpeed);
        Button("НАЧАТЬ ЗАНОВО  [R]", Restart);
        Text("ЛКМ / рамка — выделить\nПКМ — идти / атаковать / рубить\nShift — добавить · C — замок\nWASD, стрелки, СКМ — камера\nКолесо — масштаб", 12);
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
            _markers.DrawCircle(rect.Position + actor.Position / world * rect.Size, actor.Structure ? 3 : 2, new Color(actor.Team == 0 ? "63e3bd" : "f37769"));
        var view = GetViewportRect().Size / Camera.Zoom;
        _markers.DrawRect(new Rect2(rect.Position + (Camera.Position - view / 2) / world * rect.Size, view / world * rect.Size), new Color("f1da93"), false);
    }
    private void RefreshUi()
    {
        if (Battle == null) return;
        _status.Text = Battle.Outcome != "" ? Battle.Outcome + "\nR — повторить" : Battle.Paused ? "ПАУЗА · Пробел — продолжить" :
            !Battle.Objective.Alive ? $"Замок разрушен\nОсталось защитников: {Battle.Actors.Count(a => a.Team == 1 && !a.Structure && a.Alive)} · T — найти" :
            $"Время {TimeSpan.FromSeconds(Battle.Elapsed):mm\\:ss} · ×{Speed}";
        var units = Selection.Select(Battle.Find).OfType<SkirmishActor>().ToArray();
        _selection.Text = units.Length == 1 ? $"{units[0].Source.TemplateName}\nЗдоровье {units[0].Health}/{units[0].Rules.Health}"
            : $"Выбрано отрядов: {units.Length}\nЖивых союзников: {Battle.Actors.Count(a => a.Team == 0 && a.Alive && !a.Structure)}";
        if (units is [{ Worker: true } worker]) _selection.Text += $"\n{worker.JobText}";
        if (units is [var selectedFarm] && selectedFarm.Source.TemplateName == "Ферма")
        {
            uint support = (sbyte)selectedFarm.Record[0x19] > (sbyte)selectedFarm.Parameters[0x46] ? BinaryPrimitives.ReadUInt32LittleEndian(selectedFarm.Parameters.AsSpan(0x78)) : 0;
            _selection.Text += selectedFarm.ConstructionRemaining > 0 ? $"\nСтроится: {selectedFarm.ConstructionRemaining:0} с" : $"\nПоддержка поселения: {support}";
        }
        if (units is [var selectedMill] && selectedMill.Source.TemplateName == "Лесопилка")
            _selection.Text += selectedMill.ConstructionRemaining > 0 ? $"\nСтроится: {selectedMill.ConstructionRemaining:0} с" : $"\nГрузов в очереди: {selectedMill.Record[0x24]}";
        _economy.Text = $"Металл: {Battle.Metal} · Дерево: {Battle.Wood}\nЗолото: {Battle.Gold} · Люди: {Battle.FreePeople}\nПоддержка ферм: {Battle.Population.FarmSupport} · Нагрузка: {Battle.Population.Used}";
        if (Battle.FreePeople == 0) _economy.Text += Battle.PopulationGrowthSeconds is { } seconds ? $"\nПополнение через {Math.Ceiling(seconds)} с" : "\nДля прироста нужна ферма";
        bool castle = units is [var selected] && selected.Id == Battle.PlayerCastle.Id && selected.Alive;
        SkirmishActor? selectedBarracks = units is [var selectedB] && Skirmish.IsBarracks(selectedB) ? selectedB : null;
        bool barracks = selectedBarracks != null;
        _hire.Visible = _buildMill.Visible = _buildFarm.Visible = _buildBarracks.Visible = castle;
        _hireMilitia.Visible = barracks;
        _hire.Text = $"РАБОЧИЙ\n{Skirmish.PriceText(Battle.WorkerTemplate)}";
        _hireMilitia.Text = $"ОПОЛЧЕНЕЦ\n{Skirmish.PriceText(Battle.MilitiaTemplate)}";
        _buildMill.Text = $"ЛЕСОПИЛКА\n{Skirmish.PriceText(Battle.MillTemplate)}";
        _buildFarm.Text = $"ФЕРМА\n{Skirmish.PriceText(Battle.FarmTemplate)}";
        _buildBarracks.Text = $"КАЗАРМА · {Skirmish.PriceText(Battle.BarracksTemplate).Replace("\n", " · ")}";
        bool blocked = Battle.Paused || Battle.Outcome != "";
        _hire.Disabled = blocked || Battle.RecruitmentCount == 5;
        _hireMilitia.Disabled = blocked || selectedBarracks?.ConstructionRemaining > 0 || (selectedBarracks != null && Battle.BarracksRecruitmentCountFor(selectedBarracks) == 5);
        _buildMill.Disabled = blocked || !Battle.CanAfford(Battle.MillTemplate);
        _buildFarm.Disabled = blocked || !Battle.CanAfford(Battle.FarmTemplate);
        _buildBarracks.Disabled = blocked || !Battle.CanAfford(Battle.BarracksTemplate);
        _recruitment.Visible = _cancelRecruitment.Visible = castle || barracks;
        _recruitment.Text = selectedBarracks != null ? Battle.BarracksRecruitmentTextFor(selectedBarracks) : Battle.RecruitmentText;
        _cancelRecruitment.Disabled = blocked || (selectedBarracks != null ? Battle.BarracksRecruitmentCountFor(selectedBarracks) == 0 : Battle.RecruitmentCount == 0);
        _cancelRecruitment.TooltipText = "Снимает все заказы. Оплаченный заказ возвращает людей целиком, материалы — пропорционально накопленному прогрессу по формуле оригинала.";
        _hire.TooltipText = "Добавить в очередь; оплата при начале подготовки. " + Battle.MissingResources(Battle.WorkerTemplate);
        _hireMilitia.TooltipText = "Заказ в казарме; оплата при начале подготовки. " + Battle.MissingResources(Battle.MilitiaTemplate);
        _buildMill.TooltipText = Battle.MissingResources(Battle.MillTemplate);
        _buildFarm.TooltipText = Battle.MissingResources(Battle.FarmTemplate);
        _buildBarracks.TooltipText = Battle.MissingResources(Battle.BarracksTemplate);
        if (PlacingBarracks) _status.Text = "Место для казармы\nЛКМ — строить · ПКМ/Esc — отмена";
        if (PlacingFarm) _status.Text = "Место для фермы\nЛКМ — строить · ПКМ/Esc — отмена";
        if (PlacingMill) _status.Text = "Место для лесопилки\nОставьте проход к жёлтой точке.\nЛКМ — строить · ПКМ/Esc — отмена";
        _pause.Text = Battle.Paused ? "ПРОДОЛЖИТЬ" : "ПАУЗА [ПРОБЕЛ]"; _speed.Text = $"СКОРОСТЬ ×{Speed}";
        _events.Text = string.Join('\n', Battle.Events.TakeLast(2));
    }
}
