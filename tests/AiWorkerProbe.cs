using Godot;
using Northwind;
public partial class TestRunner
{
    private void AiWorkerProbe()
    {
        using var battle = CreateSkirmish();
        for (int minute = 0; minute <= 40; minute++)
        {
            var r = battle.EnemyResources;
            var enemy = battle.Actors.Where(a => a.Team == 1 && a.Alive).GroupBy(a => a.Source.TemplateName).Select(g => g.Key + g.Count());
            var workers = battle.Actors.Where(a => a.Team == 1 && a.Alive && a.Worker).Select(a => $"{a.Record[0x14]:x}/{a.Record[0x31]}/{a.Record[0x1b]}");
            var ai = battle.EnemyAiBlock;
            var sq = new List<string>();
            for (int k = 0; k < ai[OriginalAiSquads.List]; k++) { int at = OriginalAiSquads.Record(k); sq.Add($"st{ai[at + 2]}.{ai[at + 5]}n{ai[at + 0x22]}"); }
            GD.Print($"min {minute} res {r.First}/{r.Second}/{r.Third}/{r.Fourth} sq [{string.Join(' ', sq)}] w [{string.Join(' ', workers)}] {string.Join(',', enemy)}");
            for (int t = 0; t < 60 * 18; t++) battle.Advance(1 / 18.2);
        }
    }
}
