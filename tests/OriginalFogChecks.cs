using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private static (byte[] Map, byte[] Framebuffer) FogInputs(int width, int height, int salt)
    {
        byte[] map = Enumerable.Range(0, width * height).Select(i => salt == 0 ? (byte)255 : salt == 1
            ? (byte)(width / 3 <= i % width && i % width < 2 * width / 3 && height / 3 <= i / width && i / width < 2 * height / 3 ? 0 : 255)
            : unchecked((byte)(i * 37 + 19))).ToArray();
        byte[] framebuffer = Enumerable.Range(0, 640 * 480).Select(i => unchecked((byte)(i * 37 + salt * 19))).ToArray();
        return (map, framebuffer);
    }

    private async Task FogChecks()
    {
        OriginalFogChecks();
        var graphics = OriginalFogGraphics.Load();
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/fog-raster.json");
        var preview = fixture.RootElement.GetProperty("preview");
        int[] a = preview.GetProperty("case").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var (map, pixels) = FogInputs(a[0], a[1], a[5]);
        graphics.ShadeViewport(pixels, 640, map, a[0], a[1], a[2], a[3], (byte)a[4]);
        using var image = Image.CreateFromData(640, 480, false, Image.Format.Rgb8, graphics.ToRgb(pixels));
        using var texture = ImageTexture.CreateFromImage(image);
        var sprite = new Sprite2D { Texture = texture, Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        AddChild(sprite);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var screenshot = GetViewport().GetTexture().GetImage();
            using var crop = screenshot.GetRegion(new Rect2I(0, 0, 640, 480)); crop.Convert(Image.Format.Rgb8);
            Check(Convert.ToHexStringLower(SHA256.HashData(crop.GetData())) == preview.GetProperty("rgb_sha256").GetString(),
                "Godot renders all 640x480 RGB pixels exactly as native 9F38");
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            crop.SavePng("res://build/fog-preview.png");
        }
        RemoveChild(sprite); sprite.Free();
    }

    private void OriginalFogChecks()
    {
        var graphics = OriginalFogGraphics.Load();
        using var manifest = GameData.Read<JsonDocument>("res://assets/imported/visibility/manifest.json");
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/fog-raster.json");
        var root = fixture.RootElement;
        foreach (var source in root.GetProperty("source_sha256").EnumerateObject())
            Check(source.Value.GetString() == manifest.RootElement.GetProperty("source_sha256").GetProperty(source.Name).GetString(), "Prepared fog assets match the native oracle source profile");
        Check(Convert.ToHexStringLower(SHA256.HashData(graphics.ShadeIndices)) == manifest.RootElement.GetProperty("shade_sha256").GetString(), "PNG decoding preserves every FADE_COL index");
        Check(Convert.ToHexStringLower(SHA256.HashData(graphics.Palette)) == manifest.RootElement.GetProperty("palette_sha256").GetString(), "Prepared palette preserves DAC colours");
        foreach (var weight in manifest.RootElement.GetProperty("weights").EnumerateArray())
        {
            int[] rect = weight.GetProperty("rectangle").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            byte[] pixels = new byte[1024];
            for (int y = 0; y < 32; y++) graphics.Weights.AsSpan((rect[1] + y) * 256 + rect[0], 32).CopyTo(pixels.AsSpan(y * 32));
            Check(Convert.ToHexStringLower(SHA256.HashData(pixels)) == weight.GetProperty("sha256").GetString(), "Fog atlas preserves original 32x32 weights");
        }
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var rgbDigest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            int[] a = row.EnumerateArray().Select(v => v.GetInt32()).ToArray();
            int width = a[0], height = a[1], salt = a[5];
            var (map, framebuffer) = FogInputs(width, height, salt);
            graphics.ShadeViewport(framebuffer, 640, map, width, height, a[2], a[3], (byte)a[4]);
            digest.AppendData(framebuffer); rgbDigest.AppendData(graphics.ToRgb(framebuffer)); count++;
        }
        Check(count == root.GetProperty("case_count").GetInt32(), "All native viewport cases replayed at every map edge and both parities");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == root.GetProperty("indices_sha256").GetString(), "Every indexed framebuffer pixel matches native 9F38 and 1E133");
        Check(Convert.ToHexStringLower(rgbDigest.GetHashAndReset()) == root.GetProperty("rgb_sha256").GetString(), "Every RGB framebuffer pixel matches native fog with the original VGA palette");
    }
}
