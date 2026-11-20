using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalMenuAnimationChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/menu-animation.json");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        Span<byte> output = stackalloc byte[6];
        foreach (bool forward in new[] { true, false })
        foreach (byte tick in new byte[] { 0, 1, 2, 3, 4, 5, 127, 254, 255 })
        foreach (byte last in new byte[] { 0, 1, 2, 3, 4, 127, 253, 254, 255 })
        foreach ((uint frame, uint limit, uint reset) in new (uint, uint, uint)[] {
            (60, 90, 60), (89, 90, 60), (90, 90, 60),
            (122, 173, 122), (172, 173, 122), (173, 173, 122),
            (0, 0, 0), (255, 255, 204), (uint.MaxValue, 1, 0) })
        foreach (byte delay in new byte[] { 0, 3, 5 })
        {
            MenuFrameStep result = OriginalMenuAnimationRules.Step(forward, frame, last, tick, limit, reset, delay);
            output[0] = result.LastTimer;
            BinaryPrimitives.WriteUInt32LittleEndian(output[1..5], result.Frame);
            output[5] = result.Wrapped ? (byte)1 : (byte)0;
            hash.AppendData(output);
            count++;
        }
        Check(count == fixture.RootElement.GetProperty("case_count").GetInt32(), "Menu animation native case count");
        Check(Convert.ToHexStringLower(hash.GetHashAndReset()) == fixture.RootElement.GetProperty("output_sha256").GetString(),
            "Menu frame progression matches LE:1 18ED4 and 18EF5");
    }
}
