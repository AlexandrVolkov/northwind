using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private void OriginalRoutePoolChecks()
    {
        using var doc = GameData.Read<JsonDocument>("res://tests/fixtures/route-pool.json"); var f = doc.RootElement;
        int[] Values(string key) => f.GetProperty(key).EnumerateArray().Select(v => v.GetInt32()).ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); int count = 0;
        void Verify(string name)
        {
            var expected = f.GetProperty("results").GetProperty(name);
            Check(count == expected.GetProperty("case_count").GetInt32(), $"Route pool {name} coverage");
            Check(Convert.ToHexStringLower(hash.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), $"Route pool {name} matches original x86"); count = 0;
        }
        Check(f.GetProperty("format_version").GetInt32() == 1, "Route pool fixture schema");
        var pool = new OriginalRoutePool(); var bytes = new byte[32 * OriginalRoutePool.OwnerBytes]; var raw = new byte[49];
        foreach (int seed in Values("seeds")) foreach (int owner in Values("owners"))
        foreach (int occupied in Values("occupied")) foreach (int length in Values("lengths"))
        {
            int start = owner * 8192;
            for (int i = 0; i < 8192; i++) bytes[start + i] = (byte)(i * 37 + seed);
            for (int block = 0; block < 128; block++) bytes[start + block * 64] = block < occupied ? (byte)0 : (byte)255;
            pool.LoadBytes(bytes); Array.Clear(raw); raw[3] = (byte)owner; raw[0x25] = (byte)seed;
            OriginalRoutePool.SetCursor(raw, OriginalRoutePool.NoRoute);
            var directions = new byte[length + 5];
            for (int i = 0; i < length; i++) directions[i] = (byte)((i * 3 + seed) & 7);
            directions[length] = 8;
            hash.AppendData(new byte[] { pool.Allocate(raw, directions) ? (byte)1 : (byte)0 });
            hash.AppendData(raw.AsSpan(0x28, 4)); hash.AppendData(pool.Bytes.Slice(start, 8192)); count++;
        }
        Verify("allocation");
        foreach (int owner in Values("owners")) foreach (int offset in new[] { 0, 1, 62, 63 })
        for (int value = 0; value < 256; value++) for (int high = 0; high < 2; high++) for (int reached = 0; reached < 2; reached++)
        {
            int start = owner * 8192; Array.Clear(bytes, start, 8192); bytes[start + 64] = 2; bytes[start + 64 + offset] = (byte)value;
            pool.LoadBytes(bytes); Array.Clear(raw); raw[3] = (byte)owner;
            OriginalRoutePool.SetCursor(raw, (uint)((start + 64 + offset) * 2 + high));
            byte nibble = pool.Peek(raw), status = 0;
            if ((nibble & 8) != 0) { pool.Release(raw); status = reached == 1 ? (byte)2 : (byte)1; nibble = 255; }
            hash.AppendData(new byte[] { status, nibble }); hash.AppendData(raw.AsSpan(0x28, 4)); hash.AppendData(pool.Bytes.Slice(start + 64, 64)); count++;
        }
        Verify("read_release");
        foreach (int owner in Values("owners"))
        for (int index = -1; index < 16384; index++)
        {
            int start = owner * 8192;
            for (int i = 0; i < 8192; i++) bytes[start + i] = (byte)(i * 37 + 7);
            pool.LoadBytes(bytes); Array.Clear(raw); raw[3] = (byte)owner;
            OriginalRoutePool.SetCursor(raw, index == -1 ? uint.MaxValue : (uint)(owner * 16384 + index));
            pool.Release(raw); hash.AppendData(raw.AsSpan(0x28, 4)); hash.AppendData(pool.Bytes.Slice(start, 8192)); count++;
        }
        Verify("release");
    }
    private void PackedRouteIntegrationChecks()
    {
        var pool = new OriginalRoutePool(2);
        var first = new SkirmishRoute(); var second = new SkirmishRoute(); var other = new SkirmishRoute();
        var a = new byte[49]; var b = new byte[49]; var c = new byte[49]; c[3] = 1;
        first.Attach(pool, a); second.Attach(pool, b); other.Attach(pool, c);
        Vector2I[] path = [new(10,10),new(11,10),new(11,11),new(10,12),new(9,11)];
        Check(first.Set(path) && second.Set(path) && other.Set(path) && OriginalRoutePool.Cursor(a) == 0 && OriginalRoutePool.Cursor(b) == 128 && OriginalRoutePool.Cursor(c) == 16384,
            "Live routes share a pool, use first free block, and separate owners");
        Check(first.SequenceEqual(path.Skip(1)) && first.Peek() == path[1], "Packed route enumeration and peek preserve all eight direction encoding");
        Check(first.Dequeue() == path[1] && OriginalRoutePool.Cursor(a) == 1 && first.SequenceEqual(path.Skip(2)), "Step consumption advances native cursor and preview without changing the other route");
        first.Clear(); Check(OriginalRoutePool.Cursor(a) == uint.MaxValue && second.Count == 4 && second.Peek() == path[1], "Cancellation frees only the caller's route");
        Check(first.Set(path) && OriginalRoutePool.Cursor(a) == 0, "Cancelled route block can be reused");
        while (first.Count > 0) first.Dequeue();
        Check(OriginalRoutePool.Cursor(a) == uint.MaxValue && pool.Bytes[0] == 255, "Prototype releases a route after its last admitted step");
        var longPath = Enumerable.Range(0, 201).Select(x => new Vector2I(x, 10)).ToArray();
        Check(first.Set(longPath) && first.Count == 125 && first.Last() == longPath[125], "Long AStar paths use a safe terminated prefix, retaining the external destination for replanning");
        first.Clear(); second.Clear(); other.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(0x28), 0x0a100b30);
        first.Clear(); Check(OriginalRoutePool.Cursor(a) == 0x0a100b30 && first.Set(path),
            "Idle route cancellation preserves the worker's tree-coordinate union; the next route can reuse that field");
        first.Clear();
        var leases = new List<SkirmishRoute>();
        for (int i = 0; i < 128; i++) { var r = new SkirmishRoute(); r.Attach(pool, new byte[49]); if (r.Set(path)) leases.Add(r); }
        Check(leases.Count == 128 && !first.Set(path) && first.Count == 0, "Full owner arena rejects a route without overwriting other actors");
        leases[37].Clear(); Check(first.Set(path) && OriginalRoutePool.Cursor(a) == 37 * 128, "A released interior block resolves arena exhaustion");
        first.Clear(); foreach (var route in leases) route.Clear();
        Check(Enumerable.Range(0,256).All(i => pool.Bytes[i * 64] == 255), "Stop and completion leave all per-owner route blocks free");
        LongPackedMoveChecks();
    }
    private void LongPackedMoveChecks()
    {
        using var battle = CreateSkirmish(); var hero = battle.Hero;
        // An open arena gives a deterministic cardinal route longer than one native block.
        int width = battle.Map.Width;
        var properties = Convert.FromHexString(battle.Map.SegmentPropertiesHex);
        uint ground = (uint)Enumerable.Range(0, properties.Length / 8).First(i => properties[i * 8] == 0 && battle.Map.Terrain[i] == "ground");
        Array.Fill(battle.Map.Cells, ground); battle.Occupancy.LoadCells(new uint[3 * width * battle.Map.Height]);
        foreach (var actor in battle.Actors.Where(a => a != hero)) actor.Health = 0;
        // Keep the objective alive so this movement scenario does not finish the battle.
        battle.Objective.Health = 100;
        BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(4), 10 * 32 + 16);
        BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(8), 10 * 32 + 16);
        battle.Occupancy.WriteFootprint(1, 1, hero.Record[0x0d], 10 * 32 + 16, 10 * 32 + 16, hero.Id);
        var goal = new Vector2I(110,50);
        Check(battle.Move([hero.Id],goal) == 1 && hero.Route.Count == 125 && hero.Goal == goal, "Ordinary long move keeps its goal beyond the packed prefix");
        for (int i = 0; i < 3000 && (hero.Cell != goal || hero.Stepping); i++) battle.Advance(.1);
        Check(hero.Cell == goal && !hero.Stepping && hero.Route.Count == 0 && OriginalRoutePool.Cursor(hero.Record) == uint.MaxValue,
            $"Ordinary movement replans after a full prefix and reaches its final destination: {hero.Cell}");
        Check(battle.Occupancy.Cells.ToArray().Count(v => v == hero.Id) == 1, "Long packed movement leaves no stale occupancy");
    }
}
