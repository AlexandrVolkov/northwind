using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private void OriginalWorkSpriteChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/work-sprites.json");
        using var manifest = GameData.Read<JsonDocument>("res://assets/imported/work_large/manifest.json");
        var root = fixture.RootElement;
        Check(root.GetProperty("records").GetArrayLength() == 59, "All confirmed large WORK records have native image evidence");
        Check(root.GetProperty("work_sha256").GetString() == manifest.RootElement.GetProperty("source_sha256").GetString(), "WORK assets and native fixture use the same original bank");
        foreach (var record in root.GetProperty("records").EnumerateArray())
        {
            string filename = $"res://assets/imported/work_large/{record.GetProperty("sprite").GetInt32():D3}.png";
            using var file = Godot.FileAccess.Open(filename, Godot.FileAccess.ModeFlags.Read);
            using var image = new Image();
            Check(image.LoadPngFromBuffer(file.GetBuffer((long)file.GetLength())) == Error.Ok && image.GetSize() == new Vector2I(record.GetProperty("width").GetInt32(), record.GetProperty("height").GetInt32()),
                "Confirmed WORK PNG decodes with the original widget dimensions");
            image.Convert(Image.Format.Rgba8);
            Check(Convert.ToHexStringLower(SHA256.HashData(image.GetData())) == record.GetProperty("rgba_sha256").GetString(),
                "Every RGBA byte matches original 1DF08/1E098 blitters, including transparent runs");
        }
    }
}
