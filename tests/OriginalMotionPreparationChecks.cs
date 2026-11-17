using System.Buffers.Binary;
using System.Security.Cryptography;
using Northwind;

public partial class TestRunner
{
    private void OriginalMotionPreparationChecks()
    {
        var fixture = GameData.Read<MotionPreparationReference>("res://tests/fixtures/motion-preparation.json");
        Check(fixture.FormatVersion == 1, "Motion preparation reference schema");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        foreach (uint x in fixture.Coordinates)
        foreach (uint y in fixture.Coordinates)
        foreach (uint tx in fixture.Coordinates)
        foreach (uint ty in fixture.Coordinates)
        {
            digest.AppendData(new byte[] { OriginalMotionPreparation.DirectionToward(x, y, tx, ty) }); count++;
        }
        Check(count == fixture.HeadingCaseCount, "Heading reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == fixture.HeadingSha256,
            "Unsigned heading selection including doubling overflow matches original x86");

        count = 0;
        foreach (byte direction in fixture.Turn.Directions)
        foreach (byte target in fixture.Turn.Targets)
        foreach (ushort frameBase in fixture.Turn.Bases)
        foreach (byte stride in fixture.Turn.Strides)
        {
            var raw = SpatialRecord(64, 0, 0); raw[0x25] = direction;
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(1), 0xbeef);
            bool zero = OriginalMotionPreparation.TurnToward(raw, target, MotionTemplate(frameBase, stride));
            digest.AppendData(raw); digest.AppendData(new byte[] { zero ? (byte)1 : (byte)0 }); count++;
        }
        Check(count == fixture.TurnCaseCount, "Single-sector turn reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == fixture.TurnSha256,
            "Full turn record and native ZF match original x86 including truncated product and wrapped frame");

        var grid = new OriginalOccupancyMap(12, 12);
        var map = Enumerable.Repeat(0xabc00001u, 144).ToArray();
        var properties = new byte[16];
        var cells = new uint[432];
        count = 0;
        foreach (var profile in fixture.InitializationProfiles)
        foreach (byte value in profile.Values)
        foreach (byte property in profile.Properties)
        foreach (byte prior in profile.Prior)
        foreach (byte modifier in profile.Modifiers)
        foreach (byte layer in profile.Layers)
        foreach (uint occupied in profile.Occupied)
        {
            var raw = SpatialRecord(64, 0, layer); raw[0x26] = prior; raw[0x16] = modifier;
            cells[6 * 12 + 5] = occupied; grid.LoadCells(cells); properties[8] = property;
            OriginalMotionPreparation.InitializeAccumulator(grid, map, properties, raw, MotionTemplate(value: value));
            digest.AppendData(raw); count++;
        }
        Check(count == fixture.InitializeCaseCount, "DAT/SEG initialization reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == fixture.InitializeSha256,
            "Movement accumulator preparation and complete record match original x86");

