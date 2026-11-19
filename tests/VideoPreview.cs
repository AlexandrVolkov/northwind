using Godot;
using Northwind;

public partial class VideoPreview : Control
{
    private readonly OriginalVideoCatalog _catalog = OriginalVideoCatalog.Load();
    private readonly VideoStreamPlayer _player = new() { Expand = true, BufferingMsec = 100 };
    private readonly AspectRatioContainer _aspect = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Label _info = new();
    private VideoStreamTheora? _stream;

    public override void _Ready()
    {
        var panel = new VBoxContainer { OffsetLeft = 16, OffsetTop = 16, OffsetRight = -16, OffsetBottom = -16 };
        panel.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(panel);
        var choices = new OptionButton();
        foreach (var movie in _catalog.Videos)
            choices.AddItem(movie.Role == "startup_buka" ? "Заставка «Бука» — со звуком" : "От авторов — без звуковой дорожки");
        panel.AddChild(choices); panel.AddChild(_aspect); _aspect.AddChild(_player); panel.AddChild(_info);
        var row = new HBoxContainer(); panel.AddChild(row);
        var play = new Button { Text = "С начала" }; row.AddChild(play); play.Pressed += () => { _player.Paused = false; _player.Play(); };
        var pause = new Button { Text = "Пауза / продолжить" }; row.AddChild(pause); pause.Pressed += () => _player.Paused = !_player.Paused;
        var stop = new Button { Text = "Остановить" }; row.AddChild(stop); stop.Pressed += _player.Stop;
        choices.ItemSelected += index => ShowMovie((int)index);
        ShowMovie(0);
    }

    private void ShowMovie(int index)
    {
        _player.Stop(); _player.Stream = null; _stream?.Dispose();
        var movie = _catalog.Videos[index]; _stream = OriginalVideoCatalog.Stream(movie);
        _aspect.Ratio = (float)movie.Header.Width / movie.Header.Height;
        _info.Text = $"{movie.Source} → {movie.File} — {movie.Header.Width}×{movie.Header.Height}, {movie.Header.Frames} кадров, {movie.Header.DurationSeconds:F2} с";
        _player.Paused = false; _player.Stream = _stream; _player.Play();
    }

    public override void _ExitTree()
    {
        _player.Stop(); _player.Stream = null; _stream?.Dispose(); _stream = null;
    }
}
