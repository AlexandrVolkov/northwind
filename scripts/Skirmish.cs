using System.Buffers.Binary;
using Godot;
namespace Northwind;

public sealed class SkirmishRules
{
    public int PlayerSlot { get; set; }
    public int EnemySlot { get; set; } = 3;
    public int TicksPerSecond { get; set; } = 30;
    public float AggroRange { get; set; } = 360;
    public Dictionary<string, SkirmishUnitRules> Units { get; set; } = [];
    public EconomyRules Economy { get; set; } = new();
}
public sealed class SkirmishUnitRules
{
    public int Health { get; set; }
    public int Damage { get; set; }
    public float Range { get; set; }
    public float Cooldown { get; set; } = 1;
    public bool Worker { get; set; }
}
public sealed class SkirmishActor
{
    public uint Id => Source.RuntimeReference;
    public ObjectRecord Source { get; init; } = new();
    public TemplateEntry Template { get; set; } = new();
    public byte[] Parameters { get; set; } = [];
    public byte[] Record { get; init; } = [];
    public SkirmishUnitRules Rules { get; init; } = new();
    public int Team { get; init; }
    public int Health { get; set; }
    public bool Alive => Health > 0;
    public bool Worker => Rules.Worker;
    public bool Structure => Rules.Damage == 0 && !Worker;
    public bool Hero => Source.TemplateName == "Предводитель";
    public Vector2 Position => new(BinaryPrimitives.ReadUInt32LittleEndian(Record.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(Record.AsSpan(8)));
    public Vector2I Cell => new((int)Position.X >> 5, (int)Position.Y >> 5);
    public ushort Frame => BinaryPrimitives.ReadUInt16LittleEndian(Record.AsSpan(1));
    public Queue<Vector2I> Route { get; } = new();
    public Vector2I? Goal { get; set; }
    public uint Target { get; set; }
    public bool Stepping { get; set; }
    public bool Turning { get; set; }
    public Vector2I StepGoal { get; set; }
    public double AttackTimer { get; set; }
    public double ReplanTimer { get; set; }
    public int StalledTicks { get; set; }
    public double ConstructionRemaining { get; set; }
    public double ConstructionDuration { get; set; }
    public Vector2I? GatherTree { get; set; }
    public uint PreferredMill { get; set; }
    public bool HarvestPrepared { get; set; }
    public string JobText { get; set; } = "Без задания";
}
public sealed class BattleEffect
{
    public Vector2 From { get; init; }
    public Vector2 To { get; init; }
    public int Team { get; init; }
    public double Remaining { get; set; } = .22;
}

/// <summary>Playable experimental mission. Native motion kernels; provisional route search, combat, teams and timing.</summary>
public sealed partial class Skirmish : IDisposable
{
    public MapData Map { get; }
    public SkirmishRules Rules { get; }
    public List<SkirmishActor> Actors { get; } = [];
    public List<BattleEffect> Effects { get; } = [];
    public OriginalOccupancyMap Occupancy { get; }
    public List<string> Events { get; } = [];
    public string Outcome { get; private set; } = "";
    public bool Paused { get; set; }
    public double Elapsed { get; private set; }
    private readonly AStarGrid2D _paths = new();
    private readonly byte[] _properties;
    private readonly Dictionary<Vector2I, uint> _reservations = [];
    private double _accumulator;
    public SkirmishActor Hero => Actors.Single(a => a.Team == 0 && a.Hero);
    public SkirmishActor Objective => Actors.Single(a => a.Team == 1 && a.Source.TemplateName == "Замок");

