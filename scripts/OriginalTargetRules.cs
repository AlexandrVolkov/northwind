using System.Buffers.Binary;
namespace Northwind;

public readonly record struct EnemySearch(uint Target, uint Enemies, uint Others, bool Searched);
public readonly record struct FirstContact(byte Owner, byte TheirOwner, byte CellX, byte CellY);

/// <summary>Inputs of D895 that come from the settlement (participant) records and the world.</summary>
public interface IEnemySearchWorld
{
    byte MapWidth { get; }
    byte MapHeight { get; }
    /// <summary>Occupancy reference in plane 0..2 at a cell index.</summary>
    uint Occupant(int plane, int cell);
    /// <summary>Record and DAT template of a live object reference.</summary>
    (byte[] Record, byte[] Template) Resolve(uint reference);
    /// <summary>Participant u32 +12: owners this settlement fights.</summary>
    uint HostileMask { get; }
    /// <summary>Participant +B9 + owner: contact already established.</summary>
    bool Contact(byte owner);
    /// <summary>D157 terrain class (descriptor &amp; 7) under an object.</summary>
    byte TerrainClass(ReadOnlySpan<byte> record);
    /// <summary>EDD5: first contact with another settlement.</summary>
    void Contacted(FirstContact contact);
}

/// <summary>Enemy search LE:1 D895 used by idle (82C3/8D3E, sight radius) and moving (8E81, attack range) units.</summary>
public static class OriginalTargetRules
{
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);

    /// <summary>219C3: clip a centred area of byte width/height to the map; returns left/top and inclusive right/bottom.</summary>
    public static (byte Left, byte Top, byte Right, byte Bottom) Clip(byte x, byte y, byte width, byte height, byte mapWidth, byte mapHeight)
    {
        byte left = unchecked((byte)(x - (width >> 1))), right = width;
        if (x < (width >> 1)) { right = unchecked((byte)(right + left)); left = 0; }
        right = unchecked((byte)(right + left)); if (right > mapWidth) right = mapWidth; right--;
        byte top = unchecked((byte)(y - (height >> 1))), bottom = height;
        if (y < (height >> 1)) { bottom = unchecked((byte)(bottom + top)); top = 0; }
        bottom = unchecked((byte)(bottom + top)); if (bottom > mapHeight) bottom = mapHeight; bottom--;
        return (left, top, right, bottom);
    }

    /// <summary>D7B1: unique occupants of the area, rows top to bottom, cells left to right, planes 0..2; at most 504.</summary>
    public static List<uint> Collect(IEnemySearchWorld world, byte left, byte top, byte width, byte height)
    {
        var list = new List<uint>();
        for (int row = 0; row < (height == 0 ? 256 : height); row++)
        for (int column = 0; column < (width == 0 ? 256 : width); column++)
        {
            int cell = (top + row) * world.MapWidth + left + column;
            for (int plane = 0; plane < 3; plane++)
            {
                uint reference = world.Occupant(plane, cell);
                if (reference != 0 && !list.Contains(reference) && list.Count < 504) list.Add(reference);
            }
        }
        return list;
    }

    public static EnemySearch FindEnemy(uint self, ReadOnlySpan<byte> unit, ReadOnlySpan<byte> template, bool moving, IEnemySearchWorld world)
    {
        byte radius = template[0x72];
        if (!moving)
        {
            radius = template[0x47];
            if ((U32(template, 0x40) & 8) == 0 && unit[0x16] >= 0x28) radius++;
        }
        byte twice = unchecked((byte)(radius * 2));
        var (left, top, right, bottom) = Clip((byte)(U32(unit, 4) >> 5), (byte)(U32(unit, 8) >> 5),
            unchecked((byte)(template[3] + twice)), unchecked((byte)(template[4] + twice)), world.MapWidth, world.MapHeight);
        var list = Collect(world, left, top, unchecked((byte)(right - left + 1)), unchecked((byte)(bottom - top + 1)));
        if (list.Count == 1) return new(0, 0, 0, false);
        byte visibility = unit[3] < 8 ? (byte)(1 << unit[3]) : (byte)0;
        uint hostile = world.HostileMask, best = 0, bestDistance = uint.MaxValue, enemies = 0, others = 0;
        foreach (uint reference in list)
        {
            if (reference == self) continue;
            var (record, targetTemplate) = world.Resolve(reference);
            if (record[3] == unit[3]) { others++; continue; }
            if ((record[0x20] & visibility) == 0) continue;
            if ((sbyte)record[0x19] <= (sbyte)targetTemplate[0x46]) continue;
            uint flags = U32(targetTemplate, 0x40), bit = 1u << (record[3] & 31);
            if (!world.Contact(record[3]) && (flags & 0x20) == 0)
            {
                world.Contacted(new(unit[3], record[3], (byte)(U32(record, 4) >> 5), (byte)(U32(record, 8) >> 5)));
                continue;
            }
            if ((flags & 0x400000) == 0 && (hostile & bit) == 0) { others++; continue; }
            if ((flags & 0x8020) != 0) continue;
            enemies++;
            if (((template[0x74] >> (record[0x0d] & 3)) & 1) == 0) continue;
            if (template[0x71] <= targetTemplate[0x44]) continue;
            uint dx = U32(unit, 4) - U32(record, 4), dy = U32(unit, 8) - U32(record, 8);
            if ((dx & 0x80000000) != 0) dx = unchecked(0u - dx);
            if ((dy & 0x80000000) != 0) dy = unchecked(0u - dy);
            uint distance = dx > dy ? dx : dy;
            if ((record[0x0d] & 2) == 0 && world.TerrainClass(record) == 1 && distance >= 0x30) continue;
            if (distance > bestDistance) continue;
            if (distance == bestDistance && U32(record, 0x1c) != self) continue;
            best = reference; bestDistance = distance;
        }
        return new(best, enemies, others, true);
    }
}
