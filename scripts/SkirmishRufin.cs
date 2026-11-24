using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>The mages of the forest (ARMY__03 13 «Руфин» … 21 «Кочет») run their quest script (<see cref="OriginalRufin"/>, ARMY__03.WRK 8EC): they say what they want to the player
/// through the message queue (the dialog window shows it), take the answers and switch the traps off. ADAPTERS in the world below: the sweep of the territory (FS:16F), the creation of
/// the switch object (FS:45), the unit step of the mage (DD/E1: he stands) and the reveal of the map (clears the fog of the player).</summary>
public sealed partial class Skirmish
{
    private SkirmishRufinWorld? _rufinWorld;
    public byte[] ItemWorldInventory(int participant) => ItemWorld.Inventory(participant);
    public static bool IsPhantom(SkirmishActor actor) => actor.Source.TemplateFile == "ARMY__03.DAT" && actor.Source.TemplateIndex is 3 or 32;
    /// <summary>A mage or his phantom: the units of the mage's script (the discharge, the vanishing action).</summary>
    public static bool IsMageUnit(SkirmishActor actor) => IsHermit(actor) || IsPhantom(actor);
    public static bool IsHermit(SkirmishActor actor) => actor.Scenery && actor.Source.TemplateFile == "ARMY__03.DAT" && actor.Source.TemplateIndex is >= 13 and <= 21;

    /// <summary>A mage at war with the player (the player attacked him or his friends): he fights back like any unit (DAT of ARMY__03 13: reach 10, strike 7).</summary>
    public bool MageFights(SkirmishActor mage) => IsMageUnit(mage) && Diplomacy.AtWar(mage.Record[3], Rules.PlayerSlot);

    /// <summary>The flashes of the mages' strikes (ARMY__03 effect type 0, 22h-byte records): 72×72, F0h + 4·heading + stage.</summary>
    public List<byte[]> MageFlashes { get; } = [];
    private SkirmishSpellWorld? _spellWorld;

    /// <summary>9727 with BL = DAT[70] = 0 for an attacker of army 3: the mage's strike creates the effect type 0 of ARMY__03 (<see cref="OriginalMageSpell"/>); its ending decides between
    /// the blast (type 8, BD0F around the point), fire (ARMY__00 type 13h) and, one time in eight, a summoned phantom.</summary>
    private void MageDischarge(SkirmishActor mage, WorkerImpact impact)
    {
        if (impact.X >= Map.Width * 32 || impact.Y >= Map.Height * 32) return;
        var flash = new byte[0x22]; flash[1] = impact.Army; flash[5] = impact.Layer;
        BinaryPrimitives.WriteUInt32LittleEndian(flash.AsSpan(7), impact.X); BinaryPrimitives.WriteUInt32LittleEndian(flash.AsSpan(0xc), impact.Y);
        _spellWorld ??= new SkirmishSpellWorld(this); _spellWorld.Attacker = mage;
        OriginalMageSpell.Create(_spellWorld, flash, mage.Record, impact.TargetX, impact.TargetY);
        MageFlashes.Add(flash);
    }
    private void TickMageFlashes()
    {
        foreach (var flash in MageFlashes) OriginalMageSpell.Step(flash);
        MageFlashes.RemoveAll(f => f[0] == 0xff);
    }

