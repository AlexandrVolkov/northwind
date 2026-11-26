using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/placement.json (tools/verify_placement.py): the creation placement check AC63 and the free-spot search 10AD2.</summary>
    private void OriginalPlacementChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/placement.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Placement template {item.Name} matches bundled DAT");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const int W = 32, H = 32;
        int count = 0, firstMismatch = -1, found = 0, blockedCount = 0, exhausted = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int t = c.GetProperty("t").GetInt32(), plane = c.GetProperty("plane").GetInt32();
            var river = c.GetProperty("river").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            byte Class(int cell)
            {
                int x = cell % W, y = cell / W;
                if (river[0] <= (river[2] == 0 ? x : y) && (river[2] == 0 ? x : y) < river[0] + river[1]) return 2;
                return (x * 5 + y * 3 + river[3]) % 29 == 0 ? (byte)3 : (x + y + river[3]) % 17 == 0 ? (byte)1 : (byte)0;
            }
            var properties = new byte[W * H * 8]; for (int i = 0; i < W * H; i++) properties[i * 8] = Class(i);
            var tiles = Enumerable.Range(0, W * H).Select(i => (uint)i).ToArray();
            var grid = new uint[3 * W * H]; var objects = new Dictionary<uint, byte[]>(); uint at = 0x200;
            foreach (var b in c.GetProperty("blockers").EnumerateArray())
            {
                var cell = b.GetProperty("cell").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                var rec = new byte[0x40]; rec[0] = 2; rec[3] = 5; rec[0x0e] = (byte)b.GetProperty("t").GetInt32(); rec[0x19] = (byte)b.GetProperty("health").GetInt32();
                objects[at] = rec; grid[b.GetProperty("plane").GetInt32() * W * H + cell[1] * W + cell[0]] = at; at += 0x40;
            }
            Func<uint, (byte[], byte[])> resolve = reference => (objects[reference], Dat(objects[reference][0x0e]));
            uint x0 = c.GetProperty("x").GetUInt32(), y0 = c.GetProperty("y").GetUInt32(); var tp = Dat(t);
            ushort seed = (ushort)c.GetProperty("seed").GetInt32();
            byte Next() { seed = (ushort)(seed * 25173 + 13849); return (byte)(seed >> 8); }
            BuilderProbe Probe(uint x, uint y) => OriginalCreationPlacement.Probe((byte)W, (byte)H, tiles, properties, grid, (byte)plane, tp, x, y, resolve).Status;
            byte[] output;
            if (c.GetProperty("search").GetBoolean())
            {
                var result = OriginalCreationPlacement.Search(Next, Next, W * 32, H * 32, tp, x0, y0, Probe);
                if (result.Found) found++; else exhausted++;
                output = [(byte)(result.Found ? 1 : 0), 0, 0, 0, 0, 0, .. BitConverter.GetBytes(result.X), .. BitConverter.GetBytes(result.Y)];
            }
            else
            {
                var result = OriginalCreationPlacement.Probe((byte)W, (byte)H, tiles, properties, grid, (byte)plane, tp, x0, y0, resolve);
                if (result.Status == BuilderProbe.Blocked) blockedCount++;
                output = [(byte)(result.Status == BuilderProbe.Clear ? 1 : 0), (byte)(result.Status == BuilderProbe.Blocked ? 1 : 0), .. BitConverter.GetBytes(result.Blocker), 0, 0, 0, 0, 0, 0, 0, 0];
            }
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0)
            { firstMismatch = count; GD.Print($"PLACEMENT MISMATCH {count} t{t} plane{plane} search {c.GetProperty("search").GetBoolean()} " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Placement: {found} spots found, {exhausted} searches exhausted, {blockedCount} blocked; first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("placement");
        Check(count == summary.GetProperty("case_count").GetInt32() && found > 500 && blockedCount > 50, $"Placement reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Creation placement AC63 and free-spot search 10AD2 match original x86");
    }
}
