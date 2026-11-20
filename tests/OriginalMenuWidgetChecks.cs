using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalMenuWidgetChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/menu-widgets.json");
        using var catalog = GameData.Read<JsonDocument>("res://data/original_menu_widgets.json");
        var widgets = catalog.RootElement.GetProperty("widgets");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> output = stackalloc byte[6];
        int count = 0;
        foreach (int index in new[] { 3, 4, 5 })
        foreach (byte phase in new byte[] { 0, 1, 2, 3 })
        foreach ((byte tick, byte last) in new (byte, byte)[] {
            (0, 0), (2, 0), (3, 0), (5, 0), (0, 254), (255, 250) })
        foreach (uint offset in new uint[] { 0, 1, 29, 30, 50, 51 })
        {
            var source = widgets[index];
            uint baseFrame = source.GetProperty("sprite_base").GetUInt32();
            byte flags = (byte)source.GetProperty("flags").GetInt32();
            byte numberOfFrames = (byte)source.GetProperty("parameters")[0].GetInt32();
            MenuWidgetTick result = OriginalMenuWidgetRules.Tick(flags, baseFrame, numberOfFrames,
                                                       baseFrame + offset, last, tick, phase, index == 4);
            output[0] = result.LastTimer;
            BinaryPrimitives.WriteUInt32LittleEndian(output[1..5], result.Frame);
            output[5] = result.Phase;
            hash.AppendData(output);
            count++;
        }
        Check(count == fixture.RootElement.GetProperty("cases").GetInt32(), "Menu widget native cases");
        Check(Convert.ToHexStringLower(hash.GetHashAndReset()) == fixture.RootElement.GetProperty("output_sha256").GetString(),
              "Bush, candles and owl ticks match original callbacks");
    }
}
