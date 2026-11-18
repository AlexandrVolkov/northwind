using System.Buffers.Binary;
using Godot;
namespace Northwind;

public sealed class EconomyRules
{
    // Optional explicit scenario override for experiments/tests; normal play reads SCN.
    public ResourceQuad? StartingResources { get; set; }
    public float BuildRadius { get; set; } = 640;
}

public sealed partial class Skirmish
{
    public uint Metal { get; private set; }
    public uint Wood { get; private set; }
    public uint Gold { get; private set; }
    public ResourceQuad Resources => new(Metal, Wood, Gold, FreePeople);
    public TemplateEntry WorkerTemplate => _templates.Armies["ARMY__02.DAT"].Templates[10];
    public TemplateEntry MilitiaTemplate => _templates.Armies["ARMY__02.DAT"].Templates[0];
    public static ResourceQuad Price(TemplateEntry template) => OriginalResourceRules.ReadPrice(Convert.FromHexString(template.RawRecordHex));
    public bool CanAfford(TemplateEntry template) => OriginalConstructionRules.TryDebitUpfront(Resources, Price(template)).Accepted;
    public static string PriceText(TemplateEntry template)
    {
        var price = Price(template); var parts = new List<string>();
        if (price.First != 0) parts.Add($"{price.First} мет.");
        if (price.Second != 0) parts.Add($"{price.Second} дер.");
        if (price.Third != 0) parts.Add($"{price.Third} зол.");
        if (price.Fourth != 0) parts.Add($"{price.Fourth} чел.");
        return string.Join("\n", parts.Chunk(2).Select(pair => string.Join(" · ", pair)));
    }
    public string MissingResources(TemplateEntry template)
    {
        var price = Price(template); var missing = new List<string>();
        if (Metal < price.First) missing.Add($"металла: {price.First - Metal}");
        if (Wood < price.Second) missing.Add($"дерева: {price.Second - Wood}");
        if (Gold < price.Third) missing.Add($"золота: {price.Third - Gold}");
        if (FreePeople < price.Fourth) missing.Add($"людей: {price.Fourth - FreePeople}");
        return missing.Count == 0 ? "" : "Не хватает " + string.Join(", ", missing) + ".";
    }
    private void SetResources(ResourceQuad value)
    { Metal = value.First; Wood = value.Second; Gold = value.Third; FreePeople = value.Fourth; }
    private bool Spend(TemplateEntry template)
    {
        var payment = OriginalConstructionRules.TryDebitUpfront(Resources, Price(template));
        if (!payment.Accepted) { Log(MissingResources(template)); return false; }
        SetResources(payment.Balances); return true;
    }
    public uint ProducedWood { get; private set; }
    public int TerrainRevision { get; private set; }
    public HashSet<Vector2I> ClearedGroves { get; } = [];
    private OriginalTerrainCatalog _terrainCatalog = null!;
    public SkirmishActor PlayerCastle => Actors.Single(a => a.Team == 0 && a.Source.TemplateName == "Замок");
    public TemplateEntry MillTemplate => _templates.Armies["ARMY__02.DAT"].Templates[4];
    private TemplatesData _templates = null!;
    private ObjectRecord _workerSeed = null!;
    // Experimental runtime allocation, deliberately outside first-map source references.
    private uint _nextEconomyId = 0x100000;

