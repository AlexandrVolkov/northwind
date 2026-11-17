using System.Buffers.Binary;
using System.Security.Cryptography;
using Northwind;

public partial class TestRunner
{
    private void OriginalSpatialChecks()
    {
        var fixture = GameData.Read<SpatialReference>("res://tests/fixtures/spatial.json");
        Check(fixture.FormatVersion == 1, "Spatial reference schema");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var grid = new OriginalOccupancyMap(fixture.Width, fixture.Height);
        int count = 0;
        foreach (var profile in fixture.MotionProfiles)
        foreach (byte speed in profile.Speeds)
        foreach (byte remaining in profile.Remaining)
        foreach (byte direction in profile.Directions)
        foreach (var footprint in profile.Footprints)
        foreach (byte layer in profile.Layers)
        foreach (var pattern in profile.Patterns)
        foreach (uint phase in profile.Phases)
        {
            SeedSpatialGrid(grid, pattern);
            var raw = SpatialRecord(64, 0, layer, 160 + phase, 192 + phase);
            raw[0x25] = direction; raw[0x26] = speed; raw[0x2c] = remaining;
            var result = OriginalSpatialRules.AdvanceMotion(grid, raw, fixture.Reference,
                footprint[0], footprint[1], 255);
            digest.AppendData(raw);
            var state = new byte[6]; state[0] = result.StepFlags;
            BinaryPrimitives.WriteUInt32LittleEndian(state.AsSpan(1), result.Distance);
            state[5] = result.OccupancyTransferred ? (byte)1 : (byte)0;
            digest.AppendData(state); AppendSpatialGrid(digest, grid); count++;
        }
        Check(count == fixture.MotionCaseCount, "Motion reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == fixture.MotionSha256,
            "Motion records, flags and all occupancy planes match original x86");

        count = 0;
        var spec = fixture.Detach;
        foreach (int length in spec.Lengths)
        for (int removed = 0; removed < length; removed++)
        for (int cursorIndex = 0; cursorIndex <= removed; cursorIndex++)
        foreach (uint start in spec.Bases)
        foreach (byte owner in spec.Owners)
        foreach (byte layer in spec.Layers)
        foreach (var footprint in spec.Footprints)
        foreach (uint flags in spec.TemplateFlags)
        foreach (ushort recordFlags in spec.RecordFlags)
        foreach (var counters in spec.Counters)
        {
            int bytes = spec.Sizes.Take(length).Sum();
            var pool = new byte[start + bytes]; int offset = (int)start;
            foreach (int size in spec.Sizes.Take(length))
            {
                var raw = SpatialRecord(size, owner, layer);
                BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x17), recordFlags);
                raw.CopyTo(pool, offset); offset += size;
            }
            uint reference = start + (uint)spec.Sizes.Take(removed).Sum();
            uint cursor = start + (uint)spec.Sizes.Take(cursorIndex).Sum();
            SeedSpatialGrid(grid, spec.Pattern);
            var result = OriginalSpatialRules.DetachRecord(grid, pool, reference,
                new(start, (uint)pool.Length, cursor), footprint[0], footprint[1], flags, new(counters[0], counters[1]));
            digest.AppendData(pool.AsSpan((int)start));
            AppendDetachment(digest, result); AppendSpatialGrid(digest, grid); count++;
        }
        Check(count == fixture.DetachCaseCount, "Detachment reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == fixture.DetachSha256,
            "Record links, owner cursor/end, raw counters and occupancy match original x86");

        count = 0;
        foreach (var order in fixture.Sequences)
        foreach (uint start in spec.Bases)
        foreach (uint flags in new uint[] { 0, 8, 40 })
        {
            var pool = new byte[start + spec.Sizes.Sum()]; int offset = (int)start;
            foreach (int size in spec.Sizes)
            {
                var raw = SpatialRecord(size, 0, 1);
                BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x17), 0);
                raw.CopyTo(pool, offset); offset += size;
            }
            var chain = new OriginalObjectChain(start, (uint)pool.Length, start);
            var counters = new OriginalRemovalCounters();
            SeedSpatialGrid(grid, spec.Pattern);
            foreach (int index in order)
            {
                uint reference = start + (uint)spec.Sizes.Take(index).Sum();
                var result = OriginalSpatialRules.DetachRecord(grid, pool, reference, chain, 4, 3, flags, counters);
                digest.AppendData(pool.AsSpan((int)start)); AppendDetachment(digest, result); AppendSpatialGrid(digest, grid);
                chain = result.Chain; counters = result.Counters; count++;
            }
        }
        Check(count == fixture.SequenceCaseCount, "Sequential detachment coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == fixture.SequenceSha256,
            "Repeated removals follow modified links and match original x86 through empty-chain reset");

        var edge = SpatialRecord(64, 0, 0, 0, 0);
        edge[0x25] = 0x12; edge[0x26] = 63; edge[0x2c] = 16;
        var before = edge.ToArray(); string beforeGrid = grid.ComputeSha256();
        bool rejected = false;
        try { OriginalSpatialRules.AdvanceMotion(grid, edge, 1, 4, 3, 255); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected && edge.SequenceEqual(before) && grid.ComputeSha256() == beforeGrid,
            "Unsupported motion rectangles fail before mutating record or grid");
        var invalidPool = new byte[100];
        beforeGrid = grid.ComputeSha256(); rejected = false;
        try { OriginalSpatialRules.DetachRecord(grid, invalidPool, 50, new(1, 100, 1), 1, 1, 0, default); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected && invalidPool.All(b => b == 0) && grid.ComputeSha256() == beforeGrid,
            "Broken forward chains fail before mutating occupancy or links");
    }

    private static byte[] SpatialRecord(int size, byte owner, byte layer, uint x = 176, uint y = 208)
    {
        var raw = Enumerable.Range(0, size).Select(i => unchecked((byte)(i * 17 + 3))).ToArray();
        raw[0] = 0; raw[3] = owner; raw[13] = layer; raw[14] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), x);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), y);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(16), (uint)size);
        return raw;
    }

    private static void SeedSpatialGrid(OriginalOccupancyMap grid, uint[] pattern)
    {
        var initial = new uint[grid.Cells.Length];
        for (int i = 0; i < initial.Length; i++) initial[i] = pattern[i % pattern.Length];
        grid.LoadCells(initial);
    }

    private static void AppendSpatialGrid(IncrementalHash digest, OriginalOccupancyMap grid)
    {
        var encoded = new byte[grid.Cells.Length * 4];
        for (int i = 0; i < grid.Cells.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(i * 4), grid.Cells[i]);
        digest.AppendData(encoded);
    }

    private static void AppendDetachment(IncrementalHash digest, OriginalDetachment result)
    {
        Span<byte> encoded = stackalloc byte[20];
        uint[] values = [result.Chain.BaseReference, result.Chain.EndReference, result.Chain.CursorReference,
            result.Counters.Counter19F5, result.Counters.Counter19F9];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(encoded[(i * 4)..], values[i]);
        digest.AppendData(encoded);
    }

    private void ImportedSpatialChecks(OriginalOccupancySnapshot snapshot, MapData map, TemplateEntry castle)
    {
        var scenario = GameData.Read<ScenarioData>("res://assets/imported/scenario.json");
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json");
        var slot = scenario.Slots[0];
        var pool = new byte[slot.ObjectBytes + 1];
        foreach (var obj in slot.Objects) Convert.FromHexString(obj.RawRecordHex).CopyTo(pool, (int)obj.RuntimeReference);
        var archer = slot.Objects[1];
        var parameters = templates.Armies[archer.TemplateFile].Templates[archer.TemplateIndex];
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(Convert.FromHexString(parameters.RawRecordHex).AsSpan(0x40));
        var live = new OriginalOccupancyMap((byte)map.Width, (byte)map.Height);
        live.LoadCells(snapshot.Grid.Cells);
        var raw = pool.AsSpan((int)archer.RuntimeReference, archer.RecordBytes);
        // Controlled active-motion phase for the actual imported archer, 15 units into an eastward step.
        BinaryPrimitives.WriteUInt32LittleEndian(raw[4..], archer.RawX + 15);
        raw[0x25] = 0x12; raw[0x26] = 63; raw[0x2c] = 17;
        var move = OriginalSpatialRules.AdvanceMotion(live, raw, archer.RuntimeReference,
            parameters.Construction.FootprintWidth, parameters.Construction.FootprintHeight, 255);
        int oldCell = (archer.RawLayer & 3) * map.Width * map.Height + (int)(archer.RawY >> 5) * map.Width + (int)(archer.RawX >> 5);
        Check(move is { Distance: 15, OccupancyTransferred: true } && live.Cells[oldCell] == 0 &&
            live.Cells[oldCell + 1] == archer.RuntimeReference && BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]) == archer.RawX + 30,
            "Controlled motion of an imported archer transfers its original reservation at the midpoint");
        var before = raw.ToArray();
        var removed = OriginalSpatialRules.DetachRecord(live, pool, archer.RuntimeReference,
            new(1, (uint)pool.Length, 1), parameters.Construction.FootprintWidth, parameters.Construction.FootprintHeight, flags, default);
        Check(live.Cells[oldCell + 1] == 0 && raw.SequenceEqual(before) && removed.Counters.Counter19F9 == 1 &&
            BinaryPrimitives.ReadUInt32LittleEndian(pool.AsSpan(1 + 0x10)) == slot.Objects[2].RuntimeReference - 1,
            "Imported object detachment clears the new reservation and joins the original forward links without compacting bytes");
        uint castleFlags = BinaryPrimitives.ReadUInt32LittleEndian(Convert.FromHexString(castle.RawRecordHex).AsSpan(0x40));
        removed = OriginalSpatialRules.DetachRecord(live, pool, 1, removed.Chain, 4, 3, castleFlags, removed.Counters);
        var place = OriginalConstructionRules.ProbePlacement(live.Width, live.Height, map.Cells,
            Convert.FromHexString(map.SegmentPropertiesHex), live.Cells, 4, 3, castle.Construction.TerrainMask.ToArray(), 608, 720);
        Check(place.Status == ConstructionPlacementStatus.Clear && removed.Chain.CursorReference == slot.Objects[2].RuntimeReference &&
            removed.Counters == new OriginalRemovalCounters(1, 1),
            "Detaching the imported castle advances the owner cursor and opens its actual footprint for placement");
    }
}

