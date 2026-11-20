using System.Buffers.Binary;
using Godot;
namespace Northwind;

public sealed class SkirmishRules
{
    public int PlayerSlot { get; set; }
    public int EnemySlot { get; set; } = 3;
    /// <summary>Object updates per second. Original: PIT 1193182/16384 Hz, world step every 2 ticks at speed FDh, objects every 2nd step.</summary>
    public double TicksPerSecond { get; set; } = 1193182.0 / 16384 / 4;
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
    /// <summary>Original condition byte OBE[19] (signed); damage, healing and death act on the record.</summary>
    public int Health { get => Math.Max(0, (int)(sbyte)Record[0x19]); set => Record[0x19] = (byte)Math.Clamp(value, 0, 127); }
    /// <summary>DAT initial condition. OBE[1A] is the condition at the previous update (42BB), not a maximum.</summary>
    public int MaxHealth => Math.Max(1, (int)Template.InitialCondition);
    public bool Alive => Health > 0;
    /// <summary>A completed building at or below its DAT[46] condition threshold counts as destroyed (8ABF/C439 use the same test).</summary>
    public bool Ruined => Structure && ConstructionRemaining <= 0 && (sbyte)Record[0x19] <= (sbyte)Parameters[0x46];
    public bool Standing => Alive && !Ruined;
    public bool Worker => Rules.Worker;
    public bool Structure => Rules.Damage == 0 && !Worker;
    public bool Hero => Source.TemplateName == "Предводитель";
    public Vector2 Position => new(BinaryPrimitives.ReadUInt32LittleEndian(Record.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(Record.AsSpan(8)));
    public Vector2I Cell => new((int)Position.X >> 5, (int)Position.Y >> 5);
    public ushort Frame => BinaryPrimitives.ReadUInt16LittleEndian(Record.AsSpan(1));
    public SkirmishRoute Route { get; } = new();
    internal Vector2I? GuidanceGoal { get; set; }
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
    /// <summary>Bridge flag for WRK action 4 (86B6, mine work).</summary>
    public bool MineJob { get; set; }
    /// <summary>Dead unit still running the original death animation (A4DF); false once 98DB would remove it.</summary>
    public bool Dying { get; set; }
    public string JobText { get; set; } = "Без задания";
}
public sealed class BattleEffect
{
    public Vector2 From { get; init; }
    public Vector2 To { get; init; }
    public int Team { get; init; }
    public double Remaining { get; set; } = .22;
}

/// <summary>Playable experimental mission. Native contour search and motion kernels; provisional distant-order guidance, collisions, combat and timing.</summary>
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
    private readonly OriginalRouteSearch _search = new();
    private readonly AStarGrid2D _guidance = new();
    public int GuidedRouteSearches { get; private set; }
    private readonly OriginalRoutePool _routes = new();
    private readonly byte[] _properties;
    private readonly Dictionary<uint, MovementObject> _sourceMovementObjects = [];
    private readonly Func<uint, MovementObject> _resolveMovement;
    private readonly Dictionary<(Vector2I Cell, int Layer), uint> _reservations = [];
    private double _accumulator;
    /// <summary>Bridge-only route replanning delay: 21 object updates (0.7 s at the former experimental 30/s).</summary>
    private double ReplanDelay => 21 / Rules.TicksPerSecond;
    public SkirmishActor Hero => Actors.Single(a => a.Team == 0 && a.Hero);
    public SkirmishActor Objective => Actors.Single(a => a.Team == 1 && a.Source.TemplateName == "Замок");

