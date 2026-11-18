using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalOreChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/ore-extraction.json");
        var root = reference.RootElement;
        Check(root.GetProperty("format_version").GetInt32() == 1, "Ore extraction reference schema");
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Ore reference template {item.Name} matches bundled DAT");
        var constants = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json");
        var map = GameData.Read<MapData>("res://assets/imported/map.json");
        var properties = Convert.FromHexString(map.SegmentPropertiesHex);
        var results = root.GetProperty("results");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        void Finish(string name)
        {
            var expected = results.GetProperty(name);
            Check(count == expected.GetProperty("case_count").GetInt32(), $"Ore {name} reference coverage ({count})");
            Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), $"Ore {name} matches original x86");
            count = 0;
        }
        static byte[] Ints(JsonElement e) => e.EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
        static uint[] Uints(JsonElement e) => e.EnumerateArray().Select(v => v.GetUInt32()).ToArray();
        var empty = Dat(OriginalOreRules.EmptyWorker); var ore = Dat(OriginalOreRules.OreWorker); var timber = Dat(OriginalOreRules.TimberWorker);

        var spec = root.GetProperty("grant");
        foreach (byte cargo in Ints(spec.GetProperty("cargo")))
        foreach (byte existing in Ints(spec.GetProperty("existing")))
        foreach (byte condition in Ints(spec.GetProperty("conditions")))
        foreach (uint flags in Uints(spec.GetProperty("flags")))
        foreach (byte heading in Ints(spec.GetProperty("headings")))
        foreach (byte index in Ints(spec.GetProperty("random_indices")))
        {
            var current = (byte[])empty.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(current.AsSpan(0x40), flags);
            var catalog = new OriginalTerrainCatalog(properties, constants) { RandomIndex = index };
            var raw = OreWorkerRecord(10, 0, condition, existing, 0, heading);
            bool zf = OriginalOreRules.Grant(raw, current, ore, timber, cargo, catalog.NextRandomByte);
            digest.AppendData(raw); digest.AppendData([catalog.RandomIndex, (byte)(zf ? 1 : 0)]); count++;
        }
        Finish("grant");

        spec = root.GetProperty("extract");
        var mineTemplate = Dat(6); byte radius = mineTemplate[0x78]; int window = spec.GetProperty("window").GetInt32() / 2;
        byte width = (byte)map.Width, height = (byte)map.Height;
        foreach (var position in spec.GetProperty("positions").EnumerateArray())
        foreach (uint stock in Uints(spec.GetProperty("stocks")))
        foreach (ushort mineFlags in Uints(spec.GetProperty("mine_flags")))
        foreach (byte tick in Ints(spec.GetProperty("ticks")))
        foreach (var visitorSpec in spec.GetProperty("visitors").EnumerateArray())
        foreach (byte counter in Ints(spec.GetProperty("counters")))
        foreach (byte condition in Ints(spec.GetProperty("conditions")))
        foreach (byte kind in Ints(spec.GetProperty("types")))
        foreach (byte index in Ints(spec.GetProperty("random_indices")))
        {
            int cx = position[0].GetInt32(), cy = position[1].GetInt32();
            int vowner = visitorSpec[0].GetInt32();
            var cells = (uint[])map.Cells.Clone();
            var catalog = new OriginalTerrainCatalog(properties, constants) { RandomIndex = index };
            uint x = OriginalWorkerDelivery.BuildingAnchor((byte)cx, mineTemplate[3]), y = OriginalWorkerDelivery.BuildingAnchor((byte)cy, mineTemplate[4]);
            var mine = OreBuildingRecord(40, 6, 0, x, y, mineFlags);
            mine[0x15] = counter; mine[0x19] = condition; mine[0x23] = kind; BinaryPrimitives.WriteUInt32LittleEndian(mine.AsSpan(0x24), stock);
            var visitor = OreWorkerRecord(10, (byte)Math.Max(vowner, 0), (byte)visitorSpec[2].GetInt32(), (byte)visitorSpec[1].GetInt32(), 0, 0);
            OriginalOreRules.Extract(mine, radius, tick, vowner >= 0 ? visitor.AsSpan() : Span<byte>.Empty,
                (target, cargo) => OriginalOreRules.Grant(target, empty, ore, timber, cargo, catalog.NextRandomByte),
                (cargo, r) => OriginalOreRules.Deplete(cells, width, height, x, y, cargo, r, catalog));
            digest.AppendData(mine); digest.AppendData(visitor); digest.AppendData([catalog.RandomIndex]);
            var encoded = new byte[4];
            for (int yy = cy - window; yy <= cy + window; yy++)
            for (int xx = cx - window; xx <= cx + window; xx++)
                if (xx >= 0 && yy >= 0 && xx < width && yy < height)
                { BinaryPrimitives.WriteUInt32LittleEndian(encoded, cells[yy * width + xx]); digest.AppendData(encoded); }
            count++;
        }
        Finish("extract");

        spec = root.GetProperty("credit");
        var castleTemplate = Dat(2);
        foreach (ushort castleFlags in Uints(spec.GetProperty("castle_flags")))
        foreach (ushort visitorFlags in Uints(spec.GetProperty("visitor_flags")))
        foreach (byte visitorTemplate in Ints(spec.GetProperty("templates")))
        foreach (byte cargo in Ints(spec.GetProperty("cargo")))
        foreach (var owners in spec.GetProperty("owners").EnumerateArray())
        foreach (byte tick in Ints(spec.GetProperty("ticks")))
        foreach (byte present in Ints(spec.GetProperty("present")))
        foreach (var stocks in spec.GetProperty("stocks").EnumerateArray())
        {
            var castle = OreBuildingRecord(49, 2, (byte)owners[0].GetInt32(), OriginalWorkerDelivery.BuildingAnchor(6, castleTemplate[3]),
                OriginalWorkerDelivery.BuildingAnchor(6, castleTemplate[4]), castleFlags);
            var visitor = OreWorkerRecord(visitorTemplate, (byte)owners[1].GetInt32(), 40, cargo, visitorFlags, 0);
            var s = Uints(stocks);
            var credit = new OreCredit(s[0], s[2], s[2], s[0]);
            OriginalOreRules.CastleReceive(castle, tick, present != 0 ? visitor.AsSpan() : Span<byte>.Empty, Dat(visitorTemplate), ref credit);
            var participant = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(participant, credit.Metal); BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(4), s[1]);
            BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(8), credit.Gold); BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(12), s[3]);
            BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(16), credit.GoldStatistics); BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(20), credit.MetalStatistics);
            digest.AppendData(castle); digest.AppendData(visitor); digest.AppendData(participant); count++;
        }
        Finish("credit");

        spec = root.GetProperty("carrier");
        var world = new OreCarrierProbe();
        foreach (byte phase in Ints(spec.GetProperty("phases")))
        foreach (byte cargo in Ints(spec.GetProperty("cargo")))
        foreach (uint target in Uints(spec.GetProperty("targets")))
        foreach (uint saved in Uints(spec.GetProperty("saved")))
        foreach (byte timer in Ints(spec.GetProperty("timers")))
        foreach (var approach in spec.GetProperty("approach").EnumerateArray())
        foreach (uint receiver in Uints(spec.GetProperty("receiver")))
        foreach (byte entry in Ints(spec.GetProperty("entry_flags")))
        {
            var raw = OreWorkerRecord((byte)(cargo != 0 ? 8 : 10), 0, 40, cargo, 0, 0);
            raw[0x15] = phase; raw[0x30] = timer;
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x1c), target); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x32), saved);
            world.Reset(raw, approach.GetString() switch { "pending" => OreApproach.Pending, "done" => OreApproach.Completed, _ => OreApproach.Failed }, receiver);
            bool zf = OriginalOreRules.CarrierStep(raw, entry == 0x40, world);
            digest.AppendData(raw); digest.AppendData([(byte)(zf ? 1 : 0)]); digest.AppendData(Encoding.UTF8.GetBytes(world.Log())); count++;
        }
        Finish("carrier");

        spec = root.GetProperty("validate");
        foreach (byte receiver in Ints(spec.GetProperty("receivers")))
        foreach (uint flags in Uints(spec.GetProperty("flags")))
        foreach (var owners in spec.GetProperty("owners").EnumerateArray())
        {
            var visitorTemplate = (byte[])empty.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(visitorTemplate.AsSpan(0x40), flags);
            var building = OreBuildingRecord(40, receiver, (byte)owners[0].GetInt32(), 64, 64, 0);
            var visitor = OreWorkerRecord(10, (byte)owners[1].GetInt32(), 40, 0, 0, 0);
            bool accepted = receiver == 6 ? OriginalOreRules.MineAccepts(visitorTemplate) : OriginalOreRules.CastleAccepts(building, visitor, visitorTemplate);
            digest.AppendData([(byte)(accepted ? 1 : 0)]); count++;
        }
        Finish("validate");

        spec = root.GetProperty("return");
        foreach (byte heading in Ints(spec.GetProperty("headings")))
        foreach (byte index in Ints(spec.GetProperty("random_indices")))
        {
            var catalog = new OriginalTerrainCatalog(properties, constants) { RandomIndex = index };
            var raw = OreWorkerRecord(8, 0, 40, 0, 0, heading);
            OriginalOreRules.ReturnEmpty(raw, empty, catalog.NextRandomByte());
            digest.AppendData(raw); digest.AppendData([catalog.RandomIndex]); count++;
        }
        Finish("return");
    }

    private static byte[] OreWorkerRecord(byte template, byte owner, byte condition, byte cargo, ushort flags, byte heading)
    {
        var raw = Enumerable.Range(0, 54).Select(i => unchecked((byte)(i * 7 + 5))).ToArray();
        raw[0] = 2; raw[3] = owner; raw[0x0d] = 1; raw[0x0e] = template; raw[0x19] = condition; raw[0x25] = heading; raw[0x31] = cargo;
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x17), flags); return raw;
    }
    private static byte[] OreBuildingRecord(int size, byte template, byte owner, uint x, uint y, ushort flags)
    {
        var raw = Enumerable.Range(0, size).Select(i => unchecked((byte)(i * 11 + 3))).ToArray();
        raw[0] = 2; raw[3] = owner; raw[0x0d] = 1; raw[0x0e] = template;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), x); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), y);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x17), flags); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x10), (uint)size);
        return raw;
    }

    /// <summary>Mirrors the stubs of tools/verify_ore_extraction.py; the log uses Python's JSON spelling.</summary>
    private sealed class OreCarrierProbe : IOreCarrierWorld
    {
        private readonly Dictionary<uint, byte[]> _records = [];
        private readonly List<string> _calls = [];
        private byte[] _worker = [];
        private OreApproach _approach; private uint _receiver;
        public OreCarrierProbe()
        {
            _records[0x200] = OreBuildingRecord(40, 6, 0, 64, 64, 0);
            _records[0x280] = OreBuildingRecord(40, 6, 3, 96, 64, 0);
            var dead = OreBuildingRecord(40, 6, 0, 64, 96, 0); dead[0x19] = 0; _records[0x300] = dead;
        }
        public void Reset(byte[] worker, OreApproach approach, uint receiver)
        { _worker = worker; _approach = approach; _receiver = receiver; _calls.Clear(); }
        public string Log() => "[" + string.Join(", ", _calls) + "]";
        public ReadOnlySpan<byte> Record(uint reference) => _records.TryGetValue(reference, out var raw) ? raw : default;
        public OreApproach Approach()
        { _calls.Add($"[\"8F70\", {BinaryPrimitives.ReadUInt32LittleEndian(_worker.AsSpan(0x1c))}]"); return _approach; }
        public uint FindReceiver() { _calls.Add("[\"C439\", 1288, 3848]"); return _receiver; }
        public void StepAside() => _calls.Add("[\"8B0F\"]");
    }
}
