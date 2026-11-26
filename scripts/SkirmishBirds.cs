using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>The birds of the first map (ARMY__05 0) and their generators (ARMY__05 1, six on the map; docs/world.md). WRK 70 (the generator): every 256th update
/// the settlement of the generator counts its birds and generators and, while there are fewer than four birds per generator, a new bird is made. The birds
/// fly (plane 2) and wander; they belong to a peaceful slot so that nobody attacks them and they attack nobody. Adapter: the WRK services of the bird's
/// actions (FS:15F, 173, 177) are replaced by a random wander; the generator creates the bird beside itself (FS:16B finds the spot in the original).</summary>
public sealed partial class Skirmish
{
    private readonly List<byte[]> _generators = [];
    private readonly List<SkirmishActor> _birds = [];
    public IReadOnlyList<SkirmishActor> Birds => _birds;
    public IReadOnlyList<byte[]> Generators => _generators;
    private const byte BirdOwner = 5;   // an empty, peaceful slot of the first map
    public static bool IsBird(SkirmishActor actor) => actor.Source.TemplateFile == FaunaFile && actor.Source.TemplateIndex == 0;
    /// <summary>Animals that are never drawn (wolves and bears live in the forest); birds are drawn.</summary>
    public static bool IsHiddenAnimal(SkirmishActor actor) => IsAnimal(actor) && !IsBird(actor);

    private void InitializeBirds(ScenarioData scenario, TemplatesData templates)
    {
        if (!Rules.Fauna) return;
        // The peaceful slot: at war with nobody (the scenario's relations of the empty slot are not trusted).
        Diplomacy.Hostile[BirdOwner] = 0; for (int p = 0; p < Diplomacy.Count; p++) Diplomacy.Hostile[p] &= ~(1u << BirdOwner);
        foreach (var obj in scenario.Slots.Where(s => IsFaunaSlot(s.SlotIndex)).SelectMany(s => s.Objects).Where(o => o.TemplateFile == FaunaFile))
        {
            if (obj.TemplateIndex == 1) _generators.Add(Convert.FromHexString(obj.RawRecordHex));
            else if (obj.TemplateIndex == 0) AddBird(obj, Convert.FromHexString(obj.RawRecordHex));
        }
    }

    private SkirmishActor AddBird(ObjectRecord source, byte[] record)
    {
        var template = _templates.Armies[FaunaFile].Templates[0];
        var parameters = Convert.FromHexString(template.RawRecordHex);
        record[3] = BirdOwner;
        var bird = new SkirmishActor { Source = source, Template = template, Parameters = parameters, Record = record, Rules = SpecOf(FaunaFile, template, parameters), Team = 4 };
        bird.Route.Attach(_routes, bird.Record);
        Actors.Add(bird); _birds.Add(bird);
        Occupancy.WriteFootprint(parameters[3], parameters[4], record[0x0d], (uint)BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8)), bird.Id);
        return bird;
    }

    /// <summary>WRK 70, AH=1: with the low byte of the update counter zero, a generator makes a bird while the settlement has fewer than four birds per generator.</summary>
    private void TickBirds()
    {
        if (_generators.Count == 0 && _birds.Count == 0) return;
        if (_globalTick == 0 && _generators.Count > 0)
        {
            int alive = _birds.Count(b => b.Alive);
            if (alive < _generators.Count * 4)
            {
                var generator = _generators[_terrainCatalog.NextRandomByte() % _generators.Count];
                var at = new Vector2I((int)(BinaryPrimitives.ReadUInt32LittleEndian(generator.AsSpan(4)) >> 5), (int)(BinaryPrimitives.ReadUInt32LittleEndian(generator.AsSpan(8)) >> 5))
                    + new Vector2I(_terrainCatalog.NextRandomByte() % 5 - 2, _terrainCatalog.NextRandomByte() % 5 - 2);
                if (Inside(at)) SpawnBird(at);
            }
        }
        foreach (var bird in _birds)
        {
            if (!bird.Alive || bird.Stepping || bird.Goal != null || bird.Route.Count > 0) continue;
            if ((bird.Record[0x2e] & 63) != 0) continue;
            var next = bird.Cell + new Vector2I(_terrainCatalog.NextRandomByte() % 21 - 10, _terrainCatalog.NextRandomByte() % 21 - 10);
            if (Inside(next)) OrderMove(bird, next, 1);
        }
    }

    private void SpawnBird(Vector2I cell)
    {
        var template = _templates.Armies[FaunaFile].Templates[0];
        var raw = new byte[template.RuntimeRecordBytes];
        raw[0] = (byte)Math.Max(0, _armyFiles.IndexOf(FaunaFile)); raw[3] = BirdOwner; raw[0x0d] = (byte)(Convert.FromHexString(template.RawRecordHex)[0x77] & 3); raw[0x0e] = 0;
        raw[0x14] = 0x80; raw[0x19] = raw[0x1a] = template.InitialCondition; raw[0x20] = 0xff;
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(1), template.InitialFrame);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), (uint)(cell.X * 32 + 16)); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), (uint)(cell.Y * 32 + 16));
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x10), (uint)raw.Length);
        var source = new ObjectRecord { RuntimeReference = _nextEconomyId++, TemplateName = template.Name, TemplateFile = FaunaFile, TemplateIndex = 0, RecordBytes = raw.Length,
            RawX = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4)), RawY = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(8)), RawLayer = raw[0x0d] };
        AddBird(source, raw);
    }
}