    private sealed class SkirmishSpellWorld(Skirmish battle) : IMageSpellWorld
    {
        public SkirmishActor Attacker = null!;
        public void Effect(byte army, byte type, byte layer, uint x, uint y)   // 9727
        {
            if ((type & 0x80) != 0) { army = 0; type &= 0x7f; }
            if (x >= battle.Map.Width * 32 || y >= battle.Map.Height * 32) return;
            if (army == 0 && type == OriginalBurnRules.Small) { battle.FireWorld.Spawn(0, type, layer, x, y, null); return; }
            if (army == 3 && type == OriginalMageSpell.Blast)
            {
                var blast = new byte[0x22]; blast[1] = army; blast[5] = layer;
                BinaryPrimitives.WriteUInt32LittleEndian(blast.AsSpan(7), x); BinaryPrimitives.WriteUInt32LittleEndian(blast.AsSpan(0xc), y);
                OriginalMageSpell.BlastStep(this, blast, Attacker.Record);
            }
        }
        public void Sound(uint id) { var flash = new byte[0x22]; flash[1] = 3; BinaryPrimitives.WriteUInt32LittleEndian(flash.AsSpan(7), (uint)Attacker.Position.X); BinaryPrimitives.WriteUInt32LittleEndian(flash.AsSpan(0xc), (uint)Attacker.Position.Y); battle.EffectSound(flash, (int)id); }
        public void Blast(byte attack, byte kind, byte planes, byte cellX, byte cellY, byte width, byte height)   // BD0F
        {
            int plane = battle.Occupancy.Width * battle.Occupancy.Height; var struck = new List<SkirmishActor>();
            OriginalCatapultRules.Hit(attack, kind, planes, cellX, cellY, width, height, (byte)battle.Map.Width, (byte)battle.Map.Height,
                (p, cell) => p * plane + cell < battle.Occupancy.Cells.Length ? battle.Occupancy.Cells[p * plane + cell] : 0,
                r => { if (battle.Find(r) is not { } a) return (new byte[0x40], new byte[171]); struck.Add(a); return (a.Record, a.Parameters); });
            foreach (var victim in struck) if (!victim.Alive) battle.Kill(victim);
        }
        public (bool Found, byte Layer, uint X, uint Y) FindSpot(byte template, byte army)   // 9064 beside the mage
        {
            var phantom = Convert.FromHexString(battle._templates.Armies["ARMY__03.DAT"].Templates[template].RawRecordHex);
            var spot = OriginalSpotFinder.Find(Attacker.Record, phantom, Attacker.Parameters, battle._terrainCatalog.NextRandomByte, (layer, x, y) =>
            {
                int cx = (int)(x >> 5), cy = (int)(y >> 5);   // ADAPTER: AC63 is a free cell of the layer in the occupancy
                return cx >= 0 && cy >= 0 && cx < battle.Map.Width && cy < battle.Map.Height && battle.Occupancy.Cells[(layer & 3) * battle.Occupancy.Width * battle.Occupancy.Height + cy * battle.Occupancy.Width + cx] == 0;
            });
            return (spot.Found, spot.Layer, spot.X, spot.Y);
        }
        public byte[]? CreateObject(byte army, byte owner, byte template, byte layer, uint x, uint y)
        { var phantom = battle.SpawnPhantom(owner, template, layer, x, y); if (phantom != null) battle.Log("Руфин призывает фантома."); return null; }   // A758; the set-up 43E5 is the script's AH = 0
        public void Setup(byte[] created) { }
        public byte Random() => battle._terrainCatalog.NextRandomByte();
        public byte[] Inventory(int owner) => battle.ItemWorld.Inventory(owner);
    }

    // ---- the vanishing of the hurt mage (docs/rufin.md): the continuation of the unit step and the action FS:105
    private sealed class MageDrop(Skirmish battle) : IMageWorld
    { public void DropItems(byte cellX, byte cellY, byte owner) => battle.ItemRules.DropAll(battle.ItemWorld, owner, cellX, cellY); }
    private MageDrop? _mageDrop;

    public void MageVitals(SkirmishActor mage) { (_vitalsWorld ??= new(this)).Current = mage; OriginalCombatRules.Vitals(mage.Record, mage.Parameters, (byte)_globalTick, (byte)Map.Width, (byte)Map.Height, Map.Cells, _properties, _terrainCatalog, _vitalsWorld); }

    /// <summary>FS:E1 (8227): the counter +2E, the state machine of the unit (state 3 is the vanishing, FS:105; a pending request — bit 40h of +14 — ends any other state at once),
    /// then the continuation of the script (<see cref="OriginalMageCloak"/>). The other actions (ED, E9, F1: stand, walk, strike) are done by UpdateUnit.</summary>
    public void MageStandardStep(SkirmishActor mage)
    {
        var r = mage.Record; if (IsHermit(mage) && !MageFights(mage)) r[0x2e]++;   // UpdateUnit counts the updates of a fighting mage and of a phantom (8227)
        if ((sbyte)r[0x19] > 0 && (r[0x17] & 0x80) == 0)
        {
            bool done = false;
            if ((r[0x14] & 7) == 3)
            {
                if ((r[0x2d] & 15) == 2) { mage.Stepping = false; r[0x2d] &= 0xf0; }   // the walk is cut short (MoveTick does not run while the action goes)
                done = VanishStep(mage);
            }
            else if ((r[0x14] & 0x40) != 0) done = true;
            if (done) { r[0x14] = (byte)((r[0x14] >> 3) & 7); r[0x30] = 0; r[0x15] = 0; }
        }
        OriginalVanishAction.Motion.Step(mage.Record, mage.Parameters);
        if (IsPhantom(mage)) { OriginalPhantom.Continuation(r); return; }
        _mageDrop ??= new MageDrop(this);
        // the continuation after the FS:E1 table is the hermit's own (verified for all nine, tests/fixtures/mage-cloak.json)
        switch (mage.Source.TemplateIndex)
        {
            case 13 or 14 or 15 or 19 or 21: OriginalMageCloak.Step(_mageDrop, r, OriginalMageCloak.RufinLow, OriginalMageCloak.RufinHigh, true); break;
            case 17: OriginalMageCloak.Step(_mageDrop, r, OriginalMageCloak.RufinLow, OriginalMageCloak.RufinHigh, true, 0x55, 0x1f); break;
            case 16 or 18 or 20: OriginalMageCloak.FlagStep(_mageDrop, r); break;
            default: OriginalMageCloak.Step(_mageDrop, r, OriginalMageCloak.FirstMageLow, OriginalMageCloak.FirstMageHigh, false); break;
        }
    }

