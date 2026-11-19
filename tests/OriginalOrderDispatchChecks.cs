using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalOrderDispatchChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/order-dispatch.json");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int slot = 0; slot < 6; slot++)
        for (int mode = 0; mode < 256; mode++)
        {
            byte[] widget = new byte[32]; Array.Fill(widget, (byte)0x5a); widget[2] = (byte)slot; widget[0x1a] = 0;
            var result = OriginalOrderDispatchRules.Activate(widget, (byte)mode);
            byte[] destination = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(destination, result.OriginalTarget);
            digest.AppendData(widget); digest.AppendData([result.QueuedCommand]); digest.AppendData(destination);
        }
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == fixture.RootElement.GetProperty("sha256").GetString(),
            "All 1536 native activations match mode routes, queued slots, widget state and preserved unknown bytes");
        Check(OriginalOrderDispatchRules.Target(10) == 0 && OriginalOrderDispatchRules.Target(255) == 0,
            "Undefined modes return without a destination after activation, as native 622E");
        Check(OriginalOrderDispatchRules.Target(0) != OriginalOrderDispatchRules.Target(3),
            "The same building icon can mean direct command or target selection depending on DAT mode");
        using var catalog = GameData.Read<JsonDocument>("res://data/original_order_buttons.json");
        bool valid = true;
        foreach (var row in fixture.RootElement.GetProperty("wrk_targets").EnumerateArray())
        {
            int[] a = row.EnumerateArray().Select(v => v.GetInt32()).ToArray();
            valid &= catalog.RootElement.GetProperty("templates")[a[0]].GetProperty("states")[a[1]].GetProperty("wrk_target").GetInt32() == a[2];
        }
        Check(valid, "Prepared WRK state addresses match all 162 native dispatcher destinations; handlers remain separate");
    }
}
