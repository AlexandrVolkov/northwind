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
    public static void Flee(Span<byte> unit, byte mapWidth, byte mapHeight, OriginalTerrainCatalog random)
    {
        if ((unit[0x2d] & 15) == 4) return;
        unit[0x22] = (byte)((random.NextRandomByte() * mapWidth) >> 8);
        unit[0x23] = (byte)((random.NextSecondaryByte() * mapHeight) >> 8);
        unit[0x2d] = (byte)((unit[0x2d] & 15) | 0x20);
    }
}
