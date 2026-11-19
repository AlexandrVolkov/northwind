using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private void OriginalTextsChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/texts.json");
        var root = fixture.RootElement;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var nav = root.GetProperty("navigation"); int count = 0;
        for (int page = 0; page < nav.GetProperty("pages").GetInt32(); page++)
        foreach (var key in nav.GetProperty("keys").EnumerateArray())
        for (int buttons = 0; buttons < nav.GetProperty("button_values").GetInt32(); buttons++)
        {
            var next = OriginalHelpRules.Step((byte)page, key.GetUInt16(), (byte)buttons);
            hash.AppendData([next.Page, next.AcceptedInputs, next.Redraw ? (byte)1 : (byte)0]); count++;
        }
        Check(count == nav.GetProperty("case_count").GetInt32() && Convert.ToHexStringLower(hash.GetHashAndReset()) == nav.GetProperty("sha256").GetString(),
            "All help page/key/button combinations match original 68FC, including simultaneous previous/next");
        var messages = OriginalMessageCatalog.Load(); int found = 0;
        Check(messages.SourceSha256 == root.GetProperty("messages_sha256").GetString(), "Message catalog matches native source version");
        for (int code = 0; code < 256; code++)
        {
            var message = messages.Find((byte)code); hash.AppendData([message is null ? (byte)0 : (byte)1]);
            if (message != null)
            {
                byte[] text = Encoding.UTF8.GetBytes(message.OriginalText), size = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)text.Length); hash.AppendData(size); hash.AppendData(text); found++;
            }
        }
        var lookup = root.GetProperty("lookup");
        Check(found == lookup.GetProperty("found_count").GetInt32() && Convert.ToHexStringLower(hash.GetHashAndReset()) == lookup.GetProperty("sha256").GetString(),
            "All 256 message lookups match native 155FF/15194, including inline control bytes and missing codes");
        var help = OriginalHelpCatalog.Load();
        Check(help.SourceSha256 == root.GetProperty("help_sha256").GetString() && help.Pages.Length == 32, "Help catalog matches original PIC version");
        foreach (var page in root.GetProperty("pages").EnumerateArray())
        {
            var record = help.Pages[page.GetProperty("page").GetInt32()];
            using var file = Godot.FileAccess.Open(OriginalHelpCatalog.Root + record.File, Godot.FileAccess.ModeFlags.Read);
            using var image = new Image();
            Check(image.LoadPngFromBuffer(file.GetBuffer((long)file.GetLength())) == Error.Ok, "Original PNG reference decodes without editor remapping");
            Check(image.GetSize() == new Vector2I(448, 448), "Prepared help dimensions match original page");
            image.Convert(Image.Format.Rgb8);
            Check(Convert.ToHexStringLower(SHA256.HashData(image.GetData())) == page.GetProperty("rgb_sha256").GetString(),
                "Every help pixel matches native 14FBA row output with original DAC palette");
            Check(page.GetProperty("pending_row_bytes").GetInt32() == 1 && record.TrailerHex == "0c", "Trailing marker remains outside the completed rows");
        }
    }

    private async Task HelpChecks()
    {
        OriginalTextsChecks();
        var preview = GD.Load<PackedScene>("res://tests/HelpPreview.tscn").Instantiate<HelpPreview>(); AddChild(preview);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(preview.CurrentPage == 0, "Help preview starts at page zero");
        var catalog = OriginalHelpCatalog.Load();
        foreach (var page in catalog.Pages)
        {
            using var texture = OriginalHelpCatalog.Texture(page);
            using var image = texture.GetImage(); image.Convert(Image.Format.Rgb8);
            Check(Convert.ToHexStringLower(SHA256.HashData(image.GetData())) == page.RgbSha256, "Godot texture import preserves help pixels");
        }
        for (int i = 0; i < 40; i++) preview.Navigate(0xe04d);
        Check(preview.CurrentPage == 31, "Next clamps at page 31");
        preview._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Up, Pressed = true });
        Check(preview.CurrentPage == 30, "Up selects previous page");
        for (int i = 0; i < 40; i++) preview.Navigate(0xe04b);
        Check(preview.CurrentPage == 0, "Previous clamps at page zero");
        preview._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Down, Pressed = true });
        Check(preview.CurrentPage == 1, "Down selects next page");
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            using var image = GetViewport().GetTexture().GetImage(); image.SavePng("res://build/help-preview.png");
        }
        RemoveChild(preview); preview.Free();
    }
}
