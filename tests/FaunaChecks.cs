using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>The wolves and bears of slots 6/7 (docs/fauna.md): neutral team at war with everyone, roam, bite, a bear's death opens the ballista research.</summary>

    /// <summary>The player declares war on Rufin (a repeated attack order) and the forest — his allies — goes to war with him too.</summary>
    private static void DeclareWarOnRufin(Skirmish battle)
    {
        var mage = battle.SceneryObjects.First(Skirmish.IsMage); var hero = battle.Hero;
        battle.Attack([hero.Id], mage.Id); battle.Attack([hero.Id], mage.Id);
        for (int t = 0; t < 20; t++) battle.Advance(1 / 18.2);
        hero.Target = 0; hero.Goal = null; hero.Route.Clear();
    }
    private void FaunaChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.Fauna = true;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        void Run(double seconds) { for (int t = 0; t < (int)(seconds * 18.2); t++) battle.Advance(1 / 18.2); }
        var wolves = battle.Animals.Count(a => a.Source.TemplateName == "Волк"); var bears = battle.Animals.Count(a => a.Source.TemplateName == "Медведь");
        Check(wolves == 39 && bears == 7, $"39 wolves and 7 bears load from slots 6 and 7 ({wolves}, {bears})");
        Check(battle.Animals.All(a => a.Team == 4 && !a.Structure && !a.Worker), "The animals are units of team 4");
        var wolf = battle.Animals.First(a => a.Source.TemplateName == "Волк");
        var hero = battle.Hero; int before = hero.Health;
        Check(!battle.Diplomacy.AtWar(7, 0) && !battle.Diplomacy.AtWar(6, 0), "The beasts are at peace with the player at the start");
        // In peace a wolf next to a worker (who does not fight) leaves him alone; after the war on Rufin it bites.
        battle.GrantPeople(5); battle.QueueWorker();
        for (int t = 0; t < (int)(60 * 18.2); t++) battle.Advance(1 / 18.2);
        var worker = battle.Actors.First(x => x.Team == 0 && x.Worker && x.Alive); int workerBefore = worker.Health;
        // At the wolf's own place, away from the soldiers who would strike a wolf in peace too (the beasts bear the DAT flag of a target for all).
        var wolfCell = wolf.Cell; var cell = wolfCell + new Vector2I(-1, 0);
        void Place(SkirmishActor a, Vector2I c) { System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a.Record.AsSpan(4), (uint)(c.X * 32 + 16)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a.Record.AsSpan(8), (uint)(c.Y * 32 + 16)); }
        Place(worker, cell); worker.Goal = null; worker.Route.Clear();
        Run(20);
        Check(worker.Health == workerBefore && wolf.Alive, "In peace the wolf does not bite");
        // In peace the hero does not strike a wolf that stands beside him either (the beasts are Rufin's, user 23.11).
        Vector2I? spot = null;
        for (int r = 6; r < 16 && spot == null; r++)
            for (int dx = -r; dx <= r && spot == null; dx++) { var c = battle.PlayerCastle.Cell + new Vector2I(dx, r); if (battle.Inside(c + Vector2I.Right) && battle.CanStand(hero, c) && battle.CanStand(wolf, c + Vector2I.Right)) spot = c; }
        Check(spot != null, "A free place for the hero and a wolf beside him");
        Place(hero, spot!.Value); Place(wolf, spot.Value + Vector2I.Right); hero.Goal = null; hero.Route.Clear(); hero.Target = 0;
        int heroBefore = hero.Health; bool struck = false;
        for (int t = 0; t < 30 * 18; t++) { battle.Advance(1 / 18.2); struck |= hero.Target == wolf.Id || wolf.Target == hero.Id; }
        Check(!struck && wolf.Alive && hero.Health == heroBefore, "In peace the hero and the wolf beside him leave each other alone");
        DeclareWarOnRufin(battle);
        Check(battle.Diplomacy.AtWar(7, 0) && battle.Diplomacy.AtWar(6, 0), "War on Rufin is war with the beasts of both slots");
        foreach (var d in new[] { Vector2I.Right, Vector2I.Left, Vector2I.Down, Vector2I.Up }) if (battle.CanStand(wolf, hero.Cell + d)) { Place(wolf, hero.Cell + d); break; }
        wolf.Goal = null; wolf.Route.Clear(); wolf.Stepping = false;
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

