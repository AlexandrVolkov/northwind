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
    public static bool IsHermit(SkirmishActor actor) => actor.Scenery && actor.Source.TemplateFile == "ARMY__03.DAT" && actor.Source.TemplateIndex is >= 13 and <= 21;

    /// <summary>A mage at war with the player (the player attacked him or his friends): he fights back like any unit (DAT of ARMY__03 13: reach 10, strike 7).</summary>
    public bool MageFights(SkirmishActor mage) => IsHermit(mage) && Diplomacy.AtWar(mage.Record[3], Rules.PlayerSlot);

    /// <summary>The discharge of a fighting mage: a red lightning (ARMY__00 effect type 1Eh, 72×72, frames 1C6h + 4·variant, four frames of two updates, WRK 13A3, sound 14h)
    /// at the target; everything hostile within <see cref="BoltRadius"/> is struck. ADAPTER: the strength and the area are described by the player of the original (the unit is
    /// killed by one hit, several neighbours fall together); the WRK of the mage has no attack of its own — DAT[70] = 0 — so the values are the port's.</summary>
    public sealed class MageBolt { public Vector2 Point; public int Variant, Left = 8; }
    public List<MageBolt> Bolts { get; } = [];
    public const int BoltRadius = 56, BoltDamage = 100, MageReachCells = 9, MageReloadTicks = 50;
    private readonly Dictionary<uint, uint> _mageTarget = [];
    public uint MageTargetOf(SkirmishActor mage) => _mageTarget.TryGetValue(mage.Id, out var t) ? t : 0;

    private void MageStrike(SkirmishActor mage, SkirmishActor target)
    {
        var point = target.Position; Bolts.Add(new MageBolt { Point = point, Variant = _terrainCatalog.NextRandomByte() & 7 });
        if (Sounds.Count < 64) Sounds.Add(new("WORK.FX", 0x14, SoundRandom(), point));
        foreach (var victim in Actors.ToArray())
        {
            if (!victim.Alive || victim.Structure || victim.Scenery || IsAnimal(victim) || victim == mage || !Diplomacy.AtWar(mage.Record[3], victim.Record[3])) continue;
            if (victim.Position.DistanceTo(point) > BoltRadius) continue;
            victim.Health -= victim.Hero ? Math.Max(1, victim.MaxHealth * 2 / 5) : BoltDamage;   // the leader withstands two discharges
            if (!victim.Alive) Kill(victim);
        }
        Log("Руфин поражает врагов молнией!");
    }

    // ---- the vanishing of the hurt mage (docs/rufin.md): the continuation of the unit step and the action FS:105
    public const int VanishTicks = 24;   // ADAPTER: 8D0B plays DAT[78] = 12 frames; the pace of two updates per frame is assumed
    private readonly Dictionary<uint, int> _vanish = [];
    private sealed class MageDrop(Skirmish battle) : IMageWorld
    { public void DropItems(byte cellX, byte cellY, byte owner) => battle.ItemRules.DropAll(battle.ItemWorld, owner, cellX, cellY); }
    private MageDrop? _mageDrop;

    public void MageVitals(SkirmishActor mage) { (_vitalsWorld ??= new(this)).Current = mage; OriginalCombatRules.Vitals(mage.Record, mage.Parameters, (byte)_globalTick, (byte)Map.Width, (byte)Map.Height, Map.Cells, _properties, _terrainCatalog, _vitalsWorld); }

    /// <summary>FS:E1 (8227): the counter +2E, the state machine of the unit (state 3 is the vanishing, FS:105; a pending request — bit 40h of +14 — ends any other state at once),
    /// then the continuation of the script (<see cref="OriginalMageCloak"/>). The other actions (ED, E9, F1: stand, walk, strike) are done by UpdateUnit.</summary>
    public void MageStandardStep(SkirmishActor mage)
    {
        var r = mage.Record; if (!MageFights(mage)) r[0x2e]++;   // UpdateUnit counts the updates of a fighting mage (8227)
        if ((sbyte)r[0x19] > 0 && (r[0x17] & 0x80) == 0)
        {
            bool done = false;
            if ((r[0x14] & 7) == 3) done = VanishStep(mage);
            else if ((r[0x14] & 0x40) != 0) done = true;
            if (done) { r[0x14] = (byte)((r[0x14] >> 3) & 7); r[0x30] = 0; r[0x15] = 0; _vanish.Remove(mage.Id); }
        }
        _mageDrop ??= new MageDrop(this);
        bool rufin = mage.Source.TemplateIndex == 13;
        OriginalMageCloak.Step(_mageDrop, r, rufin ? OriginalMageCloak.RufinLow : OriginalMageCloak.FirstMageLow, rufin ? OriginalMageCloak.RufinHigh : OriginalMageCloak.FirstMageHigh, rufin);
    }

    /// <summary>FS:105 (878D): at the start +15 = 1 (vanish: the frames run forwards) or 2 (return: backwards); at the end of the frames a vanishing mage gets +32 = FF (hidden), a returning one +32 = 0 and +20 = FF.</summary>
    private bool VanishStep(SkirmishActor mage)
    {
        var r = mage.Record;
        if (!_vanish.TryGetValue(mage.Id, out int progress)) { r[0x15] = (byte)(r[0x32] == 0 ? 1 : 2); r[0x25] = 0x24; progress = 0; }
        _vanish[mage.Id] = ++progress;
        if (progress < VanishTicks) return false;
        if (r[0x15] == 1) { r[0x32] = 0xff; r[0x18] |= 0x02; }   // +17 |= 200h (the word's high byte is +18)
        else { r[0x32] = 0; r[0x20] = 0xff; }
        return true;
    }

    /// <summary>The frame of the vanishing animation to draw for a mage in state 3 (null otherwise).</summary>
    public int? VanishFrame(SkirmishActor mage)
    {
        if (!_vanish.TryGetValue(mage.Id, out int progress) || (mage.Record[0x14] & 7) != 3) return null;
        var p = mage.Parameters; int frames = p[0x78], start = BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(0x48)) + 8 * (p[0x4a] + p[0x4b]) + 4 * (p[0x4f] + p[0x50] + p[0x51] + p[0x52]);
        int k = Math.Clamp(progress * frames / VanishTicks, 0, frames - 1);
        return start + (mage.Record[0x15] == 2 ? frames - 1 - k : k);
    }
    /// <summary>A hidden mage (+32 ≠ 0, visibility mask +20 = 0) is seen and targeted by nobody.</summary>
    public static bool IsHidden(SkirmishActor actor) => IsHermit(actor) && actor.Record[0x20] == 0;

    private void TickMages()
    {
        foreach (var bolt in Bolts) bolt.Left--;
        Bolts.RemoveAll(b => b.Left <= 0);
        if (!Rules.Fauna || _scenery.Count == 0) return;
        foreach (var mage in _scenery.Where(IsHermit).ToArray())
        {
            _rufinWorld ??= new SkirmishRufinWorld(this);
            _rufinWorld.Prepare(mage);
            if (mage.Alive) OriginalRufin.Step(_rufinWorld, mage.Record);
            if (!MageFights(mage)) { mage.Target = 0; _mageTarget.Remove(mage.Id); continue; }   // at peace he stands
            mage.Target = 0;   // the discharge replaces the melee strike: the unit logic only walks him
            if (!mage.Alive || (mage.Record[0x14] & 7) == 3 || mage.Record[0x32] != 0) { mage.Route.Clear(); mage.Goal = null; continue; }   // vanishing or hidden: he does nothing
            SkirmishActor? target = null;
            if ((_globalTick & 7) == 0 || !_mageTarget.TryGetValue(mage.Id, out uint targetId) || Find(targetId) is not { Alive: true } current || !Diplomacy.AtWar(mage.Record[3], current.Record[3]))
            {
                // the nearest enemy in his sight (14 cells)
                double best = 14 * 32;
                foreach (var o in Actors)
                {
                    if (!o.Alive || o.Structure || o.Scenery || IsAnimal(o) || !Diplomacy.AtWar(mage.Record[3], o.Record[3])) continue;
                    double d = mage.Position.DistanceTo(o.Position); if (d < best) { best = d; target = o; }
                }
                if (target != null) _mageTarget[mage.Id] = target.Id; else _mageTarget.Remove(mage.Id);
            }
            else target = Find(_mageTarget[mage.Id]);
            if (target == null) continue;
            double distance = mage.Position.DistanceTo(target.Position);
            if (distance > MageReachCells * 32) { if ((_globalTick & 7) == 0) OrderMove(mage, target.Cell, 2); continue; }
            mage.Route.Clear(); mage.Goal = null;
            if (mage.AttackTimer <= 0) { MageStrike(mage, target); mage.AttackTimer = MageReloadTicks / 18.2; }
        }
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

    private sealed class SkirmishRufinWorld(Skirmish battle) : IRufinWorld
    {
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
        public (bool Created, byte[]? Object) Create(byte army, byte owner, byte template, byte layer, uint x, uint y) => (false, null);   // ADAPTER: the switch object of ARMY__00
        public void Initialize(byte[] record, uint eax, uint ecx) { }
        public void Remove(byte[] record) { }
        public void Redraw() { }
        public IReadOnlyList<byte[]> Territory()
        {
            _byRecord.Clear();
            foreach (var a in battle.Actors.Where(a => a.Alive && a.Structure && a.Position.DistanceTo(_mage.Position) < 20 * 32)) _byRecord[a.Record] = a;
            return _byRecord.Keys.ToList();
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
