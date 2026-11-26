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
/// nothing of the main loop remains unported.</item>
/// <item>Commands the AI writes into OBE +14 (48h move to +22/+23, 50h/58h attack +1C, 40h stand) are carried out by
/// <see cref="ExecuteAiCommands"/> through the port's movement and attack, as the player's orders are, and replaced by
/// activity codes (0 idle, 1 moving, 2/3 attacking) inferred from what the AI reads back; the original WRK command
/// handlers are not ported.</item>
/// <item>The fog map [18DA] is real (<see cref="OriginalVisibilityRules"/>): it opens around an object when it changes cell
/// (the original: at the end of each step, 72B3) and on placement; patrols 1EBEF go to unseen cells and mine sites 22613
/// need seen ones. Objects keep their scenario visibility FFh.</item>
/// <item>Only the enemy settlement thinks; the player's block is initialised and counted (census) so that the enemy can
/// weigh it, but it gives no orders. Participant records (F5h) are rebuilt from the diplomacy state around each turn.</item>
/// <item>[18E6]/[18EA] (bounds start of B977) are taken as the map size in fixed point; [19E9] (state 6 timer) as FFh.</item>
/// </list></summary>
public sealed partial class Skirmish
{
    private readonly byte[][] _aiBlocks = new byte[8][];
    private readonly byte[][] _participants = Enumerable.Range(0, 8).Select(_ => new byte[0xf5]).ToArray();
    private readonly uint[] _aiThreat = new uint[16];
    private readonly Dictionary<int, byte[]> _aiTrees = [];
    /// <summary>The AI settlement whose turn runs now (the original keeps it in [ED20]/[ED2C]).</summary>
    private int _aiSlot = 3;
    private byte[] _aiTree => TreeOf(_aiSlot);
    private byte[] TreeOf(int slot) => _aiTrees.TryGetValue(slot, out var tree) ? tree : [];
    private int _aiTeam => TeamOfSlot(_aiSlot);
    /// <summary>Settlements the AI runs: the enemy and, for observation, the rival.</summary>
    public IReadOnlyList<int> AiSlots => (Rules.RivalSlot >= 0 ? [Rules.EnemySlot, Rules.RivalSlot] : (IEnumerable<int>)[Rules.EnemySlot]).Concat(Rules.HordeSlot >= 0 ? [Rules.HordeSlot] : []).ToList();
    /// <summary>Team of the actors of a slot: 0 the player, 1 the enemy, 2 the rival.</summary>
    public int TeamOfSlot(int slot) => slot == Rules.PlayerSlot ? 0 : slot == Rules.HordeSlot && slot >= 0 ? 3 : Rules.Fauna && IsFaunaSlot(slot) ? 4 : slot == Rules.RivalSlot && slot >= 0 ? 2 : 1;
    public int SlotOfTeam(int team) => team == 0 ? Rules.PlayerSlot : team == 3 && Rules.HordeSlot >= 0 ? Rules.HordeSlot : team == 4 && Rules.Fauna ? 7 : team == 2 && Rules.RivalSlot >= 0 ? Rules.RivalSlot : Rules.EnemySlot;
    /// <summary>What a settlement's AI knows of its army: the DAT templates, the WRK producer rows, the army byte. The horde (slot 4)
    /// plays ARMY__01, the settlements ARMY__02.</summary>
    private sealed class AiContext
    {
        public byte Army; public byte[][] Templates = [];
        public Dictionary<byte, byte[]> Makes = [];
        public readonly Dictionary<(byte Producer, byte Made), byte> Intervals = [];
    }
    private readonly Dictionary<int, AiContext> _aiContexts = [];
    private AiContext Ctx(int slot) => _aiContexts.TryGetValue(slot, out var c) ? c : _aiContexts.Values.First();
    private byte[][] _aiTemplates => Ctx(_aiSlot).Templates;
    private byte _aiArmy => Ctx(_aiSlot).Army;
    private byte _participantCount;
    private byte[] _aiCharacters = [];
    private OriginalAiSquadStates? _aiStates;
    private OriginalAiMilitary? _aiMilitary;
    private OriginalAiGarrison? _aiGarrison;
    private OriginalAiOrders? _aiOrders;
    private OriginalAiEconomy? _aiEconomy;
    private OriginalAiWorkers? _aiWorkers;
    private Dictionary<byte, byte[]> _aiMakes => Ctx(_aiSlot).Makes;
    private Dictionary<(byte Producer, byte Made), byte> _aiIntervals => Ctx(_aiSlot).Intervals;
    private byte[] _aiFortification = [];
    private byte[] _aiRandom = [];
    private byte _aiRandom3;
    private AiSquadWorld? _aiSquadWorld;
    private AiTurnWorld? _aiTurnWorld;

