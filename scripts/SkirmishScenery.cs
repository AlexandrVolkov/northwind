using Godot;
namespace Northwind;

/// <summary>The decorations of the first map that are not animals, birds or traps (slots 6 and 7, docs/world.md): the jugs (ARMY__03 1, 30, 33; ARMY__00 29), the chests
/// (9, 10, 11, 29), the manuscripts (24..26) and the mage Rufin (13). They stand on the frame the scenario gives them and belong to a peaceful slot, so nobody attacks
/// them. Their WRK scripts (breaking, opening, giving items, phantoms) are not ported yet: only the picture.</summary>
public sealed partial class Skirmish
{
    private static readonly HashSet<(string, int)> SceneryTemplates =
        [("ARMY__03.DAT", 1), ("ARMY__03.DAT", 9), ("ARMY__03.DAT", 10), ("ARMY__03.DAT", 11), ("ARMY__03.DAT", 13), ("ARMY__03.DAT", 24), ("ARMY__03.DAT", 25),
         ("ARMY__03.DAT", 26), ("ARMY__03.DAT", 29), ("ARMY__03.DAT", 30), ("ARMY__03.DAT", 33), ("ARMY__00.DAT", 29)];
    private readonly List<SkirmishActor> _scenery = [];
    public IReadOnlyList<SkirmishActor> SceneryObjects => _scenery;
    /// <summary>Rufin, the mage of the forest (ARMY__03 13): the only decoration that may be attacked.</summary>
    public static bool IsMage(SkirmishActor actor) => actor.Source.TemplateFile == "ARMY__03.DAT" && actor.Source.TemplateIndex == 13;

    private void InitializeScenery(ScenarioData scenario, TemplatesData templates)
    {
        if (!Rules.Fauna) return;
        foreach (var obj in scenario.Slots.Where(s => IsFaunaSlot(s.SlotIndex)).SelectMany(s => s.Objects).Where(o => SceneryTemplates.Contains((o.TemplateFile, o.TemplateIndex))))
        {
            var template = templates.Armies[obj.TemplateFile].Templates[obj.TemplateIndex];
            var parameters = Convert.FromHexString(template.RawRecordHex);
            var record = Convert.FromHexString(obj.RawRecordHex); if (obj.TemplateIndex != 13 || obj.TemplateFile != "ARMY__03.DAT") record[3] = BirdOwner;   // the mage stays slot 6: the war on him is the war with the forest
            if ((sbyte)record[0x19] <= 0) record[0x19] = record[0x1a] = (byte)Math.Min(127, (int)template.InitialCondition);   // the mage's record holds a negative condition: he is a living figure
            var actor = new SkirmishActor { Source = obj, Template = template, Parameters = parameters, Record = record, Rules = SpecOf(obj.TemplateFile, template, parameters), Team = 4, Scenery = true };
            if (!actor.Structure) actor.Route.Attach(_routes, actor.Record);
            Actors.Add(actor); _scenery.Add(actor);
        }
    }
}