    public Skirmish(MapData map, ScenarioData scenario, TemplatesData templates, SkirmishRules rules)
    {
        _resolveMovement = ResolveMovementObject;
        Map = map; Rules = rules; _properties = Convert.FromHexString(map.SegmentPropertiesHex);
        var snapshot = OriginalOccupancySnapshot.Build(map, scenario, templates);
        if (!snapshot.MatchesReference) throw new InvalidDataException("Scenario occupancy does not match its source profile");
        Occupancy = snapshot.Grid;
        foreach (var (reference, obj) in snapshot.References)
            _sourceMovementObjects[reference] = new(Convert.FromHexString(obj.RawRecordHex),
                Convert.FromHexString(templates.Armies[obj.TemplateFile].Templates[obj.TemplateIndex].RawRecordHex));
        foreach (var slot in scenario.Slots.Where(s => s.SlotIndex == rules.PlayerSlot || s.SlotIndex == rules.EnemySlot))
        foreach (var obj in slot.Objects)
        {
            if (!rules.Units.TryGetValue(obj.TemplateName, out var spec) || spec.Worker) continue;
            var template = templates.Armies[obj.TemplateFile].Templates[obj.TemplateIndex];
            Actors.Add(new SkirmishActor { Source = obj, Template = template, Parameters = Convert.FromHexString(template.RawRecordHex),
                Record = Convert.FromHexString(obj.RawRecordHex), Rules = spec, Team = slot.SlotIndex == rules.PlayerSlot ? 0 : 1 });
        }
        _guidance.Region = new Rect2I(0, 0, map.Width, map.Height);
        _guidance.DiagonalMode = AStarGrid2D.DiagonalModeEnum.Always;
        _guidance.DefaultComputeHeuristic = _guidance.DefaultEstimateHeuristic = AStarGrid2D.Heuristic.Chebyshev;
        _guidance.Update();
        foreach (var actor in Actors.Where(a => !a.Structure)) actor.Route.Attach(_routes, actor.Record);
        InitializeEconomy(scenario, templates);
        InitializePopulation(scenario);
        InitializeDiplomacy(scenario);
        InitializeProduction();
        InitializeAi(scenario, templates);
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
    private MovementObject ResolveMovementObject(uint reference)
    {
        // Runtime records override the initial snapshot, including worker template changes.
        var actor = Actors.FirstOrDefault(a => a.Id == reference);
        if (actor != null) return new(actor.Record, actor.Parameters);
        if (_sourceMovementObjects.TryGetValue(reference, out var obj)) return obj;
        throw new InvalidDataException($"Unknown movement reference {reference:X}");
    }
    public bool CanStand(SkirmishActor actor, Vector2I cell)
    {
        if (!Inside(cell)) return false;
        byte fw = actor.Parameters[3], fh = actor.Parameters[4];
        byte left = unchecked((byte)(cell.X - fw / 2)), top = unchecked((byte)(cell.Y - fh / 2));
        var result = OriginalMovementAdmission.ProbeArea(Occupancy.Width, Occupancy.Height, Map.Cells, _properties,
            Occupancy.Cells, actor.Parameters, actor.Record[0x0d], left, top, 0, 0, _resolveMovement, actor.Id);
        // The prototype still avoids every occupied active-layer cell, including native soft statuses.
        if (!result.Clear) return false;
        for (int y = top; y < top + fh; y++) for (int x = left; x < left + fw; x++)
            if (_reservations.TryGetValue((new(x, y), actor.Record[0x0d] & 3), out uint id) && id != actor.Id) return false;
        return true;
    }
    private static IEnumerable<Vector2I> FootprintCells(SkirmishActor actor, Vector2I center)
    {
        int fw = actor.Parameters[3], fh = actor.Parameters[4];
        for (int y = center.Y - fh / 2; y < center.Y - fh / 2 + fh; y++)
        for (int x = center.X - fw / 2; x < center.X - fw / 2 + fw; x++) yield return new(x, y);
    }
    private void ReserveStep(SkirmishActor actor, Vector2I next)
    {
        foreach (var cell in FootprintCells(actor, next)) _reservations[(cell, actor.Record[0x0d] & 3)] = actor.Id;
    }
    private void ReleaseStep(uint reference)
    {
        foreach (var key in _reservations.Where(p => p.Value == reference).Select(p => p.Key).ToArray()) _reservations.Remove(key);
    }
    private List<Vector2I> SearchPath(SkirmishActor actor, Vector2I goal)
    {
        int fw = actor.Parameters[3], fh = actor.Parameters[4];
        var offset = new Vector2I(fw / 2, fh / 2);
        var start = actor.Cell - offset;
        var cache = new byte[Map.Width * Map.Height];
        bool Admit(byte left, byte top, byte direction)
        {
            if (left > Map.Width - fw || top > Map.Height - fh) return false;
            int index = top * Map.Width + left;
            if (cache[index] == 0) cache[index] = CanStand(actor, new Vector2I(left, top) + offset) ? (byte)1 : (byte)2;
            return cache[index] == 1;
        }
        List<Vector2I> NativePath(Vector2I destination)
        {
            // The search consumes top-left coordinates; UI orders address the anchor cell.
            var end = destination - offset;
            var result = _search.Find(Occupancy.Width, Occupancy.Height, (byte)fw, (byte)fh,
                new((byte)start.X, (byte)start.Y), new((byte)end.X, (byte)end.Y), Admit);
            var path = new List<Vector2I> { actor.Cell };
            foreach (byte direction in result.Directions)
            {
                var delta = OriginalRouteSearch.Deltas[direction];
                path.Add(path[^1] + new Vector2I(delta.X, delta.Y));
            }
            return path;
        }
        if (actor.GuidanceGoal != goal)
        {
            actor.GuidanceGoal = null;
            var native = NativePath(goal);
            if (native.Count > 1 && native[^1] != actor.Cell) return native;
            actor.GuidanceGoal = goal;
        }
        // Provisional distant-order guidance. Do not present these waypoints as recovered AI.
        GuidedRouteSearches++;
        for (int y = 0; y < Map.Height; y++) for (int x = 0; x < Map.Width; x++)
            _guidance.SetPointSolid(new(x, y), !CanStand(actor, new(x, y)));
        _guidance.SetPointSolid(actor.Cell, false);
        var guide = _guidance.GetIdPath(actor.Cell, goal);
        for (int i = Math.Min(16, guide.Count - 1); i > 0; i--)
        {
            var native = NativePath(guide[i]);
            if (native.Count > 1 && native[^1] == guide[i]) return native;
        }
        return [actor.Cell];
    }
    private bool Plan(SkirmishActor actor, Vector2I goal)
    {
        actor.Route.Clear();
        if (!CanStand(actor, goal) || goal == actor.Cell) return goal == actor.Cell;
        var route = SearchPath(actor, goal);
        return actor.Route.Set(route);
    }
    public int Move(IEnumerable<uint> ids, Vector2I destination)
    {
        if (Paused || Outcome != "" || !Inside(destination)) return 0;
        int accepted = 0; var used = new HashSet<(Vector2I Cell, int Layer)>();
        foreach (var actor in ids.Distinct().Select(Find).OfType<SkirmishActor>().Where(a => a.Team == 0 && !a.Structure))
        {
            bool assigned = false;
            for (int radius = 0; radius <= 5 && !assigned; radius++)
            for (int y = -radius; y <= radius && !assigned; y++)
            for (int x = -radius; x <= radius && !assigned; x++)
            {
                var goal = destination + new Vector2I(x, y);
                if (FootprintCells(actor, goal).Any(c => used.Contains((c, actor.Record[0x0d] & 3))) || !CanStand(actor, goal)) continue;
                if (!actor.Stepping && !Plan(actor, goal)) continue;
                actor.Target = 0; actor.Goal = goal; actor.ReplanTimer = 0;
                actor.GatherTree = null; actor.PreferredMill = 0; actor.HarvestPrepared = false;
                if (actor.Worker) { actor.Record[0x2d] &= 15; ClearDelivery(actor); }
                if (actor.Stepping) actor.Route.Clear();
                foreach (var cell in FootprintCells(actor, goal)) used.Add((cell, actor.Record[0x0d] & 3));
                assigned = true; accepted++;
            }
        }
        Log(accepted > 0 ? $"Приказ движения: {accepted} отр. · цель отмечена на карте." : "До указанного места нет прохода.");
        return accepted;
    }
    public bool Attack(IEnumerable<uint> ids, uint targetId)
    {
        if (Paused || Outcome != "" || Find(targetId) is not { Team: 1 } foe) return false;
        if (ids.Distinct().Select(Find).OfType<SkirmishActor>().Any(a => a.Team == 0 && !a.Structure && !a.Worker) && !DeclareWarByOrder(foe)) return false;
        bool accepted = false;
        foreach (var actor in ids.Distinct().Select(Find).OfType<SkirmishActor>().Where(a => a.Team == 0 && !a.Structure && !a.Worker))
        { actor.Target = targetId; actor.Goal = null; actor.Route.Clear(); actor.ReplanTimer = 0; accepted = true; }
        if (accepted) Log($"Атаковать: {Find(targetId)!.Source.TemplateName}. Отряды сами сблизятся.");
        return accepted;
    }
    /// <summary>Settlement relations, war masks and message queues (docs/diplomacy.md).</summary>
    public DiplomacyState Diplomacy { get; private set; } = new(8);
    private OriginalDiplomacyRules _diplomacyRules = null!;
    private uint _warWarning;
    public bool AtWar(SkirmishActor a, SkirmishActor b) => Diplomacy.AtWar(a.Record[3], b.Record[3]);
    private void InitializeDiplomacy(ScenarioData scenario)
    {
        Diplomacy = new DiplomacyState(8);
        // 2EF1/2F60: participant +12 from SCN +0F and relations +B9 from SCN +3A (all zero on the first map).
        foreach (var slot in scenario.Slots.Where(s => s.SlotIndex < 8))
        {
            var raw = Convert.FromHexString(slot.RawRecordHex);
            Diplomacy.Hostile[slot.SlotIndex] = BitConverter.ToUInt32(raw, 0x0f);
            for (int q = 0; q < 8; q++) Diplomacy.Relations[slot.SlotIndex, q] = raw[0x3a + q];
        }
        _diplomacyRules = new OriginalDiplomacyRules(GameData.Read<OriginalDiplomacyData>("res://data/original_diplomacy.json"));
    }
    /// <summary>4F04..4F3B: an attack order on another settlement that is not at war first warns, a repeated order on the same
    /// object sends message 5 «Объявляю тебе войну» (F000 sets war both ways and lets third parties react).</summary>
    private bool DeclareWarByOrder(SkirmishActor foe)
    {
        byte me = (byte)Rules.PlayerSlot, them = foe.Record[3];
        if (Diplomacy.AtWar(me, them)) return true;
        if (_warWarning != foe.Id)
        {
            _warWarning = foe.Id;
            Log("Этот народ с вами не воюет. Повторите приказ атаки, чтобы объявить войну.");
            return false;
        }
        Diplomacy.Time = (uint)(Elapsed * 72.8165);
        _diplomacyRules.Send(Diplomacy, 5, them, me, them, 0);
        Log("Вы объявили войну.");
        return Diplomacy.AtWar(me, them);
    }
    public void Stop(IEnumerable<uint> ids)
    {
        if (Paused || Outcome != "") return;
        foreach (var actor in ids.Select(Find).OfType<SkirmishActor>().Where(a => a.Team == 0))
        { actor.Target = 0; actor.Goal = null; actor.Route.Clear(); actor.GatherTree = null; actor.PreferredMill = 0; actor.HarvestPrepared = false; actor.MineJob = false; if (actor.Worker) { actor.Record[0x2d] &= 15; ClearDelivery(actor); } actor.JobText = "Без задания"; }
    }
    public void Advance(double delta)
    {
        if (Paused || Outcome != "") return;
        _accumulator += Math.Max(0, delta);
        double step = 1.0 / Rules.TicksPerSecond;
        // Retain time rather than discarding it when frame rate drops.
        int budget = 240;
        // A small tolerance keeps whole-tick requests (n / TicksPerSecond) from losing a tick to rounding.
        while (_accumulator >= step - 1e-9 && budget-- > 0 && Outcome == "") { Tick(step); _accumulator -= step; }
    }
    /// <summary>The wind (9E7D), updated on every running timer tick (9BF9); its vector drifts smoke and carries fire.</summary>
    public WindState Wind;
    private byte _windTimer;   // [19E8]
    private void Tick(double dt)
    {
        Elapsed += dt;
        _windTimer++;
        OriginalWindRules.Step(ref Wind, _windTimer, _terrainCatalog.NextRandomByte, _terrainCatalog.NextSecondaryByte);
        foreach (var effect in Effects) effect.Remaining -= dt;
        Effects.RemoveAll(e => e.Remaining <= 0);
        TickArrows(); // 96CD runs on every world step: twice per object update at the default speed
        TickArrows();
        TickStones(); TickStones();
        TickDeaths();
        TickBuildings();
        TickMining();
        TickProduction();
        TickPopulation();
        // 42BB visits the settlements in order and each settlement's objects in pool order.
        foreach (var actor in Actors.Where(a => a.Alive && !a.Structure).OrderBy(a => a.Record[3]).ToArray())
        {
            if (!actor.Alive) continue;
            UpdateUnit(actor, dt);
            if (!actor.Alive) continue;
            var engaged = Find(BinaryPrimitives.ReadUInt32LittleEndian(actor.Record.AsSpan(0x1c)));
            OriginalCombatRules.AfterUpdate(actor.Record, engaged != null ? engaged.Record : ReadOnlySpan<byte>.Empty, _globalTick);
        }
        TickAi(); // 1E7EA follows the object update (LE:1 31D6)
        if (!Hero.Alive) Outcome = "Поражение — предводитель погиб";
        else if (!Objective.Standing && !Actors.Any(a => a.Team == 1 && !a.Structure && a.Alive)) Outcome = "Победа — вражеское поселение разбито";
    }
    private void UpdateUnit(SkirmishActor actor, double dt)
    {
            if (!actor.Alive) return;
            actor.AttackTimer -= dt; actor.ReplanTimer -= dt;
            if (TickVitals(actor)) return;
            if (actor.Worker) { TickWorker(actor, dt); return; }
            var target = Find(actor.Target);
            if (actor.Target != 0 && target == null) { actor.Target = 0; actor.Route.Clear(); actor.Goal = null; }
            // An assault on a building engages its nearby defenders first, then resumes the objective.
            if (actor.Team == 0 && target is { Structure: true })
                target = Actors.Where(a => a.Alive && a.Team == 1 && !a.Structure && Distance(actor.Position, a) <= Rules.AggroRange)
                    .OrderBy(a => Distance(actor.Position, a)).FirstOrDefault() ?? target;
            actor.Record[0x2e]++; // 8227 counts every update of a living unit
            if (target == null && !actor.Stepping)
            {
                // 82C3/8D3E: an idle unit searches its sight radius every eighth update; 8273/8E81: a walking unit that was
                // hit (OBE[17] bit 2) answers the nearest enemy within its attack range.
                bool idle = actor.Goal == null && actor.Route.Count == 0;
                bool struck = !idle && (actor.Record[0x17] & 2) != 0;
                if ((idle && (actor.Record[0x2e] & 7) == 0) || struck)
                {
                    if (struck) actor.Record[0x17] &= 0xfd;
                    var found = OriginalTargetRules.FindEnemy(actor.Id, actor.Record, actor.Parameters, struck, new SearchWorld(this, actor.Record[3]));
                    target = Find(found.Target);
                    if (target != null) { actor.Target = target.Id; actor.Goal = null; actor.Route.Clear(); }
                }
            }
            if (target != null && !actor.Stepping && AttackStep(actor, target)) return;
            if (target != null && actor.Target == 0) target = null;
            if (!actor.Stepping && target != null && actor.ReplanTimer <= 0)
            {
                PlanAttack(actor, target); actor.ReplanTimer = ReplanDelay;
            }
            else if (!actor.Stepping && actor.Goal is { } goal && actor.Route.Count == 0 && actor.ReplanTimer <= 0)
            {
                if (actor.Cell == goal) actor.Goal = null;
                else { Plan(actor, goal); actor.ReplanTimer = ReplanDelay; }
            }
            MoveTick(actor);
            }
    private void PlanAttack(SkirmishActor actor, SkirmishActor target)
    {
        actor.Route.Clear();
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
            var path = SearchPath(actor, cell);
            if (path.Count < 2) continue;
            if (actor.Route.Set(path)) break;
        }
    }
    private void MoveTick(SkirmishActor actor)
    {
        if (!actor.Stepping)
        {
            if (actor.Route.Count == 0) { if ((actor.Record[0x2d] & 15) == 2) actor.Record[0x2d] = 0; return; }
            var next = actor.Route.Peek();
            if (!CanStand(actor, next)) { actor.Route.Clear(); actor.ReplanTimer = 0; actor.Record[0x2d] = 0; return; }
            var direction = next - actor.Cell;
            if (Math.Max(Math.Abs(direction.X), Math.Abs(direction.Y)) != 1) { actor.Route.Clear(); actor.Record[0x2d] = 0; return; }
            byte heading = OriginalMotionPreparation.DirectionToward((uint)actor.Cell.X, (uint)actor.Cell.Y, (uint)next.X, (uint)next.Y);
            actor.Record[0x25] = (byte)((actor.Record[0x25] & 7) | (heading << 3));
            actor.Record[0x24] = 3; actor.Record[0x2c] = 32;
            actor.StepGoal = next; actor.Route.Dequeue(); actor.Stepping = actor.Turning = true; actor.StalledTicks = 0;
            ReserveStep(actor, next); actor.Record[0x2d] = 0x22; // movement state 2 for every unit (90E9 skips idle-only rules)
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
            ReleaseStep(actor.Id); actor.Stepping = false;
            if (actor.Route.Count == 0) actor.Record[0x2d] = 0;
        }
    }
    /// <summary>Phase-1 attack loop of 843E/8EC7: reach 8B5E, swing delay 8AF4, turn, swing request 8C7C and the
    /// animation impact (945E) resolved by the strike effect. Returns true while the unit stays engaged in place.</summary>
    private bool AttackStep(SkirmishActor actor, SkirmishActor target)
    {
        // A move request (8E0E flight, high nibble 2) during a swing: the original ends the swing and starts moving
        // (9275 → 939A, not ported). Here the unit disengages and the flight route moves it (adapter).
        if ((actor.Record[0x2d] >> 4) == 2 && (actor.Record[0x2d] & 15) != 2) { actor.Target = 0; actor.Record[0x2d] &= 0xf0; return false; }
        if (OriginalWorkerAnimation.Pending(actor.Record))
        {
            if (OriginalWorkerAnimation.Step(actor.Record, actor.Parameters, target.Record) is { } impact) ResolveImpact(actor, target, impact);
            return true;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(actor.Record.AsSpan(0x1c), target.Id);
        if (!OriginalCombatRules.TargetAlive(actor.Record, target.Record, target.Parameters)) { actor.Target = 0; return false; }
        int index = target.Cell.Y * Map.Width + target.Cell.X;
        byte terrain = (byte)(_properties[(int)(Map.Cells[index] & 0xffff) * 8] & 7);
        byte reach = OriginalCombatRules.Reach(actor.Record, actor.Parameters, target.Record, target.Parameters, terrain);
        if (reach >= 2) { actor.Target = 0; return false; }
        if (reach == 1) return false;
        actor.Route.Clear();
        // The bridge's finished step leaves movement state 2; 8BEA waits for rest (8C98) before turning.
        if ((actor.Record[0x2d] & 15) == 2 || (actor.Record[0x2d] >> 4) == 2) { actor.Record[0x2d] = 0; return true; }
        if (!OriginalCombatRules.SwingReady(actor.Record, actor.Parameters)) return true;
        byte heading = OriginalMotionPreparation.DirectionToward((uint)actor.Position.X, (uint)actor.Position.Y, (uint)target.Position.X, (uint)target.Position.Y);
        if (!OriginalMotionPreparation.TurnToward(actor.Record, heading, actor.Parameters)) return true;
        actor.Record[0x22] = 0xff; actor.Record[0x2d] = (byte)((actor.Record[0x2d] & 15) | 0x30); // 8C7C
        return true;
    }
    /// <summary>Live arrow effect records (22h bytes, ARMY__00.WRK type 2).</summary>
    public List<byte[]> Arrows { get; } = [];
    /// <summary>Effect loop 96CD for arrows: one AH=1 update per tick.</summary>
    private void TickArrows()
    {
        int plane = Occupancy.Width * Occupancy.Height;
        foreach (var arrow in Arrows.ToArray())
        {
            uint x = BinaryPrimitives.ReadUInt32LittleEndian(arrow.AsSpan(7)), y = BinaryPrimitives.ReadUInt32LittleEndian(arrow.AsSpan(0xc));
            var found = OriginalCombatRules.FindStruck(x, y, arrow[5], (byte)Map.Width, Map.Cells, _properties,
                (p, cell) => Occupancy.Cells[p * plane + cell], r => Find(r) is { } a ? (a.Record, a.Parameters) : null);
            var victim = Find(found.Reference);
            var (state, _, _) = OriginalArrowRules.Step(arrow, victim != null ? victim.Record : new byte[54], victim != null ? victim.Parameters : new byte[171],
                victim != null, (uint)(Map.Width * 32), (uint)(Map.Height * 32), _terrainCatalog.NextRandomByte);
            if (state == ArrowState.Flying) continue;
            Arrows.Remove(arrow);
            if (state is ArrowState.Spark or ArrowState.HardSpark) Effects.Add(new BattleEffect { From = new Vector2(x, y) - new Vector2(8, 8), To = new Vector2(x, y), Team = 0, Remaining = .15 });
            if (victim is { Alive: false }) Kill(victim);
        }
    }
    /// <summary>Live missile effects, 22h-byte records: catapult stones (type 8) and their explosions (10), fire shots (11h)
    /// and their smoke (12h).</summary>
    public List<byte[]> Stones { get; } = [];
    /// <summary>Effect loop 96CD for stones: flight D6E6; on landing BD0F hits the cell (planes 0 and 1) and the stone
    /// becomes an explosion; the cell gets a crater (CFAC AX=300h).</summary>
    private void TickStones()
    {
        int plane = Occupancy.Width * Occupancy.Height;
        foreach (var stone in Stones.ToArray())
        {
            if (stone[0] == OriginalCatapultRules.Stone && OriginalCatapultRules.Step(stone, (uint)(Map.Width * 32), (uint)(Map.Height * 32)) is var (x, y))
            {
                var struck = new List<SkirmishActor>();
                OriginalCatapultRules.Hit(OriginalCatapultRules.Attack, 2, 3, x, y, 1, 1, (byte)Map.Width, (byte)Map.Height,
                    (p, cell) => p * plane + cell < Occupancy.Cells.Length ? Occupancy.Cells[p * plane + cell] : 0,
                    r => { if (Find(r) is not { } a) return (new byte[0x40], new byte[171]); struck.Add(a); return (a.Record, a.Parameters); });
                foreach (var victim in struck) if (!victim.Alive) Kill(victim);
                if (_terrainCatalog.Crater(Map.Cells, (byte)Map.Width, (byte)Map.Height, x, y, 3) > 0) TerrainRevision++;   // CFAC AX=300h
                OriginalCatapultRules.Explode(stone);
            }
            else if (stone[0] == OriginalCatapultRules.Explosion) OriginalCatapultRules.Smoke(stone, Wind.X, Wind.Y);
            else if (stone[0] == OriginalCatapultRules.Bolt)
            {
                uint width = (uint)(Map.Width * 32), height = (uint)(Map.Height * 32);
                var landed = OriginalCatapultRules.StepBolt(stone, width, height, _terrainCatalog.NextRandomByte, _terrainCatalog.NextSecondaryByte, (tx, ty) =>
                {
                    uint sx = BinaryPrimitives.ReadUInt32LittleEndian(stone.AsSpan(7)), sy = BinaryPrimitives.ReadUInt32LittleEndian(stone.AsSpan(0xc));
                    if (sx >= width || sy >= height) return null;   // 9727 rejects points outside the map
                    var shot = new byte[0x22]; shot[5] = 1;
                    BinaryPrimitives.WriteUInt32LittleEndian(shot.AsSpan(7), sx); BinaryPrimitives.WriteUInt32LittleEndian(shot.AsSpan(0xc), sy);
                    OriginalFireRules.Create(shot, ReadOnlySpan<byte>.Empty, tx, ty, width, height, _terrainCatalog.NextRandomByte, _terrainCatalog.NextSecondaryByte);
                    Stones.Add(shot); return shot;
                });
                if (landed is var (bx, by))
                {
                    var struck = new List<SkirmishActor>();
                    OriginalCatapultRules.Hit(OriginalCatapultRules.Attack, 3, 3, bx, by, 1, 1, (byte)Map.Width, (byte)Map.Height,
                        (p, cell) => p * plane + cell < Occupancy.Cells.Length ? Occupancy.Cells[p * plane + cell] : 0,
                        r => { if (Find(r) is not { } a) return (new byte[0x40], new byte[171]); struck.Add(a); return (a.Record, a.Parameters); });
                    foreach (var victim in struck) if (!victim.Alive) Kill(victim);
                    OriginalCatapultRules.CreateBigExplosion(stone);
                }
            }
            else if (stone[0] == OriginalCatapultRules.BigExplosion) OriginalCatapultRules.StepBigExplosion(stone);
            else if (stone[0] == OriginalFireRules.Shot)
            {
                SkirmishActor? hit = null;
                var (outcome, _) = OriginalFireRules.Step(stone, (x0, y0) =>
                    {
                        if (x0 >= Map.Width * 32 || y0 >= Map.Height * 32) return;   // 9727 rejects points outside the map
                        var trail = new byte[0x22]; trail[5] = 1;
                        BinaryPrimitives.WriteUInt32LittleEndian(trail.AsSpan(7), x0); BinaryPrimitives.WriteUInt32LittleEndian(trail.AsSpan(0xc), y0);
                        OriginalFireRules.CreateTrail(trail); Stones.Add(trail);
                    },
                    (px, py) =>
                    {
                        var found = OriginalCombatRules.FindStruck(px, py, stone[5], (byte)Map.Width, Map.Cells, _properties,
                            (p, cell) => Occupancy.Cells[p * plane + cell], r => Find(r) is { } a ? (a.Record, a.Parameters) : null);
                        hit = Find(found.Reference);
                        return (hit != null ? (hit.Record, hit.Parameters) : null, found.TerrainClass);
                    }, (uint)(Map.Width * 32), (uint)(Map.Height * 32), _terrainCatalog.NextRandomByte);
                if (hit is { Alive: false }) Kill(hit);
                if (outcome == FireOutcome.Ignite) OriginalBurnRules.Ignite(stone, FireWorld);
                else if (outcome == FireOutcome.Deflect) OriginalBurnRules.CreateEmbers(stone);
            }
            else if (stone[0] == OriginalFireRules.Trail) OriginalFireRules.StepTrail(stone, Wind.X, Wind.Y);
            else if (stone[0] is OriginalBurnRules.Small or OriginalBurnRules.Medium or OriginalBurnRules.Large) OriginalBurnRules.Step(stone, FireWorld);
            else if (stone[0] == OriginalBurnRules.Embers) OriginalBurnRules.StepEmbers(stone);
            else if (stone[0] == OriginalBurnRules.Column) OriginalBurnRules.StepColumn(stone, Wind.X, Wind.Y, (uint)(Map.Width * 32), (uint)(Map.Height * 32));
            if (stone[0] == 0xff) Stones.Remove(stone);
        }
        foreach (var burnt in _fireWorld?.Touched ?? []) if (!burnt.Alive && Actors.Contains(burnt) && !burnt.Dying) Kill(burnt);
        _fireWorld?.Touched.Clear();
    }
    private FireWorldBridge? _fireWorld;
    private FireWorldBridge FireWorld => _fireWorld ??= new(this);
    /// <summary>World of the fire (CE3) in the skirmish, with the ported wind and burnt ground (CFAC AX=101h). Sounds are
    /// not played.</summary>
    private sealed class FireWorldBridge(Skirmish battle) : IFireWorld
    {
        public readonly HashSet<SkirmishActor> Touched = [];
        private int Plane => battle.Occupancy.Width * battle.Occupancy.Height;
        public (byte[] Record, byte[] Template) Resolve(uint reference)
        {
            var actor = battle.Actors.FirstOrDefault(a => a.Id == reference);
            if (actor == null) return (new byte[0x40], new byte[171]);   // gone: reads as dead
            Touched.Add(actor); return (actor.Record, actor.Parameters);
        }
        public (uint Reference, byte Terrain) Struck(uint x, uint y, byte layer)
        {
            var found = OriginalCombatRules.FindStruck(x, y, layer, (byte)battle.Map.Width, battle.Map.Cells, battle._properties,
                (p, cell) => battle.Occupancy.Cells[p * Plane + cell], r => battle.Find(r) is { } a ? (a.Record, a.Parameters) : null);
            return (found.Reference, found.TerrainClass);
        }
        public (uint Reference, byte Terrain) InCell(uint x, uint y, byte layer)
        {
            int cell = (byte)(y >> 5) * battle.Map.Width + (int)(x >> 5);
            if (cell < 0 || cell >= battle.Map.Cells.Length) return (0, 0);
            return (battle.Occupancy.Cells[(layer & 3) * Plane + cell], (byte)(battle._properties[(int)(battle.Map.Cells[cell] & 0xffff) * 8] & 7));
        }
        public void Spawn(byte army, byte type, byte layer, uint x, uint y, byte? height)
        {
            if (x >= battle.Map.Width * 32 || y >= battle.Map.Height * 32) return;   // 9727 rejects points outside the map
            var effect = new byte[0x22]; effect[1] = army; effect[5] = layer;
            BinaryPrimitives.WriteUInt32LittleEndian(effect.AsSpan(7), x); BinaryPrimitives.WriteUInt32LittleEndian(effect.AsSpan(0xc), y);
            if (type == OriginalBurnRules.Embers) { OriginalBurnRules.CreateEmbers(effect); if (height is { } h) effect[0x11] = h; }
            else if (type == OriginalBurnRules.Small) OriginalBurnRules.Ignite(effect, this);
            else return;
            if (effect[0] != 0xff) battle.Stones.Add(effect);
        }
        public void Sound(int id) { }
        public void Scorch(byte x, byte y)
        {
            if (battle._terrainCatalog.Scorch(battle.Map.Cells, (byte)battle.Map.Width, (byte)battle.Map.Height, x, y, 1) > 0) battle.TerrainRevision++;   // CFAC AX=101h
        }
        public (int X, int Y) Drift => (battle.Wind.X, battle.Wind.Y);
    }
    private void ResolveImpact(SkirmishActor actor, SkirmishActor target, WorkerImpact impact)
    {
        if (impact.X >= Map.Width * 32 || impact.Y >= Map.Height * 32) return; // 9727 rejects points outside the map
        Effects.Add(new BattleEffect { From = actor.Position, To = new Vector2(impact.X, impact.Y), Team = actor.Team });
        if (impact.Code >= 128 && (impact.Code & 0x7f) == 0)
        {
            int plane = Occupancy.Width * Occupancy.Height;
            var found = OriginalCombatRules.FindStruck(impact.X, impact.Y, impact.Layer, (byte)Map.Width, Map.Cells, _properties,
                (p, cell) => Occupancy.Cells[p * plane + cell], r => Find(r) is { } a ? (a.Record, a.Parameters) : null);
            var victim = Find(found.Reference);
            OriginalCombatRules.StrikeEffect(new byte[0x22], actor.Record, victim != null ? victim.Record : new byte[54],
                victim != null ? victim.Parameters : actor.Parameters, victim != null, _terrainCatalog.NextRandomByte);
            if (victim is { Alive: false }) Kill(victim);
            return;
        }
        if (impact.Code >= 128 && (impact.Code & 0x7f) == 2)
        {
            // 9727 places effect type 2 (army 0) at the strike point; WRK 593 aims and launches the arrow.
            var arrow = new byte[0x22]; arrow[0] = 2; arrow[1] = 0; arrow[5] = impact.Layer;
            BinaryPrimitives.WriteUInt32LittleEndian(arrow.AsSpan(7), impact.X); BinaryPrimitives.WriteUInt32LittleEndian(arrow.AsSpan(0xc), impact.Y);
            OriginalArrowRules.Create(arrow, actor.Record, impact.TargetX, impact.TargetY, (uint)(Map.Width * 32), (uint)(Map.Height * 32),
                _terrainCatalog.NextRandomByte, _terrainCatalog.NextSecondaryByte);
            Arrows.Add(arrow); return;
        }
        if (impact.Code >= 128 && (impact.Code & 0x7f) == OriginalCatapultRules.Stone)
        {
            // 9727 places effect type 8 at the strike point; WRK 85A aims and launches the stone.
            var stone = new byte[0x22]; stone[0] = OriginalCatapultRules.Stone; stone[5] = impact.Layer;
            BinaryPrimitives.WriteUInt32LittleEndian(stone.AsSpan(7), impact.X); BinaryPrimitives.WriteUInt32LittleEndian(stone.AsSpan(0xc), impact.Y);
            OriginalCatapultRules.Create(stone, actor.Record, impact.TargetX, impact.TargetY, (uint)(Map.Width * 32), (uint)(Map.Height * 32),
                _terrainCatalog.NextRandomByte, _terrainCatalog.NextSecondaryByte);
            Stones.Add(stone); return;
        }
        if (impact.Code >= 128 && (impact.Code & 0x7f) == OriginalFireRules.Shot)
        {
            // 9727 places effect type 11h at the strike point; WRK BAB aims and launches the fire shot.
            var shot = new byte[0x22]; shot[0] = OriginalFireRules.Shot; shot[5] = impact.Layer;
            BinaryPrimitives.WriteUInt32LittleEndian(shot.AsSpan(7), impact.X); BinaryPrimitives.WriteUInt32LittleEndian(shot.AsSpan(0xc), impact.Y);
            OriginalFireRules.Create(shot, actor.Record, impact.TargetX, impact.TargetY, (uint)(Map.Width * 32), (uint)(Map.Height * 32),
                _terrainCatalog.NextRandomByte, _terrainCatalog.NextSecondaryByte);
            Stones.Add(shot); return;
        }
        if (impact.Code >= 128 && (impact.Code & 0x7f) == OriginalCatapultRules.Bolt)
        {
            // 9727 places effect type 1Dh at the strike point; WRK 12EC aims and launches the bolt.
            var bolt = new byte[0x22]; bolt[0] = OriginalCatapultRules.Bolt; bolt[5] = impact.Layer;
            BinaryPrimitives.WriteUInt32LittleEndian(bolt.AsSpan(7), impact.X); BinaryPrimitives.WriteUInt32LittleEndian(bolt.AsSpan(0xc), impact.Y);
            OriginalCatapultRules.CreateBolt(bolt, actor.Record, impact.TargetX, impact.TargetY, (uint)(Map.Width * 32), (uint)(Map.Height * 32),
                _terrainCatalog.NextRandomByte, _terrainCatalog.NextSecondaryByte);
            Stones.Add(bolt); return;
        }
        // Other missile types are not ported: the hit lands on the engaged target with the same attack.
        OriginalCombatRules.Hit(target.Record, target.Parameters, OriginalCombatRules.StrikeAttack(actor.Record[0x16]), (byte)(actor.Record[0x14] & 7), _terrainCatalog.NextRandomByte);
        if (!target.Alive) Kill(target);
    }
    private void Kill(SkirmishActor target)
    {
        var cells = Occupancy.Cells.ToArray();
        for (int i = 0; i < cells.Length; i++) if (cells[i] == target.Id) cells[i] = 0;
        Occupancy.LoadCells(cells);
        ReleaseStep(target.Id); target.Route.Clear(); target.Stepping = false;
        target.Dying = !target.Structure;
        Log($"{target.Source.TemplateName} {(target.Team == 0 ? "потерян" : "уничтожен")}.");
    }
    /// <summary>95D3/A4DF for dead units: death frames by hit kind, then removal. Occupancy was already cleared by Kill.</summary>
    private void TickDeaths()
    {
        foreach (var actor in Actors.Where(a => a.Dying))
        {
            var (special, _) = OriginalDeathRules.Dispatch(actor.Record, actor.Parameters);
            if (special) { actor.Dying = false; continue; } // 12D08 is not ported
            OriginalDeathRules.Step(actor.Record, actor.Parameters, new DeathWorld(this, actor));
        }
    }
    private sealed class DeathWorld(Skirmish battle, SkirmishActor actor) : IDeathWorld
    {
        public void ClearOccupancy() { }
        public void Corpse(byte army, byte code, uint x, uint y) { } // corpse effect objects are not ported
        public void Remove() => actor.Dying = false;
        public byte TerrainClass() => (byte)(battle._properties[(int)(battle.Map.Cells[actor.Cell.Y * battle.Map.Width + actor.Cell.X] & 0xffff) * 8] & 7);
    }
    /// <summary>90E9 unit prefix; returns true when the unit died this update.</summary>
    private bool TickVitals(SkirmishActor actor)
    {
        byte before = actor.Record[0x2d];
        OriginalCombatRules.Vitals(actor.Record, actor.Parameters, _globalTick, (byte)Map.Width, (byte)Map.Height, Map.Cells, _properties, _terrainCatalog, _vitalsWorld ??= new(this));
        if (!actor.Alive) { Kill(actor); return true; }
        // 8E0E requested flight to a random cell; non-workers follow it through the route bridge.
        if (!actor.Worker && (actor.Record[0x2d] >> 4) == 2 && (before >> 4) != 2)
        { actor.Target = 0; actor.Goal = new Vector2I(actor.Record[0x22], actor.Record[0x23]); actor.Route.Clear(); actor.ReplanTimer = 0; }
        return false;
    }
    private VitalsWorld? _vitalsWorld;
    /// <summary>World inputs of D895 for one searching settlement: war mask and relations from the diplomacy state.</summary>
    private sealed class SearchWorld(Skirmish battle, byte owner) : IEnemySearchWorld
    {
        public byte MapWidth => (byte)battle.Map.Width;
        public byte MapHeight => (byte)battle.Map.Height;
        public uint Occupant(int plane, int cell) => battle.Occupancy.Cells[plane * battle.Occupancy.Width * battle.Occupancy.Height + cell];
        public (byte[] Record, byte[] Template) Resolve(uint reference)
        {
            // OBE[20] is the record's own visibility mask: creation (A855/8815) and every scenario object use FFh; hidden traps (DC91) differ.
            if (battle.Find(reference) is { } actor) return (actor.Record, actor.Parameters);
            if (battle._sourceMovementObjects.TryGetValue(reference, out var source)) return (source.Record, source.Template);
            var none = new byte[54]; none[0x19] = 0x80; return (none, new byte[171]);
        }
        public uint HostileMask => owner < battle.Diplomacy.Count ? battle.Diplomacy.Hostile[owner] : 0;
        public bool Contact(byte their) => owner < battle.Diplomacy.Count && their < 16 && battle.Diplomacy.Relations[owner, their] != 0;
        public byte TerrainClass(ReadOnlySpan<byte> record)
        {
            int index = (int)(BinaryPrimitives.ReadUInt32LittleEndian(record[8..]) >> 5) * battle.Map.Width + (int)(BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) >> 5);
            return (byte)(battle._properties[(int)(battle.Map.Cells[index] & 0xffff) * 8] & 7);
        }
        public void Contacted(FirstContact contact)
        {
            if (contact.Owner >= battle.Diplomacy.Count || contact.TheirOwner >= battle.Diplomacy.Count) return;
            battle.Diplomacy.Time = (uint)(battle.Elapsed * 72.8165);
            // D98F..D997: message 3 to the searcher's own queue; F250 marks contact both ways.
            battle._diplomacyRules.Send(battle.Diplomacy, 3, contact.Owner, contact.Owner, contact.TheirOwner,
                ((uint)contact.TheirOwner << 16) | ((uint)contact.CellY << 8) | contact.CellX);
            if (contact.Owner == battle.Rules.PlayerSlot || contact.TheirOwner == battle.Rules.PlayerSlot) battle.Log("Обнаружен неизвестный народ!");
        }
    }
    private sealed class VitalsWorld(Skirmish battle) : IVitalsWorld
    {
        public void Sound(uint reference) { } // Sound banks (*.FX) are not ported yet.
        public uint Occupant(int plane, int cell) => battle.Occupancy.Cells[plane * battle.Occupancy.Width * battle.Occupancy.Height + cell];
    }
    public void Dispose() => _guidance.Dispose();
}
