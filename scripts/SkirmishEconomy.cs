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
    public TemplateEntry BarracksTemplate => _templates.Armies["ARMY__02.DAT"].Templates[3];
    public TemplateEntry MineTemplate => _templates.Armies["ARMY__02.DAT"].Templates[6];
    public TemplateEntry MillTemplate => _templates.Armies["ARMY__02.DAT"].Templates[4];
    private TemplatesData _templates = null!;
    private ObjectRecord _workerSeed = null!;
    // Experimental runtime allocation, deliberately outside first-map source references.
    private uint _nextEconomyId = 0x100000;

    private void InitializeEconomy(ScenarioData scenario, TemplatesData templates)
    {
        _templates = templates;
        var slot = scenario.Slots.Single(s => s.SlotIndex == Rules.PlayerSlot);
        var start = Rules.Economy.StartingResources ?? OriginalResourceRules.ReadScenario(Convert.FromHexString(slot.RawRecordHex));
        SetResources(new ResourceQuad(start.First * Rules.PlayerStartBoost, start.Second * Rules.PlayerStartBoost, start.Third * Rules.PlayerStartBoost, start.Fourth));   // TEMPORARY boost, see SkirmishRules.PlayerStartBoost
        _terrainCatalog = new OriginalTerrainCatalog(_properties, GameData.Read<OriginalTerrainData>("res://data/original_terrain.json"));
        _workerSeed = scenario.Slots.SelectMany(s => s.Objects)
            .First(o => o.TemplateFile == "ARMY__02.DAT" && o.TemplateIndex == 10);
    }
    /// <summary>The unit stands among trees (a timber cell): hidden from the others, visible to its owner only as a marker.</summary>
    public bool InForestCell(Vector2I cell) => Inside(cell) && (_properties[(int)(Map.Cells[cell.Y * Map.Width + cell.X] & 0xffff) * 8] & 7) == 1;
    public bool InForest(SkirmishActor actor) => !actor.Structure && InForestCell(actor.Cell);   // terrain class 1 (LE:1 6859: a foreign object there is not pickable)
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
    private SkirmishActor NewEconomyActor(int templateIndex, Vector2I cell, bool worker, bool soldier = false, int team = 0, string file = "ARMY__02.DAT")
    {
        var template = _templates.Armies[file].Templates[templateIndex];
        var parameters = Convert.FromHexString(template.RawRecordHex);
        var raw = worker ? Convert.FromHexString(_workerSeed.RawRecordHex) : soldier ? Convert.FromHexString((_soldierSeeds.GetValueOrDefault(templateIndex) ?? _militiaSeed).RawRecordHex).AsSpan(0, template.RuntimeRecordBytes).ToArray() : new byte[template.RuntimeRecordBytes];
        raw[0] = (byte)(file[7] - '0'); raw[3] = (byte)SlotOfTeam(team); raw[0x0d] = 1; raw[0x0e] = (byte)templateIndex;
        raw[0x14] = 0x80; raw[0x19] = raw[0x1a] = template.InitialCondition; raw[0x20] = 0xff; // A855: visible to every owner
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(1), template.InitialFrame);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x17), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), worker ? (uint)(cell.X * 32 + 16) : OriginalWorkerDelivery.BuildingAnchor((byte)cell.X, parameters[3]));
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), worker ? (uint)(cell.Y * 32 + 16) : OriginalWorkerDelivery.BuildingAnchor((byte)cell.Y, parameters[4]));
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x10), (uint)raw.Length);
        if (worker || soldier) { raw[0x24] = 0; raw[0x25] = 0; raw[0x26] = 0; raw[0x2c] = 0; raw[0x2d] = 0; raw[0x30] = 0; if (worker) raw[0x31] = 0; }
        return new SkirmishActor { Source = new ObjectRecord { RuntimeReference = _nextEconomyId++, TemplateName = template.Name,
                TemplateFile = file, TemplateIndex = templateIndex, RecordBytes = raw.Length,
                RawX = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4)), RawY = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(8)), RawLayer = 1 },
            Template = template, Parameters = parameters, Record = raw, Rules = SpecOf(file, template, parameters), Team = team };
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
        // Sawmills and mines may be raised anywhere on the map (user, 22.11); the other buildings keep the radius around the castle.
        bool anywhere = template.Name is "Лесопилка" or "Шахта";
        if (!Inside(cell) || !PlayerCastle.Alive || (!anywhere && (new Vector2(cell.X * 32 + 16, cell.Y * 32 + 16)).DistanceTo(PlayerCastle.Position) > Rules.Economy.BuildRadius))
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
    /// <summary>The «Ремонт» button (command slot 1, state 48h → 1 with WorkFlags = 1, docs/building-command-lifecycle.md): the building's condition rises by one
    /// every DAT[57] updates and each step pays DAT[8C, 90, 94] from the player's stores (the maintenance step C80D); it stops when the stores run dry or the condition
    /// reaches DAT[45].</summary>
    public bool StartRepair(SkirmishActor building, bool byAi = false)
    {
        if (Paused || Outcome != "" || (building.Team != 0 && !byAi) || !building.Structure || !building.Alive || building.Ruined || building.ConstructionRemaining > 0 || building.Repairing) return false;
        if ((sbyte)building.Record[0x19] >= building.Template.InitialCondition) { if (!byAi) Log($"{building.Source.TemplateName}: ремонт не нужен."); return false; }
        building.Repairing = true; building.RepairFlags = building.Record[0x0f]; building.Record[0x16] = 0;
        if (!byAi) Log($"{building.Source.TemplateName}: идёт ремонт.");
        return true;
    }
    /// <summary>The maintenance step C80D of every building under repair (the building keeps working: its production and attack go on), paid from the owner's stores
    /// (WorkFlags = 1 inside the step); the AI starts a repair as soon as one of its finished buildings is damaged (improved AI only).</summary>
    private void TickRepairs()
    {
        if (ImprovedAi && (_globalTick & 3) == 0)
            foreach (var b in Actors)
                if (b.Team != 0 && b.Alive && b.Structure && !b.Repairing && b.ConstructionRemaining <= 0 && !b.Ruined && (sbyte)b.Record[0x19] < b.Template.InitialCondition && b.Record[0x19] > 0)
                    StartRepair(b, byAi: true);
        foreach (var b in Actors.Where(a => a.Repairing).ToArray())
        {
            if (!b.Alive || b.Ruined || b.ConstructionRemaining > 0) { b.Repairing = false; b.Record[0x0f] = b.RepairFlags; continue; }
            var wallet = Wallet(b.Team);
            var m = OriginalBuildingRules.StepMaintenance(new(b.Record[0x19], b.Record[0x16], 1, BinaryPrimitives.ReadUInt16LittleEndian(b.Record.AsSpan(0x17))),
                new(b.Template.InitialCondition, b.Parameters[0x57], new(BinaryPrimitives.ReadUInt32LittleEndian(b.Parameters.AsSpan(0x8c)),
                    BinaryPrimitives.ReadUInt32LittleEndian(b.Parameters.AsSpan(0x90)), BinaryPrimitives.ReadUInt32LittleEndian(b.Parameters.AsSpan(0x94)))),
                new(wallet.First, wallet.Second, wallet.Third), false);
            b.Record[0x19] = m.State.Condition; b.Record[0x16] = m.State.Counter;
            SetWallet(b.Team, new ResourceQuad(m.Balances.First, m.Balances.Second, m.Balances.Third, wallet.Fourth));
            if (m.ContinueAction) continue;
            b.Repairing = false; b.Record[0x0f] = b.RepairFlags;
            if (b.Team == 0) Log(b.Record[0x19] >= b.Template.InitialCondition ? $"{b.Source.TemplateName}: ремонт закончен." : $"{b.Source.TemplateName}: ремонт остановлен — не хватает ресурсов.");
        }
    }
    /// <summary>The «Разрушить» button: the player pulls his own building down.</summary>
    public bool Demolish(SkirmishActor building)
    {
        if (Paused || Outcome != "" || building.Team != 0 || !building.Structure || !building.Alive || building == PlayerCastle) return false;
        building.Repairing = false; building.ConstructionRemaining = 0; building.Record[0x19] = 0; Kill(building); return true;
    }
    public TemplateEntry FactoryTemplate => _templates.Armies["ARMY__02.DAT"].Templates[15];
    public static bool IsFactory(SkirmishActor actor) => actor.Alive && actor.Team == 0 && actor.Source.TemplateName == "Завод" && actor.ConstructionRemaining <= 0;
    public TemplateEntry TowerTemplate => _templates.Armies["ARMY__02.DAT"].Templates[13];
    public TemplateEntry TemplateOf(int index) => _templates.Armies["ARMY__02.DAT"].Templates[index];
    public ConstructionPlacement ProbeFactory(Vector2I cell) => ProbeEconomyBuilding(cell, FactoryTemplate);
    public ConstructionPlacement ProbeIndex(int index, Vector2I cell) => ProbeEconomyBuilding(cell, TemplateOf(index));
    public SkirmishActor? BuildFactory(Vector2I cell) => BuildIndex(15, cell);
    /// <summary>A building of the castle's or the factory's list at the cell (the factory 15, the tower 13): near the castle, paid up front, built over time.</summary>
    public SkirmishActor? BuildIndex(int index, Vector2I cell)
    {
        if (Paused || Outcome != "" || !PlayerCastle.Alive) return null;
        var template = TemplateOf(index); string name = template.Name.ToLowerInvariant();
        if (ProbeIndex(index, cell).Status != ConstructionPlacementStatus.Clear) { Log($"Для постройки ({name}) нужна свободная площадка рядом с замком."); return null; }
        if (!Spend(template)) return null;
        var factory = NewEconomyActor(index, cell, false);
        factory.ConstructionDuration = factory.ConstructionRemaining = factory.Template.InitialCondition * Math.Max(1, (int)factory.Parameters[0x57]) / (double)Rules.TicksPerSecond;
        OriginalBuildingLifecycle.MarkConstruction(factory.Record);
        if (factory.Record.Length >= 0x28) { factory.Record.AsSpan(0x22, 5).Fill(0xff); factory.Record[0x27] = 0xff; }   // producers' queue (the tower's record is shorter)
        factory.Record[0x20] = 0xff;
        if (index == 12) { factory.Record[0x22] = factory.Record[0x23] = 1; factory.Record[0x15] = 0; }   // the laboratory's AH=0
        Actors.Add(factory);
        Occupancy.WriteFootprint(factory.Parameters[3], factory.Parameters[4], factory.Record[0x0d], (uint)factory.Position.X, (uint)factory.Position.Y, factory.Id);
        Log(index == 15 ? "Завод строится. Мосты и башни строят через него." : "Башня строится."); return factory;
    }
    public ConstructionPlacement ProbeBarracks(Vector2I cell) => ProbeEconomyBuilding(cell, BarracksTemplate);
    public SkirmishActor? BuildBarracks(Vector2I cell)
    {
        if (Paused || Outcome != "" || !PlayerCastle.Alive) return null;
        if (ProbeBarracks(cell).Status != ConstructionPlacementStatus.Clear) { Log("Для казармы нужна свободная площадка рядом с замком."); return null; }
        if (!Spend(BarracksTemplate)) return null;
        var barracks = NewEconomyActor(3, cell, false);
        barracks.ConstructionDuration = barracks.ConstructionRemaining = barracks.Template.InitialCondition * Math.Max(1, (int)barracks.Parameters[0x57]) / (double)Rules.TicksPerSecond;
        OriginalBuildingLifecycle.MarkConstruction(barracks.Record);
        barracks.Record.AsSpan(0x22, 5).Fill(0xff);
        barracks.Record[0x20] = 0xff; barracks.Record[0x27] = 0xff;
        Actors.Add(barracks);
        Occupancy.WriteFootprint(barracks.Parameters[3], barracks.Parameters[4], barracks.Record[0x0d], (uint)barracks.Position.X, (uint)barracks.Position.Y, barracks.Id);
        Log("Казарма строится. Ополченцев можно заказать после завершения."); return barracks;
    }
    public MineDeposit MineDepositAt(Vector2I cell)
    {
        var parameters = MineTemplate.Construction;
        uint x = OriginalWorkerDelivery.BuildingAnchor((byte)cell.X, parameters.FootprintWidth);
        uint y = OriginalWorkerDelivery.BuildingAnchor((byte)cell.Y, parameters.FootprintHeight);
        return OriginalMineRules.Initialize(Map.Cells, Map.Width, Map.Height, x, y);
    }
    public ConstructionPlacement ProbeMine(Vector2I cell) => ProbeEconomyBuilding(cell, MineTemplate);
    public SkirmishActor? BuildMine(Vector2I cell)
    {
        if (Paused || Outcome != "" || !PlayerCastle.Alive) return null;
        if (ProbeMine(cell).Status != ConstructionPlacementStatus.Clear) { Log("Для шахты нужна свободная площадка рядом с замком."); return null; }
        if (!Spend(MineTemplate)) return null;
        var mine = NewEconomyActor(6, cell, false);
        var deposit = OriginalMineRules.Initialize(Map.Cells, Map.Width, Map.Height, (uint)mine.Position.X, (uint)mine.Position.Y);
        mine.Record[0x23] = deposit.Type;
        BinaryPrimitives.WriteUInt32LittleEndian(mine.Record.AsSpan(0x24), deposit.Remaining);
        mine.ConstructionDuration = mine.ConstructionRemaining = mine.Template.InitialCondition * Math.Max(1, (int)mine.Parameters[0x57]) / (double)Rules.TicksPerSecond;
        OriginalBuildingLifecycle.MarkConstruction(mine.Record);
        Actors.Add(mine);
        Occupancy.WriteFootprint(mine.Parameters[3], mine.Parameters[4], mine.Record[0x0d], (uint)mine.Position.X, (uint)mine.Position.Y, mine.Id);
        Log($"Шахта строится. Рудных клеток вокруг: {deposit.Count}; руда: {OreName(deposit.Type)}."); return mine;
    }
    public int GatherTimber(IEnumerable<uint> ids, Vector2I tree)
    {
        if (Paused || Outcome != "" || !Harvestable(tree)) return 0;
        int count = 0;
        foreach (var worker in ids.Select(Find).OfType<SkirmishActor>().Where(a => a.Team == 0 && a.Worker))
        {
            worker.LastJob = new WorkerJob(WorkerJobKind.Timber, tree, 0); worker.IdleSince = -1;
            worker.GatherTree = tree; worker.PreferredMill = 0; worker.MineJob = false; ClearDelivery(worker); worker.Goal = null; worker.Target = 0;
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
        { ClearDelivery(worker); worker.MineJob = false; worker.PreferredMill = millId; worker.Route.Clear(); worker.Goal = null; worker.ReplanTimer = 0; accepted = true; }
        return accepted;
    }
    /// <summary>C8E0..CA48 for the buildings whose frames are in the atlas: the working animation, the stage frame that follows the condition
    /// (also while a building is raised, its condition climbing, and while it is damaged),
    /// the slow decay of a ruin. Debris (9727) is not drawn; a collapsed building (condition below zero) is removed.</summary>
    private void TickBuildingFrames()
    {
        foreach (var building in Actors.Where(a => a.Structure && (sbyte)a.Record[0x19] > 0 && a.Index02 is 2 or 3 or 4 or 5 or 6 or 7 or 9 or 12 or 13 or 15 or 22 or 23 or 24).ToArray())
        {
            try { OriginalBuildingFrames.Update(building.Record, building.Parameters, _globalTick, (_, _, _, _) => { }, () => { building.Record[0x19] = 0; Kill(building); }); }
            catch (Exception e) when (e is DivideByZeroException or OverflowException) { }
            // The object update 42BB keeps OBE[1A] = the condition of the previous update, so that a drop is seen (and its frame chosen) once.
            if (building.Alive) building.Record[0x1a] = building.Record[0x19];
        }
    }

    private void TickBuildings()
    {
        foreach (var building in Actors.Where(a => a.Alive && a.Structure && (a.ConstructionRemaining > 0 || a.Source.TemplateName is "Лесопилка" or "Ферма" or "Казарма" or "Шахта" or "Мост")).ToArray())
        {
            if (building.Team != 0 && building.ConstructionRemaining <= 0 && building.Source.TemplateName != "Лесопилка") continue;
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
                if (building.Source.TemplateName is "Ферма" or "Казарма" or "Шахта" or "Мост" or "Башня" or "Забор")
                    BinaryPrimitives.WriteUInt16LittleEndian(building.Record.AsSpan(1), (ushort)(LineShapeFrames(building) + building.Template.InitialFrame + Math.Min(building.Parameters[0x4a], building.Record[0x19] * (building.Parameters[0x4a] + 1) / (building.Template.InitialCondition + 1))));
                if (maintenance.ContinueAction) continue;
                var completed = OriginalBuildingLifecycle.CompleteCurrentAction(new(building.Record[0x14], building.Record[0x15], building.Record[0x0f]));
                building.Record[0x14] = completed.EncodedState; building.Record[0x15] = completed.Auxiliary; building.Record[0x0f] = completed.WorkFlags;
                BinaryPrimitives.WriteUInt16LittleEndian(building.Record.AsSpan(1), (ushort)(LineShapeFrames(building) + building.Template.InitialFrame + building.Parameters[0x4a]));
                if (building.Team == 0) { Log(building.Source.TemplateName + " готова."); var tree0 = TreeOf(Rules.PlayerSlot); if (tree0.Length > 0) { OriginalTechRules.Complete(tree0, 1, (byte)building.Source.TemplateIndex); if (Population.FarmSupport is >= 0x28 and <= 0x32) OriginalTechRules.Complete(tree0, 2, 0); } }   // AFDB's end: farm support 28h..32h is event (0, 2), as for the AI
                if (building.Team != 0) { EnemyBuildingFinished(building); continue; }
            }
            if (building.Source.TemplateName != "Лесопилка") continue;
            // The enemy's lumbermill credits the enemy's wood (participant +1C); its statistics are not kept (adapter).
            var wallet = Wallet(building.Team);
            var state = OriginalLumbermillRules.Process(new(building.Record[0x23], building.Record[0x24], wallet.Second, building.Team == 0 ? ProducedWood : 0), building.Parameters[0x79]);
            building.Record[0x23] = state.Countdown; building.Record[0x24] = state.QueuedLoads;
            if (building.Team == 0) { Wood = state.Stock; ProducedWood = state.Statistics; } else SetWallet(building.Team, wallet with { Second = state.Stock });
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
        if (mill == null || mill.Team != worker.Team || OriginalWorkerDelivery.FindReceiver((uint)worker.Position.X, (uint)worker.Position.Y, [ReceiverCandidate(mill)]) == 0)
        {
            ClearDelivery(worker); worker.Route.Clear();
            if (worker.Stepping) { worker.Record[0x2d] &= 15; worker.JobText = "Получатель недоступен"; MoveTick(worker); return; }
            worker.Record[0x2d] = 0;
            var preferred = Find(worker.PreferredMill);
            uint selected = preferred != null && preferred.Team == worker.Team ? OriginalWorkerDelivery.FindReceiver((uint)worker.Position.X, (uint)worker.Position.Y, [ReceiverCandidate(preferred)]) : 0;
            if (selected == 0) selected = OriginalWorkerDelivery.FindReceiver((uint)worker.Position.X, (uint)worker.Position.Y, Actors.Where(a => a.Team == worker.Team).Select(ReceiverCandidate));
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
        WorkerRetryTick(worker);
        // WRK 883 calls the animation core before the work dispatcher; 856C waits while state 3 is active.
        if (OriginalWorkerAnimation.Pending(worker.Record))
        {
            var impact = OriginalWorkerAnimation.Step(worker.Record, worker.Parameters);
            if (impact is { } hit) ChopHit(worker, hit);
            if (OriginalWorkerAnimation.Pending(worker.Record))
            { worker.JobText = (worker.Record[0x2d] & 15) == 1 ? "Поворачивается к лесопилке" : "Рубит дерево"; return; }
        }
        if (worker.MineJob || worker.Index02 == OriginalOreRules.OreWorker) { TickOreWorker(worker); return; }
        if (worker.Index02 == 11 && worker.Record[0x31] == 0)
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
            { if (worker.Cell == goal) worker.Goal = null; else { Plan(worker, goal); worker.ReplanTimer = ReplanDelay; } }
            MoveTick(worker); return;
        }
        if (worker.Record[0x31] == 3) { TickDelivery(worker); return; }
        var tree = worker.GatherTree!.Value;
        // Before (re)starting a load the worker checks that the tree can be felled: it still stands, nobody else fells it and a side is free to stand on; if not it
        // takes the nearest neighbouring free tree instead of waiting (user, 22.11).
        // (Improved AI mode only: the original AI's checks depend on the old timing.)
        if (!Harvestable(tree) || (ImprovedAi && !worker.HarvestPrepared && (TreeTakenByOther(worker, tree) || !HasFreeApproach(worker, tree))))
        {
            if (!(ImprovedAi && SwitchTree(worker, tree)) && !Harvestable(tree) && !FindNextGrove(worker)) return;
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
                FellOnWorkers(worker, tree);
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
            bool planned = false;
            foreach (var cell in candidates) if (Plan(worker, cell)) { planned = true; break; }
            // No side can be reached: go to a neighbouring free tree rather than standing and waiting.
            if (!planned && !SearchTimeUp && ImprovedAi) SwitchTree(worker, tree);
            worker.ReplanTimer = ReplanDelay;
        }
        MoveTick(worker);
    }
}