    /// <summary>When false the enemy keeps the old experimental behaviour (tests that need a passive enemy).</summary>
    public bool AiEnabled { get; set; } = true;
    /// <summary>The enemy's metal, wood, gold and people: participant +18, +1C, +20, +24 (the AI's own accounting).</summary>
    public ResourceQuad EnemyResources { get => AiResources(Rules.EnemySlot); set => SetAiResources(Rules.EnemySlot, value); }
    /// <summary>Metal, wood, gold and people of an AI settlement: participant +18, +1C, +20, +24.</summary>
    public ResourceQuad AiResources(int slot) { var p = _participants[slot]; return new(U32(p, 0x18), U32(p, 0x1c), U32(p, 0x20), U32(p, 0x24)); }
    private void SetAiResources(int slot, ResourceQuad value)
    { var p = _participants[slot]; W32(p, 0x18, value.First); W32(p, 0x1c, value.Second); W32(p, 0x20, value.Third); W32(p, 0x24, value.Fourth); }
    public ReadOnlySpan<byte> AiBlock(int slot) => _aiBlocks[slot];
    private static uint U32(byte[] raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(offset));
    private static void W32(byte[] raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(offset), value);
    public ReadOnlySpan<byte> EnemyAiBlock => _aiBlocks[Rules.EnemySlot];
    public int EnemySquadCount => _aiBlocks[Rules.EnemySlot][OriginalAiSquads.List];

    private void InitializeAi(ScenarioData scenario, TemplatesData templates)
    {
        var data = GameData.Read<OriginalAiData>("res://data/original_ai.json");
        var file = Actors.First(a => a.Team == 1).Source.TemplateFile;
        var armyByte = Actors.First(a => a.Team == 1).Record[0];
        foreach (int slot in AiSlots) _aiContexts[slot] = new AiContext { Army = armyByte, Templates = templates.Armies[file].Templates.Select(t => Convert.FromHexString(t.RawRecordHex)).ToArray() };
        var tech = GameData.Read<OriginalTechData>("res://data/original_tech.json");
        foreach (int slot in AiSlots) _aiTrees[slot] = tech.Armies[2].Build();
        _aiTrees[Rules.PlayerSlot] = tech.Armies[2].Build();   // the player's development tree: what he may build and hire follows what he has built and researched
        if (Horde)
        {
            _aiContexts[Rules.HordeSlot] = new AiContext { Army = 1, Templates = templates.Armies[HordeFile].Templates.Select(t => Convert.FromHexString(t.RawRecordHex)).ToArray() };
            _aiTrees[Rules.HordeSlot] = tech.Armies[1].Build();
        }
        _aiSlot = Rules.EnemySlot;
        _participantCount = (byte)Math.Min(8, scenario.Slots.Count(s => s.SlotIndex < 8));
        foreach (var slot in scenario.Slots.Where(s => s.SlotIndex < 8))
        {
            var raw = Convert.FromHexString(slot.RawRecordHex);
            _participants[slot.SlotIndex][0] = Horde && slot.SlotIndex == Rules.HordeSlot ? (byte)1 : armyByte;
            _participants[slot.SlotIndex][0x16] = raw[0x13];   // character (1E53A takes participant +16 from SCN +13)
        }
        // The observed rival mirrors the enemy (adapter): character and starting resources of the enemy's slot.
        var enemyRaw = Convert.FromHexString(scenario.Slots.Single(s => s.SlotIndex == Rules.EnemySlot).RawRecordHex);
        if (Rules.RivalSlot >= 0) _participants[Rules.RivalSlot][0x16] = enemyRaw[0x13];
        SyncParticipants();
        foreach (int slot in AiSlots)
        {
            var start = OriginalResourceRules.ReadScenario(Horde && slot == Rules.HordeSlot ? Convert.FromHexString(scenario.Slots.Single(x => x.SlotIndex == slot).RawRecordHex) : enemyRaw);
            SetAiResources(slot, new ResourceQuad(start.First * Rules.AiStartBoost, start.Second * Rules.AiStartBoost, start.Third * Rules.AiStartBoost, start.Fourth));   // TEMPORARY boost, see SkirmishRules.AiStartBoost
        }
        // The enemy castle starts without WorkFlags; the player's castle shows 24h (observed in DOSBox, docs/interface.md).
        foreach (var castle in Actors.Where(a => a.Team != 0 && a.Source.TemplateName == "Замок" && a.Record[0x0f] == 0)) castle.Record[0x0f] = 0x24;
        using (var producers = GameData.Read<System.Text.Json.JsonDocument>("res://data/original_producers.json"))
            foreach (var item in producers.RootElement.GetProperty("producers").EnumerateObject())
            {
                byte producer = byte.Parse(item.Name);
                var makes = item.Value.GetProperty("makes").EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
                var intervals = item.Value.GetProperty("intervals").EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
                foreach (var context in _aiContexts.Values.Where(c => c.Army == armyByte))
                {
                    context.Makes[producer] = makes;
                    for (int i = 0; i < intervals.Length; i++) context.Intervals.TryAdd((producer, makes[i]), intervals[i]);
                }
            }
        if (Horde)
        {
            using var hordeProducers = GameData.Read<System.Text.Json.JsonDocument>("res://data/original_producers.json");
            var context = _aiContexts[Rules.HordeSlot];
            foreach (var item in hordeProducers.RootElement.GetProperty("armies").GetProperty("1").GetProperty("producers").EnumerateObject())
            {
                byte producer = byte.Parse(item.Name);
                var makes = item.Value.GetProperty("makes").EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
                var intervals = item.Value.GetProperty("intervals").EnumerateArray().Select(v => (byte)v.GetInt32()).ToArray();
                context.Makes[producer] = makes;
                for (int i = 0; i < intervals.Length; i++) context.Intervals.TryAdd((producer, makes[i]), intervals[i]);
            }
        }
        _aiFortification = data.Fortifications.Count > 0 ? Convert.FromHexString(data.Fortifications[0]) : [];
        // A6AE marks a finished building in the development tree; the buildings standing at the start count as finished
        // (assumption: the scenario load path is not ported).
        foreach (var lab in Actors.Where(a => a.Team != 0 && a.Index02 == 12 && a.Record.Length >= 0x29)) { lab.Record[0x22] = lab.Record[0x23] = 1; lab.Record[0x15] = 0; }   // the laboratory's AH=0
        foreach (var built in Actors.Where(a => a.Structure && a.Team is 0 or 1 or 2 or 3)) OriginalTechRules.Complete(TreeOf(built.Record[3]), 1, (byte)built.Source.TemplateIndex);
        _aiSquadWorld = new AiSquadWorld(this);
        _aiTurnWorld = new AiTurnWorld(this);
        _arctangent = Convert.FromHexString(data.ArctangentHex);
        _aiStates = new OriginalAiSquadStates(_aiSquadWorld, Convert.FromHexString(data.ArctangentHex), Convert.FromHexString(data.SquadMessagesHex));
        _aiMilitary = new OriginalAiMilitary(_aiSquadWorld, Convert.FromHexString(data.ArmyClassesHex));
        _aiGarrison = new OriginalAiGarrison(_aiSquadWorld, _aiMilitary, _aiStates);
        _aiOrders = new OriginalAiOrders(_aiSquadWorld, _aiMilitary);
        _aiEconomy = new OriginalAiEconomy(_aiSquadWorld, _aiMilitary);
        _aiWorkers = new OriginalAiWorkers(_aiSquadWorld);
        _aiRandom = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json").RandomValues.ToArray();
        var characters = _aiCharacters = data.Table();
        foreach (byte p in AiSlots.Prepend(Rules.PlayerSlot).Select(x => (byte)x))
        {
            _aiBlocks[p] = new byte[OriginalAiRules.BlockSize];
            OriginalAiRules.Initialize(_aiBlocks[p], _participants[p], p, (byte)Rules.PlayerSlot, characters, AiChain(p), (uint)Map.Width, (uint)Map.Height);
        }
        for (int p = 0; p < 8; p++) _aiBlocks[p] ??= new byte[OriginalAiRules.BlockSize];
    }

