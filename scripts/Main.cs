using Godot;
namespace Northwind;

public partial class Main : Node2D
{
    public const int Tile = 32;
    private const float PanelWidth = 302;
    public Simulation Model { get; } = new();
    public bool ReadyToPlay { get; private set; }
    public int Selected { get; private set; } = -1;
    public Dictionary<int, Vector2> Positions { get; } = [];
    public Dictionary<int, Queue<Vector2I>> Animations { get; } = [];
    private MapData _map = new();
    private GameRules _rules = new();
    private Texture2D? _terrain, _warrior;
    public Camera2D Camera { get; } = new();
    private Vector2I _hover = new(-1, -1);
    private List<Vector2I> _route = [];
    private bool _grid, _dragging;
    public bool OriginalView { get; private set; }
    public List<(string File, ObjectRecord Record)> OriginalObjects { get; } = [];
    private int _objectFocus;
    public TemplateGallery? Gallery { get; private set; }
    public ObservationView? Observation { get; private set; }

    private double _clock;
    private CanvasLayer _ui = null!;
    private Label _turn = null!, _selection = null!, _log = null!, _cell = null!;
    public Button EndButton { get; private set; } = null!;
    private TextureRect _minimap = null!;
    private Control _miniMarker = null!;
    public AcceptDialog? ScenarioDialog { get; private set; }
    public RichTextLabel? ScenarioText { get; private set; }

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        AddChild(Camera); Camera.Enabled = true; BuildUi();
        if (!Godot.FileAccess.FileExists("res://assets/imported/map.json"))
        {
            _selection.Text = "Нужны ресурсы игры\n\nЗапустите импорт:\npython tools/import_game.py\nпуть/к/Horde.ISO\n\nЗатем перезапустите проект.\nПодробности — в README.";
            EndButton.Disabled = true; return;
        }
        try
        {
            _map = GameData.Read<MapData>("res://assets/imported/map.json");
            if (_map.FormatVersion != 1) throw new InvalidDataException("Unsupported map format");
            _rules = GameData.Read<GameRules>("res://data/prototype_rules.json");
            _terrain = GD.Load<Texture2D>("res://assets/imported/terrain.png");
            _warrior = GD.Load<Texture2D>("res://assets/imported/warrior.png");
            _minimap.Texture = GD.Load<Texture2D>("res://assets/imported/minimap.png");
            if (_terrain == null || _warrior == null || _minimap.Texture == null) throw new InvalidDataException("Missing textures");
            if (Godot.FileAccess.FileExists("res://assets/imported/scenario.json"))
                foreach (var slot in GameData.Read<ScenarioData>("res://assets/imported/scenario.json").Slots)
                    foreach (var record in slot.Objects) OriginalObjects.Add((slot.ObjectFile, record));
            ReadyToPlay = true; Restart();
        }
        catch (Exception error)
        {
            GD.PushError(error.Message); _selection.Text = "Не удалось загрузить ресурсы.\nПовторите импорт."; EndButton.Disabled = true;
        }
    }
    public override void _ExitTree() => Model.Dispose();
    public static Vector2 CenterOf(Vector2I cell) => (Vector2)(cell * Tile) + Vector2.One * Tile / 2f;
    public void Restart()
    {
        Model.Setup(_map, _rules); Positions.Clear(); Animations.Clear();
        foreach (var u in Model.Units) Positions[u.Id] = CenterOf(u.Cell);
        Selected = Model.Units.FirstOrDefault()?.Id ?? -1;
        FocusSelected(); RefreshUi(); QueueRedraw();
    }
    private void FocusSelected()
    {
        var unit = Model.GetUnit(Selected);
        if (unit != null) Camera.Position = CenterOf(unit.Cell) - new Vector2(PanelWidth / (2 * Camera.Zoom.X), 0);
        ClampCamera();
    }
    public override void _Process(double delta)
    {
        if (!ReadyToPlay) return;
        _clock += delta; var direction = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left)) direction.X--;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right)) direction.X++;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up)) direction.Y--;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down)) direction.Y++;
        Camera.Position += direction.Normalized() * 650 * (float)delta / Camera.Zoom.X; ClampCamera();
        foreach (int id in Animations.Keys.ToArray())
        {
            var steps = Animations[id];
            if (steps.Count == 0) { Animations.Remove(id); continue; }
            var destination = CenterOf(steps.Peek());
            Positions[id] = Positions[id].MoveToward(destination, (float)delta * 180);
            if (Positions[id].DistanceTo(destination) < 0.1f) steps.Dequeue();
        }
        var hover = (Vector2I)(GetGlobalMousePosition() / Tile).Floor();
        if (hover != _hover)
        {
            _hover = hover; _route = !OriginalView && Animations.Count == 0 ? Model.PathFor(Model.GetUnit(Selected), hover) : []; RefreshUi();
        }
        EndButton.Disabled = OriginalView || Animations.Count != 0 || Model.Outcome() != "";
        QueueRedraw(); _miniMarker.QueueRedraw();
    }
    private void ClampCamera()
    {
        var half = GetViewportRect().Size / Camera.Zoom / 2;
        var world = new Vector2(Model.Map.Width * Tile, Model.Map.Height * Tile);
        Camera.Position = new Vector2(
            Mathf.Clamp(Camera.Position.X, Math.Min(half.X, world.X / 2), Math.Max(world.X - half.X, world.X / 2)),
            Mathf.Clamp(Camera.Position.Y, Math.Min(half.Y, world.Y / 2), Math.Max(world.Y - half.Y, world.Y / 2)));
    }
    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: false }) _dragging = false;
    }
    public override void _UnhandledInput(InputEvent e)
    {
        if (!ReadyToPlay) return;
        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.B }) { OpenObservation(); return; }
        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.P }) { OpenGallery(); return; }
        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.O }) { ToggleOriginalView(); return; }
        if (OriginalView && e is InputEventKey { Pressed: true, Echo: false } viewerKey)
        {
            if (viewerKey.PhysicalKeycode == Key.N) { _objectFocus++; FocusObject(); }
            if (viewerKey.PhysicalKeycode == Key.I) OpenScenario();
            if (viewerKey.PhysicalKeycode == Key.G) _grid = !_grid;
            return;
        }
        if (e is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Middle) _dragging = button.Pressed;
            if (!button.Pressed) return;
            if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                var before = GetGlobalMousePosition();
                Camera.Zoom = Vector2.One * Mathf.Clamp(Camera.Zoom.X * (button.ButtonIndex == MouseButton.WheelUp ? 1.2f : 1 / 1.2f), .35f, 2.5f);
                Camera.Position += before - GetGlobalMousePosition(); ClampCamera();
            }
            if (OriginalView || Animations.Count != 0 || Model.Outcome() != "") return;
            var world = GetViewport().GetCanvasTransform().AffineInverse() * button.Position;
            var cell = (Vector2I)(world / Tile).Floor(); var target = Model.UnitAt(cell);
            if (button.ButtonIndex == MouseButton.Left)
            {
                if (target?.Team == 0) Selected = target.Id;
                else if (target?.Team == 1) AttackTarget(target);
            }
            else if (button.ButtonIndex == MouseButton.Right)
            {
                if (target?.Team == 1) AttackTarget(target);
                else
                {
                    var path = Model.MoveUnit(Selected, cell);
                    if (path.Count == 0) Model.Log("Маршрут недоступен или не хватает очков движения.");
                    else AnimateRoute(Selected, path);
                }
            }
            _route = Model.PathFor(Model.GetUnit(Selected), cell); RefreshUi();
        }
        else if (e is InputEventMouseMotion motion && _dragging) { Camera.Position -= motion.Relative / Camera.Zoom; ClampCamera(); }
        else if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.Space: FinishTurn(); break;
                case Key.I: OpenScenario(); break;
                case Key.G: _grid = !_grid; break;
                case Key.F: FocusSelected(); break;
                case Key.R: Restart(); break;
                case Key.Escape: Selected = -1; _route.Clear(); RefreshUi(); break;
            }
        }
    }
    private void AnimateRoute(int id, List<Vector2I> path) { Animations[id] = new Queue<Vector2I>(path.Skip(1)); _route.Clear(); }
    private void AttackTarget(UnitState target)
    {
        if (!Model.Attack(Selected, target.Id)) Model.Log("Для атаки встаньте на соседнюю клетку; нужен 1 ОД и неиспользованная атака.");
    }
    public void FinishTurn()
    {
        if (!ReadyToPlay || OriginalView || Animations.Count != 0) return;
        foreach (var action in Model.EndTurn()) AnimateRoute(action.Id, action.Path);
        RefreshUi();
    }
    public override void _Draw()
    {
        if (!ReadyToPlay) return;
        var half = GetViewportRect().Size / Camera.Zoom / 2;
        var first = (Vector2I)((Camera.Position - half) / Tile).Floor();
        var last = (Vector2I)((Camera.Position + half) / Tile).Ceil();
        for (int y = Math.Max(0, first.Y); y < Math.Min(Model.Map.Height, last.Y); y++)
        for (int x = Math.Max(0, first.X); x < Math.Min(Model.Map.Width, last.X); x++)
        {
            int tile = (int)(Model.Map.Cells[y * Model.Map.Width + x] & 0xffff);
            var rect = new Rect2(x * Tile, y * Tile, Tile, Tile);
            DrawTextureRectRegion(_terrain!, rect, new Rect2(tile % 32 * Tile, tile / 32 * Tile, Tile, Tile));
            if (_grid) DrawRect(rect, new Color(1, 1, 1, .12f), false);
        }
        if (OriginalView)
        {
            foreach (var (_, record) in OriginalObjects)
            {
                var point = new Vector2(record.RawX, record.RawY);
                var color = Color.FromHsv((record.RawCode % 5) / 5f, .6f, 1);
                DrawArc(point, 8, 0, Mathf.Tau, 24, color, 2);
                DrawLine(point - Vector2.Right * 12, point + Vector2.Right * 12, color);
                DrawLine(point - Vector2.Down * 12, point + Vector2.Down * 12, color);
            }
            return;
        }
        var selected = Model.GetUnit(Selected);
        if (Model.Inside(_hover) && GetViewport().GetMousePosition().X > PanelWidth)
            DrawRect(new Rect2((Vector2)(_hover * Tile), Vector2.One * Tile), Model.Passable(_hover) ? new Color(1, .75f, .4f, .8f) : new Color(1, .2f, .2f, .8f), false, 1.5f);
        int spent = 0;
        if (selected != null && Animations.Count == 0)
        for (int i = 1; i < _route.Count; i++)
        {
            spent += Model.MovementCost(_route[i]);
            var tint = spent <= selected.Moves ? new Color(.5f, 1, .55f, .85f) : new Color(1, .35f, .25f, .65f);
            DrawLine(CenterOf(_route[i - 1]), CenterOf(_route[i]), tint, 2); DrawCircle(CenterOf(_route[i]), 2.5f, tint);
        }
        foreach (var unit in Model.Units.Where(u => u.Hp > 0).OrderBy(u => Positions[u.Id].Y))
        {
            var point = Positions[unit.Id]; var tint = new Color(unit.Team == 0 ? "75dcce" : "f58c74");
            DrawSetTransform(point, 0, new Vector2(1, .45f));
            DrawCircle(Vector2.Zero, 13, new Color(tint, .2f)); DrawArc(Vector2.Zero, 13, 0, Mathf.Tau, 32, tint, 2);
            if (unit.Id == Selected) DrawArc(Vector2.Zero, 17, 0, Mathf.Tau, 32, new Color("f2d895"), 2);
            DrawSetTransform(Vector2.Zero);
            int frame = Animations.ContainsKey(unit.Id) ? (int)(_clock * 8) % 8 : 0;
            DrawTextureRectRegion(_warrior!, new Rect2(point - new Vector2(31, 45), new Vector2(62, 62)), new Rect2(frame * 62, 0, 62, 62));
            DrawRect(new Rect2(point + new Vector2(-14, 17), new Vector2(28, 4)), new Color(0, 0, 0, .8f));
            DrawRect(new Rect2(point + new Vector2(-13, 18), new Vector2(26f * unit.Hp / _rules.Unit.MaxHp, 2)), tint);
        }
    }
    private static Label LabelFor(Node parent, string text, int size, string color)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", new Color(color)); parent.AddChild(label); return label;
    }
    private void BuildUi()
    {
        _ui = new CanvasLayer(); AddChild(_ui);
        var panel = new PanelContainer(); panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.LeftWide); panel.OffsetRight = PanelWidth;
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("14201e"), BorderColor = new Color("42584d"), BorderWidthRight = 1,
            ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 22, ContentMarginBottom = 18 });
        _ui.AddChild(panel);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8); scroll.AddChild(column);
        LabelFor(column, "СЕВЕРНЫЙ ВЕТЕР", 22, "f0dfb7"); LabelFor(column, "ОРДА / ИССЛЕДОВАТЕЛЬСКИЙ ПРОТОТИП", 10, "91a59b"); column.AddChild(new HSeparator());
        _turn = LabelFor(column, "Карта 01", 18, "e3eadd");
        _minimap = new TextureRect { CustomMinimumSize = new Vector2(256, 190), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        _minimap.GuiInput += MinimapInput; column.AddChild(_minimap);
        _miniMarker = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; _miniMarker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _miniMarker.Draw += DrawMiniMarker; _minimap.AddChild(_miniMarker);
        _selection = LabelFor(column, "Загрузка…", 15, "e3eadd"); _selection.CustomMinimumSize = new Vector2(0, 66);
        _cell = LabelFor(column, "", 12, "91a59b");
        EndButton = new Button { Text = "ЗАВЕРШИТЬ ХОД    [ПРОБЕЛ]", CustomMinimumSize = new Vector2(0, 42), FocusMode = Control.FocusModeEnum.None };
        EndButton.Pressed += FinishTurn; column.AddChild(EndButton);
        var scenario = new Button { Text = "ДАННЫЕ СЦЕНАРИЯ    [I]", FocusMode = Control.FocusModeEnum.None }; scenario.Pressed += OpenScenario; column.AddChild(scenario);
        LabelFor(column, "ЛКМ — выбрать / атаковать\nПКМ — идти / атаковать\nWASD, стрелки, СКМ — камера\nКолесо — масштаб · F — к отряду\nG — сетка · R — начать заново", 12, "aebfb1");
        var gallery = new Button { Text = "ШАБЛОНЫ / КАДРЫ    [P]", FocusMode = Control.FocusModeEnum.None };
        gallery.Pressed += OpenGallery; column.AddChild(gallery);
        var observed = new Button { Text = "ЭТАЛОНЫ НАБЛЮДЕНИЙ    [B]", FocusMode = Control.FocusModeEnum.None };
        observed.Pressed += OpenObservation; column.AddChild(observed);
        var original = new Button { Text = "ИСХОДНЫЕ ОБЪЕКТЫ    [O]", FocusMode = Control.FocusModeEnum.None };
        original.Pressed += ToggleOriginalView; column.AddChild(original);
        column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill }); _log = LabelFor(column, "", 12, "cfbd94");
        LabelFor(column, "Оригинальные карта и графика.\nДвижение, бой и ИИ — экспериментальные;\nсоответствие оригиналу не проверено.", 11, "99a59a");
    }
    private Rect2 MinimapRect()
    {
        float side = Math.Min(_minimap.Size.X, _minimap.Size.Y);
        return new Rect2((_minimap.Size - Vector2.One * side) / 2, Vector2.One * side);
    }
    private void MinimapInput(InputEvent e)
    {
        if (!ReadyToPlay || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } button) return;
        var rect = MinimapRect();
        if (rect.HasPoint(button.Position)) { Camera.Position = (button.Position - rect.Position) / rect.Size * new Vector2(Model.Map.Width, Model.Map.Height) * Tile; ClampCamera(); }
    }
    private void DrawMiniMarker()
    {
        if (!ReadyToPlay) return;
        var rect = MinimapRect(); var world = new Vector2(Model.Map.Width * Tile, Model.Map.Height * Tile); var view = GetViewportRect().Size / Camera.Zoom;
        _miniMarker.DrawRect(new Rect2(rect.Position + (Camera.Position - view / 2) / world * rect.Size, view / world * rect.Size), new Color(1, .9f, .6f, .85f), false);
        if (OriginalView)
        {
            foreach (var (_, record) in OriginalObjects)
                _miniMarker.DrawCircle(rect.Position + new Vector2(record.RawX, record.RawY) / world * rect.Size, 2, Colors.Yellow);
            return;
        }
        foreach (var u in Model.Units.Where(u => u.Hp > 0)) _miniMarker.DrawCircle(rect.Position + CenterOf(u.Cell) / world * rect.Size, 2, new Color(u.Team == 0 ? "75dcce" : "f58c74"));
    }
    private void RefreshUi()
    {
        if (!ReadyToPlay) return;
        if (OriginalView)
        {
            _turn.Text = $"OBE · {OriginalObjects.Count} записей";
            _selection.Text = "Координаты-кандидаты\nМаркеры не являются юнитами.\nO — бой · N — следующая запись";
            _cell.Text = "Масштаб 1:1 — гипотеза.\nНе подтверждён оригиналом.";
            if (OriginalObjects.Count > 0)
            {
                var (file, r) = OriginalObjects[_objectFocus % OriginalObjects.Count];
                _log.Text = $"{file} · запись {r.RecordIndex}\n{r.TemplateName} ({r.TemplateFile}:{r.TemplateIndex})\nСмещение {r.FileOffset} · {r.RecordBytes} байт\nКод {r.RawCode}, байт 3: {r.RawByte3}\nx={r.RawX}, y={r.RawY}";
            }
            else _log.Text = "Повторите импорт обновлённым конвертером.";
            return;
        }
        _turn.Text = $"Карта {_map.MapNumber:00}  ·  Ход {Model.Turn}"; var u = Model.GetUnit(Selected);
        _selection.Text = Model.Outcome() != "" ? Model.Outcome() : u == null ? "Выберите свой отряд\nБирюзовый круг — ваши войска."
            : $"{_rules.Unit.Name} · отряд {u.Id}\nЗдоровье {u.Hp}/{_rules.Unit.MaxHp}  ·  Движение {u.Moves}\nАтака: {(u.Attacked ? "использована" : "готова")}";
        string terrain = Model.TerrainAt(_hover) switch { "ground" => "земля", "forest" => "лес", "water" => "вода", "rock" => "скалы", _ => "за картой" };
        _cell.Text = $"Клетка {_hover.X}, {_hover.Y} · {terrain}" + (_route.Count > 1 ? $"\nСтоимость маршрута: {Model.RouteCost(_route)} ОД" : "");
        _log.Text = string.Join('\n', Model.Events.TakeLast(4));
    }
    public void ToggleOriginalView()
    {
        if (!ReadyToPlay || Animations.Count != 0) return;
        OriginalView = !OriginalView; _route.Clear();
        if (OriginalView) FocusObject(); else FocusSelected();
        EndButton.Disabled = OriginalView || Model.Outcome() != "";
        RefreshUi(); QueueRedraw();
    }
    private void FocusObject()
    {
        if (OriginalObjects.Count == 0) return;
        var r = OriginalObjects[_objectFocus % OriginalObjects.Count].Record;
        Camera.Position = new Vector2(r.RawX - PanelWidth / (2 * Camera.Zoom.X), r.RawY);
        ClampCamera(); RefreshUi();
    }
    public void OpenObservation()
    {
        if (Observation == null) { Observation = new ObservationView(); _ui.AddChild(Observation); }
        Observation.PopupCentered(new Vector2I(700, 680));
    }
    public void OpenGallery()
    {
        if (Gallery == null) { Gallery = new TemplateGallery(); _ui.AddChild(Gallery); }
        Gallery.PopupCentered(new Vector2I(760, 560));
    }
    public void OpenScenario()
    {
        if (ScenarioDialog == null)
        {
            ScenarioDialog = new AcceptDialog { Title = "Исходные данные сценария", MinSize = new Vector2I(740, 520), OkButtonText = "Закрыть" };
            _ui.AddChild(ScenarioDialog); ScenarioText = new RichTextLabel { CustomMinimumSize = new Vector2(700, 450), BbcodeEnabled = false, SelectionEnabled = true }; ScenarioDialog.AddChild(ScenarioText);
        }
        if (!Godot.FileAccess.FileExists("res://assets/imported/scenario.json")) ScenarioText!.Text = "Данные сценария ещё не импортированы.\nПовторите импорт обновлённым tools/import_game.py.";
        else
        {
            try
            {
                var scenario = GameData.Read<ScenarioData>("res://assets/imported/scenario.json");
                var lines = new List<string> { $"Карта: {scenario.MapFile} · записей: {scenario.SlotCount}",
                    "Имена и ссылки прочитаны из SCN. Порядок записей не определяет команду.",
                    "Размер OBE — число байтов, не число объектов. Расстановка в бою пока демонстрационная.", "" };
                foreach (var slot in scenario.Slots)
                {
                    lines.Add($"Запись {slot.SlotIndex + 1} · {(slot.LeaderName.Length > 0 ? slot.LeaderName : "(имя не задано)")}");
                    lines.Add($"Поселение: {slot.SettlementName}"); lines.Add($"{slot.ObjectFile} · {slot.ObjectBytes} байт · смещение SCN: {slot.FileOffset}\n");
                }
                ScenarioText!.Text = string.Join('\n', lines);
            }
            catch (Exception error) { ScenarioText!.Text = "Неверный scenario.json. Повторите импорт."; GD.PushError(error.Message); }
        }
        ScenarioDialog.PopupCentered(new Vector2I(760, 540));
    }
}
