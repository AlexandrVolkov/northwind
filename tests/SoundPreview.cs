using Godot;
using Northwind;

/// <summary>Manual audition of bundled WAVs without loading the gameplay UI.</summary>
public partial class SoundPreview : Control
{
    private readonly OriginalSoundBank _catalog = OriginalSoundBank.Load();
    private readonly AudioStreamPlayer _player = new();
    private readonly OptionButton _banks = new();
    private readonly ItemList _samples = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly Label _info = new();
    private SoundVariant[] _clips = [];
    private AudioStreamWav? _stream;

    public override void _Ready()
    {
        AddChild(_player);
        var panel = new VBoxContainer { OffsetLeft = 20, OffsetTop = 20, OffsetRight = -20, OffsetBottom = -20 };
        panel.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(panel);
        panel.AddChild(new Label { Text = "Звуки оригинала — 11025 Гц, 8 бит, моно" });
        panel.AddChild(_banks); panel.AddChild(_samples); panel.AddChild(_info);
        var play = new Button { Text = "Слушать выбранный звук (или двойной щелчок)" };
        var stop = new Button { Text = "Остановить" };
        panel.AddChild(play); panel.AddChild(stop);
        foreach (var bank in _catalog.Banks) _banks.AddItem(bank.Source);
        _banks.ItemSelected += index => ShowBank((int)index);
        _samples.ItemActivated += index => Play((int)index);
        play.Pressed += () => { if (_samples.GetSelectedItems() is { Length: > 0 } indices) Play(indices[0]); };
        stop.Pressed += _player.Stop;
        ShowBank(0);
    }

    private void ShowBank(int index)
    {
        _player.Stop(); _samples.Clear();
        var bank = _catalog.Banks[index];
        _clips = bank.Sounds.SelectMany(s => s.Variants).ToArray();
        foreach (var sound in bank.Sounds)
        foreach (var sample in sound.Variants)
            _samples.AddItem($"Звук {sound.Sound}, вариант {sample.Variant} — {sample.DurationSeconds:F2} с — {sample.File}");
        _info.Text = $"{bank.SoundCount} номеров, {_clips.Length} записей. Номера начинаются с нуля.";
        if (_clips.Length > 0) _samples.Select(0);
    }

    private void Play(int index)
    {
        _player.Stop(); _player.Stream = null; _stream?.Dispose();
        _stream = OriginalSoundBank.Stream(_clips[index]);
        _player.Stream = _stream; _player.Play();
        _info.Text = $"{_clips[index].File} — {_clips[index].Frames} отсчётов";
    }

    public override void _ExitTree()
    {
        _player.Stop(); _player.Stream = null; _stream?.Dispose(); _stream = null;
    }
}
