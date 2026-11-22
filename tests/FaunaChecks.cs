using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>The wolves and bears of slots 6/7 (docs/fauna.md): neutral team at war with everyone, roam, bite, a bear's death opens the ballista research.</summary>
    private void FaunaChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.Fauna = true;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        void Run(double seconds) { for (int t = 0; t < (int)(seconds * 18.2); t++) battle.Advance(1 / 18.2); }
        var wolves = battle.Animals.Count(a => a.Source.TemplateName == "Волк"); var bears = battle.Animals.Count(a => a.Source.TemplateName == "Медведь");
        Check(wolves == 39 && bears == 7, $"39 wolves and 7 bears load from slots 6 and 7 ({wolves}, {bears})");
        Check(battle.Animals.All(a => a.Team == 4 && !a.Structure && !a.Worker) && battle.Diplomacy.AtWar(7, 0) && battle.Diplomacy.AtWar(6, 3), "The animals are units of team 4 at war with the settlements");
        var wolf = battle.Animals.First(a => a.Source.TemplateName == "Волк");
        var hero = battle.Hero; int before = hero.Health;
        // A wolf next to the hero bites.
        var cell = hero.Cell + new Vector2I(1, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(wolf.Record.AsSpan(4), (uint)(cell.X * 32 + 16)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(wolf.Record.AsSpan(8), (uint)(cell.Y * 32 + 16));
        Run(40);
        Check(hero.Health < before || !wolf.Alive, $"A wolf and the hero fight (hero {before} → {hero.Health}, wolf alive {wolf.Alive})");
        var bear = battle.Animals.First(a => a.Source.TemplateName == "Медведь");
        var tree = battle.DevelopmentTree(3);
        int ballista = OriginalTechRules.Find(tree, 0, 26);
        Check(ballista >= 0 && !OriginalTechRules.Researchable(tree).Contains(ballista), "The ballista cannot be researched before a bear is found");
        var near = battle.Actors.First(a => a.Team == 1 && a.Alive && !a.Structure && !a.Worker);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bear.Record.AsSpan(4), (uint)near.Position.X); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bear.Record.AsSpan(8), (uint)near.Position.Y);
        bear.Record[0x19] = 0;
        typeof(Skirmish).GetMethod("Kill", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(battle, [bear]);
        Check(OriginalTechRules.Researchable(tree).Contains(ballista), "A bear's death near the enemy opens the research of the ballista (event 200, kind 3)");
        var moved = battle.Animals.Count(a => a.Alive && a.Cell != new Vector2I((int)(a.Source.RawX >> 5), (int)(a.Source.RawY >> 5)));
        Check(moved > 0, $"Animals roam ({moved} moved)");
        battle.Sounds.Clear(); Run(30);
        Check(battle.Sounds.Any(s => s.Bank == "ARMY__05.FX"), "An animal voice is heard (ARMY__05.FX)");
    }
}
