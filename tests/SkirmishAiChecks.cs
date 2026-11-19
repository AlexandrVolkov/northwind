using System.Buffers.Binary;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Mode "skirmish-ai": the enemy settlement driven by the original AI (scripts/SkirmishAi.cs). In peace the garrison
    /// only guards its own buildings; after the player declares war it raises military requests and sends its units into battle.</summary>
    private void SkirmishAiChecks()
    {
        using var battle = CreateSkirmish();
        void Run(double seconds) { for (int t = 0; t < (int)(seconds * 18.2); t++) battle.Advance(1 / 18.2); }
        var enemy = battle.Actors.Where(a => a.Team == 1 && !a.Structure).ToArray();
        var start = enemy.ToDictionary(a => a.Id, a => a.Cell);
        Run(120);
        var ai = battle.EnemyAiBlock.ToArray();
        Check(BinaryPrimitives.ReadUInt32LittleEndian(ai.AsSpan(1)) > 2000, "Enemy AI turns once per game tick");
        Check(BinaryPrimitives.ReadUInt32LittleEndian(ai.AsSpan(0x19e9)) > 0, "Enemy AI census counts its army strength");
        var castle0 = battle.Actors.First(a => a.Team == 1 && a.Source.TemplateName == "Замок");
        Check(Enumerable.Range(0, ai[0x9df]).All(k => ai[0x9e0 + k * 0x52 + 2] == 4), "In peace the garrison (209B0) only sets guards on its own buildings (state 4)");
        Check(enemy.All(a => a.Target == 0 && a.Record[0x14] is not (2 or 3) && (a.Record[0x14] & 0xf8) is not (0x50 or 0x58)), "In peace the enemy attacks nobody");
        Check(enemy.All(a => (a.Cell - castle0.Cell).LengthSquared() <= 15 * 15), "In peace the enemy units stay at home");

        var castle = battle.Actors.First(a => a.Team == 1 && a.Source.TemplateName == "Замок");
        var mine = battle.Actors.Where(a => a.Team == 0 && !a.Structure && !a.Worker).Select(a => a.Id).ToArray();
        battle.Attack(mine, castle.Id); battle.Attack(mine, castle.Id); battle.Stop(mine);
        Check(battle.AtWar(battle.Hero, castle), "The player declared war");
        bool squad = false, commanded = false;
        for (int second = 0; second < 150 && !(squad && commanded); second += 5)
        {
            Run(5);
            squad |= battle.EnemySquadCount > 0;
            commanded |= enemy.Any(a => a.Alive && a.Record[0x14] is 1 or 2 or 3 or >= 0x48);
        }
        ai = battle.EnemyAiBlock.ToArray();
        Check(Enumerable.Range(0, 20).Any(k => ai[0x1ab + k * 0x15] != 0xff), "At war the enemy AI raises military requests");
        Check(squad, "At war the enemy AI has a squad");
        Check(commanded, "The squad's members carry out move or attack commands (+14 activity 1..3)");
        ai = battle.EnemyAiBlock.ToArray();
        Check(Enumerable.Range(0, 20).Any(k => ai[0x1ab + k * 0x15] is 7 or 8 or 9), "At war the AI plans a campaign, an army or a chase (requests 7/8/9)");
        // The three militia guard the castle (state 4, priority FEh), so the military requests find no free units and order
        // new ones, which the economy does not deliver yet. The guards fight what comes near the castle.
        battle.Move(mine, castle0.Cell + new Vector2I(4, 4));
        bool fought = false;
        for (int second = 0; second < 300 && !fought; second += 5)
        {
            Run(5);
            fought = enemy.Any(a => !a.Alive || a.Target != 0 || a.Record[0x14] is 2 or 3);
        }
        Check(fought, "The castle guards attack the player's army near the castle");
        GD.Print($"Skirmish AI: squads {battle.EnemySquadCount}, enemy alive {enemy.Count(a => a.Alive)}");
    }
}
