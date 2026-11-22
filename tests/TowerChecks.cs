using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>A player's tower (built from the factory list) shoots the enemy that comes into its sight.</summary>
    private void TowerChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json");
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        battle.GrantPeople(10);
        SkirmishActor? tower = null;
        for (int r = 4; r < 14 && tower == null; r++)
            for (int dy = -r; dy <= r && tower == null; dy++)
                for (int dx = -r; dx <= r && tower == null; dx++)
                {
                    var cell = battle.PlayerCastle.Cell + new Vector2I(dx, dy);
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && battle.ProbeIndex(13, cell).Status == ConstructionPlacementStatus.Clear) tower = battle.BuildIndex(13, cell);
                }
        Check(tower != null && tower.Structure && tower.Source.TemplateName == "Башня", "The player builds a tower (a structure with an attack)");
        for (int t = 0; t < (int)(400 * 18.2) && tower!.ConstructionRemaining > 0; t++) battle.Advance(1 / 18.2);
        var foe = battle.Actors.First(a => a.Team == 1 && a.Alive && !a.Structure && !a.Worker);
        var near = tower!.Cell + new Vector2I(0, 5);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(foe.Record.AsSpan(4), (uint)(near.X * 32 + 16)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(foe.Record.AsSpan(8), (uint)(near.Y * 32 + 16));
        int before = foe.Health, arrows = 0;
        for (int t = 0; t < 30 * 18; t++) { battle.Advance(1 / 18.2); arrows = Math.Max(arrows, battle.Arrows.Count); if (!foe.Alive) break; }
        Check(arrows > 0 || foe.Health < before || !foe.Alive, $"The tower shoots an enemy in sight (arrows in flight {arrows}, enemy {before} → {foe.Health})");
    }
}
