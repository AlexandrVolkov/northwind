using System.Buffers.Binary;
namespace Northwind;

/// <summary>Services of the tower's attack: D895 (nearest enemy in sight, al = 1) and 9727 (the effect of DAT[70] at a point).</summary>
public interface ITowerWorld
{
    /// <summary>D895 with AL = 1; null when nothing is in sight.</summary>
    byte[]? FindEnemy(byte[] tower);
    /// <summary>9727: bl = DAT[70] (bit 7 = army 0), al = the tower's army byte, bh = the target's layer, the point is the muzzle, the pushed arguments are the tower and the target point.</summary>
    void Effect(byte type, byte layer, byte army, uint x, uint y, byte[] tower, uint targetX, uint targetY);
}

/// <summary>A ranged building (the tower, ARMY__02 13): FS:9D = LE:1 CA7C. Every update the reload counter +24 grows to DAT[75] and +23 counts; every eighth update
/// the tower looks for an enemy (D895); with a full reload it fires: the muzzle point is the tower's position plus the signed pair DAT[60 + 2·direction],
/// the direction (9688) is one of eight from the tower to the target, the effect type is DAT[70] (82h: the arrow of WRK 593). Verified against the original x86 with the
/// two services recorded (tools/verify_tower.py, tests/fixtures/tower.json).</summary>
public static class OriginalTowerFire
{
    private static readonly byte[] DirectionTable = [7, 6, 5, 0, 0xff, 4, 1, 2, 3];   // DS:19D7, index 3·x class + y class

    /// <summary>9688: the direction (0..7) from the point to the target (FFh for the same point).</summary>
    public static byte Direction(uint fromX, uint fromY, uint targetX, uint targetY)
    {
        int cl = 2, ch = 2;
        uint dx = unchecked(targetX - fromX), dy = unchecked(targetY - fromY);
        if (dx == 0) cl--; else if (targetX < fromX) { dx = unchecked((uint)-(int)dx); cl -= 2; }
        if (dy == 0) ch--; else if (targetY < fromY) { dy = unchecked((uint)-(int)dy); ch -= 2; }
        if (dx >= dy) { if (unchecked(dy * 2) < dx) ch = 1; }
        else if (unchecked(dx * 2) < dy) cl = 1;
        return DirectionTable[3 * cl + ch];
    }

    public static void Step(ITowerWorld world, byte[] tower, ReadOnlySpan<byte> template)
    {
        tower[0x23]++;
        if ((sbyte)tower[0x19] <= (sbyte)template[0x46]) return;
        if (tower[0x24] < template[0x75]) tower[0x24]++;
        if ((tower[0x23] & 7) != 0) return;
        var target = world.FindEnemy(tower);
        if (target == null) return;
        if (tower[0x24] < template[0x75]) return;
        tower[0x24] = 0; tower[0x16] = 0x28;
        uint tx = BinaryPrimitives.ReadUInt32LittleEndian(target.AsSpan(4)), ty = BinaryPrimitives.ReadUInt32LittleEndian(target.AsSpan(8));
        byte direction = Direction(BinaryPrimitives.ReadUInt32LittleEndian(tower.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(tower.AsSpan(8)), tx, ty);
        int at = 0x60 + direction * 2;
        uint x = unchecked(BinaryPrimitives.ReadUInt32LittleEndian(tower.AsSpan(4)) + (uint)(int)(sbyte)template[at]);
        uint y = unchecked(BinaryPrimitives.ReadUInt32LittleEndian(tower.AsSpan(8)) + (uint)(int)(sbyte)template[at + 1]);
        world.Effect(template[0x70], (byte)(target[0x0d] & 3), tower[0], x, y, tower, tx, ty);
        tower[0x16] = 0;
    }
}
