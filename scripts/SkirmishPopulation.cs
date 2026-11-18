using System.Buffers.Binary;
using Godot;
namespace Northwind;

public sealed partial class Skirmish
{
    public uint FreePeople { get; private set; }
    public PopulationTotals Population { get; private set; }
    private uint _populationTick;
    private ObjectRecord _militiaSeed = null!;
    public TemplateEntry FarmTemplate => _templates.Armies["ARMY__02.DAT"].Templates[9];
    public double? PopulationGrowthSeconds => unchecked(FreePeople + Population.Used) < Population.Limit
        ? (OriginalPopulationRules.GrowthPeriod(Population.FarmSupport) - _populationTick % OriginalPopulationRules.GrowthPeriod(Population.FarmSupport)) / (double)Rules.TicksPerSecond : null;
    private void InitializePopulation(ScenarioData scenario)
    {
        _militiaSeed = scenario.Slots.SelectMany(s => s.Objects).First(o => o.TemplateFile == "ARMY__02.DAT" && o.TemplateIndex == 0);
        RecountPopulation();
    }
    private void RecountPopulation()
    {
        var objects = new List<PopulationObject>();
        var visible = Actors.ToDictionary(a => a.Id);
        void Add(byte[] record, byte[] template, bool alive)
        {
            if (record[3] != Rules.PlayerSlot) return;
            objects.Add(new(alive ? record[0x19] : (byte)0, template[0x46], BinaryPrimitives.ReadUInt32LittleEndian(template.AsSpan(0x40)),
                BinaryPrimitives.ReadUInt32LittleEndian(template.AsSpan(0x88)), BinaryPrimitives.ReadUInt32LittleEndian(template.AsSpan(0x78)),
                BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8))));
        }
        foreach (var (id, obj) in _sourceMovementObjects)
            if (!visible.ContainsKey(id)) Add(obj.Record, obj.Template, true);
        foreach (var actor in Actors) Add(actor.Record, actor.Parameters, actor.Alive);
        Population = OriginalPopulationRules.Recount(Occupancy.Width, Occupancy.Height, objects);
    }
    private void TickPopulation()
    {
        _populationTick = unchecked(_populationTick + 1);
        if ((_populationTick & 31) == 0) RecountPopulation();
        FreePeople = OriginalPopulationRules.Grow(_populationTick, FreePeople, Population.Used, Population.FarmSupport, Population.Limit);
    }
    public SkirmishActor? TrainMilitia()
    {
        if (Paused || Outcome != "" || !PlayerCastle.Alive) return null;
        if (!CanAfford(MilitiaTemplate)) { Log(MissingResources(MilitiaTemplate)); return null; }
        var soldier = NewEconomyActor(0, PlayerCastle.Cell, false, true);
        if (!PlaceRecruit(soldier)) { Log("Вокруг замка нет свободного места для отряда."); return null; }
        if (!Spend(MilitiaTemplate)) return null;
        soldier.Route.Attach(_routes, soldier.Record); Actors.Add(soldier);
        Occupancy.WriteFootprint(soldier.Parameters[3], soldier.Parameters[4], soldier.Record[0x0d], (uint)soldier.Position.X, (uint)soldier.Position.Y, soldier.Id);
        RecountPopulation(); Log("Ополченец нанят. Выделите его и отдайте приказ."); return soldier;
    }
    private bool PlaceRecruit(SkirmishActor recruit)
    {
        var cells = new List<Vector2I>();
        for (int y = -6; y <= 6; y++) for (int x = -6; x <= 6; x++) cells.Add(PlayerCastle.Cell + new Vector2I(x, y));
        var cell = cells.Where(c => CanStand(recruit, c)).OrderBy(c => ((Vector2)c).DistanceSquaredTo(Hero.Cell)).Cast<Vector2I?>().FirstOrDefault();
        if (cell == null) return false;
        uint xRaw = (uint)(cell.Value.X * 32 + 16), yRaw = (uint)(cell.Value.Y * 32 + 16);
        BinaryPrimitives.WriteUInt32LittleEndian(recruit.Record.AsSpan(4), xRaw); BinaryPrimitives.WriteUInt32LittleEndian(recruit.Record.AsSpan(8), yRaw);
        recruit.Source.RawX = xRaw; recruit.Source.RawY = yRaw; return true;
    }
    public ConstructionPlacement ProbeFarm(Vector2I cell) => ProbeEconomyBuilding(cell, FarmTemplate);
    public SkirmishActor? BuildFarm(Vector2I cell)
    {
        if (Paused || Outcome != "" || !PlayerCastle.Alive) return null;
        if (ProbeFarm(cell).Status != ConstructionPlacementStatus.Clear) { Log("Для фермы нужна свободная площадка рядом с замком."); return null; }
        if (!Spend(FarmTemplate)) return null;
        var farm = NewEconomyActor(9, cell, false);
        farm.ConstructionDuration = farm.ConstructionRemaining = farm.Template.InitialCondition * Math.Max(1, (int)farm.Parameters[0x57]) / (double)Rules.TicksPerSecond;
        OriginalBuildingLifecycle.MarkConstruction(farm.Record); Actors.Add(farm);
        Occupancy.WriteFootprint(farm.Parameters[3], farm.Parameters[4], farm.Record[0x0d], (uint)farm.Position.X, (uint)farm.Position.Y, farm.Id);
        Log("Ферма строится. Следите за поддержкой поселения и свободными людьми."); return farm;
    }
}
