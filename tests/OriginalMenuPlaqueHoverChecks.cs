using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalMenuPlaqueHoverChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/menu-button-hover.json");
        using var catalog = GameData.Read<JsonDocument>("res://data/original_menu_widgets.json");
        var records = catalog.RootElement.GetProperty("widgets");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> output = stackalloc byte[7];
        int count = 0;
        foreach (int index in new[] { 0, 1, 2, 6 })
        foreach (bool inside in new[] { false, true })
        foreach (bool mouse in new[] { false, true })
        foreach (bool key in new[] { false, true })
        foreach ((byte tick, byte last) in new (byte, byte)[] { (0, 0), (3, 0), (0, 254) })
        foreach (uint offset in new uint[] { 0, 1, 2, 3 })
        {
            var widget = records[index];
            uint baseFrame = widget.GetProperty("sprite_base").GetUInt32();
            byte[] sections = widget.GetProperty("parameters").EnumerateArray().Select(x => (byte)x.GetInt32()).ToArray();
            MenuPlaqueHover result = OriginalMenuPlaqueRules.HoverTick(baseFrame, sections,
                baseFrame + offset, last, tick, inside, mouse, key);
            output[0] = result.LastTimer;
            BinaryPrimitives.WriteUInt32LittleEndian(output[1..5], result.Frame);
            output[5] = result.PressedState;
            output[6] = result.InvokeAction ? (byte)1 : (byte)0;
            hash.AppendData(output); count++;
        }
        Check(count == fixture.RootElement.GetProperty("cases").GetInt32(), "Plaque native hover cases");
        Check(Convert.ToHexStringLower(hash.GetHashAndReset()) == fixture.RootElement.GetProperty("output_sha256").GetString(),
              "Hover, mouse press and hotkey branch match original callback");
    }
}