public partial class TestRunner
{
    /// <summary>Animals in the forest go for units that come near; soldiers answer animals; a walking unit that is struck stops, fights, then goes on.</summary>
    private void FaunaBehaviourChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.Fauna = true;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        void Run(double seconds) { for (int t = 0; t < (int)(seconds * 18.2); t++) battle.Advance(1 / 18.2); }
        var hero = battle.Hero;
        DeclareWarOnRufin(battle);
        // The nearest wolf in the forest, hero placed 8 cells from it on a standable cell.
        var wolf = battle.Animals.First(a => a.Source.TemplateName == "Волк" && battle.InForest(a));
        Vector2I? spot = null;
        for (int r = 6; r <= 9 && spot == null; r++)
            for (int dx = -r; dx <= r && spot == null; dx++)
                { var c = wolf.Cell + new Vector2I(dx, r); if (battle.Inside(c) && battle.CanStand(hero, c) && !battle.InForestCell(c)) spot = c; }
        Check(spot != null, "A free cell 6..9 cells from a forest wolf exists");
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(4), (uint)(spot!.Value.X * 32 + 16)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(8), (uint)(spot.Value.Y * 32 + 16));
        Run(10);
        Check(wolf.Target == hero.Id || !wolf.Alive || wolf.Cell != new Vector2I((int)(wolf.Source.RawX >> 5), (int)(wolf.Source.RawY >> 5)), "A forest wolf goes for a unit that comes near");
        // A walking hero that is struck stops, fights the wolf, then resumes the walk.
        var goal = hero.Cell + new Vector2I(10, 0);
        if (!battle.CanStand(hero, goal)) goal = hero.Cell + new Vector2I(-10, 0);
        battle.Move([hero.Id], goal);
        Run(0.6);
        var biter = battle.Animals.First(a => a.Alive && a.Source.TemplateName == "Волк");
        var next = hero.Cell + new Vector2I(0, 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(biter.Record.AsSpan(4), (uint)(next.X * 32 + 16)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(biter.Record.AsSpan(8), (uint)(next.Y * 32 + 16));
        biter.Target = hero.Id;
        int engaged = 0; for (int t = 0; t < 40 * 18; t++) { battle.Advance(1 / 18.2); if (hero.Target == biter.Id) engaged++; }
        Check(engaged > 0 || !biter.Alive, $"The struck hero engages the animal (target for {engaged} ticks)");
        Run(60);
        Check(Math.Max(Math.Abs(hero.Cell.X - goal.X), Math.Abs(hero.Cell.Y - goal.Y)) <= 3 || hero.ResumeGoal != null || hero.Goal != null, $"After the fight the hero goes on to {goal} (now {hero.Cell})");
    }
}

public partial class TestRunner
{
    /// <summary>The bird generators of the first map keep up to four birds each; the birds fly about and are neutral.</summary>
    private void BirdChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.Fauna = true;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        Check(battle.Generators.Count == 6 && battle.Birds.Count == 1, $"Six generators and one bird load ({battle.Generators.Count}, {battle.Birds.Count})");
        for (int t = 0; t < (int)(600 * 18.2); t++) battle.Advance(1 / 18.2);
        int alive = battle.Birds.Count(b => b.Alive);
        Check(alive > 1 && alive <= 24, $"The generators make birds, at most four per generator ({alive})");
        Check(battle.Birds.All(b => b.Record[3] == 5 && b.Team == 4 && Skirmish.IsBird(b) && !Skirmish.IsHiddenAnimal(b)), "Birds are neutral and drawn");
        var bird = battle.Birds.First(b => b.Alive); var start = bird.Cell;
        for (int t = 0; t < 30 * 18; t++) battle.Advance(1 / 18.2);
        Check(battle.Birds.Any(b => b.Alive && (b.Stepping || b.Goal != null || b.Cell != new Vector2I((int)(b.Source.RawX >> 5), (int)(b.Source.RawY >> 5)))), "Birds fly about");
        Check(!battle.Actors.Any(a => a.Team == 0 && a.Alive && a.Target != 0 && battle.Find(a.Target) is { } t && Skirmish.IsBird(t)), "Nobody attacks the birds");
    }
}

public partial class TestRunner
{
    /// <summary>The decorations of slots 6 and 7 load as neutral scenery and survive a few minutes.</summary>
    private void SceneryChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.Fauna = true;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        Check(battle.SceneryObjects.Count == 7, $"Seven decorations load: jugs, a chest, a manuscript, the mage ({battle.SceneryObjects.Count})");
        Check(battle.SceneryObjects.All(a => a.Scenery && a.Alive && (a.Record[3] == 5 || Skirmish.IsMage(a) && a.Record[3] == 6)), "The decorations are neutral (the mage belongs to slot 6)");
        for (int t = 0; t < (int)(120 * 18.2); t++) battle.Advance(1 / 18.2);
        Check(battle.SceneryObjects.All(a => a.Alive), "The decorations stay standing");
    }
}
