using Godot;
namespace Northwind;

/// <summary>The port's own AI logic (the default; the original's ported logic is chosen in the settings). It sits on top of the ported
/// economy, construction and research and changes how the army is used: the original's squads hold buildings and wait for a
/// campaign that can stall (see docs/tech.md); the improved logic sends the army out in waves.
///
/// A wave: when a settlement is at war and has enough soldiers (siege engines included), most of them are taken out of the squads
/// (<see cref="SkirmishActor.Detached"/>; the squad machinery no longer sees them) and sent against the nearest enemy building,
/// then the next one; machines and melee units go together. Once the wave has thinned out, or nobody is at war, the survivors
/// return to the squads. A fifth of the soldiers always stays at home as the guard.</summary>
public sealed partial class Skirmish
{
    private readonly Dictionary<int, double> _waveCooldown = [];
    private readonly Dictionary<int, double> _waveNext = [];
    private readonly Dictionary<int, int> _waveSize = [];

    /// <summary>Soldiers needed to launch a wave; machines count double.</summary>
    private const int WaveStrength = 14;

    private void ImprovedStrategy(int slot)
    {
        if (Elapsed < _waveNext.GetValueOrDefault(slot)) return;
        _waveNext[slot] = Elapsed + 2;
        var mine = Actors.Where(a => a.Alive && !a.Structure && !a.Worker && a.Record[3] == slot).ToList();
        var wave = mine.Where(a => a.Detached).ToList();
        var foes = Enumerable.Range(0, Diplomacy.Count).Where(o => o != slot && Diplomacy.AtWar(slot, o)).ToArray();
        var targets = Actors.Where(a => a.Alive && a.Structure && foes.Contains(a.Record[3])).ToList();
        if (foes.Length == 0 || targets.Count == 0 || (wave.Count > 0 && wave.Count * 3 < _waveSize.GetValueOrDefault(slot)))
        {
            foreach (var unit in wave) { unit.Detached = false; unit.Target = 0; unit.Goal = null; unit.Route.Clear(); }
            if (wave.Count > 0) _waveCooldown[slot] = Elapsed + 90;
            if (foes.Length == 0 || targets.Count == 0 || wave.Count > 0) return;
        }
        if (wave.Count == 0)
        {
            if (Elapsed < _waveCooldown.GetValueOrDefault(slot)) return;
            var ready = mine.Where(a => !a.Detached && a.Parameters[3] > 0).ToList();
            int strength = ready.Sum(a => a.Parameters[0x76] >= 50 ? 2 : 1);
            if (strength < WaveStrength) return;
            int take = Math.Max(WaveStrength / 2, ready.Count * 4 / 5);
            // The nearest first: the soldiers closest to the first target go.
            var first = NearestTarget(ready.Average(a => a.Position.X), ready.Average(a => a.Position.Y), targets);
            foreach (var unit in ready.OrderBy(a => a.Position.DistanceSquaredTo(first.Position)).Take(take)) { unit.Detached = true; unit.Record[0x1b] = 0; wave.Add(unit); }
            _waveSize[slot] = wave.Count;
            Log($"ИИ (участник {slot}) выступает в поход: {wave.Count} бойцов.");
        }
        // Targets of the wave: the nearest building to the group's centre; each unit without a living target attacks it.
        var centre = new Vector2(wave.Average(a => a.Position.X), wave.Average(a => a.Position.Y));
        var goal = NearestTarget(centre.X, centre.Y, targets);
        // The route search of the port is local, so the wave follows a coarse path over the whole map (cells a soldier can stand on,
        // bridges included), recomputed every 20 s, and each walker goes to the point ten cells further along it.
        if (!_wavePath.TryGetValue(slot, out var cached) || cached.Goal != goal.Id || Elapsed > cached.Until)
        {
            var sample = wave[0];
            _wavePath[slot] = cached = (goal.Id, Elapsed + 20, CoarsePath(sample, sample.Cell, goal.Cell, 14));
        }
        var path = cached.Path;
        foreach (var unit in wave)
        {
            var aim = Find(unit.Target);
            if (aim != null && aim.Alive) continue;                       // engaged (or walking the last stretch to its target)
            unit.Target = 0;
            if (Math.Max(Math.Abs(goal.Cell.X - unit.Cell.X), Math.Abs(goal.Cell.Y - unit.Cell.Y)) <= 16) { unit.Target = goal.Id; unit.Goal = null; unit.Route.Clear(); unit.ReplanTimer = 0; continue; }
            if (unit.Stepping || (unit.Goal != null && unit.Route.Count > 0) || path.Count == 0) continue;
            int near = 0; float best = float.MaxValue;
            for (int i = 0; i < path.Count; i++) { float d = ((Vector2)(path[i] - unit.Cell)).LengthSquared(); if (d < best) { best = d; near = i; } }
            var stage = path[Math.Min(path.Count - 1, near + 10)];
            if (!OrderMove(unit, stage, 3)) OrderMove(unit, path[Math.Min(path.Count - 1, near + 4)], 3);
        }
    }

    private readonly Dictionary<int, (uint Goal, double Until, List<Vector2I> Path)> _wavePath = [];

    /// <summary>Breadth-first path over the whole map for a soldier (8 neighbours, no corner cutting through blocked cells) to any cell
    /// within <paramref name="reach"/> of the goal; empty when there is none.</summary>
    private List<Vector2I> CoarsePath(SkirmishActor sample, Vector2I from, Vector2I goal, int reach)
    {
        int w = Map.Width, h = Map.Height;
        var back = new int[w * h]; Array.Fill(back, -2);
        var queue = new Queue<Vector2I>(); queue.Enqueue(from); back[from.Y * w + from.X] = -1;
        var stand = new sbyte[w * h];
        bool Free(Vector2I c) { int i = c.Y * w + c.X; if (stand[i] == 0) stand[i] = (sbyte)(CanStand(sample, c) ? 1 : -1); return stand[i] > 0; }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (Math.Max(Math.Abs(c.X - goal.X), Math.Abs(c.Y - goal.Y)) <= reach)
            {
                var path = new List<Vector2I>();
                for (int i = c.Y * w + c.X; i >= 0; i = back[i]) path.Add(new Vector2I(i % w, i / w));
                path.Reverse(); return path;
            }
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = new Vector2I(c.X + dx, c.Y + dy);
                if (n.X < 0 || n.Y < 0 || n.X >= w || n.Y >= h || back[n.Y * w + n.X] != -2) continue;
                if (!Free(n) || (dx != 0 && dy != 0 && (!Free(new(c.X + dx, c.Y)) || !Free(new(c.X, c.Y + dy))))) continue;
                back[n.Y * w + n.X] = c.Y * w + c.X; queue.Enqueue(n);
            }
        }
        return [];
    }

    private static SkirmishActor NearestTarget(double x, double y, List<SkirmishActor> targets) =>
        targets.OrderBy(t => new Vector2((float)x, (float)y).DistanceSquaredTo(t.Position)).First();
}
