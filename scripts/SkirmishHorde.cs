using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>The horde of Teymur (docs/horde.md): the camps of slot <see cref="SkirmishRules.HordeSlot"/> run the ported LE:1 11943
/// (<see cref="OriginalCamp"/>); a camp founds a «Шатер» of ARMY__01 and wakes the settlement's AI with the character 4. Off by default
/// (HordeSlot = -1): the camp initialisation draws from the random generator and would shift the other scenarios.</summary>
public sealed partial class Skirmish
{
    public const string HordeFile = "ARMY__01.DAT";
    private readonly List<byte[]> _camps = [];
    private SkirmishCampWorld? _campWorld;
    private bool Horde => Rules.HordeSlot >= 0;
    /// <summary>Army file of an AI settlement: ARMY__01 for the horde, ARMY__02 for the others.</summary>
    private string FileOf(int slot) => Horde && slot == Rules.HordeSlot ? HordeFile : "ARMY__02.DAT";
    public IReadOnlyList<byte[]> Camps => _camps;

    /// <summary>Unit rules of a template of another army. ARMY__02 names are in the rule table; for ARMY__01 the DAT decides:
    /// flag 8 of +40 is a building (no damage), 300h a worker, a ranged soldier has the range of the archer (adapter value).</summary>
    private SkirmishUnitRules SpecOf(string file, TemplateEntry template, byte[] parameters)
    {
        if (file == "ARMY__02.DAT") return Rules.Units[template.Name];
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(parameters.AsSpan(0x40));
        bool building = (flags & 8) != 0, worker = (flags & 0x300) == 0x300;
        return new SkirmishUnitRules { Health = template.InitialCondition, Damage = building || worker ? 0 : 1, Worker = worker, Range = (flags & 0xf00) == 0x900 ? 176 : 48 };
    }

    private void InitializeHorde(ScenarioData scenario)
    {
        if (!Horde) return;
        foreach (var obj in scenario.Slots.Single(s => s.SlotIndex == Rules.HordeSlot).Objects.Where(o => o.TemplateFile == "ARMY__00.DAT" && o.TemplateName == "Лагерь"))
        {
            var record = Convert.FromHexString(obj.RawRecordHex);
            // WRK AH=0: the direction away from the map centre and the first timer 2710h + 9E41.
            double angle = Math.Atan2((int)(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8)) >> 5) - Map.Height / 2, (int)(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4)) >> 5) - Map.Width / 2);
            record[0x22] = (byte)(((int)Math.Round(angle / (Math.PI / 4)) + 2 + 8) & 7);   // octant, 0 = north (adapter: the original reads an arctangent table)
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x23), 0x2710u + _terrainCatalog.NextRandomByte());
            record[0x15] = 0;
            _camps.Add(record);
        }
        _campWorld = new SkirmishCampWorld(this);
    }

    private void TickCamps()
    {
        if (_campWorld == null) return;
        _aiSlot = Rules.HordeSlot;
        foreach (var camp in _camps) OriginalCamp.Step(_campWorld, camp);
        _aiSlot = Rules.EnemySlot;
    }

    private sealed class SkirmishCampWorld(Skirmish battle) : ICampWorld
    {
        public Span<byte> Block(byte p) => battle._aiBlocks[p];
        public Span<byte> Participant(byte p) => battle._participants[p];
        public byte Random() => battle._terrainCatalog.NextRandomByte();
        public (byte Template, byte Army)? Pick(byte participant, uint mask, uint value)
        {
            var found = OriginalTechRules.Buildable(battle.Ctx(participant).Templates, battle.TreeOf(participant), true, mask, value);
            return found.Count == 0 ? null : (found[0], battle.Ctx(participant).Army);
        }
        public ReadOnlySpan<byte> Template(byte army, byte template) => battle.Ctx(battle.Rules.HordeSlot).Templates[template];
        public (uint X, uint Y)? Spot(byte template, byte army, byte variant, uint x, uint y)
        {
            battle._aiSlot = battle.Rules.HordeSlot;
            return battle._aiSquadWorld!.FreeSpot(template, variant, x, y);
        }
        public byte[]? Create(byte army, byte owner, byte template, byte variant, uint x, uint y)
        {
            if (army == 0) return new byte[0x28];   // beacons: scratch records (103EC/98DB are host adapters, nothing is left on the map)
            return battle.EnemyConstruct(template, new Vector2I((int)(x >> 5), (int)(y >> 5)), owner).Record;
        }
        public void Register(byte[] record) { }
        public void SetCharacter(byte participant, byte character) =>
            OriginalAiRules.SetCharacter(battle._aiBlocks[participant], battle._participants[participant], character, battle._aiCharacters);
        public void Beacon(byte[] record, byte cellX, byte cellY) { }
        public (byte X, byte Y) Cursor => (0, 0);
        public IEnumerable<byte[]> Objects(byte owner) => battle._camps.Where(c => c[3] == owner);
        public void Retire(byte[] record) { }
    }
}