public sealed class SpatialReference
{
    public int FormatVersion { get; set; }
    public byte Width { get; set; }
    public byte Height { get; set; }
    public uint Reference { get; set; }
    public MotionReference[] MotionProfiles { get; set; } = [];
    public int MotionCaseCount { get; set; }
    public string MotionSha256 { get; set; } = "";
    public DetachReference Detach { get; set; } = new();
    public int DetachCaseCount { get; set; }
    public string DetachSha256 { get; set; } = "";
    public int[][] Sequences { get; set; } = [];
    public int SequenceCaseCount { get; set; }
    public string SequenceSha256 { get; set; } = "";
}
public sealed class MotionReference
{
    public List<byte> Speeds { get; set; } = [];
    public List<byte> Remaining { get; set; } = [];
    public List<byte> Directions { get; set; } = [];
    public List<byte>[] Footprints { get; set; } = [];
    public List<byte> Layers { get; set; } = [];
    public uint[][] Patterns { get; set; } = [];
    public uint[] Phases { get; set; } = [];
}
public sealed class DetachReference
{
    public int[] Sizes { get; set; } = [];
    public int[] Lengths { get; set; } = [];
    public uint[] Bases { get; set; } = [];
    public List<byte> Owners { get; set; } = [];
    public List<byte> Layers { get; set; } = [];
    public List<byte>[] Footprints { get; set; } = [];
    public uint[] TemplateFlags { get; set; } = [];
    public ushort[] RecordFlags { get; set; } = [];
    public uint[][] Counters { get; set; } = [];
    public uint[] Pattern { get; set; } = [];
}
