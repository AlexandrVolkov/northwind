using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalRouteSearchChecks()
    {
        using var doc = GameData.Read<JsonDocument>("res://tests/fixtures/route-search.json"); var root = doc.RootElement;
        Check(root.GetProperty("format_version").GetInt32() == 1, "Contour search fixture version");
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, probes = 0, missing = 0, partial = 0;
        var search = new OriginalRouteSearch();
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int[] Get(string name) => c.GetProperty(name).EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var size = Get("size"); var footprint = Get("footprint"); var start = Get("start"); var target = Get("target");
            var blocked = Convert.FromHexString(c.GetProperty("blocked").GetString()!); var trace = new List<byte>();
            bool Probe(byte x, byte y, byte direction)
            {
                trace.AddRange(new byte[] { x, y, direction });
                if (x > size[0] - footprint[0] || y > size[1] - footprint[1]) return false;
                for (int yy = y; yy < y + footprint[1]; yy++) for (int xx = x; xx < x + footprint[0]; xx++)
                    if (blocked[yy * size[0] + xx] != 0) return false;
                return true;
            }
            var result = search.Find((byte)size[0], (byte)size[1], (byte)footprint[0], (byte)footprint[1],
                new((byte)start[0], (byte)start[1]), new((byte)target[0], (byte)target[1]), Probe);
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write(result.Found); writer.Write((ushort)result.Directions.Length); writer.Write(result.Directions);
            writer.Write(result.SearchEnd.X); writer.Write(result.SearchEnd.Y); writer.Write(trace.Count / 3); writer.Write(trace.ToArray()); writer.Flush();
            var bytes = stream.ToArray(); aggregate.AppendData(bytes);
            string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Check(hash == c.GetProperty("sha256").GetString(), $"Native contour route and probe order: {c.GetProperty("name").GetString()}, route={Convert.ToHexString(result.Directions)}, end={result.SearchEnd}, probes={trace.Count / 3}, hash={hash}");
            count++; probes += trace.Count / 3; if (!result.Found) missing++;
            int x = start[0], y = start[1];
            foreach (byte d in result.Directions) { var delta = OriginalRouteSearch.Deltas[d]; x += delta.X; y += delta.Y; }
            if (result.Found && (x != target[0] || y != target[1])) partial++;
        }
        var expected = root.GetProperty("result");
        Check(count == expected.GetProperty("case_count").GetInt32() && probes == expected.GetProperty("probe_count").GetInt32(), "Contour route and probe coverage");
        Check(missing == expected.GetProperty("no_route_count").GetInt32() && partial == expected.GetProperty("partial_count").GetInt32(), "Contour no-path and partial-route outcomes match x86");
        Check(Convert.ToHexStringLower(aggregate.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), "All contour results match original x86");
        OriginalRoutePlanChecks();
    }

    /// <summary>Replays tests/fixtures/route-plan.json (tools/verify_route_plan.py): the planner 72FE with explicit parameters.</summary>
    private void OriginalRoutePlanChecks()
    {
        using var doc = GameData.Read<JsonDocument>("res://tests/fixtures/route-plan.json"); var root = doc.RootElement;
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, foundCount = 0; var search = new OriginalRouteSearch();
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int[] Get(string name) => c.GetProperty(name).EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var size = Get("size"); var footprint = Get("footprint"); var start = Get("start"); var target = Get("target");
            var blocked = Convert.FromHexString(c.GetProperty("blocked").GetString()!); var trace = new List<byte>();
            bool Probe(byte x, byte y, byte direction)
            {
                trace.AddRange(new byte[] { x, y, direction });
                if (x > size[0] - footprint[0] || y > size[1] - footprint[1]) return false;
                for (int yy = y; yy < y + footprint[1]; yy++) for (int xx = x; xx < x + footprint[0]; xx++)
                    if (blocked[yy * size[0] + xx] != 0) return false;
                return true;
            }
            byte half = (byte)(footprint[0] >> 1);
            var result = search.Plan72FE((byte)size[0], (byte)size[1], (byte)footprint[0], new((byte)(start[0] + half), (byte)(start[1] + half)), new((byte)(target[0] + half), (byte)(target[1] + half)), Probe);
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            writer.Write(result.Found); writer.Write((ushort)result.Route.Length); writer.Write(result.Route);
            writer.Write(result.End.X); writer.Write(result.End.Y); writer.Write(result.Left); writer.Write(trace.Count / 3); writer.Write(trace.ToArray()); writer.Flush();
            var bytes = stream.ToArray(); aggregate.AppendData(bytes);
            Check(Convert.ToHexStringLower(SHA256.HashData(bytes)) == c.GetProperty("sha256").GetString(), $"Native planner 72FE: {c.GetProperty("name").GetString()}, route={Convert.ToHexString(result.Route)}, end={result.End}, left={result.Left}, probes={trace.Count / 3}");
            count++; if (result.Found) foundCount++;
        }
        var expected = root.GetProperty("result");
        Check(count == expected.GetProperty("case_count").GetInt32() && foundCount == expected.GetProperty("found_count").GetInt32(), "Planner 72FE coverage and outcomes");
        Check(Convert.ToHexStringLower(aggregate.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), "All planner 72FE results match original x86");
    }
}

