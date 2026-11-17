using System.Buffers.Binary;
using System.Security.Cryptography;
using Northwind;

public partial class TestRunner
{
    private void OriginalConstructionChecks()
    {
        var reference = GameData.Read<ConstructionReference>("res://tests/fixtures/construction.json");
        Check(reference.FormatVersion == 1, "Construction reference schema");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> output = stackalloc byte[17];
        int count = 0;
        foreach (var cost in reference.Payment.Costs)
        foreach (uint first in reference.Payment.BalanceValues)
        foreach (uint second in reference.Payment.BalanceValues)
        foreach (uint third in reference.Payment.BalanceValues)
        foreach (uint fourth in reference.Payment.BalanceValues)
        {
            var result = OriginalConstructionRules.TryDebitUpfront(new(first, second, third, fourth), new(cost[0], cost[1], cost[2], cost[3]));
            output[0] = result.Accepted ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt32LittleEndian(output[1..], result.Balances.First);
            BinaryPrimitives.WriteUInt32LittleEndian(output[5..], result.Balances.Second);
            BinaryPrimitives.WriteUInt32LittleEndian(output[9..], result.Balances.Third);
            BinaryPrimitives.WriteUInt32LittleEndian(output[13..], result.Balances.Fourth);
            digest.AppendData(output); count++;
        }
        Check(count == reference.Payment.CaseCount, "Upfront payment reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == reference.Payment.Sha256, "Upfront payment matches original x86");

        var p = reference.Placement;
        byte[] properties = Convert.FromHexString(p.PropertiesHex);
        var occupancy = new uint[p.Width * p.Height * 3];
        count = 0;
        foreach (var footprint in p.Footprints)
        foreach (var mask in p.Masks)
        foreach (var occupied in p.OccupancyVariants)
        foreach (uint x in p.Positions)
        foreach (uint y in p.Positions)
        {
            Array.Clear(occupancy);
            foreach (var entry in occupied)
                occupancy[entry[0] * p.Width * p.Height + entry[1]] = entry[2];
            var result = OriginalConstructionRules.ProbePlacement(p.Width, p.Height, p.Cells, properties, occupancy,
                footprint[0], footprint[1], System.Runtime.InteropServices.CollectionsMarshal.AsSpan(mask), x, y);
            output[0] = (byte)result.Status;
            BinaryPrimitives.WriteUInt32LittleEndian(output[1..], result.BlockingObjectOffset);
            digest.AppendData(output[..5]); count++;
        }
        Check(count == p.CaseCount, "Placement reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == p.Sha256, "Placement matches original x86");
    }
}

public sealed class ConstructionReference
{
    public int FormatVersion { get; set; }
    public PaymentReference Payment { get; set; } = new();
    public PlacementReference Placement { get; set; } = new();
}
public sealed class PaymentReference
{
    public uint[][] Costs { get; set; } = [];
    public uint[] BalanceValues { get; set; } = [];
    public int CaseCount { get; set; }
    public string Sha256 { get; set; } = "";
}
public sealed class PlacementReference
{
    public byte Width { get; set; }
    public byte Height { get; set; }
    public uint[] Cells { get; set; } = [];
    public string PropertiesHex { get; set; } = "";
    // List<byte> elements deserialize JSON numeric arrays, unlike byte[].
    public List<byte>[] Footprints { get; set; } = [];
    public List<byte>[] Masks { get; set; } = [];
    public uint[][][] OccupancyVariants { get; set; } = [];
    public uint[] Positions { get; set; } = [];
    public int CaseCount { get; set; }
    public string Sha256 { get; set; } = "";
}
