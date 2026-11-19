using System.Buffers.Binary;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Mode "skirmish-ai": the enemy settlement driven by the original AI (scripts/SkirmishAi.cs). Without contact or war
    /// it gives no orders; after the player declares war it forms a squad and sends its units into battle.</summary>
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
        Check(battle.EnemySquadCount == 0 && Enumerable.Range(0, 20).All(k => ai[0x1ab + k * 0x15] == 0xff), "Without war the enemy AI raises no military requests");
        Check(enemy.All(a => a.Cell == start[a.Id]), "Without war the enemy units stay at home");

        var castle = battle.Actors.First(a => a.Team == 1 && a.Source.TemplateName == "Замок");
        var mine = battle.Actors.Where(a => a.Team == 0 && !a.Structure && !a.Worker).Select(a => a.Id).ToArray();
        battle.Attack(mine, castle.Id); battle.Attack(mine, castle.Id); battle.Stop(mine);
        Check(battle.AtWar(battle.Hero, castle), "The player declared war");
        bool squad = false, commanded = false;
        for (int second = 0; second < 150 && !(squad && commanded); second += 5)
        {
            Run(5);
            squad |= battle.EnemySquadCount > 0;
            commanded |= enemy.Any(a => a.Alive && (a.Record[0x14] & 0xf8) is 0x48 or 0x50 or 0x58);
        }
        ai = battle.EnemyAiBlock.ToArray();
        Check(Enumerable.Range(0, 20).Any(k => ai[0x1ab + k * 0x15] != 0xff), "At war the enemy AI raises military requests");
        Check(squad, "At war the enemy AI forms a squad");
        Check(commanded, "The squad gives its members move or attack commands");
        Run(60);
        Check(enemy.Any(a => !a.Alive || (a.Cell - start[a.Id]).LengthSquared() > 25), "Enemy units leave home to fight");
        GD.Print($"Skirmish AI: squads {battle.EnemySquadCount}, enemy alive {enemy.Count(a => a.Alive)}");
    }
}
