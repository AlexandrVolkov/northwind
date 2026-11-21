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
        foreach (var unit in wave)
        {
            var aim = Find(unit.Target);
            if (aim != null && aim.Alive) continue;                       // engaged (or walking the last stretch to its target)
            unit.Target = 0;
            var offset = goal.Cell - unit.Cell; double distance = offset.Length();
            if (distance <= 16) { unit.Target = goal.Id; unit.Goal = null; unit.Route.Clear(); unit.ReplanTimer = 0; continue; }
            if (unit.Stepping || (unit.Goal != null && unit.Route.Count > 0)) continue;
            // Long marches go by stages: the route search only reaches a few dozen cells; the walkers also answer enemies on the way.
            var forward = (Vector2)offset / (float)distance;
            bool moved = false;
            foreach (double step in new[] { 24.0, 12.0, 40.0, 7.0 })
            {
                foreach (var side in new[] { 0f, 0.5f, -0.5f, 1.0f, -1.0f, 1.6f, -1.6f })
                {
                    var stage = unit.Cell + (Vector2I)(forward.Rotated(side) * (float)Math.Min(step, distance - 8)).Round();
                    if (OrderMove(unit, stage, 3)) { moved = true; break; }
                }
                if (moved) break;
            }
        }
    }

    private static SkirmishActor NearestTarget(double x, double y, List<SkirmishActor> targets) =>
        targets.OrderBy(t => new Vector2((float)x, (float)y).DistanceSquaredTo(t.Position)).First();
}
