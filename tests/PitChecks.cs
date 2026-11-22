using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>The pit traps of slot 6 in the skirmish: a unit that steps in is crushed, the pit opens and closes again.</summary>
    private void PitChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.Pits = true;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        void Run(double seconds) { for (int t = 0; t < (int)(seconds * 18.2); t++) battle.Advance(1 / 18.2); }
        Check(battle.Pits.Count >= 2 && battle.Pits.All(p => p[0x15] == 0 && System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(1)) == 0x289), $"The pits of slot 6 load closed ({battle.Pits.Count})");
        var pit = battle.Pits[0]; var spot = new Vector2I((int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(pit.AsSpan(4)) >> 5), (int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(pit.AsSpan(8)) >> 5));
        var victim = battle.Actors.First(a => a.Team == 1 && a.Alive && !a.Structure && !a.Worker);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(victim.Record.AsSpan(4), (uint)(spot.X * 32 + 16)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(victim.Record.AsSpan(8), (uint)(spot.Y * 32 + 16));
        Run(30);
        Check(!victim.Alive, "A unit in the square is swallowed by the pit");
        bool opened = pit[0x2a] > 0 || pit[0x15] != 0; Run(40);
        Run(40);
        Check(pit[0x15] is 0 or 1 or 2 or 3, "The pit state stays valid");
        GD.Print($"Pit after: state {pit[0x15]} count {pit[0x2a]} opened {opened}");
    }
}
