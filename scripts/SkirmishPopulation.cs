using System.Buffers.Binary;
using Godot;
namespace Northwind;

public sealed partial class Skirmish
{
    public uint FreePeople { get; private set; }
    /// <summary>Test and observation hook: free people the farms would supply later.</summary>
    public void GrantPeople(uint count) => FreePeople += count;
    public PopulationTotals Population { get; private set; }
    private uint _populationTick;
    private ObjectRecord _militiaSeed = null!;
    private readonly Dictionary<int, ObjectRecord> _soldierSeeds = [];
    public TemplateEntry FarmTemplate => _templates.Armies["ARMY__02.DAT"].Templates[9];
    public double? PopulationGrowthSeconds => unchecked(FreePeople + Population.Used) < Population.Limit
        ? (OriginalPopulationRules.GrowthPeriod(Population.FarmSupport) - _populationTick % OriginalPopulationRules.GrowthPeriod(Population.FarmSupport)) / (double)Rules.TicksPerSecond : null;
    private void InitializePopulation(ScenarioData scenario)
    {
        _militiaSeed = scenario.Slots.SelectMany(s => s.Objects).First(o => o.TemplateFile == "ARMY__02.DAT" && o.TemplateIndex == 0);
        // A recruit copies a scenario object of its own template (militia, archer…); the militia record is the fallback.
        foreach (var o in scenario.Slots.SelectMany(s => s.Objects).Where(o => o.TemplateFile == "ARMY__02.DAT"))
            _soldierSeeds.TryAdd(o.TemplateIndex, o);
        RecountPopulation();
    }
    private void RecountPopulation() => Population = PopulationOf((byte)Rules.PlayerSlot);
    /// <summary>Population totals of one settlement (the player's, or the enemy's for its AI).</summary>
    private PopulationTotals PopulationOf(byte owner)
    {
        var objects = new List<PopulationObject>();
        var visible = Actors.ToDictionary(a => a.Id);
        void Add(byte[] record, byte[] template, bool alive)
        {
            if (record[3] != owner) return;
            objects.Add(new(alive ? record[0x19] : (byte)0, template[0x46], BinaryPrimitives.ReadUInt32LittleEndian(template.AsSpan(0x40)),
                BinaryPrimitives.ReadUInt32LittleEndian(template.AsSpan(0x88)), BinaryPrimitives.ReadUInt32LittleEndian(template.AsSpan(0x78)),
                BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8))));
        }
        foreach (var (id, obj) in _sourceMovementObjects)
            if (!visible.ContainsKey(id)) Add(obj.Record, obj.Template, true);
        foreach (var actor in Actors) Add(actor.Record, actor.Parameters, actor.Alive);
        return OriginalPopulationRules.Recount(Occupancy.Width, Occupancy.Height, objects);
    }
    private void TickPopulation()
    {
        _populationTick = unchecked(_populationTick + 1);
        if ((_populationTick & 31) == 0) RecountPopulation();
        FreePeople = OriginalPopulationRules.Grow(_populationTick, FreePeople, Population.Used, Population.FarmSupport, Population.Limit);
        TickEnemyPopulation();
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
