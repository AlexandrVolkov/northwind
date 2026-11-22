using Godot;
namespace Northwind;

public enum WorkerJobKind { Timber, Mine }
/// <summary>The last work order a worker was given (user, 22.11): the tree cell of a felling job or the mine of a mining job.</summary>
public sealed record WorkerJob(WorkerJobKind Kind, Vector2I Cell, uint Id);

/// <summary>Traffic around the buildings that work with units (docs/flow.md): units never stand in the way of a mine, a sawmill, the castle or a producer; a
/// produced unit steps away from the exit; a full exit is cleared by moving the unit that blocks it, one at a time; a worker that could not carry out its last
/// order tries it again after a pause. All of it is port adapters, not recovered original logic.</summary>
public sealed partial class Skirmish
{
    private static readonly string[] ServiceNames = ["Замок", "Казарма", "Лесопилка", "Шахта", "Завод", "Конюшня", "Шатер"];
    private readonly Dictionary<Vector2I, int> _serviceCells = [];
    private double _serviceBuiltAt = -10; private int _serviceActors = -1;
    public const double WorkerRetrySeconds = 6;

    /// <summary>The ring of one cell around the footprint of each building that serves units: workers go to its sides, recruits come out of it.</summary>
    private void EnsureServiceZones()
    {
        if (Elapsed - _serviceBuiltAt < 1 && _serviceActors == Actors.Count) return;
        _serviceBuiltAt = Elapsed; _serviceActors = Actors.Count; _serviceCells.Clear();
        foreach (var b in Actors)
        {
            if (!b.Alive || !b.Structure || Array.IndexOf(ServiceNames, b.Source.TemplateName) < 0) continue;
            int fw = b.Parameters[3], fh = b.Parameters[4], left = b.Cell.X - fw / 2, top = b.Cell.Y - fh / 2;
            for (int y = top - 1; y <= top + fh; y++)
                for (int x = left - 1; x <= left + fw; x++)
                    if (x < left || x >= left + fw || y < top || y >= top + fh) _serviceCells[new(x, y)] = b.Team;
        }
    }
    /// <summary>The cell lies in the ring of a service building of the team.</summary>
    public bool IsServiceCell(Vector2I cell, int team) { EnsureServiceZones(); return _serviceCells.TryGetValue(cell, out var owner) && owner == team; }

    /// <summary>A unit that is doing nothing: it may be moved out of the way. Workers busy with a job (felling, mining, carrying) are not idle.</summary>
    private static bool IdleForFlow(SkirmishActor a) =>
        a.Alive && !a.Structure && !a.Stepping && a.Goal == null && a.Target == 0 && a.Route.Count == 0
        && (!a.Worker || (a.GatherTree == null && !a.MineJob && a.Record[0x31] == 0 && a.PreferredMill == 0));

    /// <summary>The nearest cell to <paramref name="from"/> (at least <paramref name="minDistance"/> away from <paramref name="avoid"/>) where the unit can stand outside every service ring.</summary>
    private Vector2I? ParkingCell(SkirmishActor actor, Vector2I from, int radius, Vector2I? avoid = null, int minDistance = 0)
    {
        EnsureServiceZones();
        for (int r = 1; r <= radius; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    var c = from + new Vector2I(dx, dy);
                    if (!Inside(c) || _serviceCells.ContainsKey(c) || !CanStand(actor, c) || _reservations.ContainsKey((c, actor.Record[0x0d] & 3))) continue;
                    if (avoid is { } a && Math.Max(Math.Abs(c.X - a.X), Math.Abs(c.Y - a.Y)) < minDistance) continue;
                    return c;
                }
        return null;
    }

    private int _flowSweep;
    /// <summary>Once a second idle units that stand in the ring of their own service building walk to the nearest free cell outside every ring (three per sweep).</summary>
    private void TickFlow()
    {
        if (!ImprovedAi || (++_flowSweep % 18) != 0) return;
        EnsureServiceZones();
        int moved = 0;
        foreach (var unit in Actors.ToArray())
        {
            if (moved >= 3) break;
            if (!IdleForFlow(unit) || !_serviceCells.TryGetValue(unit.Cell, out var owner) || owner != unit.Team || IsAnimal(unit)) continue;
            if (ParkingCell(unit, unit.Cell, 8) is { } spot && OrderMove(unit, spot, 1)) moved++;
        }
    }

    /// <summary>A produced unit walks off the exit to a free cell a few steps away (and not into a ring) instead of standing in the doorway.</summary>
    private void RecruitStepsAway(SkirmishActor recruit, SkirmishActor producer)
    {
        if (ParkingCell(recruit, recruit.Cell, 7, recruit.Cell, 3) is { } spot) OrderMove(recruit, spot, 1);
    }

    /// <summary>The exit is full: the idle unit that stands nearest to it walks away to a free cell at least three cells from the exit — one unit at a time,
    /// each retry of the producer moves the next.</summary>
    private void ClearExit(SkirmishActor producer, TemplateEntry template)
    {
        var exit = RecruitExit(producer, template); var cell = new Vector2I((int)(exit.X >> 5), (int)(exit.Y >> 5));
        var blocker = Actors.Where(a => a.Team == producer.Team && IdleForFlow(a) && Math.Max(Math.Abs(a.Cell.X - cell.X), Math.Abs(a.Cell.Y - cell.Y)) <= 2)
            .OrderBy(a => (a.Cell - cell).LengthSquared()).FirstOrDefault();
        if (blocker == null) return;
        if (ParkingCell(blocker, cell, 12, cell, 3) is { } spot) OrderMove(blocker, spot, 1);
    }

    /// <summary>A worker that has no job any more keeps its last order: after a pause it takes it up again (a felled grove → the nearest trees, a mine that was
    /// full or busy → the same mine). A move order or Stop forgets the job.</summary>
    private void WorkerRetryTick(SkirmishActor worker)
    {
        if (worker.Team != 0 || worker.LastJob is not { } job) return;
        bool idle = !worker.Stepping && worker.Goal == null && worker.Route.Count == 0 && worker.GatherTree == null && !worker.MineJob && worker.Record[0x31] == 0 && worker.Target == 0 && worker.PreferredMill == 0;
        if (!idle) { worker.IdleSince = -1; return; }
        if (worker.IdleSince < 0) { worker.IdleSince = Elapsed; return; }
        if (Elapsed - worker.IdleSince < WorkerRetrySeconds) return;
        worker.IdleSince = Elapsed;
        if (job.Kind == WorkerJobKind.Mine) { WorkMine([worker.Id], job.Id); return; }
        var tree = job.Cell;
        if (!Harvestable(tree))
        {
            Vector2I? near = null;
            for (int r = 1; r <= 14 && near == null; r++)
                for (int dy = -r; dy <= r && near == null; dy++)
                    for (int dx = -r; dx <= r && near == null; dx++)
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && Harvestable(job.Cell + new Vector2I(dx, dy))) near = job.Cell + new Vector2I(dx, dy);
            if (near == null) return;
            tree = near.Value;
        }
        GatherTimber([worker.Id], tree);
    }
}
