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
        Check(gate!.ConstructionRemaining <= 0 && !gate.GateIsOpen && battle.Occupancy.Cells.Contains(gate.Id), "The finished gate is closed and blocks its cells");
        battle.SetGateMode(gate, 1); battle.Advance(1 / 18.2);
        Check(gate.GateIsOpen && !battle.Occupancy.Cells.Contains(gate.Id), "«Открыть» frees the cells");
        battle.SetGateMode(gate, 0); battle.Advance(1 / 18.2);
        Check(!gate.GateIsOpen && battle.Occupancy.Cells.Contains(gate.Id), "«Закрыть» blocks them again");
        battle.SetGateMode(gate, 2);
        var worker = battle.Hero;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(worker.Record.AsSpan(4), (uint)gate.Position.X); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(worker.Record.AsSpan(8), (uint)gate.Position.Y + 40);
        for (int t = 0; t < 20; t++) battle.Advance(1 / 18.2);
        Check(gate.GateIsOpen, "The automatic gate opens for a unit nearby");
    }
}
