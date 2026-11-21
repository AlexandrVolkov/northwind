using System.Buffers.Binary;
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
    /// <summary>Waves launched so far (tests).</summary>
    public int WavesLaunched { get; private set; }
    private readonly Dictionary<int, double> _waveCooldown = [];
    private readonly Dictionary<int, double> _waveNext = [];
    private readonly Dictionary<int, int> _waveSize = [];

    /// <summary>Soldiers needed to launch a wave; machines count double.</summary>
    private const int WaveStrength = 14;

    /// <summary>Target priority of the wave: enemy soldiers first, then towers, then the other buildings, workers last.</summary>
    private static int TargetClass(SkirmishActor a) =>
        !a.Structure ? (a.Worker ? 3 : 0) : a.Source.TemplateName == "Башня" ? 1 : 2;

    private SkirmishActor? PickTarget(Vector2 from, List<SkirmishActor> candidates, double reach = double.MaxValue)
    {
        SkirmishActor? best = null; int bestClass = int.MaxValue; float bestDistance = float.MaxValue;
        foreach (var c in candidates)
        {
            float d = from.DistanceSquaredTo(c.Position);
            if (d > reach * reach) continue;
            int k = TargetClass(c);
            if (k < bestClass || (k == bestClass && d < bestDistance)) { best = c; bestClass = k; bestDistance = d; }
        }
        return best;
    }

    private void ImprovedStrategy(int slot)
    {
        if (Elapsed < _waveNext.GetValueOrDefault(slot)) return;
        _waveNext[slot] = Elapsed + 2;
        var mine = Actors.Where(a => a.Alive && !a.Structure && !a.Worker && a.Record[3] == slot).ToList();
        var wave = mine.Where(a => a.Detached).ToList();
        var foes = Enumerable.Range(0, Diplomacy.Count).Where(o => o != slot && Diplomacy.AtWar(slot, o)).ToArray();
        var targets = Actors.Where(a => a.Alive && foes.Contains(a.Record[3]) && a.Source.TemplateFile == "ARMY__02.DAT").ToList();
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
            var centre0 = new Vector2(ready.Average(a => a.Position.X), ready.Average(a => a.Position.Y));
            var first = PickTarget(centre0, targets)!;
            foreach (var unit in ready.OrderBy(a => a.Position.DistanceSquaredTo(first.Position)).Take(take)) { unit.Detached = true; unit.Record[0x1b] = 0; wave.Add(unit); }
            _waveSize[slot] = wave.Count; WavesLaunched++;
            Log($"ИИ (участник {slot}) выступает в поход: {wave.Count} бойцов.");
        }
        var centre = new Vector2(wave.Average(a => a.Position.X), wave.Average(a => a.Position.Y));
        var goal = PickTarget(centre, targets)!;
        // The route search of the port is local, so the wave follows a coarse path over the whole map (cells a soldier can stand on,
        // bridges included), recomputed every 20 s, and each walker goes to the point ten cells further along it. When the river cuts
        // every way, a bridge is ordered where the shortest way across the water would go.
        if (!_wavePath.TryGetValue(slot, out var cached) || Elapsed > cached.Until || Find(cached.Goal) is not { Alive: true })
        {
            var sample = wave[0];
            var found = CoarsePath(sample, sample.Cell, goal.Cell, 14);
            if (found.Count == 0) OrderBridge(slot, sample, goal.Cell);
            _wavePath[slot] = cached = (goal.Id, Elapsed + 20, found);
        }
        var path = cached.Path;
        foreach (var unit in wave)
        {
            // Within sight the soldier takes the best target there (units, then towers, then other buildings); otherwise it follows the path.
            var aim = Find(unit.Target);
            var near = PickTarget(unit.Position, targets, 14 * 32);
            if (near != null && (aim == null || !aim.Alive || TargetClass(near) < TargetClass(aim)))
            { unit.Target = near.Id; unit.Goal = null; unit.Route.Clear(); unit.ReplanTimer = 0; continue; }
            if (aim != null && aim.Alive) continue;
            unit.Target = 0;
            if (unit.Stepping || (unit.Goal != null && unit.Route.Count > 0) || path.Count == 0) continue;
            int at = 0; float best = float.MaxValue;
            for (int i = 0; i < path.Count; i++) { float d = ((Vector2)(path[i] - unit.Cell)).LengthSquared(); if (d < best) { best = d; at = i; } }
            if (at >= path.Count - 1) { unit.Target = goal.Id; unit.Goal = null; unit.Route.Clear(); unit.ReplanTimer = 0; continue; }
            var stage = path[Math.Min(path.Count - 1, at + 10)];
            if (!OrderMove(unit, stage, 3)) OrderMove(unit, path[Math.Min(path.Count - 1, at + 4)], 3);
        }
    }

    private readonly Dictionary<int, double> _bridgeUntil = [];

    /// <summary>No land way to the goal: find the way that crosses the least water (cells a bridge can stand on count as passable)
    /// and order the castle to build a bridge over the first stretch of water on it (command 58h, template 7, from the first to the last
    /// water cell of that stretch). One order per 120 s.</summary>
    private void OrderBridge(int slot, SkirmishActor sample, Vector2I goal)
    {
        if (Elapsed < _bridgeUntil.GetValueOrDefault(slot)) return;
        var castle = Actors.FirstOrDefault(a => a.Alive && a.Record[3] == slot && a.Source.TemplateName == "Замок" && a.ConstructionRemaining <= 0);
        if (castle == null || (castle.Record[0x14] & 0xf8) != 0) return;
        var bridge = _templates.Armies["ARMY__02.DAT"].Templates[7];
        var bridgeRaw = Convert.FromHexString(bridge.RawRecordHex);
        int w = Map.Width, h = Map.Height;
        bool Water(Vector2I c) => !CanStand(sample, c) && bridgeRaw[0x58 + (_properties[(int)(Map.Cells[c.Y * w + c.X] & 0xffff) * 8] & 7)] != 0 && Occupancy.Cells[c.Y * w + c.X] == 0;
        var cost = new int[w * h]; Array.Fill(cost, int.MaxValue);
        var back = new int[w * h]; Array.Fill(back, -1);
        var queue = new PriorityQueue<Vector2I, int>(); cost[sample.Cell.Y * w + sample.Cell.X] = 0; queue.Enqueue(sample.Cell, 0);
        Vector2I? end = null;
        while (queue.TryDequeue(out var c, out int spent))
        {
            if (spent > cost[c.Y * w + c.X]) continue;
            if (Math.Max(Math.Abs(c.X - goal.X), Math.Abs(c.Y - goal.Y)) <= 14) { end = c; break; }
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = new Vector2I(c.X + dx, c.Y + dy);
                if (n.X < 0 || n.Y < 0 || n.X >= w || n.Y >= h) continue;
                int step = CanStand(sample, n) ? 1 : Water(n) ? 12 : -1;
                if (step < 0) continue;
                int total = spent + step;
                if (total < cost[n.Y * w + n.X]) { cost[n.Y * w + n.X] = total; back[n.Y * w + n.X] = c.Y * w + c.X; queue.Enqueue(n, total); }
            }
        }
        if (end is not { } last) return;
        var way = new List<Vector2I>();
        for (int i = last.Y * w + last.X; i >= 0; i = back[i]) way.Add(new Vector2I(i % w, i / w));
        way.Reverse();
        int first = way.FindIndex(c => !CanStand(sample, c));
        if (first < 0) return;
        int stop = way.FindIndex(first, c => CanStand(sample, c));
        var from = way[first]; var to = way[(stop < 0 ? way.Count : stop) - 1];
        // The bridge is a straight line along the axis of the crossing.
        if (Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y)) to = new Vector2I(to.X, from.Y); else to = new Vector2I(from.X, to.Y);
        _bridgeUntil[slot] = Elapsed + 120;
        var record = castle.Record;
        record[0x22] = 7; record[0x14] = (byte)((record[0x14] & 7) | 0x58);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x29), (ushort)(from.X * 32)); BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x2b), (ushort)(from.Y * 32));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x2d), (ushort)(to.X * 32)); BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x2f), (ushort)(to.Y * 32));
        Log($"ИИ (участник {slot}) строит мост через реку: {from} — {to}.");
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
