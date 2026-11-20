using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>The settlement AI of the original (LE:1 1E7EA, docs/ai.md) driving the enemy settlement of the skirmish.
///
/// Ported and verified parts run as in the original, once per game tick after the object update (LE:1 31D6): census
/// (1E87A, every 32 turns), squad steps (1ED26), squad states (1F078), garrison (209B0), military requests (20DD0), orders (227C7), economy (21E99, 221AE), upkeep (226F1), the turn
/// with its decisions (2323A), the plan executor (23667) and research (23C11). Adapters of the port, not the original:
/// <list type="bullet">
/// <item>Not yet ported steps are skipped: the worker states 9/10 (workers for mines and lumbermills), 21E99/221AE (economy), the
/// the formation table of step 4, messages A4C2, the rescue 20001.</item>
/// <item>Step 4 (formation march) sends the members to the target cell with command 48h instead of a formation.</item>
/// <item>Commands the AI writes into OBE +14 (48h move to +22/+23, 50h/58h attack +1C, 40h stand) are carried out by
/// <see cref="ExecuteAiCommands"/> through the port's movement and attack, as the player's orders are, and replaced by
/// activity codes (0 idle, 1 moving, 2/3 attacking) inferred from what the AI reads back; the original WRK command
/// handlers are not ported.</item>
/// <item>Fog of war is not ported: the fog map [18DA] counts the enemy's land +63..+66 grown by 8 cells as seen and the rest
/// as unseen (patrols 1EBEF go there; mine sites 22613 need seen cells); objects keep their scenario visibility FFh.</item>
/// <item>Only the enemy settlement thinks; the player's block is initialised and counted (census) so that the enemy can
/// weigh it, but it gives no orders. Participant records (F5h) are rebuilt from the diplomacy state around each turn.</item>
/// <item>[18E6]/[18EA] (bounds start of B977) are taken as the map size in fixed point; [19E9] (state 6 timer) as FFh.</item>
/// </list></summary>
public sealed partial class Skirmish
{
    private readonly byte[][] _aiBlocks = new byte[8][];
    private readonly byte[][] _participants = Enumerable.Range(0, 8).Select(_ => new byte[0xf5]).ToArray();
    private readonly uint[] _aiThreat = new uint[16];
    private byte[] _aiTree = [];
    private byte[][] _aiTemplates = [];
    private byte _aiArmy, _participantCount;
    private OriginalAiSquadStates? _aiStates;
    private OriginalAiMilitary? _aiMilitary;
    private OriginalAiGarrison? _aiGarrison;
    private OriginalAiOrders? _aiOrders;
    private OriginalAiEconomy? _aiEconomy;
    private OriginalAiWorkers? _aiWorkers;
    private Dictionary<byte, byte[]> _aiMakes = [];
    private readonly Dictionary<(byte Producer, byte Made), byte> _aiIntervals = [];
    private byte[] _aiFortification = [];
    private byte[] _aiRandom = [];
    private byte _aiRandom3;
    private AiSquadWorld? _aiSquadWorld;
    private AiTurnWorld? _aiTurnWorld;

    /// <summary>When false the enemy keeps the old experimental behaviour (tests that need a passive enemy).</summary>
    public bool AiEnabled { get; set; } = true;
    /// <summary>The enemy's metal, wood, gold and people: participant +18, +1C, +20, +24 (the AI's own accounting).</summary>
    public ResourceQuad EnemyResources
    {
        get { var p = _participants[Rules.EnemySlot]; return new(U32(p, 0x18), U32(p, 0x1c), U32(p, 0x20), U32(p, 0x24)); }
        set { var p = _participants[Rules.EnemySlot]; W32(p, 0x18, value.First); W32(p, 0x1c, value.Second); W32(p, 0x20, value.Third); W32(p, 0x24, value.Fourth); }
    }
    private static uint U32(byte[] raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(offset));
    private static void W32(byte[] raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(offset), value);
    public ReadOnlySpan<byte> EnemyAiBlock => _aiBlocks[Rules.EnemySlot];
    public int EnemySquadCount => _aiBlocks[Rules.EnemySlot][OriginalAiSquads.List];