        count = 0; properties[8] = 0x91;
        foreach (byte direction in fixture.Combined.Directions)
        foreach (ushort frameBase in fixture.Combined.Bases)
        foreach (byte stride in fixture.Combined.Strides)
        foreach (byte value in fixture.Combined.Values)
        foreach (byte remaining in fixture.Combined.Remaining)
        foreach (byte layer in fixture.Combined.Layers)
        foreach (var pattern in fixture.Combined.Patterns)
        foreach (uint phase in fixture.Combined.Phases)
        foreach (var footprint in fixture.Combined.Footprints)
        {
            SeedSpatialGrid(grid, pattern);
            var raw = SpatialRecord(64, 0, layer, 160 + phase, 192 + phase);
            raw[0x25] = direction; raw[0x2c] = remaining;
            var result = OriginalMotionPreparation.TurnAndAdvance(grid, map, properties, raw,
                MotionTemplate(frameBase, stride, value, footprint[0], footprint[1]), 65537, 255);
            digest.AppendData(raw);
            var state = new byte[7]; state[0] = result.Step.StepFlags;
            state[1] = result.EnteredMotion ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt32LittleEndian(state.AsSpan(2), result.Step.Distance);
            state[6] = result.Step.OccupancyTransferred ? (byte)1 : (byte)0;
            digest.AppendData(state); AppendSpatialGrid(digest, grid); count++;
        }
        Check(count == fixture.CombinedCaseCount, "Combined turn/preparation/motion coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == fixture.CombinedSha256,
            "Native continuation and pending-turn exits match C# through record, flags and all occupancy planes");
    }

    private static byte[] MotionTemplate(ushort frameBase = 0, byte stride = 0, byte value = 0, byte width = 1, byte height = 1)
    {
        var raw = new byte[171]; raw[3] = width; raw[4] = height;
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x48), frameBase); raw[0x4a] = stride;
        for (int i = 0; i < 8; i++) raw[0x58 + i] = unchecked((byte)(value + 13 * i));
        return raw;
    }

    private void ImportedMotionPreparationChecks(OriginalOccupancySnapshot snapshot, MapData map,
        ObjectRecord archer, TemplateEntry parameters)
    {
        var grid = new OriginalOccupancyMap((byte)map.Width, (byte)map.Height);
        grid.LoadCells(snapshot.Grid.Cells);
        var raw = Convert.FromHexString(archer.RawRecordHex);
        var template = Convert.FromHexString(parameters.RawRecordHex);
        var properties = Convert.FromHexString(map.SegmentPropertiesHex);
        byte target = OriginalMotionPreparation.DirectionToward(archer.RawX, archer.RawY, archer.RawX + 32, archer.RawY);
        Check(target == 2 && template[0x4a] == 11, "Imported archer selects east and uses the actual DAT direction stride");
        // Controlled north-facing turn toward an already selected eastward step.
        raw[0x25] = (byte)(target << 3); raw[0x24] = 3; raw[0x2c] = 32; raw[0x26] = 0;
        string initialGrid = grid.ComputeSha256();
        var result = OriginalMotionPreparation.TurnAndAdvance(grid, map.Cells, properties, raw, template, archer.RuntimeReference, 255);
        Check(!result.EnteredMotion && (raw[0x25] & 7) == 1 && BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(1)) == 602 &&
            BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4)) == archer.RawX && grid.ComputeSha256() == initialGrid,
            "First source-backed turn chooses northeast frame without advancing or changing occupancy");
        result = OriginalMotionPreparation.TurnAndAdvance(grid, map.Cells, properties, raw, template, archer.RuntimeReference, result.Step.StepFlags);
        Check(!result.EnteredMotion && (raw[0x25] & 7) == 2 && BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(1)) == 613,
            "Reaching the east-facing frame still returns the original pending-turn exit");
        result = OriginalMotionPreparation.TurnAndAdvance(grid, map.Cells, properties, raw, template, archer.RuntimeReference, result.Step.StepFlags);
        Check(result.EnteredMotion && result.Step.Distance == 2 && raw[0x24] == 4 && raw[0x26] == 8 && raw[0x2c] == 30 &&
            BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4)) == archer.RawX + 2 && grid.ComputeSha256() == initialGrid,
            "Aligned source archer initializes DAT/SEG accumulator and advances before the occupancy midpoint");
    }
}

public sealed class MotionPreparationReference
{
    public int FormatVersion { get; set; }
    public uint[] Coordinates { get; set; } = [];
    public TurnPreparationReference Turn { get; set; } = new();
    public AccumulatorReference[] InitializationProfiles { get; set; } = [];
    public CombinedPreparationReference Combined { get; set; } = new();
    public int HeadingCaseCount { get; set; }
    public string HeadingSha256 { get; set; } = "";
    public int TurnCaseCount { get; set; }
    public string TurnSha256 { get; set; } = "";
    public int InitializeCaseCount { get; set; }
    public string InitializeSha256 { get; set; } = "";
    public int CombinedCaseCount { get; set; }
    public string CombinedSha256 { get; set; } = "";
}
public sealed class TurnPreparationReference
{
    public List<byte> Directions { get; set; } = [];
    public List<byte> Targets { get; set; } = [];
    public ushort[] Bases { get; set; } = [];
    public List<byte> Strides { get; set; } = [];
}
public sealed class AccumulatorReference
{
    public List<byte> Values { get; set; } = [];
    public List<byte> Properties { get; set; } = [];
    public List<byte> Prior { get; set; } = [];
    public List<byte> Modifiers { get; set; } = [];
    public List<byte> Layers { get; set; } = [];
    public uint[] Occupied { get; set; } = [];
}
public sealed class CombinedPreparationReference
{
    public List<byte> Directions { get; set; } = [];
    public ushort[] Bases { get; set; } = [];
    public List<byte> Strides { get; set; } = [];
    public List<byte> Values { get; set; } = [];
    public List<byte> Remaining { get; set; } = [];
    public List<byte> Layers { get; set; } = [];
    public uint[][] Patterns { get; set; } = [];
    public uint[] Phases { get; set; } = [];
    public List<byte>[] Footprints { get; set; } = [];
}
