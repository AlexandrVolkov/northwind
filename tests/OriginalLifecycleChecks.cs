using System.Security.Cryptography;
using Northwind;

public partial class TestRunner
{
    private void OriginalLifecycleChecks()
    {
        var reference = GameData.Read<LifecycleReference>("res://tests/fixtures/building-lifecycle.json");
        Check(reference.FormatVersion == 1, "Lifecycle reference schema");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> encoded = stackalloc byte[3];
        int count = 0;
        for (int state = reference.Queue.StateRange[0]; state <= reference.Queue.StateRange[1]; state++)
        foreach (int slot in reference.Queue.Slots)
        {
            encoded[0] = OriginalBuildingLifecycle.QueueDirectCommand((byte)state, slot);
            digest.AppendData(encoded[..1]); count++;
        }
        Check(count == reference.Queue.CaseCount, "Command reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == reference.Queue.Sha256, "Direct commands match original x86");

        count = 0;
        for (int state = reference.Complete.StateRange[0]; state <= reference.Complete.StateRange[1]; state++)
        foreach (byte work in reference.Complete.WorkFlags)
        foreach (byte auxiliary in reference.Complete.Auxiliary)
        {
            var result = OriginalBuildingLifecycle.CompleteCurrentAction(new((byte)state, auxiliary, work));
            encoded[0] = result.EncodedState; encoded[1] = result.Auxiliary; encoded[2] = result.WorkFlags;
            digest.AppendData(encoded); count++;
        }
        Check(count == reference.Complete.CaseCount, "Transition reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == reference.Complete.Sha256, "Action transitions match original x86");

        var creation = reference.Create;
        count = 0;
        foreach (byte army in creation.ArmySlots)
        foreach (byte owner in creation.Owners)
        foreach (byte layer in creation.Layers)
        foreach (var point in creation.Coordinates)
        foreach (ushort frame in creation.Frames)
        foreach (byte condition in creation.Conditions)
        foreach (bool construction in creation.Construction)
        {
            var record = OriginalBuildingLifecycle.CreateFreshCastleRecord(army, owner, 2, point[0], point[1], layer, frame, condition);
            if (construction) OriginalBuildingLifecycle.MarkConstruction(record);
            digest.AppendData(record); count++;
        }
        Check(count == creation.CaseCount, "Fresh castle reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == creation.Sha256, "Fresh records match original allocator and WRK callback");
    }
}

public sealed class LifecycleReference
{
    public int FormatVersion { get; set; }
    public QueueReference Queue { get; set; } = new();
    public CompleteReference Complete { get; set; } = new();
    public CreationReference Create { get; set; } = new();
}
public sealed class QueueReference
{
    public int[] StateRange { get; set; } = [];
    public int[] Slots { get; set; } = [];
    public int CaseCount { get; set; }
    public string Sha256 { get; set; } = "";
}
public sealed class CompleteReference
{
    public int[] StateRange { get; set; } = [];
    public List<byte> WorkFlags { get; set; } = [];
    public List<byte> Auxiliary { get; set; } = [];
    public int CaseCount { get; set; }
    public string Sha256 { get; set; } = "";
}
public sealed class CreationReference
{
    public List<byte> ArmySlots { get; set; } = [];
    public List<byte> Owners { get; set; } = [];
    public List<byte> Layers { get; set; } = [];
    public uint[][] Coordinates { get; set; } = [];
    public ushort[] Frames { get; set; } = [];
    public List<byte> Conditions { get; set; } = [];
    public bool[] Construction { get; set; } = [];
    public int CaseCount { get; set; }
    public string Sha256 { get; set; } = "";
}