    /// <summary>FS:105 (<see cref="OriginalVanishAction"/>) and the playback of its frames (the state-5 slice of 9210).</summary>
    private bool VanishStep(SkirmishActor mage) => OriginalVanishAction.Step(mage.Record, mage.Parameters);

    /// <summary>The frame of the vanishing animation while it plays (state 5 of the motion byte), null otherwise.</summary>
    public int? VanishFrame(SkirmishActor mage) => IsMageUnit(mage) && (mage.Record[0x2d] & 15) == 5 ? BinaryPrimitives.ReadUInt16LittleEndian(mage.Record.AsSpan(1)) : null;
    /// <summary>A hidden mage (+32 ≠ 0, visibility mask +20 = 0) is seen and targeted by nobody.</summary>
    public static bool IsHidden(SkirmishActor actor) => IsHermit(actor) && actor.Record[0x20] == 0;
    /// <summary>A mage or a phantom in state 3 (the vanishing action): UpdateUnit leaves him alone.</summary>
    public bool IsVanishing(SkirmishActor actor) => IsMageUnit(actor) && ((actor.Record[0x14] & 7) == 3 || (actor.Record[0x2d] & 15) == 5 || (actor.Record[0x2d] >> 4) == 5);

    private void TickMages()
    {
        TickMageFlashes();
        if (!Rules.Fauna || _scenery.Count == 0) return;
        foreach (var mage in _scenery.Where(IsHermit).ToArray())
        {
            _rufinWorld ??= new SkirmishRufinWorld(this);
            _rufinWorld.Prepare(mage);
            if (mage.Alive)
            {
                // each hermit has his own script (ARMY__03.WRK); all eight hermits (13..21) run their own
                switch (mage.Source.TemplateIndex)
                {
                    case 14: OriginalOnis.Step(_rufinWorld, mage.Record); break;
                    case 15: OriginalIrvil.Step(_rufinWorld, mage.Record); break;
                    case 16: OriginalMiron.Step(_rufinWorld, mage.Record); break;
                    case 17: OriginalMagus.Step(_rufinWorld, mage.Record); break;
                    case 18: OriginalKevin.Step(_rufinWorld, mage.Record); break;
                    case 19: OriginalGarik.Step(_rufinWorld, mage.Record); break;
                    case 20: OriginalEfil.Step(_rufinWorld, mage.Record); break;
                    case 21: OriginalKochet.Step(_rufinWorld, mage.Record); break;
                    default: OriginalRufin.Step(_rufinWorld, mage.Record); break;
                }
            }
            if (!MageFights(mage)) { mage.Target = 0; continue; }   // at peace he stands
            if (!mage.Alive || (mage.Record[0x14] & 7) == 3 || mage.Record[0x32] != 0) { mage.Route.Clear(); mage.Goal = null; continue; }   // vanishing or hidden: he does nothing
            MageSeek(mage);
        }
        foreach (var phantom in _phantoms.ToArray())
        {
            if (!phantom.Alive) { _phantoms.Remove(phantom); continue; }
            MageStandardStep(phantom);
            if ((phantom.Record[0x14] & 7) == 3) { phantom.Route.Clear(); phantom.Goal = null; continue; }   // still appearing
            MageSeek(phantom);
        }
    }