    private void InitializeAi(ScenarioData scenario, TemplatesData templates)
    {
        var data = GameData.Read<OriginalAiData>("res://data/original_ai.json");
        var file = Actors.First(a => a.Team == 1).Source.TemplateFile;
        _aiTemplates = templates.Armies[file].Templates.Select(t => Convert.FromHexString(t.RawRecordHex)).ToArray();
        _aiTree = GameData.Read<OriginalTechData>("res://data/original_tech.json").Armies[2].Build();
        _aiArmy = Actors.First(a => a.Team == 1).Record[0];
        _participantCount = (byte)Math.Min(8, scenario.Slots.Count(s => s.SlotIndex < 8));
        foreach (var slot in scenario.Slots.Where(s => s.SlotIndex < 8))
        {
            var raw = Convert.FromHexString(slot.RawRecordHex);
            _participants[slot.SlotIndex][0] = _aiArmy;
            _participants[slot.SlotIndex][0x16] = raw[0x13];   // character (1E53A takes participant +16 from SCN +13)
        }
        SyncParticipants();
        var enemySlot = scenario.Slots.Single(s => s.SlotIndex == Rules.EnemySlot);
        EnemyResources = OriginalResourceRules.ReadScenario(Convert.FromHexString(enemySlot.RawRecordHex));
        // The enemy castle starts without WorkFlags; the player's castle shows 24h (observed in DOSBox, docs/interface.md).
        foreach (var castle in Actors.Where(a => a.Team == 1 && a.Source.TemplateName == "Замок" && a.Record[0x0f] == 0)) castle.Record[0x0f] = 0x24;
        using (var producers = GameData.Read<System.Text.Json.JsonDocument>("res://data/original_producers.json"))
            foreach (var item in producers.RootElement.GetProperty("producers").EnumerateObject())
            {
                byte producer = byte.Parse(item.Name);
                _aiMakes[producer] = item.Value.GetProperty("makes").EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
                var intervals = item.Value.GetProperty("intervals").EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
                for (int i = 0; i < intervals.Length; i++) _aiIntervals.TryAdd((producer, _aiMakes[producer][i]), intervals[i]);
            }
        _aiFortification = data.Fortifications.Count > 0 ? Convert.FromHexString(data.Fortifications[0]) : [];
        // A6AE marks a finished building in the development tree; the buildings standing at the start count as finished
        // (assumption: the scenario load path is not ported).
        foreach (var built in Actors.Where(a => a.Team == 1 && a.Structure)) OriginalTechRules.Complete(_aiTree, 1, (byte)built.Source.TemplateIndex);
        _aiSquadWorld = new AiSquadWorld(this);
        _aiTurnWorld = new AiTurnWorld(this);
        _aiStates = new OriginalAiSquadStates(_aiSquadWorld, Convert.FromHexString(data.ArctangentHex), Convert.FromHexString(data.SquadMessagesHex));
        _aiMilitary = new OriginalAiMilitary(_aiSquadWorld, Convert.FromHexString(data.ArmyClassesHex));
        _aiGarrison = new OriginalAiGarrison(_aiSquadWorld, _aiMilitary, _aiStates);
        _aiOrders = new OriginalAiOrders(_aiSquadWorld, _aiMilitary);
        _aiEconomy = new OriginalAiEconomy(_aiSquadWorld, _aiMilitary);
        _aiWorkers = new OriginalAiWorkers(_aiSquadWorld);
        _aiRandom = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json").RandomValues.ToArray();
        var characters = data.Table();
        foreach (byte p in new[] { (byte)Rules.PlayerSlot, (byte)Rules.EnemySlot })
        {
            _aiBlocks[p] = new byte[OriginalAiRules.BlockSize];
            OriginalAiRules.Initialize(_aiBlocks[p], _participants[p], p, (byte)Rules.PlayerSlot, characters, AiChain(p), (uint)Map.Width, (uint)Map.Height);
        }
        for (int p = 0; p < 8; p++) _aiBlocks[p] ??= new byte[OriginalAiRules.BlockSize];
    }

    private List<(byte[] Record, byte[] Template)> AiChain(byte participant) =>
        Actors.Where(a => a.Record[3] == participant).Select(a => (a.Record, a.Parameters)).ToList();

