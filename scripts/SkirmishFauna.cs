using Godot;
namespace Northwind;

/// <summary>The animals of the first map (docs/fauna.md): the wolves and bears of the neutral slots 6 and 7 (ARMY__05) are units of team 4 at war with
/// every settlement. They have no walking frames (the original does not show them: they live in the forest, see <see cref="InForest"/>), so they are not drawn.
/// Off by default (<see cref="SkirmishRules.Fauna"/>): the animals roam with random numbers and would shift other scenarios.</summary>
public sealed partial class Skirmish
{
    public const string FaunaFile = "ARMY__05.DAT";
    private readonly List<SkirmishActor> _fauna = [];
    public IReadOnlyList<SkirmishActor> Animals => _fauna;
    public static bool IsAnimal(SkirmishActor actor) => actor.Source.TemplateFile == FaunaFile;
    private static bool IsFaunaSlot(int slot) => slot is 6 or 7;

    private void InitializeFauna(ScenarioData scenario, TemplatesData templates)
    {
        if (!Rules.Fauna) return;
        foreach (var slot in scenario.Slots.Where(s => IsFaunaSlot(s.SlotIndex)))
            foreach (var obj in slot.Objects.Where(o => o.TemplateFile == FaunaFile && o.TemplateIndex is 2 or 3 or 4))
            {
                var template = templates.Armies[FaunaFile].Templates[obj.TemplateIndex];
                var parameters = Convert.FromHexString(template.RawRecordHex);
                var animal = new SkirmishActor { Source = obj, Template = template, Parameters = parameters, Record = Convert.FromHexString(obj.RawRecordHex),
                    Rules = SpecOf(FaunaFile, template, parameters), Team = 4 };
                animal.Route.Attach(_routes, animal.Record);
                Actors.Add(animal); _fauna.Add(animal);
            }
        // The animals attack everyone (message 5 sets war both ways with each settlement).
        foreach (int animalSlot in new[] { 6, 7 })
            foreach (int other in new[] { Rules.PlayerSlot, Rules.EnemySlot, Rules.RivalSlot, Rules.HordeSlot }.Where(o => o >= 0))
                _diplomacyRules.Send(Diplomacy, 5, (byte)other, (byte)animalSlot, (byte)other, 0);
        _diplomacyRules.Send(Diplomacy, 5, 6, 7, 6, 0);
    }

    /// <summary>The development tree (TRE copy) of a settlement's participant.</summary>
    public byte[] DevelopmentTree(int slot) => TreeOf(slot);
    private double _faunaVoiceAt;

    /// <summary>Idle animals roam among the trees; now and then one howls or growls (FS:129 = A444 plays the object's own sound 2, adapter timing).</summary>
    private void TickFauna()
    {
        if (_fauna.Count == 0) return;
        foreach (var animal in _fauna)
        {
            if (!animal.Alive || animal.Stepping || animal.Target != 0 || animal.Goal != null || animal.Route.Count > 0) continue;
            if ((animal.Record[0x2e] & 63) != 0) continue;
            // In the forest an animal sees every unit within 12 cells (the forest visibility rule) and goes for the nearest one on its own.
            if (InForest(animal))
            {
                SkirmishActor? prey = null; int best = 13;
                foreach (var o in Actors)
                {
                    if (!o.Alive || o.Structure || o.Scenery || IsAnimal(o)) continue;
                    int d = Math.Max(Math.Abs(o.Cell.X - animal.Cell.X), Math.Abs(o.Cell.Y - animal.Cell.Y));
                    if (d < best) { best = d; prey = o; }
                }
                if (prey != null) { animal.Target = prey.Id; animal.Goal = null; animal.Route.Clear(); animal.ReplanTimer = 0; continue; }
            }
            if (_terrainCatalog.NextRandomByte() > 40) continue;
            // Adapter (speed): an animal far from every settlement's units stays put; route searches of 46 animals tripled the step time.
            if (!Actors.Any(o => o.Alive && !o.Structure && !IsAnimal(o) && Math.Abs(o.Cell.X - animal.Cell.X) < 30 && Math.Abs(o.Cell.Y - animal.Cell.Y) < 30)) continue;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var cell = animal.Cell + new Vector2I(_terrainCatalog.NextRandomByte() % 13 - 6, _terrainCatalog.NextRandomByte() % 13 - 6);
                if (!Inside(cell) || !InForestCell(cell) || !CanStand(animal, cell)) continue;
                if (OrderMove(animal, cell, 1)) break;
            }
        }
        if (Elapsed >= _faunaVoiceAt)
        {
            _faunaVoiceAt = Elapsed + 6 + _terrainCatalog.NextRandomByte() / 32.0;
            var alive = _fauna.Where(a => a.Alive).ToList();
            if (alive.Count > 0 && alive[_terrainCatalog.NextRandomByte() % alive.Count] is var voice) ObjectSound(voice, 2);
        }
    }

    /// <summary>The death of a bear opens «Находка медвежих жил» (event 200, kind 3, A6AE) in the development tree of the settlement that stood nearest.</summary>
    private void AnimalDied(SkirmishActor animal)
    {
        if (animal.Source.TemplateIndex != 3) return;
        var owner = Actors.Where(a => a.Alive && !a.Structure && !IsAnimal(a) && Distance(animal.Position, a) < 6 * 32).OrderBy(a => Distance(animal.Position, a)).FirstOrDefault();
        if (owner == null) return;
        var tree = TreeOf(owner.Record[3]);
        if (tree.Length > 0) OriginalTechRules.Complete(tree, 3, 200);
    }
}
