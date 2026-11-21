using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/buildcheck.json (tools/verify_buildcheck.py): «may this be built here», AF07 with B1EC and B28A.</summary>
    private void OriginalBuildCheckChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/buildcheck.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Build check template {item.Name} matches bundled DAT");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const int W = 32, H = 32;
        int count = 0, firstMismatch = -1, allowed = 0, hooks = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int t = c.GetProperty("t").GetInt32(), owner = c.GetProperty("owner").GetInt32();
            var river = c.GetProperty("river").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var p = c.GetProperty("p").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            byte Class(int cell)
            {
                int x = cell % W, y = cell / W;
                if (river[0] <= (river[2] == 0 ? x : y) && (river[2] == 0 ? x : y) < river[0] + river[1]) return 2;
                return (x * 5 + y * 3 + river[3]) % 29 == 0 ? (byte)3 : (x + y + river[3]) % 17 == 0 ? (byte)1 : (byte)0;
            }
            var properties = new byte[W * H * 8]; for (int i = 0; i < W * H; i++) properties[i * 8] = Class(i);
            var tiles = Enumerable.Range(0, W * H).Select(i => (uint)i).ToArray();
            var grid = new uint[3 * W * H]; var blockerRecords = new Dictionary<uint, byte[]>(); uint at = 0x200;
            foreach (var b in c.GetProperty("blockers").EnumerateArray())
            {
                bool same = b.GetProperty("same").GetBoolean(); int bt = b.GetProperty("t").GetInt32();
                var bc = b.GetProperty("cell").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                int bx = same ? p[0] : bc[0], by = same ? p[1] : bc[1];
                var rec = new byte[0x40]; rec[0] = (byte)b.GetProperty("army").GetInt32(); rec[3] = (byte)owner; rec[0x0e] = (byte)bt;
                BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(4), (uint)(bx * 32 + 16)); BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(8), (uint)(by * 32 + 16));
                blockerRecords[at] = rec; grid[b.GetProperty("plane").GetInt32() * W * H + by * W + bx] = at; at += 0x40;
            }
            var objects = new List<(byte[], byte[])>();
            foreach (var o in c.GetProperty("objects").EnumerateArray())
            {
                var oc = o.GetProperty("cell").EnumerateArray().Select(v => v.GetInt32()).ToArray(); int ot = o.GetProperty("t").GetInt32();
                var rec = new byte[0x40]; rec[0] = (byte)o.GetProperty("army").GetInt32(); rec[3] = (byte)owner; rec[0x0e] = (byte)ot; rec[0x19] = (byte)o.GetProperty("health").GetInt32();
                BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(4), (uint)(oc[0] * 32 + 16)); BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(8), (uint)(oc[1] * 32 + 16));
                objects.Add((rec, Dat(ot)));
            }
            var log = new List<byte>();
            var world = new BuildCheckWorld(W, H, tiles, properties, grid, blockerRecords, Dat, objects, c.GetProperty("hook").GetInt32() != 0, c.GetProperty("line").GetInt32() != 0,
                c.GetProperty("fog").GetBoolean() ? (p[1] * W + p[0]) : -1, log);
            var tp = Dat(t);
            bool ok = OriginalBuildCheck.Allowed(world, tp, (byte)t, 2, (byte)owner, (uint)(p[0] * 32 + 16), (uint)(p[1] * 32 + 16));
            if (ok) allowed++; if (log.Count > 0) hooks++;
            byte[] output = [(byte)(ok ? 1 : 0), .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0)
            { firstMismatch = count; GD.Print($"BUILDCHECK MISMATCH {count} t{t} " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Build check: {allowed} allowed, {hooks} reached the template check; first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("buildcheck");
        Check(count == summary.GetProperty("case_count").GetInt32() && allowed > 200 && hooks > 500, $"Build check reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Build check AF07 with range rules B1EC/B28A matches original x86");
    }

    private sealed class BuildCheckWorld(int w, int h, uint[] tiles, byte[] properties, uint[] grid, Dictionary<uint, byte[]> blockers, Func<int, byte[]> dat,
        List<(byte[], byte[])> objects, bool hook, bool line, int foggedCell, List<byte> log) : IBuildCheckWorld
    {
        public BuilderProbe Probe(byte template, byte army, uint x, uint y, out byte[]? blocker)
        {
            var tp = dat(template); blocker = null;
            var result = OriginalConstructionRules.ProbePlacement((byte)w, (byte)h, tiles, properties, grid, tp[3], tp[4], tp.AsSpan(0x58, 8).ToArray(), x, y);
            if (result.Status == ConstructionPlacementStatus.Clear) return BuilderProbe.Clear;
            if (result.Status == ConstructionPlacementStatus.Rejected) return BuilderProbe.Rejected;
            blocker = blockers[result.BlockingObjectOffset]; return BuilderProbe.Blocked;
        }
        public IEnumerable<(byte[] Record, byte[] Template)> Objects(byte participant) => objects;
        private void Log(char kind, int ax, uint x, uint y) { log.Add((byte)kind); log.AddRange(BitConverter.GetBytes((ushort)ax)); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y)); }
        public bool Hook(byte template, byte army, uint x, uint y) { Log('H', 4 << 8, x, y); return hook; }
        public bool Line(byte template, byte army, uint x, uint y) { Log('L', template | army << 8, x, y); return line; }
        public bool Explored(byte participant, uint x, uint y) => !(foggedCell == (int)((y >> 5) * w + (x >> 5)) && participant < 8);
    }
}
