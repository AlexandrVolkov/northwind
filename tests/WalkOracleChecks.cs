using System.Buffers.Binary;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>tests/fixtures/walk-oracle.json (tools/make_walk_oracle.py): the position after every tick of the original unit script on open ground equals the walk of the skirmish (turn, speed, steps), tick for tick.</summary>
    private void WalkOracleChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/walk-oracle.json");
        int matched = 0, total = 0, compared = 0;
        foreach (var c in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            using var battle = CreateSkirmish(); total++;
            var u = battle.Actors.First(a => a.Team == 0 && !a.Structure && !a.Hero && a.Source.TemplateName == "Ополченец");
            var properties = Convert.FromHexString(battle.Map.SegmentPropertiesHex);
            uint ground = (uint)Enumerable.Range(0, properties.Length / 8).First(i => properties[i * 8] == 0 && battle.Map.Terrain[i] == "ground");
            Array.Fill(battle.Map.Cells, ground); battle.Occupancy.LoadCells(new uint[battle.Occupancy.Cells.Length]);
            foreach (var a in battle.Actors.Where(a => a != u && a != battle.Hero)) a.Health = 0;
            battle.Objective.Health = 100;
            BinaryPrimitives.WriteUInt32LittleEndian(u.Record.AsSpan(4), 50 * 32 + 16); BinaryPrimitives.WriteUInt32LittleEndian(u.Record.AsSpan(8), 50 * 32 + 16);
            u.Record[0x25] = (byte)c.GetProperty("heading").GetInt32();
            var goal = c.GetProperty("goal"); battle.Move([u.Id], new Vector2I(goal[0].GetInt32(), goal[1].GetInt32()));
            bool same = true; int index = 0;
            foreach (var p in c.GetProperty("positions").EnumerateArray())
            {
                battle.Advance(1 / 18.2); compared++;
                if (u.Position.X != p[0].GetUInt32() || u.Position.Y != p[1].GetUInt32()) { same = false; break; }
                index++;
            }
            if (same) matched++; else GD.Print($"Walk oracle: heading {c.GetProperty("heading")} goal {goal} differs at tick {index}: {u.Position}");
        }
        Check(matched == total && total == 12, $"The skirmish walk equals the original unit script tick for tick ({matched} of {total} walks, {compared} ticks)");
    }

    /// <summary>tests/fixtures/walk-oracle-real.json (tools/make_walk_oracle.py ... real): the same on the real terrain of the first map from (21,29) with near, far and blocked goals; the AStar guidance is off.</summary>
    private void WalkOracleRealChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/walk-oracle-real.json");
        int matched = 0, total = 0;
        foreach (var c in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            using var battle = CreateSkirmish(); total++; battle.ProvisionalGuidance = false;
            var u = battle.Actors.First(a => a.Team == 0 && !a.Structure && !a.Hero && a.Source.TemplateName == "Ополченец");
            battle.Occupancy.LoadCells(new uint[battle.Occupancy.Cells.Length]);
            foreach (var a in battle.Actors.Where(a => a != u && a != battle.Hero)) a.Health = 0;
            battle.Objective.Health = 100;
            var start = c.GetProperty("start");
            BinaryPrimitives.WriteUInt32LittleEndian(u.Record.AsSpan(4), (uint)(start[0].GetInt32() * 32 + 16)); BinaryPrimitives.WriteUInt32LittleEndian(u.Record.AsSpan(8), (uint)(start[1].GetInt32() * 32 + 16));
            u.Record[0x25] = (byte)c.GetProperty("heading").GetInt32();
            var goal = c.GetProperty("goal"); battle.Move([u.Id], new Vector2I(goal[0].GetInt32(), goal[1].GetInt32()));
            int index = 0, firstDiff = -1; string detail = "";
            foreach (var p in c.GetProperty("positions").EnumerateArray())
            {
                battle.Advance(1 / 18.2);
                if (firstDiff < 0 && (u.Position.X != p[0].GetUInt32() || u.Position.Y != p[1].GetUInt32())) { firstDiff = index; detail = $"{u.Position} vs {p[0]},{p[1]}"; }
                index++;
            }
            if (firstDiff < 0) matched++; else GD.Print($"Walk oracle real: heading {c.GetProperty("heading")} goal {goal} first difference at tick {firstDiff}: {detail}; end {u.Cell}");
        }
        Check(matched == total && total == 10, $"The skirmish walk equals the original on the real terrain, tick for tick ({matched} of {total} walks of 400 ticks)");
    }
}