    private List<(byte[] Record, byte[] Template)> AiChain(byte participant) =>
        Actors.Where(a => a.Record[3] == participant && !a.Detached).Select(a => (a.Record, a.Parameters)).ToList();

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
        byte player = (byte)Rules.PlayerSlot;
        var mine = _aiBlocks[player];
        BinaryPrimitives.WriteUInt32LittleEndian(mine.AsSpan(1), BinaryPrimitives.ReadUInt32LittleEndian(mine.AsSpan(1)) + 1);
        if ((BinaryPrimitives.ReadUInt32LittleEndian(mine.AsSpan(1)) & 0x1f) == 0)
            OriginalAiRules.Census(mine, _participants[player], AiChain(player), (byte)Map.Width, (byte)Map.Height);

        TickCamps();
        foreach (int slot in AiSlots) { _aiSlot = slot; TurnOf((byte)slot); }
        _aiSlot = Rules.EnemySlot;
        if (ImprovedAi) foreach (int slot in AiSlots) Prof("ai.improved", () => ImprovedStrategy(slot));
    }

    /// <summary>Per-section timings of the world step (milliseconds, count), filled while <see cref="ProfileOn"/> is set.</summary>
    public Dictionary<string, (double Ms, int Calls)> Profile { get; } = [];
    /// <summary>Longest single call of each profiled section (milliseconds).</summary>
    public Dictionary<string, double> ProfilePeak { get; } = [];
    public bool ProfileOn { get; set; }
    internal long P0() => ProfileOn ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
    internal void P1(string name, long start)
    {
        if (!ProfileOn) return;
        var old = Profile.GetValueOrDefault(name);
        double took = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Profile[name] = (old.Ms + took, old.Calls + 1); ProfilePeak[name] = Math.Max(ProfilePeak.GetValueOrDefault(name), took);
    }
    private void Prof(string name, Action work)
    {
        if (!ProfileOn) { work(); return; }
        long t = System.Diagnostics.Stopwatch.GetTimestamp(); work();
        var old = Profile.GetValueOrDefault(name);
        double took = (System.Diagnostics.Stopwatch.GetTimestamp() - t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Profile[name] = (old.Ms + took, old.Calls + 1); ProfilePeak[name] = Math.Max(ProfilePeak.GetValueOrDefault(name), took);
    }

    /// <summary>1E7EA for one AI settlement.</summary>
    private void TurnOf(byte me)
    {
        SyncParticipants();
        var ai = _aiBlocks[me];
        BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(1), BinaryPrimitives.ReadUInt32LittleEndian(ai.AsSpan(1)) + 1);
        var chain = AiChain(me);
        if ((BinaryPrimitives.ReadUInt32LittleEndian(ai.AsSpan(1)) & 0x1f) == 0)
            Prof("ai.census", () => OriginalAiRules.Census(ai, _participants[me], chain, (byte)Map.Width, (byte)Map.Height, _aiSquadWorld!.Fog));
        Prof("ai.steps", () => OriginalAiSquadSteps.Tick(ai, _aiSquadWorld!));
        Prof("ai.states", () => _aiStates!.Run(ai));
        Prof("ai.garrison", () => _aiGarrison!.Run(ai));
        Prof("ai.military", () => _aiMilitary!.Run(ai));
        Prof("ai.orders", () => _aiOrders!.Run(ai));
        Prof("ai.economy", () => { _aiEconomy!.Plan(ai); _aiEconomy.Execute(ai); });
        Prof("ai.upkeep", () => OriginalAiRules.Upkeep(ai, chain));
        if (OriginalAiTurn.Decide(_aiTurnWorld!, me, 0, (uint)Map.Width, _aiThreat)) _aiEconomy.Construction(ai);   // 23551..23666
        Prof("ai.plans", () => OriginalAiPlans.Execute(_aiTurnWorld!, me, _aiThreat));
        OriginalAiResearch.Step(ai, me, _aiArmy, _aiTree, _aiTemplates, chain, _terrainCatalog.NextRandomByte);
        SyncDiplomacy();
        Prof("ai.commands", ExecuteAiCommands);
    }

    /// <summary>Carries out the commands the AI wrote into OBE +14 of the enemy's units (adapter, see the class summary). A
    /// command (bit 40h: 40h stand, 48h move to +22/+23, 50h/58h attack +1C) is replaced by the activity the original's
    /// command handlers leave in +14 — 0 idle, 1 moving, 2/3 attacking — as read by 1EE02 (retarget of idle, moving and
    /// out-of-area attackers) and by the squad census (205FB counts +14 &amp; 7 when bit 40h is clear). Moving ends in 0 on
    /// arrival, attacking when the target is gone.</summary>
    private void ExecuteAiCommands()
    {
        foreach (var building in Actors.Where(a => a.Team == _aiTeam && a.Alive && a.Structure).ToArray())
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
            if (building.Index02 == 12 && building.ConstructionRemaining <= 0 && building.Record.Length >= 0x29)
                OriginalLabResearch.Tick(new SkirmishLabWorld(this, building.Record[3]), building.Record, _participants[building.Record[3]], _aiBlocks[building.Record[3]] ?? [], _globalTick);
            byte order = building.Record[0x14];
            if ((order & 0xf8) == 0x60) building.Record[0x14] = 4;              // produce: the queue head +22 is the template
            else if ((order & 0xf8) == 0x58) { EnemyBuild(building); building.Record[0x14] = 0; }
        }
        foreach (var worker in Actors.Where(a => a.Team == _aiTeam && a.Alive && a.Worker)) EnemyWorkerCommand(worker);
        foreach (var actor in Actors.Where(a => a.Team == _aiTeam && a.Alive && !a.Structure && !a.Worker))
        {
            byte value = actor.Record[0x14];
            // A new unit's first update clears bit 7 of +14 (A839 sets 80h; the WRK entries start with TEST [ESI+14],80 / AND 7F).
            if ((value & 0x80) != 0 && (value & 0x40) == 0) actor.Record[0x14] = value &= 0x7f;
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
                case 1 when actor.Goal is { } goal && NoProgress(actor, goal): actor.Goal = null; actor.Route.Clear(); actor.Record[0x14] = 0; break;
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
                    if (cargo is 1 or 2 || Find(mine) is { Alive: true } m && m.Team == worker.Team && m.Source.TemplateName == "Шахта")
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

    /// <summary>Adapter for the original's failed route search, which ends the move: an AI move whose goal has not come closer
    /// for 20 seconds (an unreachable cell across the river) is dropped, so the squad sees it did not arrive (and orders a
    /// bridge, 1FC28 → 1EB5C mode 0Ah).</summary>
    /// <summary>Adapter: a unit that has not moved for 10 seconds ends its move (the original's stuck patrol never reports arrival).</summary>
    private bool NoProgress(SkirmishActor actor, Vector2I goal)
    {
        if (actor.Position.DistanceTo(actor.LastPosition) > 1f || Elapsed < actor.StillSince) { actor.LastPosition = actor.Position; actor.StillSince = Elapsed; return false; }
        return Elapsed - actor.StillSince > 10;
    }
    private readonly Dictionary<int, PopulationTotals> _enemyPopulation = [];
    /// <summary>The free people (participant +24) of every AI settlement grow by the ported population rules, as the player's do.</summary>
    private void TickEnemyPopulation()
    {
        if (!AiEnabled) return;
        foreach (int slot in AiSlots)
        {
            if (_aiBlocks[slot] == null) continue;
            var population = _enemyPopulation.GetValueOrDefault(slot);
            if ((_populationTick & 31) == 0 || population.Limit == 0)
            {
                _enemyPopulation[slot] = population = PopulationOf((byte)slot);
                // D453 keeps participant +28 (load) and +2C (farm support); the AI's census and 21E99 read them.
                var record = _participants[slot];
                W32(record, 0x28, population.Used); W32(record, 0x2c, population.FarmSupport);
            }
            var r = AiResources(slot);
            uint people = OriginalPopulationRules.Grow(_populationTick, r.Fourth, population.Used, population.FarmSupport, population.Limit);
            if (people != r.Fourth) SetAiResources(slot, r with { Fourth = people });
        }
    }

    /// <summary>A6AE when an enemy building is finished: kind 1 «after building template N» in the AI's tree.</summary>
    private void EnemyBuildingFinished(SkirmishActor building)
    {
        var tree = TreeOf(building.Record[3]);
        if (tree.Length > 0) OriginalTechRules.Complete(tree, 1, (byte)building.Source.TemplateIndex);
        if (tree.Length > 0 && BinaryPrimitives.ReadUInt32LittleEndian(_participants[building.Record[3]].AsSpan(0x2c)) is >= 0x28 and <= 0x32) OriginalTechRules.Complete(tree, 2, 0);   // AFDB's end of a build: farms support 28h..32h
    }

    /// <summary>Enemy objects that make something (WRK AH=6 table with rows): castle, barracks, factory, stable…</summary>
    private bool IsEnemyProducer(SkirmishActor actor) =>
        actor.Team != 0 && actor.Structure && Ctx(actor.Record[3]).Makes.TryGetValue((byte)actor.Source.TemplateIndex, out var makes) && makes.Length > 0;

    /// <summary>Production interval of the producer's WRK row for the template (data/original_producers.json).</summary>
    private byte EnemyInterval(SkirmishActor producer, byte template) =>
        Ctx(producer.Record[3]).Intervals.TryGetValue(((byte)producer.Source.TemplateIndex, template), out var interval) ? interval : (byte)40;

    /// <summary>An enemy recruit walks from the producer's exit to the settlement's home cell (AI block +61/+62), so the exit
    /// is free for the next one (adapter: the player clears the exit by hand, the original's WRK release is not ported).</summary>
    private void EnemyRecruitLeaves(SkirmishActor recruit)
    {
        var ai = _aiBlocks[recruit.Record[3]];
        if (ai == null) return;
        OrderMove(recruit, new Vector2I(ai[0x61], ai[0x62]), 15);   // a crowded home still leaves room further out
    }

    /// <summary>An enemy producer whose exit is taken by one of the enemy's own idle units sends that unit away towards the
    /// home (adapter, like <see cref="EnemyRecruitLeaves"/>; the player clears the exit by hand).</summary>
    private void EnemyClearExit(SkirmishActor producer, TemplateEntry template)
    {
        var exit = RecruitExit(producer, template);
        var cell = new Vector2I((int)(exit.X >> 5), (int)(exit.Y >> 5));
        var blocker = Actors.FirstOrDefault(a => a.Team == producer.Team && a.Alive && !a.Structure && !a.Stepping && a.Goal == null && a.Target == 0
            && FootprintCells(a, a.Cell).Contains(cell));
        var ai = _aiBlocks[producer.Record[3]];
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
        var templates = _templates.Armies["ARMY__02.DAT"].Templates;
        if (index >= templates.Count() || !Rules.Units.ContainsKey(templates[index].Name) || templates[index].Name is "Замок") { builder.Record[0x22] = 0xff; return; }
        int slot = builder.Record[3];
        var template = templates[index];
        // The builder engine AFDB (ported): pays per segment, probes, creates the segments and steps along a line (bridges, fences).
        var engine = new OriginalBuilder(_arctangent) { Toggle = _builderToggle };
        engine.Run(new SkirmishBuilderWorld(this, slot), builder.Record, Convert.FromHexString(template.RawRecordHex), _participants[slot]);
        _builderToggle = engine.Toggle;
        builder.Record[0x22] = 0xff;
    }

    /// <summary>AA07..AA35: a produced unit counts in its class (DAT[41] &amp; 0Fh) in the settlement's statistics (+1A16 + class); the tenth
    /// of class 8 (militia, knights) completes development event (8, kind 2), the tenth of class 9 (archers, arsonists) event 9.</summary>
    private void CountProduced(SkirmishActor recruit)
    {
        if (recruit.Record[3] >= _aiBlocks.Length || _aiBlocks[recruit.Record[3]] is not { } stats) return;
        stats[0x1a16 + (recruit.Parameters[0x41] & 0xf)]++;
        var tree = TreeOf(recruit.Record[3]);
        if (tree.Length == 0) return;
        if (stats[0x1a1e] == 10) OriginalTechRules.Complete(tree, 2, 8);
        if (stats[0x1a1f] == 10) OriginalTechRules.Complete(tree, 2, 9);
    }

    /// <summary>The port's own AI logic (default) or the original's (settings): see <see cref="IAiMilitaryWorld.Improved"/>.</summary>
    public bool ImprovedAi { get; set; } = !DisplaySettings.OriginalAi;
    private byte[] _arctangent = [];
    private readonly OriginalLineNeighbors _lineNeighbors = OriginalLineNeighbors.From(GameData.Read<LineNeighborData>("res://data/line_neighbors.json"));
    private byte _builderToggle = 1;

    /// <summary>The builder's world in the skirmish: AD5F on the occupancy, A758 as a new actor under construction (adapter for
    /// the timing); registration and tree updates happen when the building is finished.</summary>
    /// <summary>The laboratory's research (CCCB): items of the settlement's tree (A716), the random byte 9E41, completion A6AE.</summary>
    private sealed class SkirmishLabWorld(Skirmish battle, byte owner) : ILabResearchWorld
    {
        public IReadOnlyList<(byte Id, uint Cost)> Researchable(byte owner, byte army)
        {
            var tree = battle.TreeOf(owner);
            return tree.Length == 0 ? [] : OriginalTechRules.Researchable(tree).Select(at => (tree[at + 1], BinaryPrimitives.ReadUInt32LittleEndian(tree.AsSpan(at + 7)))).ToList();
        }
        public byte Random() => battle._terrainCatalog.NextRandomByte();
        public void Complete(byte item) { var tree = battle.TreeOf(owner); if (tree.Length > 0) OriginalTechRules.Complete(tree, 0, item); }
    }

    private sealed class SkirmishBuildCheckWorld(Skirmish battle, int slot) : IBuildCheckWorld
    {
        private readonly SkirmishBuilderWorld _probe = new(battle, slot);
        public BuilderProbe Probe(byte template, byte army, uint x, uint y, out byte[]? blocker) => _probe.Probe(template, army, x, y, out blocker);
        public IEnumerable<(byte[] Record, byte[] Template)> Objects(byte participant) =>
            battle.Actors.Where(a => a.Alive && a.Record[3] == participant && a.Source.TemplateFile == "ARMY__02.DAT").Select(a => (a.Record, a.Parameters));
        /// <summary>WRK AH=4 of the mine (template 6): ore of type 1 or 2 must lie under the site (FS:59, as the deposit at its completion);
        /// the other templates answer «allowed» (the entry returns at once, ZF=0). The entry of template 19 is not ported (allows).</summary>
        public BuilderProbe CreationProbe(byte template, byte army, byte plane, uint x, uint y) =>
            OriginalCreationPlacement.Probe((byte)battle.Map.Width, (byte)battle.Map.Height, battle.Map.Cells, battle._properties, battle.Occupancy.Cells, Math.Min(plane, (byte)2),
                Convert.FromHexString(battle._templates.Armies["ARMY__02.DAT"].Templates[template].RawRecordHex), x, y,
                reference => (battle.Actors.First(a => a.Id == reference).Record, battle.Actors.First(a => a.Id == reference).Parameters)).Status;
        public bool Hook(byte template, byte army, uint x, uint y) =>
            template != 6 || OriginalMineRules.Initialize(battle.Map.Cells, battle.Map.Width, battle.Map.Height, x, y).Remaining > 0;
        public bool Line(byte template, byte army, uint x, uint y) => battle._lineNeighbors.Allowed((byte)battle.Map.Width, (byte)battle.Map.Height,
            (plane, cell) => battle.Occupancy.Cells[plane * battle.Map.Width * battle.Map.Height + cell] is var id and not 0 ? battle.Actors.FirstOrDefault(a => a.Alive && a.Id == id)?.Record : null,
            index => battle._aiTemplates[index], template, x, y);
        public bool Explored(byte participant, uint x, uint y)
        {
            int cell = (int)((y >> 5) * battle.Map.Width + (x >> 5));
            return cell < 0 || cell >= battle.FogMap.Length || (battle.FogMap[cell] & (1 << participant)) == 0;
        }
    }

    private sealed class SkirmishBuilderWorld(Skirmish battle, int slot) : IBuilderWorld
    {
        public BuilderProbe Probe(byte template, byte army, uint x, uint y, out byte[]? blocker)
        {
            blocker = null;
            var cell = new Vector2I((int)(x >> 5), (int)(y >> 5));
            if (!battle.Inside(cell)) return BuilderProbe.Rejected;
            var parameters = battle._templates.Armies["ARMY__02.DAT"].Templates[template].Construction;
            if (parameters.FootprintWidth == 0) return BuilderProbe.Rejected;
            var result = OriginalConstructionRules.ProbePlacement(battle.Occupancy.Width, battle.Occupancy.Height, battle.Map.Cells, battle._properties, battle.Occupancy.Cells,
                parameters.FootprintWidth, parameters.FootprintHeight, parameters.TerrainMask.ToArray(), x, y);
            if (result.Status == ConstructionPlacementStatus.Rejected) return BuilderProbe.Rejected;
            if (result.Status == ConstructionPlacementStatus.Occupied)
            {
                blocker = battle.Actors.FirstOrDefault(a => a.Alive && a.Id == result.BlockingObjectOffset)?.Record;
                return blocker == null ? BuilderProbe.Rejected : BuilderProbe.Blocked;
            }
            var area = new Rect2I(cell - new Vector2I(parameters.FootprintWidth / 2, parameters.FootprintHeight / 2), new(parameters.FootprintWidth, parameters.FootprintHeight));
            return battle._reservations.Any(r => area.HasPoint(r.Key.Cell)) ? BuilderProbe.Rejected : BuilderProbe.Clear;
        }
        public byte[]? Create(byte army, byte owner, byte template, byte variant, uint x, uint y) =>
            battle.EnemyConstruct(template, new Vector2I((int)(x >> 5), (int)(y >> 5)), slot).Record;
        public void Register(byte[] record) { }
        public void Complete(byte template, byte kind) { }
        public void Recount(byte[] builder) { }
    }

    /// <summary>A new enemy building under construction at the cell (the castle's 58h and 23551's A758, adapter).</summary>
    private SkirmishActor EnemyConstruct(byte index, Vector2I cell, int slot)
    {
        string file = FileOf(slot);
        var template = _templates.Armies[file].Templates[index];
        var building = NewEconomyActor(index, cell, false, false, TeamOfSlot(slot), file);
        // A line template stands in the plane DAT[77] & 3 names (the bridge in plane 0: units walk in plane 1 and cross water on a bridge found below them, 7EAC).
        if ((BinaryPrimitives.ReadUInt32LittleEndian(building.Parameters.AsSpan(0x40)) & 0x80) != 0) building.Record[0x0d] = (byte)(building.Parameters[0x77] & 3);
        building.ConstructionDuration = building.ConstructionRemaining = template.InitialCondition * Math.Max(1, (int)building.Parameters[0x57]) / (double)Rules.TicksPerSecond;
        OriginalBuildingLifecycle.MarkConstruction(building.Record);
        building.Record[0x14] &= 7;   // the handled construction command leaves activity 1 at once (see ExecuteAiCommands)
        if (building.Record.Length > 0x20) building.Record[0x20] = 0xff;
        if (building.Record.Length >= 0x28) { building.Record.AsSpan(0x22, 5).Fill(0xff); building.Record[0x27] = 0xff; }   // producers' queue
        if (building.Index02 == 12) { building.Record[0x22] = building.Record[0x23] = 1; building.Record[0x15] = 0; }   // the laboratory's AH=0
        if (building.Index02 != -1 && template.Name == "Шахта" && building.Record.Length >= 0x28)
        {
            var deposit = OriginalMineRules.Initialize(Map.Cells, Map.Width, Map.Height, (uint)building.Position.X, (uint)building.Position.Y);
            building.Record[0x23] = deposit.Type; BinaryPrimitives.WriteUInt32LittleEndian(building.Record.AsSpan(0x24), deposit.Remaining);
        }
        Actors.Add(building);
        Occupancy.WriteFootprint(building.Parameters[3], building.Parameters[4], building.Record[0x0d], (uint)building.Position.X, (uint)building.Position.Y, building.Id);
        if (building.Index02 != -1 && (BinaryPrimitives.ReadUInt32LittleEndian(building.Parameters.AsSpan(0x40)) & 0x80) != 0) ReshapeLine(building);   // WRK AH=0 → FS:55
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
            actor.Target = 0; actor.Goal = goal; actor.ResumeGoal = null; actor.ReplanTimer = 0;
            actor.LastPosition = actor.Position; actor.StillSince = Elapsed;
            if (actor.Stepping) actor.Route.Clear();
            return true;
        }
        return false;
    }

    private sealed class AiSquadWorld(Skirmish battle) : IAiGarrisonWorld, IAiSquadStateWorld, IAiOrdersWorld, IAiEconomyWorld, IAiWorkerWorld
    {
        private SearchWorld _search => new(battle, (byte)battle._aiSlot);   // the settlement whose turn runs
        public IReadOnlyList<(byte[] Record, byte[] Template)> Chain(byte participant) => battle.AiChain(participant);
        public (byte[] Record, byte[] Template) Resolve(uint reference) => _search.Resolve(reference);
        // Cells outside the map read memory beyond the grid in the original; here they are empty.
        public uint Occupant(int plane, int cell) => cell >= 0 && cell < battle.Map.Width * battle.Map.Height ? _search.Occupant(plane, cell) : 0;
        public byte MapWidth => (byte)battle.Map.Width;
        public byte MapHeight => (byte)battle.Map.Height;
        public uint HostileMask(byte participant) => participant < battle.Diplomacy.Count ? battle.Diplomacy.Hostile[participant] : 0;
        public (uint X, uint Y) BoundsLimit => ((uint)battle.Map.Width * 32, (uint)battle.Map.Height * 32);
        private readonly OriginalAiFormations _formations = OriginalAiFormations.From(GameData.Read<OriginalAiData>("res://data/original_ai.json"));
        /// <summary>Step 4 (1EF26, ported) with its message A4C2.</summary>
        public bool StepFour(Span<byte> squad, bool started)
        {
            byte x = squad[0x20], y = squad[0x21];
            return _formations.Step(squad, Chain(squad[0]), text => battle.WorkSound(text, x, y), MapWidth, MapHeight);
        }
        public byte Global19E9 => 0xff;
        public byte Random() => battle._terrainCatalog.NextRandomByte();
        public byte Random2() => battle._terrainCatalog.NextSecondaryByte();
        public byte Random3() => battle._aiRandom[battle._aiRandom3++];   // 9E69: third index [19ED] into the same table
        public byte Fog(int cell)
        {
            return cell >= 0 && cell < battle.FogMap.Length ? battle.FogMap[cell] : (byte)0xff;   // the real [18DA]
        }
        public Span<byte> ParticipantRecord(byte participant) => battle._participants[participant];
        public byte TileOre(int cell) => cell >= 0 && cell < battle.Map.Cells.Length ? (byte)(battle.Map.Cells[cell] >> 16) : (byte)0;
        /// <summary>D363 (ported, <see cref="OriginalBuildCheck.SiteAllowed"/>).</summary>
        public bool SiteAllowed(byte template, byte army, byte participant, byte x, byte y) =>
            battle.Inside(new Vector2I(x, y)) && OriginalBuildCheck.SiteAllowed(new SkirmishBuildCheckWorld(battle, participant),
                Convert.FromHexString(battle._templates.Armies["ARMY__02.DAT"].Templates[template].RawRecordHex), template, army, participant, x, y);
        public void Send(byte type, byte other, byte owner, byte bh, uint parameter) => battle._aiTurnWorld!.Send(type, other, owner, bh, parameter);
        /// <summary>10AD2 (ported, <see cref="OriginalCreationPlacement"/>): random points in growing squares around the point,
        /// each checked by AC63 on the occupancy planes.</summary>
        public (uint X, uint Y)? FreeSpot(uint templateArmy, byte variant, uint x, uint y)
        {
            long t0 = battle.P0(); try {
            var template = battle._aiTemplates[(byte)templateArmy];
            byte plane = (byte)Math.Min(variant & 3, 2);
            BuilderProbe Probe(uint px, uint py) => OriginalCreationPlacement.Probe((byte)battle.Map.Width, (byte)battle.Map.Height, battle.Map.Cells, battle._properties,
                battle.Occupancy.Cells, plane, template, px, py, reference => _search.Resolve(reference)).Status;
            var result = OriginalCreationPlacement.Search(Random, Random2, (uint)battle.Map.Width * 32, (uint)battle.Map.Height * 32, template, x, y, Probe);
            return result.Found ? (result.X, result.Y) : null;
            } finally { battle.P1("w.freespot", t0); }
        }
        /// <summary>AF07 (ported, <see cref="OriginalBuildCheck"/>): probe, range of a centre building, explored cell. The template's own
        /// check (WRK AH=4) is not ported and allows; the neighbour rule of lines is BC2E (ported).</summary>
        public bool BuildingSite(uint templateArmy, byte participant, byte x, byte y) =>
            battle.Inside(new Vector2I(x, y)) && OriginalBuildCheck.Allowed(new SkirmishBuildCheckWorld(battle, participant),
                Convert.FromHexString(battle._templates.Armies["ARMY__02.DAT"].Templates[(byte)templateArmy].RawRecordHex), (byte)templateArmy, 2, participant, (uint)(x * 32 + 16), (uint)(y * 32 + 16));
        public byte[]? Create(byte participant, byte army, byte template, byte variant, uint x, uint y) =>
            battle.AiSlots.Contains(participant) ? battle.EnemyConstruct(template, new Vector2I((int)(x >> 5), (int)(y >> 5)), participant).Record : null;
        public void Register(byte[] record) { }
        /// <summary>A4C2: the squad's message is a sound of WORK.FX at the squad's centre.</summary>
        public void Message(ReadOnlySpan<byte> squad, uint text) => battle.WorkSound(text, squad[0x20], squad[0x21]);
        public byte Army(byte participant) => battle._participants[participant][0];
        public void WorkerState(Span<byte> ai, int at) => battle._aiWorkers!.Run(ai, at);
        public uint TileProperty(int cell) => cell >= 0 && cell < battle.Map.Cells.Length
            ? BinaryPrimitives.ReadUInt32LittleEndian(battle._properties.AsSpan((int)(battle.Map.Cells[cell] & 0xffff) * 8)) : 0;
        public Span<byte> Block(byte participant) => battle._aiBlocks[participant];
        public ReadOnlySpan<byte> Participant(byte participant) => battle._participants[participant];
        public byte ParticipantCount => battle._participantCount;
        /// <summary>Adapter: siege engines (16, 26) do not march with the campaign's army yet (they stay idle at home and the army never reports ready), so the
        /// fill of the army request leaves them out; other requests may still order them.</summary>
        public bool Improved => battle.ImprovedAi;
        public IReadOnlyList<byte> ArmyFill(byte participant, uint mask, uint value) => battle.ImprovedAi ? Buildable(participant, mask, value) : Buildable(participant, mask, value).Where(t => t is not (16 or 26)).ToList();
        public IReadOnlyList<byte> Buildable(byte participant, uint mask, uint value) => OriginalTechRules.Buildable(battle.Ctx(participant).Templates, battle.TreeOf(participant), true, mask, value);
        public byte[] Template(byte participant, byte template) => battle.Ctx(participant).Templates[template];
        public uint Reference(byte[] record) { long t = battle.P0(); var id = battle.ActorOfRecord(record)?.Id ?? 0; battle.P1("w.reference", t); return id; }
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
        public bool TreeAllows(byte participant, byte template) => OriginalTechRules.Available(battle.TreeOf(participant), template);
    }

    private sealed class AiTurnWorld(Skirmish battle) : IAiTurnWorld
    {
        private SearchWorld _search => new(battle, (byte)battle._aiSlot);   // the settlement whose turn runs
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
