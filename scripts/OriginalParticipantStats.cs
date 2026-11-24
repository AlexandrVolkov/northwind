using System.Buffers.Binary;
namespace Northwind;

/// <summary>The services of the participant statistics (recorded stubs in tools/verify_participant_stats.py).</summary>
public interface IParticipantStatsWorld
{
    (ushort Center, ushort Size) FitTerritory(uint center, uint size);   // 219C3: fits the territory rectangle (centre CX, size DX) to the map
    void Notice();                                                        // EEBB
    void Pulse();                                                         // 1188F
}

/// <summary>The statistics of the participants, LE:1 D3E5..D5B1. A participant record (stride F5h): +24 growth, +28 strength (the sum of DAT[88] of its living objects), +2C the weight of the
/// workers (DAT[78] of the class 108h), +30 the territory limit, +C9..CC the territory rectangle (centre cell x, y and size). <see cref="Recompute"/> (D453, every 32 updates) rebuilds them
/// from the objects; <see cref="Grow"/> (D40C) lets +24 follow the strength; <see cref="All"/> is D3E5. The territory is the box around the buildings (flags &amp; 68h = 8), fitted by 219C3, and
/// its size in cells squared (&gt;&gt; 14) plus the weight is the limit. Verified against the original x86 by tests/fixtures/participant-stats.json.</summary>
public static class OriginalParticipantStats
{
    public const int Participant = 0xf5;
    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static void Put(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);

    /// <summary>The globals 1C6E (max x), 1C72 (max y), 1C76 (min x), 1C7A (min y) left by the last participant.</summary>
    public sealed class Box { public uint MaxX, MaxY, MinX = uint.MaxValue, MinY = uint.MaxValue; }

    public static void Grow(byte[][] participants, uint tick)
    {
        foreach (var p in participants)
        {
            uint weight = Math.Min(U32(p, 0x2c), 0x64u);
            uint mask = (0x3ffu >> (int)(weight >> 5)) | 1u;
            // AL | 1 on the low byte only: the upper bits of EAX stay
            mask = (mask & 0xffffff00) | (byte)((byte)mask | 1);
            if ((tick & mask) != 0) continue;
            if (U32(p, 0x28) + U32(p, 0x24) < U32(p, 0x30)) Put(p, 0x24, U32(p, 0x24) + 1);
        }
    }

    public static void Recompute(byte[][] participants, Func<int, IEnumerable<(byte[] Unit, byte[] Template)>> objects, Box box, IParticipantStatsWorld world)
    {
        for (int i = 0; i < participants.Length; i++)
        {
            var p = participants[i];
            Put(p, 0x2c, 0); Put(p, 0x30, 0); Put(p, 0x28, 0); p[0xc9] = p[0xca] = p[0xcb] = p[0xcc] = 0;
            box.MaxX = 0; box.MaxY = 0; box.MinX = uint.MaxValue; box.MinY = uint.MaxValue;
            foreach (var (unit, template) in objects(i))
            {
                if ((sbyte)unit[0x19] <= (sbyte)template[0x46]) continue;
                Put(p, 0x28, U32(p, 0x28) + U32(template, 0x88));
                uint flags = U32(template, 0x40);
                if ((flags & 0xf08) == 0x108) Put(p, 0x2c, U32(p, 0x2c) + U32(template, 0x78));
                if ((flags & 0x68) != 8) continue;
                uint x = U32(unit, 4), y = U32(unit, 8);
                if (x > box.MaxX) box.MaxX = x; if (x < box.MinX) box.MinX = x;
                if (y > box.MaxY) box.MaxY = y; if (y < box.MinY) box.MinY = y;
            }
            uint center = (box.MaxX + box.MinX) >> 6, centerY = (box.MaxY + box.MinY) >> 6;
            center = (center & 0xffff00ff) | ((centerY & 0xff) << 8);
            if (box.MaxX < box.MinX) continue;
            uint size = (box.MaxX - box.MinX) >> 5;
            if (box.MaxY < box.MinY) continue;
            size = (size & 0xffff00ff) | (((box.MaxY - box.MinY) >> 5 & 0xff) << 8);
            size += 0x1414;
            var (c, s) = world.FitTerritory(center, size);
            center = (center & 0xffff0000) | c; size = (size & 0xffff0000) | s;
            byte cx = (byte)center, cy = (byte)(center >> 8), dx = (byte)size, dy = (byte)(size >> 8);
            p[0xc9] = cx; p[0xca] = cy; p[0xcb] = dx; p[0xcc] = dy;
            if (dx < cx) continue; dx -= cx;
            if (dy < cy) continue; dy -= cy;
            uint area = (uint)(dy * dx); area = (uint)((ulong)area * area);
            Put(p, 0x30, (area >> 14) + U32(p, 0x2c));
        }
    }

    public static void All(byte[][] participants, bool campaign, byte counter19e9, uint tick, Func<int, IEnumerable<(byte[] Unit, byte[] Template)>> objects, Box box, IParticipantStatsWorld world)
    {
        if (!campaign) return;
        if ((counter19e9 & 0x1f) == 0) Recompute(participants, objects, box, world);
        Grow(participants, tick);
        world.Notice(); world.Pulse();
    }
}
