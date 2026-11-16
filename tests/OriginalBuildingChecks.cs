using System.Buffers.Binary;
using System.Security.Cryptography;
using Northwind;

public partial class TestRunner
{
    private void OriginalBuildingChecks()
    {
        var reference = GameData.Read<BuildingKernelReference>("res://tests/fixtures/building-kernel.json");
        Check(reference.FormatVersion == 1, "Building reference schema");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var maintenance = reference.Maintenance;
        Span<byte> encoded = stackalloc byte[19];
        int count = 0;
        foreach (var p in maintenance.Parameters)
        foreach (byte condition in maintenance.Conditions)
        foreach (byte counter in maintenance.Counters)
        foreach (byte workFlags in maintenance.WorkFlags)
        foreach (ushort objectFlags in maintenance.ObjectFlags)
        foreach (bool stateBit40 in maintenance.StateBit40)
        foreach (var funds in maintenance.Balances)
        {
            var state = new BuildingCondition(condition, counter, workFlags, objectFlags);
            var parameters = new MaintenanceParameters(p.Limit, p.Interval, new(p.Costs[0], p.Costs[1], p.Costs[2]));
            var result = OriginalBuildingRules.StepMaintenance(state, parameters, new(funds[0], funds[1], funds[2]), stateBit40);
            EncodeCondition(encoded, result.State);
            BinaryPrimitives.WriteUInt32LittleEndian(encoded[5..], result.Balances.First);
            BinaryPrimitives.WriteUInt32LittleEndian(encoded[9..], result.Balances.Second);
            BinaryPrimitives.WriteUInt32LittleEndian(encoded[13..], result.Balances.Third);
            encoded[17] = result.ContinueAction ? (byte)1 : (byte)0;
            encoded[18] = result.Signal;
            digest.AppendData(encoded);
            count++;
        }
        Check(count == maintenance.CaseCount, "Maintenance reference case coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == maintenance.Sha256,
            $"C# maintenance matches original x86 across {count} cases");

        var damage = reference.Damage;
        count = 0;
        for (int condition = damage.ConditionRange[0]; condition <= damage.ConditionRange[1]; condition++)
        foreach (byte strength in damage.Strengths)
        foreach (byte protection in damage.Protections)
        foreach (ushort objectFlags in damage.ObjectFlags)
        foreach (byte reaction in damage.Reactions)
        {
            var result = OriginalBuildingRules.ApplyDamageTail(new((byte)condition, 0xa5, 0x5a, objectFlags), strength, protection, reaction);
            EncodeCondition(encoded, result);
            digest.AppendData(encoded[..5]);
            count++;
        }
        Check(count == damage.CaseCount, "Damage reference case coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == damage.Sha256,
            $"C# damage tail matches original x86 across {count} cases");
    }

    private static void EncodeCondition(Span<byte> output, BuildingCondition state)
    {
        output[0] = state.Condition; output[1] = state.Counter; output[2] = state.WorkFlags;
        BinaryPrimitives.WriteUInt16LittleEndian(output[3..], state.ObjectFlags);
    }
}

public sealed class BuildingKernelReference
{
    public int FormatVersion { get; set; }
    public MaintenanceReference Maintenance { get; set; } = new();
    public DamageReference Damage { get; set; } = new();
}
public sealed class MaintenanceReference
{
    public MaintenanceReferenceParameters[] Parameters { get; set; } = [];
    public List<byte> Conditions { get; set; } = [];
    public List<byte> Counters { get; set; } = [];
    public List<byte> WorkFlags { get; set; } = [];
    public ushort[] ObjectFlags { get; set; } = [];
    public bool[] StateBit40 { get; set; } = [];
    public uint[][] Balances { get; set; } = [];
    public int CaseCount { get; set; }
    public string Sha256 { get; set; } = "";
}
public sealed class MaintenanceReferenceParameters
{
    public byte Limit { get; set; }
    public byte Interval { get; set; }
    public uint[] Costs { get; set; } = [];
}
public sealed class DamageReference
{
    public int[] ConditionRange { get; set; } = [];
    public List<byte> Strengths { get; set; } = [];
    public List<byte> Protections { get; set; } = [];
    public ushort[] ObjectFlags { get; set; } = [];
    public List<byte> Reactions { get; set; } = [];
    public int CaseCount { get; set; }
    public string Sha256 { get; set; } = "";
}
