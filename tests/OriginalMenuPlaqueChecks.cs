using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalMenuPlaqueChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/menu-button-press.json");
        using var catalog = GameData.Read<JsonDocument>("res://data/original_menu_widgets.json");
        var records = catalog.RootElement.GetProperty("widgets");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> output = stackalloc byte[6];
        int count = 0;
        foreach (int index in new[] { 0, 1, 2, 6 })
        foreach ((byte tick, byte last) in new (byte, byte)[] {
            (0, 0), (2, 0), (3, 0), (5, 0), (0, 254), (255, 250) })
        foreach (uint offset in new uint[] { 0, 1, 2, 3 })
        {
            var widget = records[index];
            uint baseFrame = widget.GetProperty("sprite_base").GetUInt32();
            byte[] sections = widget.GetProperty("parameters").EnumerateArray().Select(x => (byte)x.GetInt32()).ToArray();
            MenuPlaquePress result = OriginalMenuPlaqueRules.PressedTick(baseFrame, sections,
                                                                           baseFrame + offset, last, tick);
            output[0] = result.LastTimer;
            BinaryPrimitives.WriteUInt32LittleEndian(output[1..5], result.Frame);
            output[5] = result.InvokeAction ? (byte)1 : (byte)0;
            hash.AppendData(output); count++;
        }
        Check(count == fixture.RootElement.GetProperty("cases").GetInt32(), "Plaque native pressed cases");
        Check(Convert.ToHexStringLower(hash.GetHashAndReset()) == fixture.RootElement.GetProperty("output_sha256").GetString(),
              "Third plaque frame and action timing match original callback");
    }
}
