using Godot;
namespace Northwind;

/// <summary>Inspection only. Candidate frames do not establish animation state or map anchors.</summary>
public partial class TemplateGallery : AcceptDialog
{
    public int EntryCount { get; private set; }
    public int CandidateCount { get; private set; }
    public bool AtlasLoaded { get; private set; }
    public override void _Ready()
    {
        Title = "Шаблоны: кадры-кандидаты"; MinSize = new Vector2I(700, 520); OkButtonText = "Закрыть";
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(680, 450), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(column);
        column.AddChild(new Label { Text = "RLE и размер проверены. Состояние анимации, палитра команды и якорь не подтверждены.", AutowrapMode = TextServer.AutowrapMode.WordSmart });
        if (!Godot.FileAccess.FileExists("res://assets/imported/templates.json") || !Godot.FileAccess.FileExists("res://assets/imported/template_previews.png"))
        { column.AddChild(new Label { Text = "Повторите импорт обновлённым конвертером." }); return; }
        try
        {
            var data = GameData.Read<TemplatesData>("res://assets/imported/templates.json");
            var atlas = GD.Load<Texture2D>("res://assets/imported/template_previews.png") ?? throw new InvalidDataException("Atlas was not imported by the editor");
            AtlasLoaded = true;
            foreach (var (file, table) in data.Armies)
            foreach (var template in table.Templates)
            {
                EntryCount++;
                var row = new HBoxContainer(); column.AddChild(row);
                var preview = template.Preview;
                var image = new TextureRect { CustomMinimumSize = new Vector2(140, 140), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
                row.AddChild(image);
                bool candidate = preview.Status == "candidate" && preview.Frame.HasValue && preview.Width > 0 && preview.Height > 0;
                if (candidate)
                {
                    CandidateCount++;
                    image.Texture = new AtlasTexture { Atlas = atlas, Region = new Rect2(preview.AtlasX, preview.AtlasY, preview.Width, preview.Height) };
                }
                var label = new Label { Text = $"{template.Name}\n{file} · шаблон {template.TemplateIndex}\n" +
                    (candidate ? $"Кадр-кандидат {preview.Frame} · {preview.Width}×{preview.Height}" : "Кадр не установлен; см. rejections в templates.json"),
                    AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                row.AddChild(label);
            }
            Title = $"Кадры-кандидаты: {CandidateCount} из {EntryCount}";
        }
        catch (Exception error) { column.AddChild(new Label { Text = "Не удалось прочитать галерею. Повторите импорт." }); GD.PushError(error.Message); }
    }
}
