using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private void OriginalCreditsChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/credits.json");
        using var manifest = GameData.Read<JsonDocument>("res://assets/imported/credits/manifest.json");
        foreach (var source in fixture.RootElement.GetProperty("source_sha256").EnumerateObject())
            Check(source.Value.GetString() == manifest.RootElement.GetProperty("source_sha256").GetProperty(source.Name).GetString(), "Credits assets share the native renderer source profile");
        foreach (var sprite in fixture.RootElement.GetProperty("sprites").EnumerateArray())
        {
            using var file = Godot.FileAccess.Open($"res://assets/imported/credits/{sprite.GetProperty("sprite").GetInt32():D3}.png", Godot.FileAccess.ModeFlags.Read);
            using var image = new Image();
            Check(image.LoadPngFromBuffer(file.GetBuffer((long)file.GetLength())) == Error.Ok && image.GetSize() == new Vector2I(sprite.GetProperty("width").GetInt32(), sprite.GetProperty("height").GetInt32()),
                "Credits sprite/page uses the original caller's dimensions");
            image.Convert(Image.Format.Rgba8);
            Check(Convert.ToHexStringLower(SHA256.HashData(image.GetData())) == sprite.GetProperty("rgba_sha256").GetString(), "Every credits RGBA pixel matches original 19465, including 320-pixel-wide pages");
        }
    }
}