    private void InitializeEconomy(ScenarioData scenario, TemplatesData templates)
    {
        _templates = templates;
        var slot = scenario.Slots.Single(s => s.SlotIndex == Rules.PlayerSlot);
        SetResources(Rules.Economy.StartingResources ?? OriginalResourceRules.ReadScenario(Convert.FromHexString(slot.RawRecordHex)));
        _terrainCatalog = new OriginalTerrainCatalog(_properties, GameData.Read<OriginalTerrainData>("res://data/original_terrain.json"));
        _workerSeed = scenario.Slots.SelectMany(s => s.Objects)
            .First(o => o.TemplateFile == "ARMY__02.DAT" && o.TemplateIndex == 10);
    }
    public bool Harvestable(Vector2I cell) => Inside(cell) && OriginalForestryRules.IsTimber(
        OriginalForestryRules.Property(_properties, Map.Cells[cell.Y * Map.Width + cell.X]));
    private bool FindNextGrove(SkirmishActor worker)
    {
        foreach (byte radius in new byte[] { 1, worker.Parameters[0x47] })
        {
            if (!OriginalForestryRules.FindTimber(Occupancy.Width, Occupancy.Height, Map.Cells, _properties,
                (uint)worker.Position.X, (uint)worker.Position.Y, worker.Parameters[3], worker.Parameters[4], radius, out var cell)) continue;
            worker.GatherTree = new Vector2I(cell.X, cell.Y); worker.HarvestPrepared = false;
            worker.Route.Clear(); worker.ReplanTimer = 0; return true;
        }
        worker.GatherTree = null; worker.Route.Clear(); worker.JobText = "Поблизости нет леса";
        Log("Поблизости нет подходящего леса. Укажите рабочему другой участок."); return false;
    }
    private SkirmishActor NewEconomyActor(int templateIndex, Vector2I cell, bool worker, bool soldier = false)
    {
        var template = _templates.Armies["ARMY__02.DAT"].Templates[templateIndex];
        var parameters = Convert.FromHexString(template.RawRecordHex);
        var raw = worker ? Convert.FromHexString(_workerSeed.RawRecordHex) : soldier ? Convert.FromHexString(_militiaSeed.RawRecordHex).AsSpan(0, template.RuntimeRecordBytes).ToArray() : new byte[template.RuntimeRecordBytes];
        raw[0] = 2; raw[3] = (byte)Rules.PlayerSlot; raw[0x0d] = 1; raw[0x0e] = (byte)templateIndex;
        raw[0x14] = 0x80; raw[0x19] = raw[0x1a] = template.InitialCondition;
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(1), template.InitialFrame);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x17), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), worker ? (uint)(cell.X * 32 + 16) : OriginalWorkerDelivery.BuildingAnchor((byte)cell.X, parameters[3]));
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), worker ? (uint)(cell.Y * 32 + 16) : OriginalWorkerDelivery.BuildingAnchor((byte)cell.Y, parameters[4]));
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x10), (uint)raw.Length);
        if (worker || soldier) { raw[0x24] = 0; raw[0x25] = 0; raw[0x26] = 0; raw[0x2c] = 0; raw[0x2d] = 0; raw[0x30] = 0; if (worker) raw[0x31] = 0; }
        return new SkirmishActor { Source = new ObjectRecord { RuntimeReference = _nextEconomyId++, TemplateName = template.Name,
                TemplateFile = "ARMY__02.DAT", TemplateIndex = templateIndex, RecordBytes = raw.Length,
                RawX = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4)), RawY = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(8)), RawLayer = 1 },
            Template = template, Parameters = parameters, Record = raw, Rules = Rules.Units[template.Name], Team = 0,
            Health = Rules.Units[template.Name].Health };
    }
    public SkirmishActor? TrainWorker()
    {
        if (Paused || Outcome != "" || !PlayerCastle.Alive) return null;
        if (!CanAfford(WorkerTemplate)) { Log(MissingResources(WorkerTemplate)); return null; }
        var worker = NewEconomyActor(10, PlayerCastle.Cell, true);
        if (!PlaceRecruit(worker)) { Log("Вокруг замка нет свободного места для рабочего."); return null; }
        if (!Spend(WorkerTemplate)) return null;
        OriginalWorkerDelivery.InitializeEmpty(worker.Record, worker.Parameters, _terrainCatalog.NextRandomByte());
        worker.Route.Attach(_routes, worker.Record);
        Actors.Add(worker); Occupancy.WriteFootprint(worker.Parameters[3], worker.Parameters[4], worker.Record[0x0d], (uint)worker.Position.X, (uint)worker.Position.Y, worker.Id);
        RecountPopulation(); Log("Рабочий нанят. Выберите его и нажмите ПКМ по лесу."); return worker;
    }
    public DeliveryPoint MillEntry(Vector2I cell)
    {
        if (!Inside(cell)) return new(0, 0, false, false);
        var target = Convert.FromHexString(MillTemplate.RawRecordHex);
        var visitor = Convert.FromHexString(_templates.Armies["ARMY__02.DAT"].Templates[11].RawRecordHex);
        return OriginalWorkerDelivery.Approach(visitor, target, OriginalWorkerDelivery.BuildingAnchor((byte)cell.X, target[3]),
            OriginalWorkerDelivery.BuildingAnchor((byte)cell.Y, target[4]), (uint)(Map.Width * 32), (uint)(Map.Height * 32));
    }
    public ConstructionPlacement ProbeMill(Vector2I cell) => ProbeEconomyBuilding(cell, MillTemplate);
    private ConstructionPlacement ProbeEconomyBuilding(Vector2I cell, TemplateEntry template)
    {
        if (!Inside(cell) || !PlayerCastle.Alive || (new Vector2(cell.X * 32 + 16, cell.Y * 32 + 16)).DistanceTo(PlayerCastle.Position) > Rules.Economy.BuildRadius)
            return new(ConstructionPlacementStatus.Rejected);
        var parameters = template.Construction;
        var result = OriginalConstructionRules.ProbePlacement(Occupancy.Width, Occupancy.Height, Map.Cells, _properties, Occupancy.Cells,
            parameters.FootprintWidth, parameters.FootprintHeight, parameters.TerrainMask.ToArray(), (uint)(cell.X * 32 + 16), (uint)(cell.Y * 32 + 16));
        if (result.Status != ConstructionPlacementStatus.Clear) return result;
        var area = new Rect2I(cell - new Vector2I(parameters.FootprintWidth / 2, parameters.FootprintHeight / 2), new(parameters.FootprintWidth, parameters.FootprintHeight));
        if (_reservations.Any(r => area.HasPoint(r.Key.Cell))) return new(ConstructionPlacementStatus.Occupied);
        return result;
    }
    public SkirmishActor? BuildMill(Vector2I cell)
    {
        if (Paused || Outcome != "" || !PlayerCastle.Alive) return null;
        if (ProbeMill(cell).Status != ConstructionPlacementStatus.Clear) { Log("Здесь нельзя строить: нужна свободная площадка рядом с замком."); return null; }
        if (!Spend(MillTemplate)) return null;
        var mill = NewEconomyActor(4, cell, false);
        mill.ConstructionDuration = mill.ConstructionRemaining = mill.Template.InitialCondition * Math.Max(1, (int)mill.Parameters[0x57]) / (double)Rules.TicksPerSecond;
        OriginalBuildingLifecycle.MarkConstruction(mill.Record); mill.Record[0x23] = 100; mill.Record[0x24] = 0;
        Actors.Add(mill); Occupancy.WriteFootprint(mill.Parameters[3], mill.Parameters[4], 1, (uint)mill.Position.X, (uint)mill.Position.Y, mill.Id);
        Log("Лесопилка строится. Рабочий сможет сдавать сюда древесину после завершения."); return mill;
    }
    public int GatherTimber(IEnumerable<uint> ids, Vector2I tree)
    {
        if (Paused || Outcome != "" || !Harvestable(tree)) return 0;
        int count = 0;
        foreach (var worker in ids.Select(Find).OfType<SkirmishActor>().Where(a => a.Team == 0 && a.Worker))
        {
            worker.GatherTree = tree; worker.PreferredMill = 0; ClearDelivery(worker); worker.Goal = null; worker.Target = 0;
            worker.Route.Clear(); worker.HarvestPrepared = false; worker.Record[0x2d] &= 15; worker.ReplanTimer = 0; count++;
        }
        if (count > 0) Log($"Рубка леса: {count} раб. Груз будет доставляться в готовую лесопилку.");
        return count;
    }
    public bool DeliverTimber(IEnumerable<uint> ids, uint millId)
    {
        if (Paused || Outcome != "" || Find(millId) is not { Team: 0 } mill || mill.Source.TemplateName != "Лесопилка") return false;
        bool accepted = false;
        foreach (var worker in ids.Select(Find).OfType<SkirmishActor>().Where(a => a.Team == 0 && a.Worker && a.Record[0x31] == 3))
        { ClearDelivery(worker); worker.PreferredMill = millId; worker.Route.Clear(); worker.Goal = null; worker.ReplanTimer = 0; accepted = true; }
        return accepted;
    }
    private void TickBuildings()
    {
        foreach (var building in Actors.Where(a => a.Alive && a.Team == 0 && (a.Source.TemplateName is "Лесопилка" or "Ферма")))
        {
            if (building.ConstructionRemaining > 0)
            {
                var maintenance = OriginalBuildingRules.StepMaintenance(new(building.Record[0x19], building.Record[0x16], building.Record[0x0f],
                        BinaryPrimitives.ReadUInt16LittleEndian(building.Record.AsSpan(0x17))),
                    new(building.Template.InitialCondition, building.Parameters[0x57], new(
                        BinaryPrimitives.ReadUInt32LittleEndian(building.Parameters.AsSpan(0x8c)),
                        BinaryPrimitives.ReadUInt32LittleEndian(building.Parameters.AsSpan(0x90)),
                        BinaryPrimitives.ReadUInt32LittleEndian(building.Parameters.AsSpan(0x94)))), default, false);
                building.Record[0x19] = maintenance.State.Condition; building.Record[0x16] = maintenance.State.Counter; building.Record[0x0f] = maintenance.State.WorkFlags;
                BinaryPrimitives.WriteUInt16LittleEndian(building.Record.AsSpan(0x17), maintenance.State.ObjectFlags);
                building.ConstructionRemaining = maintenance.ContinueAction
                    ? ((building.Template.InitialCondition - maintenance.State.Condition + 1) * Math.Max(1, (int)building.Parameters[0x57]) - maintenance.State.Counter) / (double)Rules.TicksPerSecond : 0;
                // Provisional visual stage binding; the full C88C frame/effects handler is not connected.
                if (building.Source.TemplateName == "Ферма")
                    BinaryPrimitives.WriteUInt16LittleEndian(building.Record.AsSpan(1), (ushort)(building.Template.InitialFrame + Math.Min(building.Parameters[0x4a], building.Record[0x19] * (building.Parameters[0x4a] + 1) / (building.Template.InitialCondition + 1))));
                if (maintenance.ContinueAction) continue;
                var completed = OriginalBuildingLifecycle.CompleteCurrentAction(new(building.Record[0x14], building.Record[0x15], building.Record[0x0f]));
                building.Record[0x14] = completed.EncodedState; building.Record[0x15] = completed.Auxiliary; building.Record[0x0f] = completed.WorkFlags;
                BinaryPrimitives.WriteUInt16LittleEndian(building.Record.AsSpan(1), (ushort)(building.Template.InitialFrame + building.Parameters[0x4a]));
                Log(building.Source.TemplateName + " готова.");
            }
            if (building.Source.TemplateName != "Лесопилка") continue;
            var state = OriginalLumbermillRules.Process(new(building.Record[0x23], building.Record[0x24], Wood, ProducedWood), building.Parameters[0x79]);
            building.Record[0x23] = state.Countdown; building.Record[0x24] = state.QueuedLoads;
            Wood = state.Stock; ProducedWood = state.Statistics;
            uint visitorId = OriginalWorkerDelivery.WaitingWorker(Occupancy.Width, Occupancy.Height, Occupancy.Cells, building.Record[0x0d],
                (uint)building.Position.X, (uint)building.Position.Y, building.Parameters);
            if (Find(visitorId) is { } visitor)
            {
                var handoff = OriginalLumbermillRules.Receive(BinaryPrimitives.ReadUInt16LittleEndian(building.Record.AsSpan(0x17)), building.Record[0x24],
                    BinaryPrimitives.ReadUInt16LittleEndian(visitor.Record.AsSpan(0x17)), visitor.Record.Length > 0x31 ? visitor.Record[0x31] : (byte)0, BinaryPrimitives.ReadUInt32LittleEndian(visitor.Parameters.AsSpan(0x40)));
                BinaryPrimitives.WriteUInt16LittleEndian(building.Record.AsSpan(0x17), handoff.MillFlags); building.Record[0x24] = handoff.QueuedLoads;
                BinaryPrimitives.WriteUInt16LittleEndian(visitor.Record.AsSpan(0x17), handoff.WorkerFlags); if (visitor.Record.Length > 0x31) visitor.Record[0x31] = handoff.Cargo;
            }
        }
    }
    private static void ClearDelivery(SkirmishActor worker)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(worker.Record.AsSpan(0x1c), 0);
        worker.Record[0x30] = 0; worker.Record[0x17] &= 0xbf;
    }
    private void ApplyWorkerTemplate(SkirmishActor worker)
    {
        int index = worker.Record[0x0e]; worker.Source.TemplateIndex = index;
        worker.Template = _templates.Armies["ARMY__02.DAT"].Templates[index]; worker.Parameters = Convert.FromHexString(worker.Template.RawRecordHex);
    }
    private static DeliveryCandidate ReceiverCandidate(SkirmishActor actor) => new(actor.Id, (uint)actor.Position.X, (uint)actor.Position.Y,
        BinaryPrimitives.ReadUInt32LittleEndian(actor.Parameters.AsSpan(0x40)), actor.Alive ? actor.Record[0x19] : (byte)0, actor.Parameters[0x46]);
    private void TickDelivery(SkirmishActor worker)
    {
        uint targetId = BinaryPrimitives.ReadUInt32LittleEndian(worker.Record.AsSpan(0x1c));
        var mill = Find(targetId);
        if (mill == null || mill.Team != 0 || OriginalWorkerDelivery.FindReceiver((uint)worker.Position.X, (uint)worker.Position.Y, [ReceiverCandidate(mill)]) == 0)
        {
            ClearDelivery(worker); worker.Route.Clear();
            if (worker.Stepping) { worker.Record[0x2d] &= 15; worker.JobText = "Получатель недоступен"; MoveTick(worker); return; }
            worker.Record[0x2d] = 0;
            var preferred = Find(worker.PreferredMill);
            uint selected = preferred is { Team: 0 } ? OriginalWorkerDelivery.FindReceiver((uint)worker.Position.X, (uint)worker.Position.Y, [ReceiverCandidate(preferred)]) : 0;
            if (selected == 0) selected = OriginalWorkerDelivery.FindReceiver((uint)worker.Position.X, (uint)worker.Position.Y, Actors.Where(a => a.Team == 0).Select(ReceiverCandidate));
            mill = Find(selected);
            if (mill == null) { worker.JobText = "С грузом · нужна лесопилка"; return; }
            BinaryPrimitives.WriteUInt32LittleEndian(worker.Record.AsSpan(0x1c), selected); worker.Record[0x15] = 1;
            worker.Route.Clear(); worker.ReplanTimer = 0; worker.JobText = "Выбирает лесопилку"; return;
        }
        if ((worker.Record[0x30] & 15) == 2 && mill.ConstructionRemaining > 0)
        { worker.JobText = "Ожидает строительства лесопилки"; return; }
        var result = OriginalWorkerApproach.Step(worker.Record, mill.Record, worker.Parameters, mill.Parameters, (uint)(Map.Width * 32), (uint)(Map.Height * 32));
        if (result.Status != WorkerApproachStatus.Pending)
        {
            ClearDelivery(worker); worker.Route.Clear(); worker.Record[0x2d] &= 15;
            if (!worker.Stepping) worker.Record[0x2d] = 0;
            worker.JobText = result.Status == WorkerApproachStatus.Blocked ? "Проход занят · повторяет выбор" : "Получатель недоступен · повторяет выбор";
            if (worker.Stepping) MoveTick(worker); return;
        }
        if (result.StopRequested)
        { worker.Route.Clear(); if (!worker.Stepping) worker.Record[0x2d] = 0; }
        if (result.MoveRequested)
        {
            var goal = new Vector2I(worker.Record[0x22], worker.Record[0x23]);
            Plan(worker, goal);
            if (worker.Route.Count == 0) worker.Record[0x2d] = 0;
            worker.JobText = "Доставляет древесину"; return;
        }
        worker.JobText = (worker.Record[0x30] & 15) switch {
            3 => "Ожидает приём груза", 2 => "Поворачивается к лесопилке", _ => "Доставляет древесину" };
        MoveTick(worker);
    }
    private void TickWorker(SkirmishActor worker, double dt)
    {
        // WRK 883 calls the animation core before the work dispatcher; 856C waits while state 3 is active.
        if (OriginalWorkerAnimation.Pending(worker.Record))
        {
            var impact = OriginalWorkerAnimation.Step(worker.Record, worker.Parameters);
            if (impact is { } hit)
                Effects.Add(new BattleEffect { From = new Vector2(hit.X, hit.Y), To = new Vector2(hit.TargetX, hit.TargetY), Team = 0, Remaining = .12 });
            if (OriginalWorkerAnimation.Pending(worker.Record))
            { worker.JobText = (worker.Record[0x2d] & 15) == 1 ? "Поворачивается к лесопилке" : "Рубит дерево"; return; }
        }
        if (worker.Source.TemplateIndex == 11 && worker.Record[0x31] == 0)
        {
            var empty = Convert.FromHexString(_templates.Armies["ARMY__02.DAT"].Templates[10].RawRecordHex);
            OriginalWorkerDelivery.ReturnEmpty(worker.Record, empty, _terrainCatalog.NextRandomByte()); ApplyWorkerTemplate(worker);
            ClearDelivery(worker); worker.PreferredMill = 0; worker.Route.Clear(); worker.HarvestPrepared = false;
        }
        if (worker.Stepping && worker.Record[0x31] != 3) { MoveTick(worker); return; }
        if (worker.GatherTree == null && worker.PreferredMill == 0)
        {
            worker.JobText = worker.Record[0x31] == 3 ? "Несёт древесину · укажите лесопилку" : "Без задания · ПКМ по лесу";
            if (worker.Goal is { } goal && worker.Route.Count == 0 && worker.ReplanTimer <= 0)
            { if (worker.Cell == goal) worker.Goal = null; else { Plan(worker, goal); worker.ReplanTimer = .7; } }
            MoveTick(worker); return;
        }
        if (worker.Record[0x31] == 3) { TickDelivery(worker); return; }
        var tree = worker.GatherTree!.Value;
        if (!Harvestable(tree))
        {
            if (!FindNextGrove(worker)) return;
            tree = worker.GatherTree!.Value;
        }
        var point = new Vector2(tree.X * 32 + 16, tree.Y * 32 + 16);
        worker.JobText = "Идёт к лесу";
        // 84F0..8535: exact Chebyshev stand-off for the selected footprint, not a circular radius.
        float distance = Math.Max(Math.Abs(worker.Position.X - point.X), Math.Abs(worker.Position.Y - point.Y));
        int standOff = ((worker.Parameters[3] << 4) + 16) & 255;
        if (distance == standOff)
        {
            worker.Route.Clear();
            byte heading = OriginalMotionPreparation.DirectionToward((uint)worker.Position.X, (uint)worker.Position.Y, (uint)point.X, (uint)point.Y);
            if (!OriginalMotionPreparation.TurnToward(worker.Record, heading, worker.Parameters)) { worker.JobText = "Поворачивается к дереву"; return; }
            if (!worker.HarvestPrepared)
            {
                worker.Record[0x32] = (byte)tree.X; worker.Record[0x33] = (byte)tree.Y;
                OriginalForestryRules.InitializeWork(worker.Record, worker.Parameters);
                worker.HarvestPrepared = true; worker.JobText = "Готовится рубить";
                return; // 8542 increments the work phase and returns before the first gate update.
            }
            worker.JobText = $"Рубит дерево · {worker.Record[0x34] & 31}";
            if ((worker.Record[0x34] & 31) != 0)
            {
                OriginalForestryRules.AdvanceWork(worker.Record, worker.Parameters);
            }
            else
            {
                bool clear = OriginalForestryRules.FinishLoad(worker.Record);
                if (clear && _terrainCatalog.ClearTimber(Map.Cells, Occupancy.Width, Occupancy.Height, tree.X, tree.Y) > 0)
                { TerrainRevision++; if (!Harvestable(tree)) ClearedGroves.Add(tree); }
                var loaded = Convert.FromHexString(_templates.Armies["ARMY__02.DAT"].Templates[11].RawRecordHex);
                if (!OriginalWorkerDelivery.GrantTimber(worker.Record, worker.Parameters, loaded, _terrainCatalog.NextRandomByte()))
                    throw new InvalidOperationException("Harvest callback rejected the worker");
                ApplyWorkerTemplate(worker); ClearDelivery(worker);
                OriginalForestryRules.ApplyLoadModifier(worker.Record); worker.HarvestPrepared = false;
            }
        }
        else if (worker.ReplanTimer <= 0)
        {
            var candidates = new[] { Vector2I.Up, Vector2I.Down, Vector2I.Left, Vector2I.Right }.Select(d => tree + d).OrderBy(c => ((Vector2)c).DistanceSquaredTo(worker.Cell));
            foreach (var cell in candidates) if (Plan(worker, cell)) break;
            worker.ReplanTimer = .7;
        }
        MoveTick(worker);
    }
}
