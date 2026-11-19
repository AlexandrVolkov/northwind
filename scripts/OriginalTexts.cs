using Godot;

namespace Northwind;

public sealed class OriginalMessageCatalog
{
    public int FormatVersion { get; set; }
    public string SourceSha256 { get; set; } = "";
    public int MessageCount { get; set; }
    public OriginalMessage[] Messages { get; set; } = [];
    public static OriginalMessageCatalog Load() => GameData.Read<OriginalMessageCatalog>("res://data/original_messages.json");
    public OriginalMessage? Find(byte code) => Messages.FirstOrDefault(m => m.Code == code);
}

public sealed class OriginalMessage
{
    public byte Code { get; set; }
    public string Section { get; set; } = "";
    public int SourceOffset { get; set; }
    public string OriginalText { get; set; } = "";
    public string Text { get; set; } = "";
    public OriginalTextRun[] Runs { get; set; } = [];
}

public sealed class OriginalTextRun
{
    public byte PaletteOffset { get; set; }
    public string Text { get; set; } = "";
}

public sealed class OriginalHelpCatalog
{
    public const string Root = "res://assets/imported/help/";
    public int FormatVersion { get; set; }
    public string SourceSha256 { get; set; } = "";
    public int[] DisplayRect { get; set; } = [];
    public OriginalHelpPage[] Pages { get; set; } = [];
    public static OriginalHelpCatalog Load() => GameData.Read<OriginalHelpCatalog>(Root + "manifest.json");
    public static Texture2D Texture(OriginalHelpPage page) => GD.Load<Texture2D>(Root + page.File)
        ?? throw new InvalidOperationException($"Missing prepared help page: {page.File}");
}

public sealed class OriginalHelpPage
{
    public byte Page { get; set; }
    public string File { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public string RgbSha256 { get; set; } = "";
    public string TrailerHex { get; set; } = "";
}
