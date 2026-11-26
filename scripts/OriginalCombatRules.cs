using System.Buffers.Binary;
namespace Northwind;

public enum VitalsResult : byte { Alive, Dead }
public readonly record struct StruckObject(uint Reference, byte TerrainClass);

/// <summary>Callbacks of the 90E9 unit prefix that stay outside this kernel.</summary>
public interface IVitalsWorld
{
    /// <summary>A416 with a DAT u32 voice/sound reference.</summary>
    void Sound(uint reference);
    /// <summary>Occupancy reference in the given plane at a cell index.</summary>
    uint Occupant(int plane, int cell);
}

/// <summary>Melee hit chain and unit vitals from LE:1 and ARMY__00.WRK. See docs/combat.md.</summary>
public static class OriginalCombatRules
{
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);

    /// <summary>ARMY__00.WRK 4F5..4FD: attack of a melee strike from the attacker's experience byte OBE[16].</summary>
    public static byte StrikeAttack(byte experience) => (byte)(((byte)(experience + 1) >> 6) + 5);

    /// <summary>BFC9 (FS 119). Returns native ZF=0: a point of damage or more was taken.</summary>
    public static bool Hit(Span<byte> target, ReadOnlySpan<byte> targetTemplate, byte attack, byte kind, Func<byte> random)
    {
        if ((sbyte)target[0x19] <= 0) return false;
        target[0x17] = (byte)((target[0x17] & 0xe0) | (byte)((kind << 3) | 1));
        byte defence = targetTemplate[0x44], damage;
        if (attack > defence) damage = (byte)(attack - defence);
        else
        {
            byte odds = unchecked((byte)(2 - (byte)(attack - defence)));
            if (odds > 4) return false;
            if (((random() * odds) >> 8) != 0) return false;
            damage = 1;
        }
        target[0x19] = target[0x19] < damage ? (byte)0 : (byte)(target[0x19] - damage);
        target[0x17] |= 2;
        return true;
    }

    /// <summary>982B (FS A1): object of the effect's plane whose DAT footprint contains the point.</summary>
    public static StruckObject FindStruck(uint x, uint y, byte layer, byte mapWidth, ReadOnlySpan<uint> cells, ReadOnlySpan<byte> properties,
        Func<int, int, uint> occupant, Func<uint, (byte[] Record, byte[] Template)?> resolve)
    {
        // MUL BYTE [18DE] multiplies only the low byte of the row; maps are at most 255 cells high.
        int index = (byte)(y >> 5) * mapWidth + (int)(x >> 5);
        byte terrain = (byte)(properties[(int)(cells[index] & 0xffff) * 8] & 7);
        uint reference = occupant(layer & 3, index);
        if (reference == 0 || resolve(reference) is not { } found) return new(0, terrain);
        var (record, template) = found;
        if ((U32(template, 0x40) & 0x8000) != 0) return new(0, terrain);
        if (!Inside((int)x, (int)U32(record, 4), (byte)(template[3] << 4)) || !Inside((int)y, (int)U32(record, 8), (byte)(template[4] << 4)))
            return new(0, terrain);
        return new(reference, terrain);
    }
    private static bool Inside(int point, int center, byte half) => point >= center - half && point <= center + half;

    /// <summary>97C6 (FS 11): the object in the cell of the point on the effect's plane, whatever its footprint, and the terrain class.</summary>
    public static StruckObject CellObject(uint x, uint y, byte layer, byte mapWidth, ReadOnlySpan<uint> cells, ReadOnlySpan<byte> properties, Func<int, int, uint> occupant)
    {
        int index = (byte)(y >> 5) * mapWidth + (int)(x >> 5);
        return new(occupant(layer & 3, index), (byte)(properties[(int)(cells[index] & 0xffff) * 8] & 7));
    }

    /// <summary>ARMY__00.WRK 762 effect type 6, AH=0: the hit on the object in the cell. The attack grows with the attacker's experience
    /// (5..7), the kind is the attacker's command &amp; 7; a living object (kind other than 3) loses attack − DAT[44] (byte arithmetic) and
    /// is marked as wounded; an empty cell or a miss on terrain class 1 ends in a hard spark. Returns the sound (A47E argument) or null.</summary>
    public static int? CellHitEffect(Span<byte> effect, ReadOnlySpan<byte> attacker, Span<byte> target, ReadOnlySpan<byte> targetTemplate,
        StruckObject found, Func<byte> random)
    {
        byte attack = StrikeAttack(attacker[0x16]), kind = (byte)(attacker[0x14] & 7);
        int sound; bool hard;
        if (found.Reference != 0 && kind != 3 && (sbyte)target[0x19] > 0)
        {
            byte bits = 1, health = target[0x19];
            if (attack > targetTemplate[0x44])
            {
                bits |= 2;
                byte left = unchecked((byte)(health - (byte)(attack - targetTemplate[0x44])));
                target[0x19] = (left & 0x80) != 0 ? (byte)0 : left;
            }
            target[0x17] = (byte)((target[0x17] & 0xe0) | bits);
            sound = 0xc; hard = (U32(targetTemplate, 0x40) & 0x10) != 0;
        }
        else if (found.TerrainClass == 1) { sound = 0xd; hard = true; }
        else { effect[0] = 0xff; return null; }
        BinaryPrimitives.WriteUInt16LittleEndian(effect[3..], hard ? (ushort)0x7d : (ushort)((random() & 8) + 0x62));
        effect[0] = (byte)(hard ? 5 : 4); effect[0x16] = 2; effect[0x17] = 8;
        return sound;
    }

    /// <summary>ARMY__00.WRK 4F3 effect type 0, AH=0. Returns the sound (A47E argument) or null when freed silently.</summary>
    public static int? StrikeEffect(Span<byte> effect, ReadOnlySpan<byte> attacker, Span<byte> target, ReadOnlySpan<byte> targetTemplate,
        bool targetFound, Func<byte> random)
    {
        byte attack = StrikeAttack(attacker[0x16]);
        if (!targetFound || !Hit(target, targetTemplate, attack, 0, random)) { effect[0] = 0xff; return null; }
        bool hard = (U32(targetTemplate, 0x40) & 0x10) != 0;
        BinaryPrimitives.WriteUInt16LittleEndian(effect[3..], hard ? (ushort)0x7d : (ushort)((random() & 8) + 0x62));
        effect[0] = (byte)(hard ? 5 : 4); effect[0x16] = 2; effect[0x17] = 8;
        return hard ? 1 : 0;
    }

    /// <summary>90E9..9210: death dispatch, regeneration, bleeding, rest, flee, wound voice, experience and terrain death.</summary>
    public static VitalsResult Vitals(Span<byte> unit, ReadOnlySpan<byte> template, byte tick, byte mapWidth, byte mapHeight,
        ReadOnlySpan<uint> cells, ReadOnlySpan<byte> properties, OriginalTerrainCatalog random, IVitalsWorld world, bool terrainDeath = true)
    {
        if ((sbyte)unit[0x19] <= 0) return VitalsResult.Dead;
        bool mechanical = (U32(template, 0x40) & 0x10) != 0;
        if (!mechanical)
        {
            if ((sbyte)unit[0x1a] > (sbyte)unit[0x19])
            {
                byte missing = (byte)(unit[0x1a] - unit[0x19]), heal = (byte)(unit[0x16] >> 5);
                if (heal >= missing) heal = missing;
                unit[0x19] = (byte)(unit[0x19] + heal);
            }
            if ((random.NextRandomByte() & 0x1f) == 0 && (sbyte)unit[0x19] <= 2) unit[0x19]--;
        }
        if ((unit[0x2d] & 15) == 0 && tick == 0)
        {
            unit[0x19] = (byte)(unit[0x19] + (unit[0x19] >> 2));
            if (unit[0x19] > template[0x45]) unit[0x19] = template[0x45];
        }
        if (!mechanical && (unit[0x14] & 7) != 3 && (sbyte)unit[0x19] <= 2) Flee(unit, mapWidth, mapHeight, random);
        if ((sbyte)(byte)(unit[0x1a] - unit[0x19]) >= 4) world.Sound(U32(template, 0xa3));
        if (tick == 0 && (random.NextSecondaryByte() & 7) == 0)
        {
            byte experience = unit[0x16];
            byte grown = (byte)(experience + 2 - (experience >> 6));
            unit[0x16] = (sbyte)grown < 0 ? (byte)0x7f : grown;
        }
        if (terrainDeath && (unit[0x2d] & 15) == 0)
        {
            int index = (byte)(U32(unit, 8) >> 5) * mapWidth + (int)(U32(unit, 4) >> 5);
            byte terrain = (byte)(properties[(int)(cells[index] & 0xffff) * 8] & 7);
            if (template[0x58 + terrain] == 0 && (unit[0x0d] == 0 || world.Occupant((unit[0x0d] - 1) & 3, index) == 0)) unit[0x19] = 0;
        }
        if ((unit[0x17] & 0x1a) == 0x1a) Flee(unit, mapWidth, mapHeight, random);
        return VitalsResult.Alive;
    }

    /// <summary>8E0E: request a move to a random map cell unless the unit is already in state 4.</summary>
    public static void Flee(Span<byte> unit, byte mapWidth, byte mapHeight, OriginalTerrainCatalog random) => Flee(unit, mapWidth, mapHeight, random.NextRandomByte, random.NextSecondaryByte);
    public static void Flee(Span<byte> unit, byte mapWidth, byte mapHeight, Func<byte> first, Func<byte> second)
    {
        if ((unit[0x2d] & 15) == 4) return;
        unit[0x22] = (byte)((first() * mapWidth) >> 8);
        unit[0x23] = (byte)((second() * mapHeight) >> 8);
        unit[0x2d] = (byte)((unit[0x2d] & 15) | 0x20);
    }

    /// <summary>8B5E: 0 in reach, 1 too far (or across water), 2 too close, 3 cannot attack this target.</summary>
    public static byte Reach(ReadOnlySpan<byte> attacker, ReadOnlySpan<byte> attackerTemplate, ReadOnlySpan<byte> target, ReadOnlySpan<byte> targetTemplate, byte targetTerrainClass)
    {
        if (attackerTemplate[0x71] <= targetTemplate[0x44]) return 3;
        if (((U32(attackerTemplate, 0x74) >> (target[0x0d] & 3)) & 1) == 0) return 3;
        uint gap = OriginalWorkerApproach.FootprintGap(attacker, target, attackerTemplate, targetTemplate);
        if (targetTerrainClass == 1 && gap >= 0x30) return 1;
        byte cells = (byte)(((gap + 15) >> 5) + 1);
        if (cells > attackerTemplate[0x72]) return 1;
        if ((byte)(cells - 1) < attackerTemplate[0x73]) return 2;
        return 0;
    }

    /// <summary>8AF4: true when the swing delay OBE[2F] has reached DAT[75] - hi8(DAT[75] * experience).</summary>
    public static bool SwingReady(Span<byte> attacker, ReadOnlySpan<byte> attackerTemplate)
    {
        byte delay = attackerTemplate[0x75];
        byte limit = (byte)(delay - ((delay * attacker[0x16]) >> 8));
        if (attacker[0x2f] >= limit) return true;
        attacker[0x2f]++; return false;
    }

    /// <summary>8ABF: false while the target in OBE[1C] lives; a dead target is forgotten and rewards experience DAT[76] / 4.</summary>
    public static bool TargetAlive(Span<byte> attacker, ReadOnlySpan<byte> target, ReadOnlySpan<byte> targetTemplate)
    {
        if (U32(attacker, 0x1c) == 0) return false;
        if ((sbyte)target[0x19] > (sbyte)targetTemplate[0x46]) return true;
        if (attacker[0x16] < 0x40)
        {
            byte grown = (byte)(attacker[0x16] + (targetTemplate[0x76] >> 2));
            attacker[0x16] = (sbyte)grown < 0 ? (byte)0x7f : grown;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(attacker[0x1c..], 0);
        return false;
    }


    /// <summary>42BB tail 434D..4384 after each AH=1 update: previous condition, removed/dead target, periodic hit-mark reset.</summary>
    public static void AfterUpdate(Span<byte> unit, ReadOnlySpan<byte> target, byte tick)
    {
        unit[0x1a] = unit[0x19];
        if (U32(unit, 0x1c) != 0 && (target.IsEmpty || (BinaryPrimitives.ReadUInt16LittleEndian(target[0x17..]) & 0x200) != 0 || (sbyte)target[0x19] < 0))
            BinaryPrimitives.WriteUInt32LittleEndian(unit[0x1c..], 0);
        if ((tick & 7) == 0)
            BinaryPrimitives.WriteUInt16LittleEndian(unit[0x17..], (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(unit[0x17..]) & 0xfdd8));
    }

}
