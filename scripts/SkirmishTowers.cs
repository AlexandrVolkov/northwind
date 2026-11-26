using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>Towers (ARMY__02 13): every finished one runs the ported attack <see cref="OriginalTowerFire"/>; the enemy search is the unit search D895 (ported) in
/// «struck» mode, the arrow is the effect type 2 of the archers' arrows (WRK 593) launched from the muzzle point.</summary>
public sealed partial class Skirmish
{
    private void TickTowers()
    {
        foreach (var tower in Actors.Where(a => a.Alive && a.Index02 == 13 && a.ConstructionRemaining <= 0 && !a.Ruined).ToArray())
            OriginalTowerFire.Step(new SkirmishTowerWorld(this, tower), tower.Record, tower.Parameters);
    }

    private sealed class SkirmishTowerWorld(Skirmish battle, SkirmishActor tower) : ITowerWorld
    {
        public byte[]? FindEnemy(byte[] record)
        {
            var found = OriginalTargetRules.FindEnemy(tower.Id, tower.Record, tower.Parameters, true, new SearchWorld(battle, tower.Record[3]));
            return battle.Find(found.Target)?.Record;
        }
        public void Effect(byte type, byte layer, byte army, uint x, uint y, byte[] record, uint targetX, uint targetY)
        {
            if (x >= battle.Map.Width * 32 || y >= battle.Map.Height * 32) return;   // 9727 rejects points outside the map
            if ((type & 0x7f) != 2) return;   // only the arrow is ported
            var arrow = new byte[0x22]; arrow[0] = 2; arrow[1] = 0; arrow[5] = layer;
            BinaryPrimitives.WriteUInt32LittleEndian(arrow.AsSpan(7), x); BinaryPrimitives.WriteUInt32LittleEndian(arrow.AsSpan(0xc), y);
            battle.EffectSound(arrow, OriginalArrowRules.Create(arrow, tower.Record, targetX, targetY, (uint)(battle.Map.Width * 32), (uint)(battle.Map.Height * 32),
                battle._terrainCatalog.NextRandomByte, battle._terrainCatalog.NextSecondaryByte));
            battle.Arrows.Add(arrow);
        }
    }
}
