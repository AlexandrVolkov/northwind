using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/builder.json (tools/verify_builder.py): the builder engine AFDB with the line step 69FE.</summary>
    private void OriginalBuilderChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/builder.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Builder template {item.Name} matches bundled DAT");
        var builderEngine = new OriginalBuilder(Convert.FromHexString(root.GetProperty("arctangent_hex").GetString()!));
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const int W = 32, H = 32, Pool = 0x400000, Scratch = 0x3000;
        int count = 0, firstMismatch = -1, segments = 0, lines = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int t = c.GetProperty("t").GetInt32(), owner = c.GetProperty("owner").GetInt32();
            var river = c.GetProperty("river").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            byte Class(int cell)
            {
                int x = cell % W, y = cell / W;
                if (river[0] <= (river[2] == 0 ? x : y) && (river[2] == 0 ? x : y) < river[0] + river[1]) return 2;
                return (x * 5 + y * 3 + river[3]) % 29 == 0 ? (byte)3 : (x + y + river[3]) % 17 == 0 ? (byte)1 : (byte)0;
            }
            var properties = new byte[W * H * 8]; for (int i = 0; i < W * H; i++) properties[i * 8] = Class(i);
            var tiles = Enumerable.Range(0, W * H).Select(i => (uint)i).ToArray();
            var grid = new uint[3 * W * H];
            var tp = Dat(t); int ox = (tp[3] & 1) << 4, oy = (tp[4] & 1) << 4;
            var p0 = c.GetProperty("p0").EnumerateArray().Select(v => v.GetInt32()).ToArray(); var p1 = c.GetProperty("p1").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var builder = new byte[0x40]; CombatUnit(2, (byte)owner, 100, 0, 200, 200, 1).CopyTo(builder, 0); builder[0] = 2; builder[3] = (byte)owner; builder[0x22] = (byte)t; builder[0x14] = 0x5b;
            BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x29), (ushort)(p0[0] * 32 + ox)); BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x2b), (ushort)(p0[1] * 32 + oy));
            BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x2d), (ushort)(p1[0] * 32 + ox)); BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x2f), (ushort)(p1[1] * 32 + oy));
            var participant = new byte[0xf5];
            int r = 0; foreach (var v in c.GetProperty("resources").EnumerateArray()) BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x18 + 4 * r++), v.GetUInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x2c), c.GetProperty("support").GetUInt32());
            var objects = new Dictionary<uint, byte[]>(); var references = new Dictionary<byte[], uint>(ReferenceEqualityComparer.Instance);
            var blockers = new List<byte[]>(); uint at = 0x200;
            foreach (var b in c.GetProperty("blockers").EnumerateArray())
            {
                bool same = b.GetProperty("same").GetBoolean(); int bt = b.GetProperty("t").GetInt32();
                var cell = b.GetProperty("cell").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                var rec = new byte[0x40];
                CombatUnit((byte)bt, (byte)(same ? owner : 5), 50, 0, (uint)(same ? p0[0] * 32 + ox : cell[0] * 32), (uint)(same ? p0[1] * 32 + oy : cell[1] * 32), 1).CopyTo(rec, 0);
                rec[0] = (byte)b.GetProperty("army").GetInt32(); rec[0x0e] = (byte)bt;
                objects[at] = rec; references[rec] = at; blockers.Add(rec);
                int slot = (same ? p0[1] : cell[1]) * W + (same ? p0[0] : cell[0]);
                grid[b.GetProperty("plane").GetInt32() * W * H + slot] = at; at += 0x40;
            }
            builderEngine.Toggle = (byte)c.GetProperty("toggle").GetInt32();
            var world = new BuilderProbeWorld(W, H, tiles, properties, grid, objects, references, Dat, c.GetProperty("fail_create").GetInt32() != 0, Pool + 0x100, Scratch);
            builderEngine.Run(world, builder, tp, participant);
            segments += world.Created.Count; if (world.Created.Count > 1) lines++;
            var created = world.Created.SelectMany(n => n).ToArray();
            byte[] output = [.. builder, .. participant.AsSpan(0x18, 0x10), .. (created.Length > 0 ? created : new byte[0x40]),
                .. blockers.SelectMany(b => b), .. world.LogBytes, builderEngine.Toggle];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0)
            { firstMismatch = count; GD.Print($"BUILDER MISMATCH {count} t{t} " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Builder: {segments} segments, {lines} lines; first mismatch {firstMismatch}");
        var result = root.GetProperty("results").GetProperty("builder");
        Check(count == result.GetProperty("case_count").GetInt32() && segments > 1000 && lines > 100, $"Builder reference coverage ({count}, {segments} segments, {lines} lines)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(), "Builder engine AFDB and line step 69FE match original x86");
    }

    private sealed class BuilderProbeWorld(int w, int h, uint[] tiles, byte[] properties, uint[] grid, Dictionary<uint, byte[]> objects, Dictionary<byte[], uint> references,
        Func<int, byte[]> dat, bool failCreate, int builderAddress, int scratch) : IBuilderWorld
    {
        public readonly List<byte[]> Created = [];
        public readonly List<byte> LogBytes = [];
        public BuilderProbe Probe(byte template, byte army, uint x, uint y, out byte[]? blocker)
        {
            var tp = dat(template); blocker = null;
            var result = OriginalConstructionRules.ProbePlacement((byte)w, (byte)h, tiles, properties, grid, tp[3], tp[4], tp.AsSpan(0x58, 8).ToArray(), x, y);
            if (result.Status == ConstructionPlacementStatus.Clear) return BuilderProbe.Clear;
            if (result.Status == ConstructionPlacementStatus.Rejected) return BuilderProbe.Rejected;
            blocker = objects[result.BlockingObjectOffset]; return BuilderProbe.Blocked;
        }
        public byte[]? Create(byte army, byte owner, byte template, byte variant, uint x, uint y)
        {
            LogBytes.Add((byte)'A'); LogBytes.AddRange(BitConverter.GetBytes((ushort)(army | owner << 8))); LogBytes.AddRange(BitConverter.GetBytes((ushort)(template | variant << 8)));
            LogBytes.AddRange(BitConverter.GetBytes(x)); LogBytes.AddRange(BitConverter.GetBytes(y));
            var record = new byte[0x40]; Created.Add(record); references[record] = (uint)(scratch + 0x40 * (Created.Count - 1));
            return failCreate ? null : record;
        }
        public void Register(byte[] record) { LogBytes.Add((byte)'R'); LogBytes.AddRange(BitConverter.GetBytes(references[record])); }
        public void Complete(byte template, byte kind) { LogBytes.Add((byte)'T'); LogBytes.AddRange(BitConverter.GetBytes((ushort)(template | kind << 8))); }
        public void Recount(byte[] builder) { LogBytes.Add((byte)'D'); LogBytes.AddRange(BitConverter.GetBytes((uint)builderAddress)); }
    }
}