    /// <summary>Participant records from the diplomacy state: +12 war mask, +34 message queue, +B9 relations.</summary>
    private void SyncParticipants()
    {
        for (int p = 0; p < Math.Min(8, Diplomacy.Count); p++)
        {
            var raw = _participants[p];
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x12), Diplomacy.Hostile[p]);
            for (int s = 0; s < 8; s++) Diplomacy.Queue[p, s].Encode().CopyTo(raw, 0x34 + s * 14);
            for (int q = 0; q < 16 && 0xb9 + q < raw.Length; q++) raw[0xb9 + q] = Diplomacy.Relations[p, q];
        }
    }

    /// <summary>Back to the diplomacy state: war masks, relations and messages the AI marked as read.</summary>
    private void SyncDiplomacy()
    {
        for (int p = 0; p < Math.Min(8, Diplomacy.Count); p++)
        {
            var raw = _participants[p];
            Diplomacy.Hostile[p] = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(0x12));
            for (int q = 0; q < 16 && 0xb9 + q < raw.Length; q++) Diplomacy.Relations[p, q] = raw[0xb9 + q];
            for (int s = 0; s < 8; s++) if (raw[0x34 + s * 14] == 0xff) Diplomacy.Queue[p, s].Type = 0xff;
        }
    }

    /// <summary>One turn of 1E7EA for the enemy settlement, a census for the player's block.</summary>
    private void TickAi()
    {
        if (!AiEnabled || _aiStates == null || _aiMilitary == null) return;
        Diplomacy.Time = (uint)(Elapsed * 72.8165);
        SyncParticipants();
        byte player = (byte)Rules.PlayerSlot, me = (byte)Rules.EnemySlot;
        var mine = _aiBlocks[player];
        BinaryPrimitives.WriteUInt32LittleEndian(mine.AsSpan(1), BinaryPrimitives.ReadUInt32LittleEndian(mine.AsSpan(1)) + 1);
        if ((BinaryPrimitives.ReadUInt32LittleEndian(mine.AsSpan(1)) & 0x1f) == 0)
            OriginalAiRules.Census(mine, _participants[player], AiChain(player), (byte)Map.Width, (byte)Map.Height);

        var ai = _aiBlocks[me];
        BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(1), BinaryPrimitives.ReadUInt32LittleEndian(ai.AsSpan(1)) + 1);
        var chain = AiChain(me);
        if ((BinaryPrimitives.ReadUInt32LittleEndian(ai.AsSpan(1)) & 0x1f) == 0)
            OriginalAiRules.Census(ai, _participants[me], chain, (byte)Map.Width, (byte)Map.Height);
        OriginalAiSquadSteps.Tick(ai, _aiSquadWorld!);
        _aiStates.Run(ai);
        _aiGarrison!.Run(ai);
        _aiMilitary.Run(ai);
        _aiOrders!.Run(ai);
        _aiEconomy!.Plan(ai);      // 21E99
        _aiEconomy.Execute(ai);    // 221AE
        OriginalAiRules.Upkeep(ai, chain);
        if (OriginalAiTurn.Decide(_aiTurnWorld!, me, 0, (uint)Map.Width, _aiThreat)) _aiEconomy.Construction(ai);   // 23551..23666
        OriginalAiPlans.Execute(_aiTurnWorld!, me, _aiThreat);
        OriginalAiResearch.Step(ai, me, _aiArmy, _aiTree, _aiTemplates, chain, _terrainCatalog.NextRandomByte);
        SyncDiplomacy();
        ExecuteAiCommands();
    }

    /// <summary>Carries out the commands the AI wrote into OBE +14 of the enemy's units (adapter, see the class summary). A
    /// command (bit 40h: 40h stand, 48h move to +22/+23, 50h/58h attack +1C) is replaced by the activity the original's
    /// command handlers leave in +14 — 0 idle, 1 moving, 2/3 attacking — as read by 1EE02 (retarget of idle, moving and
    /// out-of-area attackers) and by the squad census (205FB counts +14 &amp; 7 when bit 40h is clear). Moving ends in 0 on
    /// arrival, attacking when the target is gone.</summary>
    private void ExecuteAiCommands()
    {
        foreach (var building in Actors.Where(a => a.Team == 1 && a.Alive && a.Structure).ToArray())
        {
            // An idle producer can take work (WorkFlags bit 2, as the idle castle's 24h observed in DOSBox); the WRK that sets
            // WorkFlags after construction and production is not ported (adapter).
            if (IsEnemyProducer(building) && building.ConstructionRemaining <= 0 && building.Record.Length >= 0x27)
            {
                bool idle = building.Record[0x22] == 0xff && building.Record[0x15] == 0;
                building.Record[0x0f] = idle ? (byte)(building.Record[0x0f] | 0x24) : (byte)(building.Record[0x0f] & ~4);
            }
            // A building under construction carries 81h (command 80h, activity 1); the handled command leaves activity 1, which
            // 22E85 reads as «producer under construction». Completion (C80D path) gives the same next state for 81h and 1.
            if (building.ConstructionRemaining > 0 && (building.Record[0x14] & 0x80) != 0) building.Record[0x14] &= 7;
            byte order = building.Record[0x14];
            if ((order & 0xf8) == 0x60) building.Record[0x14] = 4;              // produce: the queue head +22 is the template
            else if ((order & 0xf8) == 0x58) { EnemyBuild(building); building.Record[0x14] = 0; }
        }
        foreach (var worker in Actors.Where(a => a.Team == 1 && a.Alive && a.Worker)) EnemyWorkerCommand(worker);
        foreach (var actor in Actors.Where(a => a.Team == 1 && a.Alive && !a.Structure && !a.Worker))
        {
            byte value = actor.Record[0x14];
            uint target = BinaryPrimitives.ReadUInt32LittleEndian(actor.Record.AsSpan(0x1c));
            if ((value & 0x40) != 0)
            {
                // A new command restarts the unit's action: a pending swing (whose target +1C the group order B851 has just
                // rewritten) is dropped unless the unit keeps attacking the same target.
                if (!actor.Stepping && !((value & 0xf8) is 0x50 or 0x58 && target == actor.Target)) actor.Record[0x2d] = 0;
                switch (value & 0xf8)
                {
                    case 0x40:
                        actor.Goal = null; if (!actor.Stepping) actor.Route.Clear();
                        actor.Record[0x14] = 0; break;
                    case 0x48:
                        actor.Record[0x14] = OrderMove(actor, new Vector2I(actor.Record[0x22], actor.Record[0x23])) ? (byte)1 : (byte)0; break;
                    case 0x50: case 0x58:
                        if (Find(target) is { } foe && AtWar(actor, foe))
                        {
                            actor.Target = foe.Id; actor.Goal = null; actor.Route.Clear(); actor.ReplanTimer = 0;
                            actor.Record[0x14] = (byte)((value >> 3) & 7);
                        }
                        else actor.Record[0x14] = 0;
                        break;
                }
                continue;
            }
            switch (value & 7)
            {
                case 1 when actor.Goal == null && actor.Route.Count == 0 && !actor.Stepping: actor.Record[0x14] = 0; break;
                case 2 or 3 when actor.Target == 0 || Find(actor.Target) == null: actor.Record[0x14] = 0; break;
            }
        }
    }

    /// <summary>Commands in +14 of an enemy worker, from the AI (1F476/1F653) or from the worker's own WRK steps (123D3 gives
    /// 60h with a load, the return to the empty worker gives 58h), are handed to the port's worker jobs (adapter):
    /// 58h — chop the tree +32/+33; 60h — with timber, deliver to the nearest own lumbermill; with ore, carry it to the
    /// castle; empty, work the mine +1C; 40h — stand; 48h — go to +22/+23. The activity left in +14 is 3 for timber work,
    /// 4 for mine work, 1 while walking and 0 (with +15 = 0) when the worker has no job; 206D5 takes members with 1 or 0/0.</summary>
    private void EnemyWorkerCommand(SkirmishActor worker)
    {
        byte value = worker.Record[0x14];
        var job = (worker.GatherTree, worker.PreferredMill, worker.MineJob);
        if ((value & 0x40) != 0)
        {
            byte cargo = worker.Record[0x31];
            switch (value & 0xf8)
            {
                case 0x40: worker.GatherTree = null; worker.PreferredMill = 0; worker.MineJob = false; worker.Goal = null; break;
                case 0x48: worker.GatherTree = null; worker.PreferredMill = 0; worker.MineJob = false;
                    OrderMove(worker, new Vector2I(worker.Record[0x22], worker.Record[0x23])); break;
                case 0x58:
                    var tree = new Vector2I(worker.Record[0x32], worker.Record[0x33]);
                    if (cargo == 0 && Harvestable(tree)) { worker.GatherTree = tree; worker.MineJob = false; worker.Goal = null; worker.HarvestPrepared = false; worker.Route.Clear(); }
                    break;
                case 0x60 when cargo == 3:
                    var mill = Find(OriginalWorkerDelivery.FindReceiver((uint)worker.Position.X, (uint)worker.Position.Y,
                        Actors.Where(a => a.Team == worker.Team).Select(ReceiverCandidate)));
                    if (mill != null) { worker.PreferredMill = mill.Id; worker.MineJob = false; worker.Goal = null; }
                    break;
                case 0x60 when worker.MineJob: break;   // 123D3's 60h on a loaded carrier: the carrier phases go on
                case 0x60:
                    uint mine = BinaryPrimitives.ReadUInt32LittleEndian(worker.Record.AsSpan(0x1c));
                    if (cargo is 1 or 2 || Find(mine) is { Team: 1, Alive: true } m && m.Source.TemplateName == "Шахта")
                    {
                        if (cargo == 0) { BinaryPrimitives.WriteUInt32LittleEndian(worker.Record.AsSpan(0x32), 0); worker.Record[0x30] = 0; }
                        worker.GatherTree = null; worker.PreferredMill = 0; worker.Goal = null; worker.Route.Clear(); worker.ReplanTimer = 0;
                        worker.Record[0x15] = 0; worker.Record[0x17] &= 0xbf; worker.MineJob = true;
                    }
                    break;
            }
        }
        // A changed job cancels a pending swing (+2D high nibble), as the player's orders do; otherwise the swing would
        // read the new job's +22/+23 as its target.
        if ((worker.GatherTree, worker.PreferredMill, worker.MineJob) != job) { worker.Record[0x2d] &= 15; worker.HarvestPrepared = false; }
        // A walk whose route could not be planned (TickWorker set the replan delay, no route) ends there, as a soldier's does.
        if (worker.Goal != null && worker.Route.Count == 0 && !worker.Stepping && worker.ReplanTimer > 0) worker.Goal = null;
        if (worker.MineJob) worker.Record[0x14] = 4;
        else if (worker.GatherTree != null || worker.PreferredMill != 0) worker.Record[0x14] = 3;
        else if (worker.Goal != null || worker.Route.Count > 0 || worker.Stepping) worker.Record[0x14] = 1;
        else { worker.Record[0x14] = 0; worker.Record[0x15] = 0; }
    }

    private PopulationTotals _enemyPopulation;
    /// <summary>The enemy's free people (participant +24) grow by the ported population rules, as the player's do.</summary>
    private void TickEnemyPopulation()
    {
        if (!AiEnabled || _aiBlocks[Rules.EnemySlot] == null) return;
        if ((_populationTick & 31) == 0 || _enemyPopulation.Limit == 0)
        {
            _enemyPopulation = PopulationOf((byte)Rules.EnemySlot);
            // D453 keeps participant +28 (load) and +2C (farm support); the AI's census and 21E99 read them.
            var record = _participants[Rules.EnemySlot];
            W32(record, 0x28, _enemyPopulation.Used); W32(record, 0x2c, _enemyPopulation.FarmSupport);
        }
        var r = EnemyResources;
        uint people = OriginalPopulationRules.Grow(_populationTick, r.Fourth, _enemyPopulation.Used, _enemyPopulation.FarmSupport, _enemyPopulation.Limit);
        if (people != r.Fourth) EnemyResources = r with { Fourth = people };
    }

    /// <summary>A6AE when an enemy building is finished: kind 1 «after building template N» in the AI's tree.</summary>
    private void EnemyBuildingFinished(SkirmishActor building)
    {
        if (_aiTree.Length > 0) OriginalTechRules.Complete(_aiTree, 1, (byte)building.Source.TemplateIndex);
    }

    /// <summary>Enemy objects that make something (WRK AH=6 table with rows): castle, barracks, factory, stable…</summary>
    private bool IsEnemyProducer(SkirmishActor actor) =>
        actor.Team == 1 && actor.Structure && _aiMakes.TryGetValue((byte)actor.Source.TemplateIndex, out var makes) && makes.Length > 0;

    /// <summary>Production interval of the producer's WRK row for the template (data/original_producers.json).</summary>
    private byte EnemyInterval(SkirmishActor producer, byte template) =>
        _aiIntervals.TryGetValue(((byte)producer.Source.TemplateIndex, template), out var interval) ? interval : (byte)40;

    /// <summary>An enemy recruit walks from the producer's exit to the settlement's home cell (AI block +61/+62), so the exit
    /// is free for the next one (adapter: the player clears the exit by hand, the original's WRK release is not ported).</summary>
    private void EnemyRecruitLeaves(SkirmishActor recruit)
    {
        var ai = _aiBlocks[Rules.EnemySlot];
        if (ai == null) return;
        OrderMove(recruit, new Vector2I(ai[0x61], ai[0x62]), 15);   // a crowded home still leaves room further out
    }

    /// <summary>An enemy producer whose exit is taken by one of the enemy's own idle units sends that unit away towards the
    /// home (adapter, like <see cref="EnemyRecruitLeaves"/>; the player clears the exit by hand).</summary>
    private void EnemyClearExit(SkirmishActor producer, TemplateEntry template)
    {
        var exit = RecruitExit(producer, template);
        var cell = new Vector2I((int)(exit.X >> 5), (int)(exit.Y >> 5));
        var blocker = Actors.FirstOrDefault(a => a.Team == 1 && a.Alive && !a.Structure && !a.Stepping && a.Goal == null && a.Target == 0
            && FootprintCells(a, a.Cell).Contains(cell));
        var ai = _aiBlocks[Rules.EnemySlot];
        if (blocker == null || ai == null) return;
        var home = new Vector2I(ai[0x61], ai[0x62]);
        if ((blocker.Cell - home).LengthSquared() <= 4) home += (blocker.Cell - (Vector2I)(producer.Position / 32)).Sign() * 6;
        OrderMove(blocker, home, 15);
    }

    /// <summary>Command 58h of the enemy castle: build template +22 on the site +29/+2B (adapter). The building rules of the
    /// port are used: ProbePlacement on the site, payment from the enemy's resources, construction over time. Templates the
    /// skirmish has no rules for are not built. The castle's +22 (head of its production queue) is cleared afterwards.</summary>
    private void EnemyBuild(SkirmishActor builder)
    {
        byte index = builder.Record[0x22];
        builder.Record[0x22] = 0xff;
        var templates = _templates.Armies["ARMY__02.DAT"].Templates;
        if (index >= templates.Count() || !Rules.Units.ContainsKey(templates[index].Name) || templates[index].Name is "Замок") return;
        var template = templates[index];
        var cell = new Vector2I(BinaryPrimitives.ReadUInt16LittleEndian(builder.Record.AsSpan(0x29)) >> 5, BinaryPrimitives.ReadUInt16LittleEndian(builder.Record.AsSpan(0x2b)) >> 5);
        if (!Inside(cell) || EnemyPlacement(index, cell) != ConstructionPlacementStatus.Clear) return;
        var payment = OriginalConstructionRules.TryDebitUpfront(EnemyResources, Price(template));
        if (!payment.Accepted) return;
        EnemyResources = payment.Balances;
        EnemyConstruct(index, cell);
    }

    /// <summary>A new enemy building under construction at the cell (the castle's 58h and 23551's A758, adapter).</summary>
    private SkirmishActor EnemyConstruct(byte index, Vector2I cell)
    {
        var template = _templates.Armies["ARMY__02.DAT"].Templates[index];
        var building = NewEconomyActor(index, cell, false, false, 1);
        building.ConstructionDuration = building.ConstructionRemaining = template.InitialCondition * Math.Max(1, (int)building.Parameters[0x57]) / (double)Rules.TicksPerSecond;
        OriginalBuildingLifecycle.MarkConstruction(building.Record);
        building.Record[0x14] &= 7;   // the handled construction command leaves activity 1 at once (see ExecuteAiCommands)
        if (building.Record.Length > 0x20) building.Record[0x20] = 0xff;
        if (building.Record.Length >= 0x28) { building.Record.AsSpan(0x22, 5).Fill(0xff); building.Record[0x27] = 0xff; }   // producers' queue
        if (template.Name == "Шахта" && building.Record.Length >= 0x28)
        {
            var deposit = OriginalMineRules.Initialize(Map.Cells, Map.Width, Map.Height, (uint)building.Position.X, (uint)building.Position.Y);
            building.Record[0x23] = deposit.Type; BinaryPrimitives.WriteUInt32LittleEndian(building.Record.AsSpan(0x24), deposit.Remaining);
        }
        Actors.Add(building);
        Occupancy.WriteFootprint(building.Parameters[3], building.Parameters[4], building.Record[0x0d], (uint)building.Position.X, (uint)building.Position.Y, building.Id);
        return building;
    }

    private ConstructionPlacementStatus EnemyPlacement(byte template, Vector2I cell)
    {
        var parameters = _templates.Armies["ARMY__02.DAT"].Templates[template].Construction;
        if (parameters.FootprintWidth == 0) return ConstructionPlacementStatus.Rejected;
        var result = OriginalConstructionRules.ProbePlacement(Occupancy.Width, Occupancy.Height, Map.Cells, _properties, Occupancy.Cells,
            parameters.FootprintWidth, parameters.FootprintHeight, parameters.TerrainMask.ToArray(), (uint)(cell.X * 32 + 16), (uint)(cell.Y * 32 + 16));
        if (result.Status != ConstructionPlacementStatus.Clear) return result.Status;
        var area = new Rect2I(cell - new Vector2I(parameters.FootprintWidth / 2, parameters.FootprintHeight / 2), new(parameters.FootprintWidth, parameters.FootprintHeight));
        return _reservations.Any(r => area.HasPoint(r.Key.Cell)) ? ConstructionPlacementStatus.Occupied : ConstructionPlacementStatus.Clear;
    }

    /// <summary>Move one unit like <see cref="Move"/> does for the player: the nearest free standing place around the cell.</summary>
    private bool OrderMove(SkirmishActor actor, Vector2I destination, int reach = 5)
    {
        if (!Inside(destination)) return false;
        for (int radius = 0; radius <= reach; radius++)
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            var goal = destination + new Vector2I(x, y);
            if (!Inside(goal) || !CanStand(actor, goal)) continue;
            if (!actor.Stepping && !Plan(actor, goal)) continue;
            actor.Target = 0; actor.Goal = goal; actor.ReplanTimer = 0;
            if (actor.Stepping) actor.Route.Clear();
            return true;
        }
        return false;
    }

    private sealed class AiSquadWorld(Skirmish battle) : IAiGarrisonWorld, IAiSquadStateWorld, IAiOrdersWorld, IAiEconomyWorld, IAiWorkerWorld
    {
        private readonly SearchWorld _search = new(battle, (byte)battle.Rules.EnemySlot);
        public IReadOnlyList<(byte[] Record, byte[] Template)> Chain(byte participant) => battle.AiChain(participant);
        public (byte[] Record, byte[] Template) Resolve(uint reference) => _search.Resolve(reference);
        // Cells outside the map read memory beyond the grid in the original; here they are empty.
        public uint Occupant(int plane, int cell) => cell >= 0 && cell < battle.Map.Width * battle.Map.Height ? _search.Occupant(plane, cell) : 0;
        public byte MapWidth => (byte)battle.Map.Width;
        public byte MapHeight => (byte)battle.Map.Height;
        public uint HostileMask(byte participant) => participant < battle.Diplomacy.Count ? battle.Diplomacy.Hostile[participant] : 0;
        public (uint X, uint Y) BoundsLimit => ((uint)battle.Map.Width * 32, (uint)battle.Map.Height * 32);
        public bool StepFour(Span<byte> squad, bool started)
        {
            if (!started) return squad[0x43] != 0;
            OriginalAiSquads.Order(Chain(squad[0]).Select(m => m.Record), 0x48, squad[1], squad[9], squad[0x0a]);
            return true;
        }
        public byte Global19E9 => 0xff;
        public byte Random() => battle._terrainCatalog.NextRandomByte();
        public byte Random2() => battle._terrainCatalog.NextSecondaryByte();
        public byte Random3() => battle._aiRandom[battle._aiRandom3++];   // 9E69: third index [19ED] into the same table
        public byte Fog(int cell)
        {
            var ai = battle._aiBlocks[battle.Rules.EnemySlot];
            int x = cell % battle.Map.Width, y = cell / battle.Map.Width;
            bool seen = ai != null && x >= ai[0x63] - 8 && x <= ai[0x65] + 8 && y >= ai[0x64] - 8 && y <= ai[0x66] + 8;
            return seen ? (byte)~(1 << battle.Rules.EnemySlot) : (byte)0xff;
        }
        public Span<byte> ParticipantRecord(byte participant) => battle._participants[participant];
        public byte TileOre(int cell) => cell >= 0 && cell < battle.Map.Cells.Length ? (byte)(battle.Map.Cells[cell] >> 16) : (byte)0;
        public bool SiteAllowed(byte template, byte army, byte participant, byte x, byte y) =>
            battle.Inside(new Vector2I(x, y)) && battle.EnemyPlacement(template, new Vector2I(x, y)) == ConstructionPlacementStatus.Clear;
        public void Send(byte type, byte other, byte owner, byte bh, uint parameter) => battle._aiTurnWorld!.Send(type, other, owner, bh, parameter);
        /// <summary>10AD2 (adapter): the nearest cell in rings of up to 12 around the point where the building fits.</summary>
        public (uint X, uint Y)? FreeSpot(uint templateArmy, byte variant, uint x, uint y)
        {
            var centre = new Vector2I((int)(x >> 5), (int)(y >> 5));
            for (int r = 0; r <= 12; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        var cell = centre + new Vector2I(dx, dy);
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && battle.Inside(cell) && battle.EnemyPlacement((byte)templateArmy, cell) == ConstructionPlacementStatus.Clear)
                            return ((uint)(cell.X * 32 + 16), (uint)(cell.Y * 32 + 16));
                    }
            return null;
        }
        public bool BuildingSite(uint templateArmy, byte participant, byte x, byte y) =>
            battle.Inside(new Vector2I(x, y)) && battle.EnemyPlacement((byte)templateArmy, new Vector2I(x, y)) == ConstructionPlacementStatus.Clear;
        public byte[]? Create(byte participant, byte army, byte template, byte variant, uint x, uint y) =>
            participant == battle.Rules.EnemySlot ? battle.EnemyConstruct(template, new Vector2I((int)(x >> 5), (int)(y >> 5))).Record : null;
        public void Register(byte[] record) { }
        public void Message(ReadOnlySpan<byte> squad, uint text) { }
        public void Rescue(Span<byte> squad) { }
        public void WorkerState(Span<byte> ai, int at) => battle._aiWorkers!.Run(ai, at);
        public uint TileProperty(int cell) => cell >= 0 && cell < battle.Map.Cells.Length
            ? BinaryPrimitives.ReadUInt32LittleEndian(battle._properties.AsSpan((int)(battle.Map.Cells[cell] & 0xffff) * 8)) : 0;
        public Span<byte> Block(byte participant) => battle._aiBlocks[participant];
        public ReadOnlySpan<byte> Participant(byte participant) => battle._participants[participant];
        public byte ParticipantCount => battle._participantCount;
        public IReadOnlyList<byte> Buildable(byte participant, uint mask, uint value) => OriginalTechRules.Buildable(battle._aiTemplates, battle._aiTree, true, mask, value);
        public byte[] Template(byte participant, byte template) => battle._aiTemplates[template];
        public uint Reference(byte[] record) => battle.Actors.FirstOrDefault(a => ReferenceEquals(a.Record, record))?.Id ?? 0;
        public IReadOnlyList<byte> Makes(byte template) => battle._aiMakes.TryGetValue(template, out var m) ? m : [];
        public byte TemplateCount => (byte)battle._aiTemplates.Length;
        /// <summary>AF07 replaced by the port's building placement check (ProbePlacement), adapter.</summary>
        public bool PlacementAllowed(byte template, byte army, byte participant, uint x, uint y)
        {
            var cell = new Vector2I((int)(x >> 5), (int)(y >> 5));
            return battle.Inside(cell) && battle.EnemyPlacement(template, cell) == ConstructionPlacementStatus.Clear;
        }
        public byte TileClass(int cell) => cell >= 0 && cell < battle.Map.Cells.Length ? (byte)(battle._properties[(int)(battle.Map.Cells[cell] & 0xffff) * 8] & 7) : (byte)0;
        public byte[]? Fortification(uint index) => index == 0 && battle._aiFortification.Length > 0 ? battle._aiFortification : null;
        public bool TreeAllows(byte participant, byte template) => OriginalTechRules.Available(battle._aiTree, template);
    }

    private sealed class AiTurnWorld(Skirmish battle) : IAiTurnWorld
    {
        private readonly SearchWorld _search = new(battle, (byte)battle.Rules.EnemySlot);
        public int Count => battle._participantCount;
        public Span<byte> Block(int participant) => battle._aiBlocks[participant];
        public Span<byte> Participant(int participant) => battle._participants[participant];
        public void Send(byte type, byte other, byte owner, byte bh, uint parameter)
        {
            battle.SyncDiplomacy();   // keep messages the AI already marked as read
            battle._diplomacyRules.Send(battle.Diplomacy, type, other, owner, bh, parameter);
            battle.SyncParticipants();
        }
        public byte MapWidth => _search.MapWidth;
        public byte MapHeight => _search.MapHeight;
        public uint Occupant(int plane, int cell) => _search.Occupant(plane, cell);
        public (byte[] Record, byte[] Template) Resolve(uint reference) => _search.Resolve(reference);
        public uint HostileMask => _search.HostileMask;
        public bool Contact(byte owner) => _search.Contact(owner);
        public byte TerrainClass(ReadOnlySpan<byte> record) => _search.TerrainClass(record);
        public void Contacted(FirstContact contact) => _search.Contacted(contact);
    }
}
