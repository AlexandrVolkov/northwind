using System.Buffers.Binary;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Mode "skirmish-ai": the enemy settlement driven by the original AI (scripts/SkirmishAi.cs). In peace it builds a
    /// barracks, hires units with its own resources, guards its buildings and patrols; it attacks nobody. After the player
    /// declares war it raises military requests, and its guards fight the player's army at the castle.</summary>
    private void SkirmishAiChecks()
    {
        using var battle = CreateSkirmish();
        void Run(double seconds) { for (int t = 0; t < (int)(seconds * 18.2); t++) battle.Advance(1 / 18.2); }
        var start = battle.EnemyResources;
        int units0 = battle.Actors.Count(a => a.Team == 1 && a.Alive && !a.Structure);
        Run(150);
        var ai = battle.EnemyAiBlock.ToArray();
        Check(BinaryPrimitives.ReadUInt32LittleEndian(ai.AsSpan(1)) > 2000, "Enemy AI turns once per game tick");
        Check(BinaryPrimitives.ReadUInt32LittleEndian(ai.AsSpan(0x19e9)) > 0, "Enemy AI census counts its army strength");
        Check(battle.Actors.Any(a => a.Team == 1 && a.Source.TemplateName == "Казарма"), "The enemy AI orders a barracks (227C7 mode 5 → 3, castle command 58h)");
        Check(battle.EnemyResources.Third < start.Third, "The enemy pays from its own resources (participant +18..+24)");
        Check(battle.Actors.Count(a => a.Team == 1 && a.Alive && !a.Structure) > units0, "The barracks hires units for the enemy (command 60h)");
        Check(Enumerable.Range(0, ai[0x9df]).All(k => ai[0x9e0 + k * 0x52 + 2] is 4 or 8), "In peace the garrison sets guards (state 4) and patrols (state 8)");
        var enemy = battle.Actors.Where(a => a.Team == 1 && !a.Structure).ToArray();
        Check(enemy.All(a => a.Target == 0 && a.Record[0x14] is not (2 or 3)), "In peace the enemy attacks nobody");

        var castle0 = battle.Actors.First(a => a.Team == 1 && a.Source.TemplateName == "Замок");
        var mine = battle.Actors.Where(a => a.Team == 0 && !a.Structure && !a.Worker).Select(a => a.Id).ToArray();
        battle.Attack(mine, castle0.Id); battle.Attack(mine, castle0.Id); battle.Stop(mine);
        Check(battle.AtWar(battle.Hero, castle0), "The player declared war");
        bool requests = false;
        for (int second = 0; second < 150 && !requests; second += 5)
        {
            Run(5);
            ai = battle.EnemyAiBlock.ToArray();
            requests = Enumerable.Range(0, 20).Any(k => ai[0x1ab + k * 0x15] is 7 or 8 or 9);
        }
        Check(requests, "At war the AI plans a campaign, an army or a chase (requests 7/8/9)");
        battle.Move(mine, castle0.Cell + new Vector2I(4, 4));
        bool fought = false;
        for (int second = 0; second < 300 && !fought; second += 5)
        {
            Run(5);
            fought = battle.Actors.Any(a => a.Team == 1 && !a.Structure && (!a.Alive || a.Target != 0 || a.Record[0x14] is 2 or 3));
        }
        Check(fought, "The enemy fights the player's army near the castle");
        GD.Print($"Skirmish AI: squads {battle.EnemySquadCount}, enemy units {battle.Actors.Count(a => a.Team == 1 && a.Alive && !a.Structure)}, resources {battle.EnemyResources}");
    }
}
