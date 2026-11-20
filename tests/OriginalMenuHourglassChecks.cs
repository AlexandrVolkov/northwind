using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalMenuHourglassChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/menu-hourglass.json");
        using var catalog = GameData.Read<JsonDocument>("res://data/original_menu_widgets.json");
        var source = catalog.RootElement.GetProperty("widgets")[8];
        uint baseFrame = source.GetProperty("sprite_base").GetUInt32();
        byte flags = (byte)source.GetProperty("flags").GetInt32();
        byte numberOfFrames = (byte)source.GetProperty("parameters")[0].GetInt32();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> output = stackalloc byte[12];
        int count = 0;
        foreach (byte phase in new byte[] { 0, 1, 2, 3 })
        foreach ((byte tick, byte last) in new (byte, byte)[] {
            (0, 0), (4, 0), (5, 0), (10, 0), (0, 254), (255, 250) })
        foreach (uint offset in new uint[] { 0, 1, 48, 49, 50, 51 })
        {
            MenuHourglassTick result = OriginalMenuHourglassRules.Tick(flags, phase, baseFrame,
                numberOfFrames, baseFrame + offset, last, tick, 0x1234, 0x12345678);
            output[0] = result.LastTimer;
            BinaryPrimitives.WriteUInt32LittleEndian(output[1..5], result.Frame);
            output[5] = result.LaunchFlag;
            BinaryPrimitives.WriteUInt16LittleEndian(output[6..8], result.ScanCode);
            BinaryPrimitives.WriteUInt32LittleEndian(output[8..12], result.MouseButtons);
            hash.AppendData(output); count++;
        }
        Check(count == fixture.RootElement.GetProperty("cases").GetInt32(), "Hourglass native cases");
        Check(Convert.ToHexStringLower(hash.GetHashAndReset()) == fixture.RootElement.GetProperty("output_sha256").GetString(),
              "Hourglass timing and launch flag match original callback");
    }
}
