using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>Observation adapter (not a scenario of the original): a second AI settlement in slot <see cref="SkirmishRules.RivalSlot"/>,
/// a copy of the enemy settlement's starting objects shifted right by <see cref="SkirmishRules.RivalShiftX"/> cells, at war
/// with the enemy from the start. For an even match it starts with the enemy's resources and character.</summary>
public sealed partial class Skirmish
{
    private void CreateRival(ScenarioData scenario, TemplatesData templates)
    {
        var source = scenario.Slots.Single(s => s.SlotIndex == Rules.EnemySlot);
        int team = TeamOfSlot(Rules.RivalSlot);
        // Buildings first, so that units find their places around them.
        var objects = source.Objects.Where(o => o.TemplateFile == "ARMY__02.DAT" && Rules.Units.ContainsKey(o.TemplateName))
            .OrderBy(o => Rules.Units[o.TemplateName].Damage != 0 || Rules.Units[o.TemplateName].Worker);
        foreach (var obj in objects)
        {
            var spec = Rules.Units[obj.TemplateName];
            var cell = new Vector2I((int)(obj.RawX >> 5) + Rules.RivalShiftX, (int)(obj.RawY >> 5));
            bool worker = spec.Worker, building = spec.Damage == 0 && !worker;
            if (building)
            {
                if (RivalSpot(cell, c => EnemyPlacement((byte)obj.TemplateIndex, c) == ConstructionPlacementStatus.Clear) is not { } site) continue;
                var actor = NewEconomyActor(obj.TemplateIndex, site, false, false, team);
                if (actor.Record.Length >= 0x28) { actor.Record.AsSpan(0x22, 5).Fill(0xff); actor.Record[0x27] = 0xff; }   // producers' queue
                actor.Record[0x14] = 0;
                // A finished building shows its last construction frame (as TickBuildings writes on completion).
                BinaryPrimitives.WriteUInt16LittleEndian(actor.Record.AsSpan(1), (ushort)(actor.Template.InitialFrame + actor.Parameters[0x4a]));
                Actors.Add(actor);
                Occupancy.WriteFootprint(actor.Parameters[3], actor.Parameters[4], actor.Record[0x0d], (uint)actor.Position.X, (uint)actor.Position.Y, actor.Id);
            }
            else
            {
                var unit = NewEconomyActor(obj.TemplateIndex, cell, worker, !worker, team);
                if (RivalSpot(cell, c => CanStand(unit, c)) is not { } place) continue;
                BinaryPrimitives.WriteUInt32LittleEndian(unit.Record.AsSpan(4), (uint)(place.X * 32 + 16));
                BinaryPrimitives.WriteUInt32LittleEndian(unit.Record.AsSpan(8), (uint)(place.Y * 32 + 16));
                unit.Source.RawX = (uint)(place.X * 32 + 16); unit.Source.RawY = (uint)(place.Y * 32 + 16);
                if (worker) OriginalWorkerDelivery.InitializeEmpty(unit.Record, unit.Parameters, _terrainCatalog.NextRandomByte());
                unit.Record[0x14] = 0;
                unit.Route.Attach(_routes, unit.Record); Actors.Add(unit);
                Occupancy.WriteFootprint(unit.Parameters[3], unit.Parameters[4], unit.Record[0x0d], (uint)(place.X * 32 + 16), (uint)(place.Y * 32 + 16), unit.Id);
            }
        }
        // The rival declares war on the enemy (message 5 sets war both ways; the enemy answers it with a campaign).
        _diplomacyRules.Send(Diplomacy, 5, (byte)Rules.EnemySlot, (byte)Rules.RivalSlot, (byte)Rules.EnemySlot, 0);
        Log("Наблюдение: на востоке второе поселение, оно воюет с красным.");
    }

    /// <summary>The nearest cell in rings of up to 10 around the wanted one that passes the test.</summary>
    private Vector2I? RivalSpot(Vector2I wanted, Func<Vector2I, bool> fits)
    {
        for (int r = 0; r <= 10; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var cell = wanted + new Vector2I(dx, dy);
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && Inside(cell) && fits(cell)) return cell;
                }
        return null;
    }
}