    public Skirmish(MapData map, ScenarioData scenario, TemplatesData templates, SkirmishRules rules)
    {
        Map = map; Rules = rules; _properties = Convert.FromHexString(map.SegmentPropertiesHex);
        var snapshot = OriginalOccupancySnapshot.Build(map, scenario, templates);
        if (!snapshot.MatchesReference) throw new InvalidDataException("Scenario occupancy does not match its source profile");
        Occupancy = snapshot.Grid;
        foreach (var slot in scenario.Slots.Where(s => s.SlotIndex == rules.PlayerSlot || s.SlotIndex == rules.EnemySlot))
        foreach (var obj in slot.Objects)
        {
            if (!rules.Units.TryGetValue(obj.TemplateName, out var spec) || spec.Worker) continue;
            var template = templates.Armies[obj.TemplateFile].Templates[obj.TemplateIndex];
            Actors.Add(new SkirmishActor { Source = obj, Template = template, Parameters = Convert.FromHexString(template.RawRecordHex),
                Record = Convert.FromHexString(obj.RawRecordHex), Rules = spec, Health = spec.Health, Team = slot.SlotIndex == rules.PlayerSlot ? 0 : 1 });
        }
        _paths.Region = new Rect2I(0, 0, map.Width, map.Height);
        _paths.DiagonalMode = AStarGrid2D.DiagonalModeEnum.Never; _paths.Update();
        InitializeEconomy(scenario, templates);
        Log("Выделите войско и атакуйте красный замок. Берегите предводителя.");
    }
    public SkirmishActor? Find(uint id) => Actors.FirstOrDefault(a => a.Id == id && a.Alive);
    public bool Inside(Vector2I cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Map.Width && cell.Y < Map.Height;
    public void Log(string text) { Events.Add(text); if (Events.Count > 5) Events.RemoveAt(0); }
    public Rect2 Bounds(SkirmishActor a)
    {
        if (!a.Structure) return new(a.Position - Vector2.One * 15, Vector2.One * 30);
        var size = new Vector2I(a.Parameters[3], a.Parameters[4]);
        return new Rect2((Vector2)((a.Cell - size / 2) * 32), (Vector2)(size * 32));
    }
    public SkirmishActor? At(Vector2 point) => Actors.Where(a => a.Alive && HitBounds(a).HasPoint(point))
        .OrderBy(a => a.Structure).ThenBy(a => (a.Position - new Vector2(0, 16)).DistanceSquaredTo(point)).FirstOrDefault();
    private Rect2 HitBounds(SkirmishActor actor)
    {
        var preview = actor.Template.Preview;
        var image = new Rect2(actor.Position - new Vector2(preview.Width / 2f, preview.Height - 16), new Vector2(preview.Width, preview.Height));
        return Bounds(actor).Merge(image).Grow(actor.Structure ? 0 : 3);
    }
    private float Distance(Vector2 position, SkirmishActor target)
    {
        if (!target.Structure) return position.DistanceTo(target.Position);
        var box = Bounds(target);
        return position.DistanceTo(new Vector2(Mathf.Clamp(position.X, box.Position.X, box.End.X), Mathf.Clamp(position.Y, box.Position.Y, box.End.Y)));
    }
    public bool CanStand(SkirmishActor actor, Vector2I cell)
    {
        if (!Inside(cell)) return false;
        uint tile = Map.Cells[cell.Y * Map.Width + cell.X] & 65535;
        if (Map.Terrain[tile] is "water" or "rock" || actor.Parameters[0x58 + (_properties[tile * 8] & 7)] == 0) return false;
        int index = cell.Y * Map.Width + cell.X, area = Map.Width * Map.Height;
        for (int plane = 0; plane < 3; plane++)
        {
            uint reference = Occupancy.Cells[index + plane * area];
            if (reference != 0 && reference != actor.Id) return false;
        }
        return !_reservations.TryGetValue(cell, out uint id) || id == actor.Id;
    }
    private void PreparePaths(SkirmishActor actor)
    {
        for (int y = 0; y < Map.Height; y++)
        for (int x = 0; x < Map.Width; x++) _paths.SetPointSolid(new(x, y), !CanStand(actor, new(x, y)));
        _paths.SetPointSolid(actor.Cell, false);
    }
    private bool Plan(SkirmishActor actor, Vector2I goal)
    {
        actor.Route.Clear();
        if (!CanStand(actor, goal) || goal == actor.Cell) return goal == actor.Cell;
        PreparePaths(actor);
        var route = _paths.GetIdPath(actor.Cell, goal);
        foreach (var cell in route.Skip(1)) actor.Route.Enqueue(cell);
        return route.Count > 1;
    }
    public int Move(IEnumerable<uint> ids, Vector2I destination)
    {
        if (Paused || Outcome != "" || !Inside(destination)) return 0;
        int accepted = 0; var used = new HashSet<Vector2I>();
        foreach (var actor in ids.Distinct().Select(Find).OfType<SkirmishActor>().Where(a => a.Team == 0 && !a.Structure))
        {
            bool assigned = false;
            for (int radius = 0; radius <= 5 && !assigned; radius++)
            for (int y = -radius; y <= radius && !assigned; y++)
            for (int x = -radius; x <= radius && !assigned; x++)
            {
                var goal = destination + new Vector2I(x, y);
                if (used.Contains(goal) || !CanStand(actor, goal)) continue;
                if (!actor.Stepping && !Plan(actor, goal)) continue;
                actor.Target = 0; actor.Goal = goal; actor.ReplanTimer = 0;
                actor.GatherTree = null; actor.PreferredMill = 0; actor.HarvestPrepared = false;
                if (actor.Worker) actor.Record[0x2d] &= 15;
                if (actor.Stepping) actor.Route.Clear();
                used.Add(goal); assigned = true; accepted++;
            }
        }
        Log(accepted > 0 ? $"Приказ движения: {accepted} отр. · цель отмечена на карте." : "До указанного места нет прохода.");
        return accepted;
    }
    public bool Attack(IEnumerable<uint> ids, uint targetId)
    {
        if (Paused || Outcome != "" || Find(targetId) is not { Team: 1 }) return false;
        bool accepted = false;
        foreach (var actor in ids.Distinct().Select(Find).OfType<SkirmishActor>().Where(a => a.Team == 0 && !a.Structure && !a.Worker))
        { actor.Target = targetId; actor.Goal = null; actor.Route.Clear(); actor.ReplanTimer = 0; accepted = true; }
        if (accepted) Log($"Атаковать: {Find(targetId)!.Source.TemplateName}. Отряды сами сблизятся.");
        return accepted;
    }
    public void Stop(IEnumerable<uint> ids)
    {
        if (Paused || Outcome != "") return;
        foreach (var actor in ids.Select(Find).OfType<SkirmishActor>().Where(a => a.Team == 0))
        { actor.Target = 0; actor.Goal = null; actor.Route.Clear(); actor.GatherTree = null; actor.PreferredMill = 0; actor.HarvestPrepared = false; if (actor.Worker) actor.Record[0x2d] &= 15; actor.JobText = "Без задания"; }
    }
    public void Advance(double delta)
    {
        if (Paused || Outcome != "") return;
        _accumulator += Math.Max(0, delta);
        double step = 1.0 / Rules.TicksPerSecond;
        // Retain time rather than discarding it when frame rate drops.
        int budget = 240;
        while (_accumulator >= step && budget-- > 0 && Outcome == "") { Tick(step); _accumulator -= step; }
    }
    private void Tick(double dt)
    {
        Elapsed += dt;
        foreach (var effect in Effects) effect.Remaining -= dt;
        Effects.RemoveAll(e => e.Remaining <= 0);
        TickBuildings();
        foreach (var actor in Actors.Where(a => a.Alive && !a.Structure).ToArray())
        {
            if (!actor.Alive) continue;
            actor.AttackTimer -= dt; actor.ReplanTimer -= dt;
            if (actor.Worker) { TickWorker(actor, dt); continue; }
            var target = Find(actor.Target);
            if (actor.Target != 0 && target == null) { actor.Target = 0; actor.Route.Clear(); actor.Goal = null; }
            // An assault on a building engages its nearby defenders first, then resumes the objective.
            if (actor.Team == 0 && target is { Structure: true })
                target = Actors.Where(a => a.Alive && a.Team == 1 && !a.Structure && Distance(actor.Position, a) <= Rules.AggroRange)
                    .OrderBy(a => Distance(actor.Position, a)).FirstOrDefault() ?? target;
            if (target == null && actor.Goal == null && actor.Route.Count == 0)
            {
                float range = actor.Team == 1 ? Rules.AggroRange : Math.Max(160, actor.Rules.Range);
                target = Actors.Where(a => a.Alive && a.Team != actor.Team && Distance(actor.Position, a) <= range)
                    .OrderBy(a => Distance(actor.Position, a)).FirstOrDefault();
                if (target != null) actor.Target = target.Id;
            }
            if (target != null && !actor.Stepping && Distance(actor.Position, target) <= actor.Rules.Range)
            {
                actor.Route.Clear();
                if (actor.AttackTimer <= 0) Strike(actor, target);
                continue;
            }
            if (!actor.Stepping && target != null && actor.ReplanTimer <= 0)
            {
                PlanAttack(actor, target); actor.ReplanTimer = .7;
            }
            else if (!actor.Stepping && actor.Goal is { } goal && actor.Route.Count == 0 && actor.ReplanTimer <= 0)
            {
                if (actor.Cell == goal) actor.Goal = null;
                else { Plan(actor, goal); actor.ReplanTimer = .7; }
            }
            MoveTick(actor);
        }
        if (!Hero.Alive) Outcome = "Поражение — предводитель погиб";
        else if (!Objective.Alive && !Actors.Any(a => a.Team == 1 && !a.Structure && a.Alive)) Outcome = "Победа — вражеское поселение разбито";
    }
    private void PlanAttack(SkirmishActor actor, SkirmishActor target)
    {
        actor.Route.Clear(); PreparePaths(actor);
        int radius = (int)Math.Ceiling(actor.Rules.Range / 32) + 3;
        var candidates = new List<Vector2I>();
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            var cell = target.Cell + new Vector2I(x, y);
            var point = actor.Position + (Vector2)((cell - actor.Cell) * 32);
            if (CanStand(actor, cell) && Distance(point, target) <= actor.Rules.Range) candidates.Add(cell);
        }
        foreach (var cell in candidates.OrderBy(c => ((Vector2)c).DistanceSquaredTo(actor.Cell)))
        {
            var path = _paths.GetIdPath(actor.Cell, cell);
            if (path.Count < 2) continue;
            foreach (var c in path.Skip(1)) actor.Route.Enqueue(c);
            break;
        }
    }
    private void MoveTick(SkirmishActor actor)
    {
        if (!actor.Stepping)
        {
            if (actor.Route.Count == 0) return;
            var next = actor.Route.Peek();
            if (!CanStand(actor, next)) { actor.Route.Clear(); actor.ReplanTimer = 0; return; }
            var direction = next - actor.Cell;
            if (Math.Abs(direction.X) + Math.Abs(direction.Y) != 1) { actor.Route.Clear(); return; }
            byte heading = direction.X > 0 ? (byte)2 : direction.X < 0 ? (byte)6 : direction.Y > 0 ? (byte)4 : (byte)0;
            actor.Record[0x25] = (byte)((actor.Record[0x25] & 7) | (heading << 3));
            actor.Record[0x24] = 3; actor.Record[0x2c] = 32;
            actor.StepGoal = next; actor.Route.Dequeue(); actor.Stepping = actor.Turning = true; actor.StalledTicks = 0;
            _reservations[next] = actor.Id;
        }
        OriginalMotionStep result;
        if (actor.Turning)
        {
            var prepared = OriginalMotionPreparation.TurnAndAdvance(Occupancy, Map.Cells, _properties, actor.Record, actor.Parameters, actor.Id, 0);
            actor.Turning = !prepared.EnteredMotion; result = prepared.Step;
        }
        else result = OriginalSpatialRules.AdvanceMotion(Occupancy, actor.Record, actor.Id, actor.Parameters[3], actor.Parameters[4], 0);
        if (!actor.Turning && result.Distance == 0) actor.StalledTicks++; else actor.StalledTicks = 0;
        if (actor.StalledTicks > 120)
        {
            actor.Route.Clear(); actor.Goal = null; actor.Target = 0;
            Log($"{actor.Source.TemplateName}: движение остановлено свойствами местности.");
        }
        if (actor.Record[0x2c] == 0 || actor.StalledTicks > 120)
        {
            _reservations.Remove(actor.StepGoal); actor.Stepping = false;
        }
    }
    private void Strike(SkirmishActor actor, SkirmishActor target)
    {
        actor.AttackTimer = actor.Rules.Cooldown;
        target.Health = Math.Max(0, target.Health - actor.Rules.Damage);
        Effects.Add(new BattleEffect { From = actor.Position, To = target.Position, Team = actor.Team });
        if (target.Alive) return;
        var cells = Occupancy.Cells.ToArray();
        for (int i = 0; i < cells.Length; i++) if (cells[i] == target.Id) cells[i] = 0;
        Occupancy.LoadCells(cells);
        _reservations.Remove(target.StepGoal); target.Route.Clear(); target.Stepping = false;
        Log($"{target.Source.TemplateName} {(target.Team == 0 ? "потерян" : "уничтожен")}.");
    }
    public void Dispose() => _paths.Dispose();
}
