using System.Buffers.Binary;
using System.Security.Cryptography;
using Northwind;

public partial class TestRunner
{
    private void OriginalLumbermillChecks()
    {
        var fixture = GameData.Read<LumbermillReference>("res://tests/fixtures/lumbermill.json");
        Check(fixture.FormatVersion == 1, "Lumbermill reference schema");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        foreach (byte queue in fixture.Process.Queues)
        foreach (byte timer in fixture.Process.Countdowns)
        foreach (byte reset in fixture.Process.Resets)
        foreach (uint stock in fixture.Process.Stocks)
        foreach (uint stats in fixture.Process.Statistics)
        foreach (byte owner in fixture.Process.Owners)
        {
            var raw = MillRecord(queue, timer, owner, 0);
            var result = OriginalLumbermillRules.Process(new(timer, queue, stock, stats), reset);
            raw[0x23] = result.Countdown; raw[0x24] = result.QueuedLoads;
            digest.AppendData(raw);
            var encoded = new byte[8]; BinaryPrimitives.WriteUInt32LittleEndian(encoded, result.Stock);
            BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(4), result.Statistics); digest.AppendData(encoded); count++;
        }
        Check(count == fixture.Process.CaseCount, "Lumbermill processing reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == fixture.Process.Sha256, "Queued processing, native owner stock and statistics match x86");
        count = 0;
        foreach (ushort millFlags in fixture.Receive.MillFlags)
        foreach (ushort workerFlags in fixture.Receive.WorkerFlags)
        foreach (byte cargo in fixture.Receive.Cargo)
        foreach (uint flags in fixture.Receive.TemplateFlags)
        foreach (byte queue in fixture.Receive.Queues)
        {
            var mill = MillRecord(queue, 100, 0, millFlags);
            var worker = Enumerable.Range(0, 54).Select(i => unchecked((byte)(i * 7 + 9))).ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(worker.AsSpan(0x17), workerFlags); worker[0x31] = cargo;
            var result = OriginalLumbermillRules.Receive(millFlags, queue, workerFlags, cargo, flags);
            BinaryPrimitives.WriteUInt16LittleEndian(mill.AsSpan(0x17), result.MillFlags); mill[0x24] = result.QueuedLoads;
            BinaryPrimitives.WriteUInt16LittleEndian(worker.AsSpan(0x17), result.WorkerFlags); worker[0x31] = result.Cargo;
            digest.AppendData(mill); digest.AppendData(worker); digest.AppendData(new byte[] { result.Accepted ? (byte)1 : (byte)0 }); count++;
        }
        Check(count == fixture.Receive.CaseCount, "Timber handoff reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == fixture.Receive.Sha256, "Handoff branches and full records match original WRK");
    }
    private static byte[] MillRecord(byte queue, byte timer, byte owner, ushort flags)
    {
        var raw = Enumerable.Range(0, 37).Select(i => unchecked((byte)(i * 11 + 7))).ToArray();
        raw[0] = 2; raw[3] = owner; raw[0x0e] = 4; raw[0x23] = timer; raw[0x24] = queue;
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x17), flags); return raw;
    }
}
public sealed class LumbermillReference
{
    public int FormatVersion { get; set; }
    public LumbermillProcessReference Process { get; set; } = new();
    public LumbermillReceiveReference Receive { get; set; } = new();
}
public sealed class LumbermillProcessReference
{
    public List<byte> Queues { get; set; } = [];
    public List<byte> Countdowns { get; set; } = [];
    public List<byte> Resets { get; set; } = [];
    public uint[] Stocks { get; set; } = [];
    public uint[] Statistics { get; set; } = [];
    public List<byte> Owners { get; set; } = [];
    public int CaseCount { get; set; }
    public string Sha256 { get; set; } = "";
}
public sealed class LumbermillReceiveReference
{
    public ushort[] MillFlags { get; set; } = [];
    public ushort[] WorkerFlags { get; set; } = [];
    public List<byte> Cargo { get; set; } = [];
    public uint[] TemplateFlags { get; set; } = [];
    public List<byte> Queues { get; set; } = [];
    public int CaseCount { get; set; }
    public string Sha256 { get; set; } = "";
}
