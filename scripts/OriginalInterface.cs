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
public sealed class InterfaceAtlas
{
    public string Atlas { get; set; } = "";
    public int[] Cell { get; set; } = [];
    public List<int> Sprites { get; set; } = [];
    public int FirstSprite { get; set; }
    public Dictionary<string, int> Known { get; set; } = [];
    /// <summary>Source rectangle of entry <paramref name="index"/> in an 8-column atlas.</summary>
    public Rect2 Region(int index) => new(index % 8 * Cell[0], index / 8 * Cell[1], Cell[0], Cell[1]);
}
public sealed class InterfaceSlot
{
    public int Sprite { get; set; }
    public int? Button { get; set; }
    public bool Present { get; set; }
    public byte Mode { get; set; }
    public byte RawGroup { get; set; }
    public string Label { get; set; } = "";
    public int? HotkeyScanCode { get; set; }
}

/// <summary>LE:1 6EC1..6F17: six DAT commands filtered by the low six OBE WorkFlags bits.</summary>
public static class OriginalCommandPanel
{
    public static bool Enabled(byte workFlags, int slot) => slot is >= 0 and < 6 && (workFlags & (1 << slot)) != 0;
}
public sealed class InterfaceTemplate
{
    public int Template { get; set; }
    public int Portrait { get; set; }
    public List<InterfaceSlot> Slots { get; set; } = [];
}
public sealed class InterfaceArmy
{
    public int Army { get; set; }
    public InterfaceAtlas Portraits { get; set; } = new();
    public InterfaceAtlas Buttons { get; set; } = new();
    public List<InterfaceTemplate> Templates { get; set; } = [];
}
/// <summary>assets/imported/interface/interface.json from tools/interface_source.py.</summary>
public sealed class InterfaceData
{
    public int FormatVersion { get; set; }
    public InterfaceAtlas HeroPortraits { get; set; } = new();
    public List<InterfaceArmy> Armies { get; set; } = [];
    public List<InterfaceScreen> Screens { get; set; } = [];
    public List<InterfaceFont> Fonts { get; set; } = [];
    public const string Root = "res://assets/imported/interface/";
    public static InterfaceData Load()
    {
        var data = GameData.Read<InterfaceData>(Root + "interface.json");
        if (data.FormatVersion != 2) throw new InvalidDataException("Update the bundled interface data: command schema 2 is required.");
        return data;
    }
    public static Texture2D Screen(string name) => GD.Load<Texture2D>(Root + name + ".png");
}

/// <summary>Bitmap font of the original (FNT). Cyrillic glyphs are contiguous (see <see cref="Code"/>); glyphs are drawn one pixel apart
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

    /// <summary>Glyph code of a character. The fonts hold А..я contiguously at 80h..BFh (unlike CP866, whose р..я are E0h..EFh;
    /// E0h..FFh of FONT_01 are dots). Ё/ё are drawn as Е/е; anything else becomes '?'.</summary>
    public static byte Code(char c) => c switch
    {
        < (char)0x80 => (byte)c,
        >= 'А' and <= 'я' => (byte)(c - 'А' + 0x80),
        'Ё' => 0x85,
        'ё' => 0xa5,
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
