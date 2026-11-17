using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private static int[] Numbers(JsonElement value) => value.EnumerateArray().Select(v => v.GetInt32()).ToArray();
    private static byte[] ForestryRecord() => Enumerable.Range(0, 54).Select(i => i == 0x32 ? (byte)5 : i == 0x33 ? (byte)3 : unchecked((byte)(i * 13 + 5))).ToArray();
    private static byte[] EncodeCells(uint[] cells)
    {
        var bytes = new byte[cells.Length * 4];
        for (int i = 0; i < cells.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), cells[i]);
        return bytes;
    }
    private static byte[] ForestryProperties(IEnumerable<uint> descriptors)
    {
        var list = descriptors.ToArray(); var bytes = new byte[list.Length * 8];
        for (int i = 0; i < list.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 8), list[i]);
        return bytes;
    }
    private static byte[] ForestryCatalog(int kind)
    {
        var descriptors = new List<uint>();
        for (int bits = 0; bits < 16; bits++)
        {
            uint corners = 0;
            for (int i = 0; i < 4; i++) corners |= (uint)((bits & (1 << i)) != 0 ? 6 : 4) << (i * 4);
            descriptors.Add((corners << 16) | (bits == 0 ? 0u : 1u));
        }
        if (kind == 1) descriptors[10] = 0x44440300;
        if (kind == 2) while (descriptors.Count < 260) descriptors.Add(0x11110300);
        descriptors.AddRange(new uint[] { 0x44440210, 0x44440210 });
        return ForestryProperties(descriptors);
    }
    private void OriginalForestryChecks()
    {
        using var document = GameData.Read<JsonDocument>("res://tests/fixtures/forestry.json");
        var fixture = document.RootElement;
        var constants = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        void Verify(string name)
        {
            var expected = fixture.GetProperty("results").GetProperty(name);
            Check(count == expected.GetProperty("case_count").GetInt32(), $"Forestry {name} coverage");
            Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), $"Forestry {name} matches native x86");
            count = 0;
        }
        Check(fixture.GetProperty("format_version").GetInt32() == 1, "Forestry fixture format");
        var encoded = new byte[4];
        foreach (int high in Numbers(fixture.GetProperty("query_high")))
        for (uint low = 0; low < 65536; low++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(encoded, OriginalForestryRules.TerrainCode(((uint)high << 16) | low));
            digest.AppendData(encoded); count++;
        }
        Verify("query");
        var find = fixture.GetProperty("find");
        var properties = ForestryProperties(find.GetProperty("descriptors").EnumerateArray().Select(v => v.GetUInt32()));
        foreach (var p in find.GetProperty("patterns").EnumerateArray())
        foreach (var f in find.GetProperty("footprints").EnumerateArray())
        foreach (int radius in Numbers(find.GetProperty("radii")))
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
        {
            var pattern = Numbers(p); var footprint = Numbers(f);
            if (x < footprint[0] / 2 || y < footprint[1] / 2 || x - footprint[0] / 2 + footprint[0] > 8 || y - footprint[1] / 2 + footprint[1] > 8) continue;
            var cells = Enumerable.Range(0, 64).Select(i => 0xabc00000u | (uint)pattern[i % pattern.Length]).ToArray();
            bool found = OriginalForestryRules.FindTimber(8, 8, cells, properties, (uint)(x * 32 + 16), (uint)(y * 32 + 16),
                (byte)footprint[0], (byte)footprint[1], (byte)radius, out var cell);
            digest.AppendData(new byte[] { found ? (byte)1 : (byte)0, cell.X, cell.Y }); count++;
        }
        Verify("find");
        var template = new byte[171]; var prepare = fixture.GetProperty("prepare");
        for (int packed = 0; packed < 256; packed++)
        foreach (int hits in Numbers(prepare.GetProperty("hits")))
        foreach (int aux in Numbers(prepare.GetProperty("auxiliary")))
        {
            var raw = ForestryRecord(); raw[0x34] = (byte)packed; raw[0x15] = (byte)aux; template[0x78] = (byte)hits;
            OriginalForestryRules.InitializeWork(raw, template); digest.AppendData(raw); count++;
        }
        Verify("prepare");
        var work = fixture.GetProperty("work");
        for (int packed = 0; packed < 256; packed++)
        {
            if ((packed & 31) == 0) continue;
            foreach (int modifier in Numbers(work.GetProperty("modifiers")))
            foreach (int counter in Numbers(work.GetProperty("counters")))
            foreach (int basis in Numbers(work.GetProperty("bases")))
            {
                var raw = ForestryRecord(); raw[0x34] = (byte)packed; raw[0x16] = (byte)modifier; raw[0x2f] = (byte)counter;
                template[0x75] = (byte)basis;
                var result = OriginalForestryRules.AdvanceWork(raw, template);
                digest.AppendData(raw); digest.AppendData(new byte[] { result == HarvestWork.SwingQueued ? (byte)1 : (byte)0 }); count++;
            }
        }
        Verify("work");
        for (int modifier = 0; modifier < 256; modifier++)
        {
            var raw = ForestryRecord(); raw[0x16] = (byte)modifier;
            OriginalForestryRules.ApplyLoadModifier(raw); digest.AppendData(raw); count++;
        }
        Verify("modifier");
        var clear = fixture.GetProperty("clear");
        foreach (int kind in Numbers(clear.GetProperty("catalogs")))
        foreach (int random in Numbers(clear.GetProperty("random_indices")))
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
        {
            var cells = Enumerable.Range(0, 64).Select(i => 0xabc00000u | (uint)((i * 7 + 5) % 16)).ToArray();
            var catalog = new OriginalTerrainCatalog(ForestryCatalog(kind), constants) { RandomIndex = (byte)random };
            catalog.ClearTimber(cells, 8, 8, x, y); digest.AppendData(EncodeCells(cells)); digest.AppendData(new byte[] { catalog.RandomIndex }); count++;
        }
        Verify("clear");
        foreach (int kind in Numbers(clear.GetProperty("catalogs")))
        foreach (int random in Numbers(clear.GetProperty("random_indices")))
        for (int packed = 0; packed < 256; packed += 32)
        {
            var raw = ForestryRecord(); raw[0x34] = (byte)packed;
            var cells = Enumerable.Range(0, 64).Select(i => 0xabc00000u | (uint)((i * 7 + 5) % 16)).ToArray();
            var catalog = new OriginalTerrainCatalog(ForestryCatalog(kind), constants) { RandomIndex = (byte)random };
            if (OriginalForestryRules.FinishLoad(raw)) catalog.ClearTimber(cells, 8, 8, raw[0x32], raw[0x33]);
            digest.AppendData(raw); digest.AppendData(EncodeCells(cells)); digest.AppendData(new byte[] { catalog.RandomIndex }); count++;
        }
        Verify("finish");
    }
    private void OriginalForestryMapChecks()
    {
        using var document = GameData.Read<JsonDocument>("res://tests/fixtures/forestry.json");
        var fixture = document.RootElement;
        var map = GameData.Read<MapData>("res://assets/imported/map.json");
        var properties = Convert.FromHexString(map.SegmentPropertiesHex);
        var constants = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json");
        var spec = fixture.GetProperty("source_map");
        Check(Convert.ToHexStringLower(SHA256.HashData(EncodeCells(map.Cells))) == spec.GetProperty("cells_sha256").GetString() &&
            Convert.ToHexStringLower(SHA256.HashData(properties)) == spec.GetProperty("properties_sha256").GetString(), "Forestry reference uses bundled map and SEG");
        var cleared = (uint[])map.Cells.Clone();
        var terrain = new OriginalTerrainCatalog(properties, constants);
        var empty = new uint[3 * map.Cells.Length]; byte[] groundOnly = [1, 0, 0, 0, 0, 0, 0, 0];
        var before = OriginalConstructionRules.ProbePlacement((byte)map.Width, (byte)map.Height, cleared, properties, empty, 1, 1, groundOnly, 6 * 32 + 16, 10 * 32 + 16);
        terrain.ClearTimber(cleared, (byte)map.Width, (byte)map.Height, 6, 10);
        var after = OriginalConstructionRules.ProbePlacement((byte)map.Width, (byte)map.Height, cleared, properties, empty, 1, 1, groundOnly, 6 * 32 + 16, 10 * 32 + 16);
        Check(before.Status == ConstructionPlacementStatus.Rejected && after.Status == ConstructionPlacementStatus.Clear,
            "Cleared source cell changes the real construction terrain classification");
        var failed = (uint[])map.Cells.Clone(); terrain.RandomIndex = 0;
        Check(terrain.ClearTimber(failed, (byte)map.Width, (byte)map.Height, 10, 20) == 0 && failed.SequenceEqual(map.Cells) && terrain.RandomIndex != 0,
            "Unresolvable native forest edge preserves every cell but consumes RNG");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); int count = 0;
        foreach (var point in spec.GetProperty("points").EnumerateArray())
        foreach (int random in Numbers(fixture.GetProperty("clear").GetProperty("random_indices")))
        {
            var p = Numbers(point); var cells = (uint[])map.Cells.Clone();
            var catalog = new OriginalTerrainCatalog(properties, constants) { RandomIndex = (byte)random };
            catalog.ClearTimber(cells, (byte)map.Width, (byte)map.Height, p[0], p[1]);
            digest.AppendData(EncodeCells(cells)); digest.AppendData(new byte[] { catalog.RandomIndex }); count++;
        }
        var result = fixture.GetProperty("results").GetProperty("source_map");
        Check(count == result.GetProperty("case_count").GetInt32() && Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(),
            "Real forest edges, RNG consumption and atomic failure match original x86");
    }
}
