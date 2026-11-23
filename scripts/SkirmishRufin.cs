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

    private void TickMages()
    {
        if (!Rules.Fauna || _scenery.Count == 0) return;
        foreach (var mage in _scenery.Where(IsHermit).ToArray())
        {
            _rufinWorld ??= new SkirmishRufinWorld(this);
            _rufinWorld.Prepare(mage);
            if (mage.Alive) OriginalRufin.Step(_rufinWorld, mage.Record);
            if (!MageFights(mage)) { mage.Target = 0; continue; }   // at peace he stands
            // At war: the nearest enemy unit in his sight (DAT[47] cells) becomes his target, as FS:17B (D895) finds it for the script.
            if (mage.Alive && (mage.Target == 0 || Find(mage.Target) is not { Alive: true }) && (_globalTick & 7) == 0)
            {
                var found = OriginalTargetRules.FindEnemy(mage.Id, mage.Record, mage.Parameters, true, new SearchWorld(this, mage.Record[3]));
                if (Find(found.Target) is { Alive: true, Scenery: false } enemy && Diplomacy.AtWar(mage.Record[3], enemy.Record[3])) { mage.Target = enemy.Id; mage.Goal = null; mage.Route.Clear(); mage.ReplanTimer = 0; }
            }
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
        public void StandardVitals() { }
        public void StandardActions() { }
        public void Voice(ushort id) { }
        public void Appearance(byte army) { }
        public byte Random() => battle._terrainCatalog.NextRandomByte();
    }
}
