using Godot;
namespace Northwind;

/// <summary>Frozen, separately observed samples. Does not assign live object anchors.</summary>
public partial class ObservationView : AcceptDialog
{
    public int SampleCount { get; private set; }
    public bool ImageLoaded { get; private set; }
    private ObservationSample[] _samples = [];
    private OptionButton _selector = null!;
    private Label _description = null!;
    private TextureRect _image = null!;
    public override void _Ready()
    {
        Title = "Подтверждённые области наблюдений"; OkButtonText = "Закрыть";
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(660, 600) }; AddChild(box);
        box.AddChild(new Label { Text = "Независимые статические наблюдения. Не полная игровая сцена и не симуляция.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _selector = new OptionButton(); box.AddChild(_selector);
        _description = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 70) }; box.AddChild(_description);
        _image = new TextureRect { CustomMinimumSize = new Vector2(448, 448), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest }; box.AddChild(_image);
        if (!Godot.FileAccess.FileExists("res://assets/imported/observations.json"))
        { _description.Text = "Повторите импорт обновлённым конвертером."; return; }
        var data = GameData.Read<ObservationsData>("res://assets/imported/observations.json");
        if (data.Status != "ready")
        { _description.Text = $"Эталон недоступен для этой карты/версии исходников.\n{data.Reason}"; return; }
        _samples = data.Samples; SampleCount = _samples.Length;
        foreach (var sample in _samples) _selector.AddItem(sample.Title);
        _selector.ItemSelected += index => ShowSample((int)index);
        if (SampleCount > 0) ShowSample(0);
    }
    public void ShowSample(int index)
    {
        if (index < 0 || index >= _samples.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var sample = _samples[index];
        if (System.IO.Path.GetFileName(sample.Image) != sample.Image || !sample.Image.StartsWith("observed_"))
            throw new InvalidDataException("Invalid observation image path");
        var texture = GD.Load<Texture2D>("res://assets/imported/" + sample.Image) ?? throw new InvalidDataException("Observation image not imported");
        if (sample.ViewportSize.Length != 2 || texture.GetWidth() != sample.ViewportSize[0] || texture.GetHeight() != sample.ViewportSize[1])
            throw new InvalidDataException("Observation image dimensions differ from metadata");
        _selector.Select(index);
        _image.Texture = texture; ImageLoaded = true;
        _description.Text = sample.Note;
    }
}
