using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalAiChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/ai-census.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"AI reference template {item.Name} matches bundled DAT");
        using var census = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var threat = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var ai = new byte[0x1a35]; var participant = new byte[0xf5];
            BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(1), c.GetProperty("turn").GetUInt32()); ai[5] = (byte)c.GetProperty("mode").GetInt32();
            BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(0x6b), c.GetProperty("previous_gold").GetUInt32());
            var r = c.GetProperty("resources"); uint g = r[0].GetUInt32(), m = r[1].GetUInt32(), w = r[2].GetUInt32(), people = r[3].GetUInt32();
            BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x18), m); BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x1c), w);
            BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x20), g); BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x24), people);
            BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x28), c.GetProperty("support")[1].GetUInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x2c), c.GetProperty("support")[0].GetUInt32());
            var world = new CensusProbe();
            var chain = new List<(byte[], byte[])>(); uint at = 0x100;
            foreach (var o in c.GetProperty("objects").EnumerateArray())
            {
                int t = o[0].GetInt32();
                var rec = CombatUnit((byte)t, (byte)o[1].GetInt32(), (byte)o[4].GetInt32(), 0, o[2].GetUInt32(), o[3].GetUInt32(), 1);
                rec[0x14] = (byte)o[5].GetInt32(); rec[0x23] = (byte)o[6].GetInt32(); BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x24), o[7].GetUInt32());
                BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x10), 0x40);
                chain.Add((rec, Dat(t))); world.Add(at, rec, Dat(t)); at += 0x40;
            }
            var bounds = OriginalAiRules.Census(ai, participant, chain, 32, 32);
            census.AppendData(ai.AsSpan(0, 0x80)); census.AppendData(ai.AsSpan(0x19e0, 0x50));
            var globals = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(globals, bounds.MaxX); BinaryPrimitives.WriteUInt32LittleEndian(globals.AsSpan(4), bounds.MaxY);
            BinaryPrimitives.WriteUInt32LittleEndian(globals.AsSpan(8), bounds.MinX); BinaryPrimitives.WriteUInt32LittleEndian(globals.AsSpan(12), bounds.MinY);
            census.AppendData(globals);
            var values = OriginalAiRules.Threat(ai, world);
            var encoded = new byte[64]; for (int i = 0; i < 16; i++) BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(i * 4), values[i]);
            threat.AppendData(encoded); count++;
        }
        var results = root.GetProperty("results");
        Check(count == results.GetProperty("census").GetProperty("case_count").GetInt32(), $"AI census reference coverage ({count})");
        Check(Convert.ToHexStringLower(census.GetHashAndReset()) == results.GetProperty("census").GetProperty("sha256").GetString(), "AI census 1E87A matches original x86");
        Check(Convert.ToHexStringLower(threat.GetHashAndReset()) == results.GetProperty("threat").GetProperty("sha256").GetString(), "Territory threat 23B10 matches original x86");
    }
    private sealed class CensusProbe : IEnemySearchWorld
    {
        private readonly uint[] _grid = new uint[3 * 1024];
        private readonly Dictionary<uint, (byte[], byte[])> _objects = [];
        public byte MapWidth => 32;
        public byte MapHeight => 32;
        public uint HostileMask => 0;
        public void Add(uint reference, byte[] record, byte[] template)
        {
            _objects[reference] = (record, template);
            _grid[1024 + (int)(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8)) >> 5) * 32 + (int)(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4)) >> 5)] = reference;
        }
        public uint Occupant(int plane, int cell) => _grid[plane * 1024 + cell];
        public (byte[] Record, byte[] Template) Resolve(uint reference) => _objects[reference];
        public bool Contact(byte owner) => true;
        public byte TerrainClass(ReadOnlySpan<byte> record) => 0;
        public void Contacted(FirstContact contact) { }
    }
}