    /// <summary>At war the nearest enemy in his sight (14 cells) becomes his target; UpdateUnit approaches to the reach of DAT[72] and strikes (8B5E, the swing), the impact is MageDischarge.</summary>
    private void MageSeek(SkirmishActor mage)
    {
        if ((_globalTick & 7) != 0 && Find(mage.Target) is { Alive: true }) return;
        SkirmishActor? best = null; double nearest = 14 * 32;
        foreach (var o in Actors)
        {
            if (!o.Alive || o.Structure || o.Scenery || IsAnimal(o) || IsPhantom(o) && o.Record[3] == mage.Record[3] || !Diplomacy.AtWar(mage.Record[3], o.Record[3])) continue;
            double d = mage.Position.DistanceTo(o.Position); if (d < nearest) { nearest = d; best = o; }
        }
        if (best != null) { if (mage.Target != best.Id) { mage.Target = best.Id; mage.Goal = null; mage.Route.Clear(); mage.ReplanTimer = 0; } } else mage.Target = 0;
    }

    /// <summary>The summoning of the discharge (1 in 8 of the strikes): a phantom stands up beside the mage (FS:16B then FS:45).</summary>
    public SkirmishActor? SummonPhantom(SkirmishActor mage)
    {
        _spellWorld ??= new SkirmishSpellWorld(this); _spellWorld.Attacker = mage;
        var spot = _spellWorld.FindSpot(3, mage.Record[0]);
        return spot.Found ? SpawnPhantom(mage.Record[3], 3, spot.Layer, spot.X, spot.Y) : null;
    }
    public IReadOnlyList<SkirmishActor> Phantoms => _phantoms;
    private readonly List<SkirmishActor> _phantoms = [];
    private sealed class PhantomWorld(Skirmish battle) : IPhantomWorld
    {
        public void Appearance(byte army) { }   // FS:4D (AE3A): the pictures of the object, not needed
        public void Redraw() { }
        public void Vitals() { }               // FS:DD is run by UpdateUnit
        public void UnitStep() { }             // FS:E1 is MageStandardStep
        public byte Random() => battle._terrainCatalog.NextRandomByte();
    }
    private PhantomWorld? _phantomWorld;