public partial class TestRunner
{
    private void RouteSearchIntegrationChecks()
    {
        using var battle = CreateSkirmish(); var hero = battle.Hero;
        int width = battle.Map.Width;
        var properties = Convert.FromHexString(battle.Map.SegmentPropertiesHex);
        uint ground = (uint)Enumerable.Range(0, properties.Length / 8).First(i => properties[i * 8] == 0 && battle.Map.Terrain[i] == "ground");
        uint water = (uint)Enumerable.Range(0, properties.Length / 8).First(i => battle.Map.Terrain[i] == "water");
        Array.Fill(battle.Map.Cells, ground); battle.Occupancy.LoadCells(new uint[battle.Occupancy.Cells.Length]);
        foreach (var actor in battle.Actors.Where(a => a != hero)) actor.Health = 0;
        battle.Objective.Health = 100;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(4), 50 * 32 + 16);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(8), 50 * 32 + 16);
        battle.Occupancy.WriteFootprint(1, 1, hero.Record[0x0d], 50 * 32 + 16, 50 * 32 + 16, hero.Id);
        // The native candidate test checks the destination footprint, permitting this diagonal.
        battle.Map.Cells[50 * width + 51] = water; battle.Map.Cells[51 * width + 50] = water;
        var goal = new Godot.Vector2I(53,53);
        Check(battle.Move([hero.Id], goal) == 1 && hero.Route.SequenceEqual(new Godot.Vector2I[] { new(51,51),new(52,52),new(53,53) }),
            "Live native search chooses three diagonal steps, including original destination-only corner admission");
        for (int i = 0; i < 300 && (hero.Cell != goal || hero.Stepping); i++) battle.Advance(.1);
        Check(hero.Cell == goal && !hero.Stepping && (hero.Record[0x25] & 7) == 3 && battle.GuidedRouteSearches == 0,
            "Ordinary diagonal movement completes on native kernels without distant-order guidance");
        Check(battle.Occupancy.Cells.ToArray().Count(v => v == hero.Id) == 1, "Diagonal movement transfers occupancy without leaving stale cells");
        for (int y = 47; y <= 57; y++) battle.Map.Cells[y * width + 57] = water;
        goal = new(62,53);
        Check(battle.Move([hero.Id], goal) == 1 && hero.Route.Any(c => c.Y != 53), "Native contour search starts an ordinary detour around a wall");
        // Invalidate the planned first step; the actor must reject it and keep the final order.
        var newlyBlocked = hero.Route.Peek(); battle.Map.Cells[newlyBlocked.Y * width + newlyBlocked.X] = water;
        battle.Advance(1.0 / battle.Rules.TicksPerSecond);
        Check(!hero.Stepping && hero.Cell != newlyBlocked && hero.Route.Count == 0 && hero.Goal == goal,
            "A newly blocked native route is discarded before entry while preserving its destination");
        for (int i = 0; i < 1200 && (hero.Cell != goal || hero.Stepping); i++) battle.Advance(.1);
        Check(hero.Cell == goal && !hero.Stepping, $"Movement rebuilds an obstructed route and reaches the far side: {hero.Cell}");
        Check(battle.Occupancy.Cells.ToArray().Count(v => v == hero.Id) == 1, "Obstacle replanning preserves a single live occupancy footprint");
    }

    /// <summary>tests/fixtures/route-oracle.json: the original 73F5 on a snapshot of the live game (tools/verify_snapshot_route.py) gives the same partial paths for far goals as the port with the admission of the skirmish.</summary>
    private void RouteOracleChecks()
    {
        using var battle = CreateSkirmish(); battle.ProvisionalGuidance = false;
        var u = battle.Actors.First(a => a.Team == 0 && a.Source.TemplateName == "Ополченец");
        using var reference = GameData.Read<System.Text.Json.JsonDocument>("res://tests/fixtures/route-oracle.json");
        var search = new OriginalRouteSearch(); int matched = 0, total = 0;
        foreach (var c in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            var goal = c.GetProperty("goal"); total++;
            var r = search.Find((byte)battle.Map.Width, (byte)battle.Map.Height, 1, 1, new(21, 29), new((byte)goal[0].GetInt32(), (byte)goal[1].GetInt32()), (l, t, d) => battle.CanStand(u, new Godot.Vector2I(l, t)));
            if (string.Concat(r.Directions) == c.GetProperty("directions").GetString()) matched++;
        }
        Check(matched == total && total == 4, $"Far-goal partial paths equal the original's on the live snapshot ({matched} of {total})");
    }
}
