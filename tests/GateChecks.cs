using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>A gate built from the factory list: closed it blocks its cells, open it frees them, automatic it opens for the player's units (docs/gates.md).</summary>
    private void GateChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json");
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        battle.GrantPeople(10);
        SkirmishActor? gate = null;
        for (int r = 4; r < 14 && gate == null; r++)
            for (int dy = -r; dy <= r && gate == null; dy++)
                for (int dx = -r; dx <= r && gate == null; dx++)
                {
                    var cell = battle.PlayerCastle.Cell + new Vector2I(dx, dy);
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && battle.ProbeIndex(14, cell).Status == ConstructionPlacementStatus.Clear) gate = battle.BuildIndex(14, cell);
                }
        Check(gate != null && gate.Source.TemplateName == "Ворота", "The player builds a gate");
        for (int t = 0; t < (int)(400 * 18.2) && gate!.ConstructionRemaining > 0; t++) battle.Advance(1 / 18.2);
        Check(gate!.ConstructionRemaining <= 0, "The gate is built");
        for (int t = 0; t < 2; t++) battle.Advance(1 / 18.2);
        // a finished gate starts in the automatic mode (WRK 0xDE5); switch to closed to test the commands
        battle.SetGateMode(gate, 0); for (int t = 0; t < 5; t++) battle.Advance(1 / 18.2);
        Check(!battle.GateIsOpen(gate) && battle.Occupancy.Cells.Contains(gate.Id) && gate.Record[0x22] == 0, "The closed gate is in plane 1, not lifted, and blocks its cells");
        battle.SetGateMode(gate, 1);
        int lifted = 0;
        for (int t = 0; t < 40; t++) { battle.Advance(1 / 18.2); lifted = Math.Max(lifted, (int)battle.GateLift(gate)); }
        Check(lifted > 0, "«Открыть» raises the gate step by step");
        for (int t = 0; t < 32 * 4 + 10; t++) battle.Advance(1 / 18.2);
        Check(battle.GateIsOpen(gate) && gate.Record[0x22] == 0x20 && gate.Record[0x0d] == 2, "The open gate stands at 32 pixels in plane 2");
        battle.SetGateMode(gate, 0);
        for (int t = 0; t < 32 * 4 + 10; t++) battle.Advance(1 / 18.2);
        Check(!battle.GateIsOpen(gate) && gate.Record[0x22] == 0 && gate.Record[0x0d] == 1, "«Закрыть» lowers it back to plane 1");
        battle.SetGateMode(gate, 2);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(battle.Hero.Record.AsSpan(4), (uint)gate.Position.X); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(battle.Hero.Record.AsSpan(8), (uint)gate.Position.Y + 48);
        for (int t = 0; t < 32 * 5 + 32 * 4; t++) battle.Advance(1 / 18.2);
        Check(battle.GateIsOpen(gate), "The automatic gate opens for a unit nearby");
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(battle.Hero.Record.AsSpan(4), (uint)gate.Position.X + 2000);
        for (int t = 0; t < 32 * 5 + 32 * 4; t++) battle.Advance(1 / 18.2);
        Check(!battle.GateIsOpen(gate), "and closes once the unit is gone");
    }
}
