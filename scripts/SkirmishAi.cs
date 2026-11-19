using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>The settlement AI of the original (LE:1 1E7EA, docs/ai.md) driving the enemy settlement of the skirmish.
///
/// Ported and verified parts run as in the original, once per game tick after the object update (LE:1 31D6): census
/// (1E87A, every 32 turns), squad steps (1ED26), squad states (1F078), garrison (209B0), military requests (20DD0), upkeep (226F1), the turn
/// with its decisions (2323A), the plan executor (23667) and research (23C11). Adapters of the port, not the original:
/// <list type="bullet">
/// <item>Not yet ported steps are skipped: 227C7, 21E99/221AE (economy), the
/// construction stage 23551, worker states 9/10, the formation table of step 4, messages A4C2, the rescue 20001.</item>
/// <item>Step 4 (formation march) sends the members to the target cell with command 48h instead of a formation.</item>
/// <item>Commands the AI writes into OBE +14 (48h move to +22/+23, 50h/58h attack +1C, 40h stand) are carried out by
/// <see cref="ExecuteAiCommands"/> through the port's movement and attack, as the player's orders are, and replaced by
/// activity codes (0 idle, 1 moving, 2/3 attacking) inferred from what the AI reads back; the original WRK command
/// handlers are not ported.</item>
/// <item>Fog of war is not ported: every cell counts as explored ([18DA]), objects keep their scenario visibility FFh.</item>
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
    private byte[] _aiRandom = [];
    private byte _aiRandom3;
    private AiSquadWorld? _aiSquadWorld;
    private AiTurnWorld? _aiTurnWorld;

    /// <summary>When false the enemy keeps the old experimental behaviour (tests that need a passive enemy).</summary>
    public bool AiEnabled { get; set; } = true;
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
        _aiSquadWorld = new AiSquadWorld(this);
        _aiTurnWorld = new AiTurnWorld(this);
        _aiStates = new OriginalAiSquadStates(_aiSquadWorld, Convert.FromHexString(data.ArctangentHex), Convert.FromHexString(data.SquadMessagesHex));
        _aiMilitary = new OriginalAiMilitary(_aiSquadWorld, Convert.FromHexString(data.ArmyClassesHex));
        _aiGarrison = new OriginalAiGarrison(_aiSquadWorld, _aiMilitary, _aiStates);
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
        OriginalAiRules.Upkeep(ai, chain);
        if (OriginalAiTurn.Decide(_aiTurnWorld!, me, 0, (uint)Map.Width, _aiThreat))
            BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(1), BinaryPrimitives.ReadUInt32LittleEndian(ai.AsSpan(1)) - 0x42); // 23551 not ported; 2365C
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
        foreach (var actor in Actors.Where(a => a.Team == 1 && a.Alive && !a.Structure && !a.Worker))
        {
            byte value = actor.Record[0x14];
            uint target = BinaryPrimitives.ReadUInt32LittleEndian(actor.Record.AsSpan(0x1c));
            if ((value & 0x40) != 0)
            {
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

    /// <summary>Move one unit like <see cref="Move"/> does for the player: the nearest free standing place around the cell.</summary>
    private bool OrderMove(SkirmishActor actor, Vector2I destination)
    {
        if (!Inside(destination)) return false;
        for (int radius = 0; radius <= 5; radius++)
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

    private sealed class AiSquadWorld(Skirmish battle) : IAiGarrisonWorld, IAiSquadStateWorld
    {
        private readonly SearchWorld _search = new(battle, (byte)battle.Rules.EnemySlot);
        public IReadOnlyList<(byte[] Record, byte[] Template)> Chain(byte participant) => battle.AiChain(participant);
        public (byte[] Record, byte[] Template) Resolve(uint reference) => _search.Resolve(reference);
        public uint Occupant(int plane, int cell) => _search.Occupant(plane, cell);
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
        public byte Explored(int cell) => 0xff;
        public void Message(ReadOnlySpan<byte> squad, uint text) { }
        public void Rescue(Span<byte> squad) { }
        public void WorkerState(Span<byte> squad) { }
        public Span<byte> Block(byte participant) => battle._aiBlocks[participant];
        public ReadOnlySpan<byte> Participant(byte participant) => battle._participants[participant];
        public byte ParticipantCount => battle._participantCount;
        public IReadOnlyList<byte> Buildable(byte participant, uint mask, uint value) => OriginalTechRules.Buildable(battle._aiTemplates, battle._aiTree, true, mask, value);
        public byte[] Template(byte participant, byte template) => battle._aiTemplates[template];
        public uint Reference(byte[] record) => battle.Actors.FirstOrDefault(a => ReferenceEquals(a.Record, record))?.Id ?? 0;
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