    /// <summary>FS:45 (A758) for the phantom: a new unit of the mage's settlement at the point, then AH = 0 of its script (<see cref="OriginalPhantom"/>).</summary>
    private SkirmishActor? SpawnPhantom(byte owner, byte template, byte layer, uint x, uint y)
    {
        if (x >= Map.Width * 32 || y >= Map.Height * 32) return null;
        var cell = new Vector2I((int)(x >> 5), (int)(y >> 5));
        var phantom = NewEconomyActor(template, cell, false, true, 4, "ARMY__03.DAT");
        var r = phantom.Record; r[3] = owner; r[0x0d] = layer;
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(4), x); BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(8), y);
        _phantomWorld ??= new PhantomWorld(this);
        OriginalPhantom.Initialize(_phantomWorld, r, owner);
        phantom.Route.Attach(_routes, r);
        Actors.Add(phantom); _phantoms.Add(phantom);
        Occupancy.WriteFootprint(phantom.Parameters[3], phantom.Parameters[4], r[0x0d], x, y, phantom.Id);
        return phantom;
    }
    /// <summary>The line of a message the player has to answer (or read): the text of the type with the player's name, the answers the type offers, the update at which it fades.</summary>
    public sealed record DialogLine(int Speaker, byte Type, string Text, IReadOnlyList<(byte Type, string Text)> Answers, uint Deadline);
    private readonly Dictionary<(int, byte), int> _dialogVariant = [];

    public DialogLine? CurrentDialog()
    {
        var data = _diplomacyData ??= GameData.Read<OriginalDiplomacyData>("res://data/original_diplomacy.json");
        int player = Rules.PlayerSlot; int bestSpeaker = -1, bestSlot = -1; uint bestTime = 0;
        for (int speaker = 0; speaker < Diplomacy.Count; speaker++)
        {
            if (speaker == player) continue;
            for (int s = 0; s < 8; s++)
            {
                var m = Diplomacy.Queue[speaker, s];
                if (m.Type == 0xff || m.Other != player || m.Type < 33 || m.Type >= data.Types.Count || m.Deadline <= Diplomacy.Time || data.Types[m.Type].Text.Length == 0) continue;
                if (bestSlot < 0 || m.Time >= bestTime) { bestSpeaker = speaker; bestSlot = s; bestTime = m.Time; }   // the latest message is the one on the screen
            }
        }
        if (bestSlot < 0) return null;
        var message = Diplomacy.Queue[bestSpeaker, bestSlot]; var type = data.Types[message.Type];
        var answers = new List<(byte, string)>();
        if (type.Parameters.Count >= 3 && type.Parameters[1] != 0 && type.Parameters[2] != 0)
            foreach (var a in new[] { type.Parameters[1], type.Parameters[2] }) if (a != 0 && a < data.Types.Count) answers.Add(((byte)a, Variant(data.Types[a].Text, 0)));
        return new DialogLine(bestSpeaker, message.Type, Variant(type.Text, (int)(message.Time & 0x7fffffff)).Replace("%n1", PlayerName), answers, message.Deadline);
    }
    private OriginalDiplomacyData? _diplomacyData;
    public string PlayerName { get; set; } = "Олег";

    /// <summary>The first group of the text of a type: [count] variant · variant … (variants end with A0h); the variant by the seed.</summary>
    private static string Variant(string text, int seed)
    {
        if (text.Length == 0) return "";
        int count = Math.Max(1, text[0] & 0x1f);
        var parts = text[1..].Split('\xa0');
        return parts[Math.Abs(seed) % Math.Min(count, parts.Length)].Replace("\r", "\n");
    }

    /// <summary>The player chooses an answer of the dialog: the answer message goes from the player to the speaker (its handler makes the trade).</summary>
    public void AnswerDialog(DialogLine line, byte answerType)
    {
        _diplomacyRules.Send(Diplomacy, answerType, (byte)line.Speaker, (byte)Rules.PlayerSlot, (byte)line.Speaker, 0);
        for (int s = 0; s < 8; s++) if (Diplomacy.Queue[line.Speaker, s].Type == line.Type && Diplomacy.Queue[line.Speaker, s].Other == Rules.PlayerSlot) Diplomacy.Queue[line.Speaker, s].Type = 0xff;
    }

    private sealed class SkirmishRufinWorld(Skirmish battle) : IMironWorld, IMagusWorld, IKochetWorld
    {
        public byte CountFreeRegions(byte army) => 0;   // ADAPTER: FS:18B counts regions of the campaign layer (OriginalCampaignRegions); a skirmish has none
        public byte[]? NearestUnit(byte[] hermit, uint mask, uint value)   // C439: the nearest living object of the participant in +3 whose template flags & mask = value
        {
            var self = battle.Actors.FirstOrDefault(a => ReferenceEquals(a.Record, hermit)); if (self == null) return null;
            return battle.Actors.Where(a => a.Alive && a.Record[3] == hermit[3] && !ReferenceEquals(a, self) && (BinaryPrimitives.ReadUInt32LittleEndian(a.Parameters.AsSpan(0x40)) & mask) == value && (sbyte)a.Record[0x19] > (sbyte)a.Parameters[0x46])
                .OrderBy(a => Math.Max(Math.Abs(a.Position.X - self.Position.X), Math.Abs(a.Position.Y - self.Position.Y))).FirstOrDefault()?.Record;
        }
        public void TakeOver(byte[] unit, byte newOwner) => battle.Log("Кочет переманивает бойца.");   // ADAPTER: the transfer of ownership D1BA is not ported
        public uint Time => (uint)(battle.Elapsed * 72.8165);
        public uint Dim4 => (uint)battle.Map.Width * 32; public uint Dim0 => (uint)battle.Map.Height * 32;   // ADAPTER: FS:4 and FS:0 are taken as the map size in pixels (x from FS:4, y from FS:0)
        public byte Random2() => battle._terrainCatalog.NextSecondaryByte();
        public (bool Found, uint X, uint Y) FreeSpot(byte radius, byte army, byte flag, uint x, uint y)
        {
            var template = Convert.FromHexString(battle._templates.Armies["ARMY__03.DAT"].Templates[3].RawRecordHex);
            var found = OriginalCreationPlacement.Search(battle._terrainCatalog.NextRandomByte, battle._terrainCatalog.NextSecondaryByte, (uint)battle.Map.Width * 32, (uint)battle.Map.Height * 32, template, x, y, (px, py) =>
            {   // ADAPTER: AC63 is a free cell of the layer in the occupancy
                int cx = (int)(px >> 5), cy = (int)(py >> 5);
                return cx >= 0 && cy >= 0 && cx < battle.Map.Width && cy < battle.Map.Height && battle.Occupancy.Cells[(flag & 3) * battle.Occupancy.Width * battle.Occupancy.Height + cy * battle.Occupancy.Width + cx] == 0 ? BuilderProbe.Clear : BuilderProbe.Blocked;
            });
            return found;
        }
        public void Occupy(byte[] record) { }   // 43E5: SpawnPhantom registers the footprint itself
        private SkirmishActor _mage = null!;
        private Dictionary<byte[], SkirmishActor> _byRecord = new(ReferenceEqualityComparer.Instance);
        public void Prepare(SkirmishActor mage) { _mage = mage; }
        public DiplomacyState Diplomacy => battle.Diplomacy;
        public int ParticipantCount => 8; public byte Tick => (byte)battle._globalTick;
        public bool HasItem(int participant, byte item) => Array.IndexOf(battle.ItemWorld.Inventory(participant), item) >= 0;
        public byte[]? FindObject(int owner, byte army, byte template, int skip)
        {
            foreach (var a in battle.Actors) if (a.Record[0x0e] == template && a.Record[0] == army && a.Source.TemplateFile == _mage.Source.TemplateFile && skip-- == 0) return a.Record;
            return null;
        }
        public void Say(byte message, byte participant, byte speakerOwner) { battle.Diplomacy.Time = (uint)(battle.Elapsed * 72.8165); battle._diplomacyRules.Send(battle.Diplomacy, message, participant, speakerOwner, participant, 0); }
        public void Reveal(uint x, uint y, uint size)   // 11C3D: the player sees the area (10×10 cells around the point)
        {
            int cx = (int)(x >> 5), cy = (int)(y >> 5), w = battle.Map.Width, h = battle.Map.Height; byte mask = (byte)~(1 << (battle.Rules.PlayerSlot & 7));
            for (int dy = -5; dy < 5; dy++) for (int dx = -5; dx < 5; dx++) { int px = cx + dx, py = cy + dy; if (px >= 0 && py >= 0 && px < w && py < h) battle.FogMap[py * w + px] &= mask; }
        }
        public void Sweep(byte participant) => battle.Log("Руфин выгоняет чужие постройки со своей земли.");   // ADAPTER: the sweep 11C73 destroys the buildings of the participant in the territory; not ported
        public (bool Found, byte[]? Object) ScanAttacker(byte mode)
        {
            var found = OriginalTargetRules.FindEnemy(_mage.Id, _mage.Record, _mage.Parameters, true, new SearchWorld(battle, _mage.Record[3]));
            var target = battle.Find(found.Target); return (target != null, target?.Record);
        }
        public (bool Created, byte[]? Object) Create(byte army, byte owner, byte template, byte layer, uint x, uint y)
        {
            if (army == 3 && template == 3 && layer == 1) { var phantom = battle.SpawnPhantom(owner, template, layer, x, y); return (phantom != null, phantom?.Record); }   // Miron's phantom
            return (false, null);   // ADAPTER: the switch object of ARMY__00
        }
        public void Initialize(byte[] record, uint eax, uint ecx) { }
        public void Remove(byte[] record) { }
        public void Redraw() { }
        public IReadOnlyList<byte[]> Territory()
        {
            _byRecord.Clear();
            foreach (var a in battle.Actors.Where(a => a.Alive && a.Structure && a.Position.DistanceTo(_mage.Position) < 20 * 32)) _byRecord[a.Record] = a;
            return _byRecord.Keys.ToList();
        }
        public uint ObjectFlags(byte[] record, out byte threshold)
        {
            threshold = 0; if (!_byRecord.TryGetValue(record, out var a)) return 0;
            threshold = a.Parameters[0x46]; return BinaryPrimitives.ReadUInt32LittleEndian(a.Parameters.AsSpan(0x40));
        }
        public bool IsBuilding(byte[] record, out byte threshold)
        {
            threshold = 0; if (!_byRecord.TryGetValue(record, out var a)) return false;
            threshold = a.Parameters[0x46]; uint flags = BinaryPrimitives.ReadUInt32LittleEndian(a.Parameters.AsSpan(0x40)); return (flags & 8) != 0 && (flags & 0x20) == 0;
        }
        public void StandardVitals() { if (!battle.MageFights(_mage)) battle.MageVitals(_mage); }   // FS:DD (UpdateUnit runs it for a fighting mage)
        public void StandardActions() => battle.MageStandardStep(_mage);                              // FS:E1 and the continuation after its table
        public void Voice(ushort id) { }
        public void Appearance(byte army) { }
        public byte Random() => battle._terrainCatalog.NextRandomByte();
    }
}
