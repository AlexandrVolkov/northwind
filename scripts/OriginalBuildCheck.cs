using System.Buffers.Binary;
namespace Northwind;

/// <summary>World of the build check: the placement probe, the participant's objects, the template's own check (WRK AH=4),
/// the line neighbour rule BC2E and the participant's fog.</summary>
public interface IBuildCheckWorld
{
    BuilderProbe Probe(byte template, byte army, uint x, uint y, out byte[]? blocker);
    IEnumerable<(byte[] Record, byte[] Template)> Objects(byte participant);
    /// <summary>The template's own check (WRK entry AH=4 at the point): true when it allows the building.</summary>
    bool Hook(byte template, byte army, uint x, uint y);
    /// <summary>BC2E: the neighbour rule of a line of segments: true when it allows the segment.</summary>
    bool Line(byte template, byte army, uint x, uint y);
    /// <summary>AC63 at the point on the plane (the creation placement check).</summary>
    BuilderProbe CreationProbe(byte template, byte army, byte plane, uint x, uint y);
    /// <summary>[18DA]: the participant has seen the cell.</summary>
    bool Explored(byte participant, uint x, uint y);
}

/// <summary>LE:1 AF07 «may template N be built at the point by participant P», with the range rules B1EC and B28A.</summary>
public static class OriginalBuildCheck
{
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static uint Distance(ReadOnlySpan<byte> record, uint x, uint y)
    {
        uint dx = (uint)Math.Abs((int)(U32(record, 4) - x)), dy = (uint)Math.Abs((int)(U32(record, 8) - y));
        return Math.Max(dx, dy);
    }

    public static bool Allowed(IBuildCheckWorld world, ReadOnlySpan<byte> template, byte index, byte army, byte participant, uint x, uint y)
    {
        bool line = (U32(template, 0x40) & 0x80) != 0;
        var probe = world.Probe(index, army, x, y, out var blocker);
        if (probe == BuilderProbe.Rejected) return false;
        // Only the same segment of a line, exactly at this point, does not block.
        if (probe == BuilderProbe.Blocked && (!line || blocker![0] != army || blocker[0x0e] != index || U32(blocker, 4) != x || U32(blocker, 8) != y)) return false;
        if (!InRange(world, template, participant, x, y)) return false;
        if (!world.Hook(index, army, x, y)) return false;
        if (line && !world.Line(index, army, x, y)) return false;
        return world.Explored(participant, x, y);
    }

    /// <summary>LE:1 D363 «may template N be placed in the cell (cx, cy)» (sites of mines and mills in the AI economy): the creation check
    /// AC63 on the plane DAT[77]&amp;3 at the cell's point (an odd footprint is shifted by half a cell), the range rules and the template's own check.</summary>
    public static bool SiteAllowed(IBuildCheckWorld world, ReadOnlySpan<byte> template, byte index, byte army, byte participant, byte cellX, byte cellY)
    {
        uint x = (uint)(cellX << 5) + (uint)((template[3] << 4) & 0x10), y = (uint)(cellY << 5) + (uint)((template[4] << 4) & 0x10);
        if (world.CreationProbe(index, army, (byte)(template[0x77] & 3), x, y) != BuilderProbe.Clear) return false;
        return InRange(world, template, participant, x, y) && world.Hook(index, army, x, y);
    }

    /// <summary>B1EC: unless DAT[4E] is FFh (anywhere), a centre building (DAT[40] bit 3) of the participant with condition above DAT[46]
    /// and none of the flags 60h must lie within DAT[4E] cells; then B28A: unless the template has the flag 40h, if the participant has
    /// a castle-class building (flags F08h = 508h) with condition above DAT[46], one must lie within 800 pixels.</summary>
    public static bool InRange(IBuildCheckWorld world, ReadOnlySpan<byte> template, byte participant, uint x, uint y)
    {
        if (template[0x4e] == 0xff) return true;
        uint limit = (uint)template[0x4e] << 5; bool found = false;
        foreach (var (record, other) in world.Objects(participant))
        {
            uint flags = U32(other, 0x40);
            if ((flags & 8) == 0 || (sbyte)record[0x19] <= (sbyte)other[0x46] || (flags & 0x60) != 0) continue;
            if (Distance(record, x, y) <= limit) { found = true; break; }
        }
        if (!found) return false;
        if ((U32(template, 0x40) & 0x40) != 0) return true;
        bool any = false;
        foreach (var (record, other) in world.Objects(participant))
        {
            if ((sbyte)record[0x19] <= (sbyte)other[0x46] || (U32(other, 0x40) & 0xf08) != 0x508) continue;
            any = true;
            if (Distance(record, x, y) <= 0x320) return true;
        }
        return !any;
    }
}
