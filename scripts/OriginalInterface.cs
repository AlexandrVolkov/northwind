using Godot;
namespace Northwind;

public sealed class InterfaceScreen
{
    public int Index { get; set; }
    public string? Name { get; set; }
    public int[] Size { get; set; } = [];
    public string? Palette { get; set; }
    public int[]? MapWindow { get; set; }
}
public sealed class InterfaceFont
{
    public string File { get; set; } = "";
    public string Atlas { get; set; } = "";
    public string Palette { get; set; } = "";
    public int[] Cell { get; set; } = [];
    /// <summary>Code (CP866) → [width, top, height].</summary>
    public Dictionary<string, int[]> Glyphs { get; set; } = [];
}
/// <summary>assets/imported/interface/interface.json from tools/interface_source.py.</summary>
public sealed class InterfaceData
{
    public int FormatVersion { get; set; }
    public List<InterfaceScreen> Screens { get; set; } = [];
    public List<InterfaceFont> Fonts { get; set; } = [];
    public const string Root = "res://assets/imported/interface/";
    public static InterfaceData Load() => GameData.Read<InterfaceData>(Root + "interface.json");
    public static Texture2D Screen(string name) => GD.Load<Texture2D>(Root + name + ".png");
}

/// <summary>Bitmap font of the original (FNT). Text is encoded as CP866; glyphs are drawn one pixel apart
/// (matched against the original's resource counters and selection name, docs/interface.md).</summary>
public sealed class OriginalFont
{
    private readonly Texture2D _atlas;
    private readonly int[][] _glyphs = new int[256][];
    private readonly Vector2I _cell;
    public int Spacing { get; set; } = 1;

    public OriginalFont(InterfaceFont font, Texture2D atlas)
    {
        _atlas = atlas; _cell = new Vector2I(font.Cell[0], font.Cell[1]);
        for (int c = 0; c < 256; c++) _glyphs[c] = font.Glyphs.TryGetValue(c.ToString(), out var g) ? g : [0, 0, 0];
    }

    public static OriginalFont Load(InterfaceData data, string file, string? atlas = null)
    {
        var font = data.Fonts.Single(f => f.File == file);
        return new OriginalFont(font, GD.Load<Texture2D>(InterfaceData.Root + (atlas ?? font.Atlas)));
    }

    /// <summary>CP866 code of a character (Latin, digits, Cyrillic; anything else becomes '?').</summary>
    public static byte Code(char c) => c switch
    {
        < (char)0x80 => (byte)c,
        >= 'А' and <= 'п' => (byte)(c - 'А' + 0x80),
        >= 'р' and <= 'я' => (byte)(c - 'р' + 0xe0),
        'Ё' => 0xf0,
        'ё' => 0xf1,
        _ => (byte)'?',
    };

    public int Width(string text)
    {
        int width = 0;
        foreach (char c in text) width += _glyphs[Code(c)][0] + Spacing;
        return Math.Max(0, width - Spacing);
    }

    public void Draw(CanvasItem canvas, Vector2 position, string text)
    {
        float x = position.X;
        foreach (char c in text)
        {
            int code = Code(c); var g = _glyphs[code];
            if (g[0] > 0 && g[2] > 0)
                canvas.DrawTextureRectRegion(_atlas, new Rect2(x, position.Y + g[1], g[0], g[2]),
                    new Rect2(code % 16 * _cell.X, code / 16 * _cell.Y + g[1], g[0], g[2]));
            x += g[0] + Spacing;
        }
    }
}
