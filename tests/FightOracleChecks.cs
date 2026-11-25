using System.Buffers.Binary;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>tests/fixtures/fight-oracle.json (tools/make_fight_oracle.py): two hostile militiamen on open ground, the original unit script against the skirmish: positions, health and states tick for tick.</summary>
    private void FightOracleChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/fight-oracle.json");
        int equal = 0, total = 0;
        foreach (var c in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            using var battle = CreateSkirmish(); battle.AiEnabled = false; battle.OriginalEngagement = true; int gap = c.GetProperty("gap").GetInt32(); total++;
            var a = battle.Actors.First(x => x.Team == 0 && !x.Structure && !x.Hero && x.Source.TemplateName == "Ополченец");
            var b = battle.Actors.First(x => x.Team == 1 && !x.Structure && x.Source.TemplateName == "Ополченец");
            var properties = Convert.FromHexString(battle.Map.SegmentPropertiesHex);
            uint ground = (uint)Enumerable.Range(0, properties.Length / 8).First(i => properties[i * 8] == 0 && battle.Map.Terrain[i] == "ground");
            Array.Fill(battle.Map.Cells, ground); battle.Occupancy.LoadCells(new uint[battle.Occupancy.Cells.Length]);
            foreach (var x in battle.Actors.Where(x => x != a && x != b && x != battle.Hero)) x.Health = 0;
            battle.Objective.Health = 100;
            void Place(SkirmishActor u, int cx) { BinaryPrimitives.WriteUInt32LittleEndian(u.Record.AsSpan(4), (uint)(cx * 32 + 16)); BinaryPrimitives.WriteUInt32LittleEndian(u.Record.AsSpan(8), 50 * 32 + 16); u.Record[0x2d] = 0; u.Record[0x14] = 0; u.Record[0x15] = 0; u.Record[0x2e] = 0; u.Record[0x25] = (byte)(u == a ? 2 : 6); }
            Place(a, 50); Place(b, 50 + gap);
            foreach (var u in new[] { a, b }) battle.Occupancy.WriteFootprint(u.Parameters[3], u.Parameters[4], u.Record[0x0d], BinaryPrimitives.ReadUInt32LittleEndian(u.Record.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(u.Record.AsSpan(8)), u.Id);
            battle.Diplomacy.Hostile[a.Record[3]] |= 1u << b.Record[3]; battle.Diplomacy.Hostile[b.Record[3]] |= 1u << a.Record[3];
            int tick = 0, first = -1; string detail = "";
            foreach (var t in c.GetProperty("ticks").EnumerateArray())
            {
                if ((sbyte)t[1].GetInt32() <= 0 || (sbyte)t[4].GetInt32() <= 0) break;   // after a death the original leaves the corpse in place while the skirmish removes it
                battle.Advance(1 / 18.2);
                var ea = $"{(uint)a.Position.X},{(uint)a.Position.Y},{a.Record[0x19]},{a.Record[0x2d]}|{(uint)b.Position.X},{(uint)b.Position.Y},{b.Record[0x19]},{b.Record[0x2d]}";
                var oa = t[0]; var ob = t[3];
                var want = $"{oa[0]},{oa[1]},{t[1]},{t[2]}|{ob[0]},{ob[1]},{t[4]},{t[5]}";
                if (first < 0 && ea != want) { first = tick; detail = $"{ea} vs {want}"; }
                tick++;
            }
            if (first < 0) equal++; else GD.Print($"Fight oracle gap {gap}: first difference at tick {first}: {detail}");
        }
        Check(equal == total && total == 3, $"The skirmish fight equals the original unit script tick for tick until a death ({equal} of {total})");
    }
}
