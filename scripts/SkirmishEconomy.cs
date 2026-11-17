using System.Buffers.Binary;
using Godot;
namespace Northwind;

public sealed class EconomyRules
{
    public uint StartingWood { get; set; } = 120;
    public uint WorkerPrice { get; set; } = 10;
    public uint MillPrice { get; set; } = 50;
    public float BuildRadius { get; set; } = 640;
    public int WorkerLimit { get; set; } = 6;
}

public sealed partial class Skirmish
{
    public uint Wood { get; private set; }
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
        _templates = templates; Wood = Rules.Economy.StartingWood;
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
    private SkirmishActor NewEconomyActor(int templateIndex, Vector2I cell, bool worker)
    {
        var template = _templates.Armies["ARMY__02.DAT"].Templates[templateIndex];
        var parameters = Convert.FromHexString(template.RawRecordHex);
        var raw = worker ? Convert.FromHexString(_workerSeed.RawRecordHex) : new byte[template.RuntimeRecordBytes];
        raw[0] = 2; raw[3] = (byte)Rules.PlayerSlot; raw[0x0d] = 1; raw[0x0e] = (byte)templateIndex;
        raw[0x14] = 0x80; raw[0x19] = raw[0x1a] = template.InitialCondition;
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(1), template.InitialFrame);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x17), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), (uint)(cell.X * 32 + 16));
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), (uint)(cell.Y * 32 + 16));
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x10), (uint)raw.Length);
        if (worker) { raw[0x25] = 0; raw[0x26] = 0; raw[0x2c] = 0; raw[0x31] = 0; }
        return new SkirmishActor { Source = new ObjectRecord { RuntimeReference = _nextEconomyId++, TemplateName = template.Name,
                TemplateFile = "ARMY__02.DAT", TemplateIndex = templateIndex, RecordBytes = raw.Length,
                RawX = (uint)(cell.X * 32 + 16), RawY = (uint)(cell.Y * 32 + 16), RawLayer = 1 },
            Template = template, Parameters = parameters, Record = raw, Rules = Rules.Units[template.Name], Team = 0,
            Health = Rules.Units[template.Name].Health };
    }
    private bool SpendWood(uint amount)
    {
        // Tutorial prices use only the confirmed +1C slot. Other original costs are not silently assigned names.
        var payment = OriginalConstructionRules.TryDebitUpfront(new(0, Wood, 0, 0), new(0, amount, 0, 0));
        if (!payment.Accepted) return false;
        Wood = payment.Balances.Second; return true;
    }
    public SkirmishActor? TrainWorker()
    {
        if (Paused || Outcome != "" || !PlayerCastle.Alive) return null;
        if (Actors.Count(a => a.Worker && a.Team == 0 && a.Alive) >= Rules.Economy.WorkerLimit)
        { Log("Достигнут предел рабочих этой схватки."); return null; }
        if (Wood < Rules.Economy.WorkerPrice) { Log("Недостаточно дерева для найма рабочего."); return null; }
        var worker = NewEconomyActor(10, PlayerCastle.Cell, true);
        var cells = new List<Vector2I>();
        for (int y = -6; y <= 6; y++) for (int x = -6; x <= 6; x++) cells.Add(PlayerCastle.Cell + new Vector2I(x, y));
        var spawn = cells.Where(c => CanStand(worker, c)).OrderBy(c => ((Vector2)c).DistanceSquaredTo(Hero.Cell)).Cast<Vector2I?>().FirstOrDefault();
        if (spawn == null) { Log("Вокруг замка нет свободного места для рабочего."); return null; }
        BinaryPrimitives.WriteUInt32LittleEndian(worker.Record.AsSpan(4), (uint)(spawn.Value.X * 32 + 16));
        BinaryPrimitives.WriteUInt32LittleEndian(worker.Record.AsSpan(8), (uint)(spawn.Value.Y * 32 + 16));
        worker.Source.RawX = (uint)(spawn.Value.X * 32 + 16); worker.Source.RawY = (uint)(spawn.Value.Y * 32 + 16);
        if (!SpendWood(Rules.Economy.WorkerPrice)) return null;
        Actors.Add(worker); Occupancy.WriteFootprint(1, 1, 1, (uint)worker.Position.X, (uint)worker.Position.Y, worker.Id);
        Log("Рабочий нанят. Выберите его и нажмите ПКМ по лесу."); return worker;
    }
    public ConstructionPlacement ProbeMill(Vector2I cell)
    {
        if (!Inside(cell) || !PlayerCastle.Alive || (new Vector2(cell.X * 32 + 16, cell.Y * 32 + 16)).DistanceTo(PlayerCastle.Position) > Rules.Economy.BuildRadius)
            return new(ConstructionPlacementStatus.Rejected);
        var parameters = MillTemplate.Construction;
        var result = OriginalConstructionRules.ProbePlacement(Occupancy.Width, Occupancy.Height, Map.Cells, _properties, Occupancy.Cells,
            parameters.FootprintWidth, parameters.FootprintHeight, parameters.TerrainMask.ToArray(), (uint)(cell.X * 32 + 16), (uint)(cell.Y * 32 + 16));
        if (result.Status != ConstructionPlacementStatus.Clear) return result;
        var area = new Rect2I(cell - new Vector2I(parameters.FootprintWidth / 2, parameters.FootprintHeight / 2), new(parameters.FootprintWidth, parameters.FootprintHeight));
        if (_reservations.Any(r => area.HasPoint(r.Key))) return new(ConstructionPlacementStatus.Occupied);
        return result;
    }
    public SkirmishActor? BuildMill(Vector2I cell)
    {
        if (Paused || Outcome != "" || !PlayerCastle.Alive) return null;
        if (ProbeMill(cell).Status != ConstructionPlacementStatus.Clear) { Log("Здесь нельзя строить: нужна свободная площадка рядом с замком."); return null; }
        if (!SpendWood(Rules.Economy.MillPrice)) { Log("Недостаточно дерева для лесопилки."); return null; }
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
            worker.GatherTree = tree; worker.PreferredMill = 0; worker.Goal = null; worker.Target = 0;
            worker.Route.Clear(); worker.HarvestPrepared = false; worker.ReplanTimer = 0; count++;
        }
        if (count > 0) Log($"Рубка леса: {count} раб. Груз будет доставляться в готовую лесопилку.");
        return count;
    }
    public bool DeliverTimber(IEnumerable<uint> ids, uint millId)
    {
        if (Paused || Outcome != "" || Find(millId) is not { Team: 0 } mill || mill.Source.TemplateName != "Лесопилка") return false;
        bool accepted = false;
        foreach (var worker in ids.Select(Find).OfType<SkirmishActor>().Where(a => a.Team == 0 && a.Worker && a.Record[0x31] == 3))
        { worker.PreferredMill = millId; worker.Route.Clear(); worker.Goal = null; worker.ReplanTimer = 0; accepted = true; }
        return accepted;
    }
    private void TickBuildings()
    {
        foreach (var mill in Actors.Where(a => a.Alive && a.Team == 0 && a.Source.TemplateName == "Лесопилка"))
        {
            if (mill.ConstructionRemaining > 0)
            {
                var maintenance = OriginalBuildingRules.StepMaintenance(new(mill.Record[0x19], mill.Record[0x16], mill.Record[0x0f],
                        BinaryPrimitives.ReadUInt16LittleEndian(mill.Record.AsSpan(0x17))),
                    new(mill.Template.InitialCondition, mill.Parameters[0x57], new(
                        BinaryPrimitives.ReadUInt32LittleEndian(mill.Parameters.AsSpan(0x8c)),
                        BinaryPrimitives.ReadUInt32LittleEndian(mill.Parameters.AsSpan(0x90)),
                        BinaryPrimitives.ReadUInt32LittleEndian(mill.Parameters.AsSpan(0x94)))), default, false);
                mill.Record[0x19] = maintenance.State.Condition; mill.Record[0x16] = maintenance.State.Counter; mill.Record[0x0f] = maintenance.State.WorkFlags;
                BinaryPrimitives.WriteUInt16LittleEndian(mill.Record.AsSpan(0x17), maintenance.State.ObjectFlags);
                mill.ConstructionRemaining = maintenance.ContinueAction
                    ? ((mill.Template.InitialCondition - maintenance.State.Condition + 1) * Math.Max(1, (int)mill.Parameters[0x57]) - maintenance.State.Counter) / (double)Rules.TicksPerSecond : 0;
                if (maintenance.ContinueAction) continue;
                var completed = OriginalBuildingLifecycle.CompleteCurrentAction(new(mill.Record[0x14], mill.Record[0x15], mill.Record[0x0f]));
                mill.Record[0x14] = completed.EncodedState; mill.Record[0x15] = completed.Auxiliary; mill.Record[0x0f] = completed.WorkFlags;
                BinaryPrimitives.WriteUInt16LittleEndian(mill.Record.AsSpan(1), (ushort)(mill.Template.InitialFrame + mill.Parameters[0x4a]));
                Log("Лесопилка готова.");
            }
            var state = OriginalLumbermillRules.Process(new(mill.Record[0x23], mill.Record[0x24], Wood, ProducedWood), mill.Parameters[0x79]);
            mill.Record[0x23] = state.Countdown; mill.Record[0x24] = state.QueuedLoads;
            Wood = state.Stock; ProducedWood = state.Statistics;
        }
    }
    private void SetWorkerAppearance(SkirmishActor worker, bool loaded)
    {
        int index = loaded ? 11 : 10;
        worker.Source.TemplateIndex = index; worker.Template = _templates.Armies["ARMY__02.DAT"].Templates[index];
        worker.Parameters = Convert.FromHexString(worker.Template.RawRecordHex); worker.Record[0x0e] = (byte)index;
        ushort frame = unchecked((ushort)(worker.Template.InitialFrame + (byte)((worker.Record[0x25] & 7) * worker.Parameters[0x4a])));
        BinaryPrimitives.WriteUInt16LittleEndian(worker.Record.AsSpan(1), frame);
    }
    private void TickWorker(SkirmishActor worker, double dt)
    {
        if (worker.Stepping) { MoveTick(worker); return; }
        if (worker.GatherTree == null && worker.PreferredMill == 0)
        {
            worker.JobText = worker.Record[0x31] == 3 ? "Несёт древесину · укажите лесопилку" : "Без задания · ПКМ по лесу";
            if (worker.Goal is { } goal && worker.Route.Count == 0 && worker.ReplanTimer <= 0)
            { if (worker.Cell == goal) worker.Goal = null; else { Plan(worker, goal); worker.ReplanTimer = .7; } }
            MoveTick(worker); return;
        }
        if (worker.Record[0x31] == 3)
        {
            var mill = Actors.Where(a => a.Alive && a.Team == 0 && a.Source.TemplateName == "Лесопилка" && a.ConstructionRemaining <= 0)
                .OrderBy(a => a.Id == worker.PreferredMill ? 0 : 1).ThenBy(a => a.Position.DistanceSquaredTo(worker.Position)).FirstOrDefault();
            if (mill == null) { worker.JobText = "С грузом · нужна готовая лесопилка"; return; }
            worker.JobText = "Доставляет древесину";
            if (Distance(worker.Position, mill) <= worker.Rules.Range && mill.Record[0x24] < 64)
            {
                ushort millFlags = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(mill.Record.AsSpan(0x17)) | 0x40);
                var state = OriginalLumbermillRules.Receive(millFlags, mill.Record[0x24], BinaryPrimitives.ReadUInt16LittleEndian(worker.Record.AsSpan(0x17)),
                    worker.Record[0x31], BinaryPrimitives.ReadUInt32LittleEndian(worker.Parameters.AsSpan(0x40)));
                BinaryPrimitives.WriteUInt16LittleEndian(mill.Record.AsSpan(0x17), state.MillFlags); mill.Record[0x24] = state.QueuedLoads;
                BinaryPrimitives.WriteUInt16LittleEndian(worker.Record.AsSpan(0x17), state.WorkerFlags); worker.Record[0x31] = state.Cargo;
                if (state.Accepted) { SetWorkerAppearance(worker, false); worker.PreferredMill = 0; worker.Route.Clear(); worker.HarvestPrepared = false; }
            }
            else if (worker.ReplanTimer <= 0) { PlanAttack(worker, mill); worker.ReplanTimer = .7; }
            MoveTick(worker); return;
        }
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
                worker.Record[0x2f] = 0; worker.HarvestPrepared = true;
            }
            worker.JobText = $"Рубит дерево · {worker.Record[0x34] & 31}";
            if ((worker.Record[0x34] & 31) != 0)
            {
                if (OriginalForestryRules.AdvanceWork(worker.Record, worker.Parameters) == HarvestWork.SwingQueued)
                {
                    // 945E resets the gate at animation impact. Full animation dispatch is still provisional.
                    worker.Record[0x2f] = 0;
                    Effects.Add(new BattleEffect { From = worker.Position, To = point, Team = 0, Remaining = .12 });
                }
            }
            else
            {
                bool clear = OriginalForestryRules.FinishLoad(worker.Record);
                if (clear && _terrainCatalog.ClearTimber(Map.Cells, Occupancy.Width, Occupancy.Height, tree.X, tree.Y) > 0)
                { TerrainRevision++; if (!Harvestable(tree)) ClearedGroves.Add(tree); }
                worker.Record[0x31] = 3; SetWorkerAppearance(worker, true);
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
