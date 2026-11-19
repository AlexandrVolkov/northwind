using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalTargetChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/enemy-search.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Search reference template {item.Name} matches bundled DAT");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var world = new SearchProbe(c.GetProperty("hostile").GetUInt32(), c.GetProperty("contacts").EnumerateArray().Select(v => v.GetInt32() != 0).ToArray());
            var self = c.GetProperty("self");
            int selfTemplate = self[0].GetInt32();
            var me = CombatUnit((byte)selfTemplate, 0, 10, 0, self[1].GetUInt32(), self[2].GetUInt32(), 1); me[0x16] = (byte)c.GetProperty("experience").GetInt32();
            var refs = new List<uint> { 0x100 };
            world.Add(0x100, me, Dat(selfTemplate));
            uint next = 0x140;
            foreach (var o in c.GetProperty("objects").EnumerateArray())
            {
                var rec = CombatUnit((byte)o[0].GetInt32(), (byte)o[1].GetInt32(), (byte)o[5].GetInt32(), 0, o[2].GetUInt32(), o[3].GetUInt32(), (byte)o[4].GetInt32());
                rec[0x20] = (byte)o[6].GetInt32(); BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x1c), o[7].GetInt32() != 0 ? 0x100u : 0);
                world.Add(next, rec, Dat(o[0].GetInt32())); refs.Add(next); next += 0x40;
            }
            var result = OriginalTargetRules.FindEnemy(0x100, me, Dat(selfTemplate), c.GetProperty("mode").GetInt32() != 0, world);
            int found = result.Target == 0 ? -1 : refs.IndexOf(result.Target);
            string calls = "[" + string.Join(", ", world.Calls) + "]";
            string text = $"[{found}, {(result.Target == 0 ? 1 : 0)}, {(result.Searched ? result.Enemies : 0)}, {(result.Searched ? result.Others : 0)}, {calls}]";
            digest.AppendData(Encoding.UTF8.GetBytes(text)); count++;
        }
        var expected = root.GetProperty("results").GetProperty("search");
        Check(count == expected.GetProperty("case_count").GetInt32(), $"Enemy search reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), "Enemy search D895 matches original x86");
    }
    private sealed class SearchProbe(uint hostile, bool[] contacts) : IEnemySearchWorld
    {
        private readonly uint[] _grid = new uint[3 * 256];
        private readonly Dictionary<uint, (byte[], byte[])> _objects = [];
        public List<string> Calls { get; } = [];
        public byte MapWidth => 16;
        public byte MapHeight => 16;
        public uint HostileMask => hostile;
        public void Add(uint reference, byte[] record, byte[] template)
        {
            _objects[reference] = (record, template);
            int w = template[3], h = template[4], x0 = (int)(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4)) >> 5) - w / 2,
                y0 = (int)(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8)) >> 5) - h / 2, plane = record[0x0d] & 3;
            for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++)
                if (x >= 0 && y >= 0 && x < 16 && y < 16) _grid[plane * 256 + y * 16 + x] = reference;
        }
        public uint Occupant(int plane, int cell) => _grid[plane * 256 + cell];
        public (byte[] Record, byte[] Template) Resolve(uint reference) => _objects[reference];
        public bool Contact(byte owner) => owner < contacts.Length && contacts[owner];
        // The reference map marks every column x % 8 == 1 as water (class 1).
        public byte TerrainClass(ReadOnlySpan<byte> record) => (byte)(((BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) >> 5) % 8) == 1 ? 1 : 0);
        public void Contacted(FirstContact c) => Calls.Add($"[{(c.Owner << 8) | 3}, {(c.TheirOwner << 8) | c.Owner}, {(c.TheirOwner << 16) | (c.CellY << 8) | c.CellX}]");
    }
}
