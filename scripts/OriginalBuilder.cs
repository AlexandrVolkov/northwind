using System.Buffers.Binary;
namespace Northwind;

/// <summary>LE:1 6996: the direction byte (0 = north, clockwise, 256 steps) between two points, from the arctangent table DS:287E.</summary>
public static class OriginalDirectionRules
{
    public static byte Between(byte[] arctangent, uint x1, uint y1, uint x2, uint y2)
    {
        byte quadrant = 0x40;
        uint ecx = x2, eax = x1, edx = y2, ebx = y1;
        if (ecx < eax) { ecx = eax - ecx; (edx, ebx) = (ebx, edx); quadrant += 0x80; } else ecx -= eax;
        if (edx < ebx) { edx = ebx - edx; (edx, ecx) = (ecx, edx); quadrant -= 0x40; } else edx -= ebx;
        if (ecx == edx) return (byte)(0x20 + quadrant);
        if (ecx > edx) return (byte)(arctangent[(byte)((edx << 8) / (ecx & 0xffff))] + quadrant);
        return (byte)(0x40 - arctangent[(byte)((ecx << 8) / (edx & 0xffff))] + quadrant);
    }
}

public enum BuilderProbe { Clear, Rejected, Blocked }

/// <summary>World of the builder: the placement probe AD5F, the new object A758, its registration 43E5, the development tree
/// A6AE and the population recount D453.</summary>
public interface IBuilderWorld
{
    /// <summary>AD5F at the centre point (pixels): free, refused by terrain or border, or blocked by an object (returned).</summary>
    BuilderProbe Probe(byte template, byte army, uint x, uint y, out byte[]? blocker);
    /// <summary>A758: a new object (army, owner, template, variant at the point); null when none can be made.</summary>
    byte[]? Create(byte army, byte owner, byte template, byte variant, uint x, uint y);
    void Register(byte[] record);
    void Complete(byte template, byte kind);
    void Recount(byte[] builder);
}

/// <summary>The builder engine of command 58h (LE:1 AFDB from B030) and the line step 69FE. The builder (+22 template, +29/+2B and
/// +2D/+2F the first and last points in pixels, +14 command) places a segment at the first point, then steps along the line
/// by the footprint toward the last until it is within one footprint of it. Every segment is paid for at once (DAT[7C..88]
/// against the participant's +18, +1C, +20, +24); a point the terrain refuses is skipped (and paid for), a point where an
/// object stands stops the build unless it is a segment of the same line. See docs/construction-payment-placement.md.</summary>
public sealed class OriginalBuilder(byte[] arctangent)
{
    private static readonly (int X, int Y)[] Directions = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];   // DS:19C7
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    /// <summary>The global byte [50D] that alternates the rounding of diagonal steps.</summary>
    public byte Toggle;

    /// <summary>69FE: false when the point is within one footprint of the target (done); otherwise the next point, one footprint
    /// along the nearer orthogonal direction (the rounding alternates with <see cref="Toggle"/>).</summary>
    public (bool Done, uint X, uint Y) Step(ReadOnlySpan<byte> template, uint x, uint y, uint targetX, uint targetY)
    {
        uint dx = x >= targetX ? x - targetX : targetX - x, dy = y >= targetY ? y - targetY : targetY - y;
        if (dx <= (uint)(template[3] << 5) && dy <= (uint)(template[4] << 5)) return (true, 0, 0);
        byte direction = OriginalDirectionRules.Between(arctangent, x, y, targetX, targetY);
        int index = (byte)((byte)(direction + 0x10) >> 5) + Toggle & 6;
        Toggle ^= 1;
        var d = Directions[index];
        // IMUL BYTE: AX = AL · (signed table byte), AL being the low byte of the footprint in pixels
        short stepY = (short)((sbyte)(byte)(template[4] << 5) * d.Y), stepX = (short)((sbyte)(byte)(template[3] << 5) * d.X);
        return (false, (ushort)(x + stepX), (ushort)(y + stepY));
    }

    /// <summary>AFDB from B030: runs the build of <paramref name="builder"/>; the participant's resources are paid in place.</summary>
    public void Run(IBuilderWorld world, byte[] builder, ReadOnlySpan<byte> template, Span<byte> participant)
    {
        byte index = builder[0x22], army = builder[0], owner = builder[3];
        uint x = BinaryPrimitives.ReadUInt16LittleEndian(builder.AsSpan(0x29)), y = BinaryPrimitives.ReadUInt16LittleEndian(builder.AsSpan(0x2b));
        uint x1 = BinaryPrimitives.ReadUInt16LittleEndian(builder.AsSpan(0x2d)), y1 = BinaryPrimitives.ReadUInt16LittleEndian(builder.AsSpan(0x2f));
        Toggle = 1;
        bool line = (U32(template, 0x40) & 0x80) != 0;
        while (true)
        {
            if (U32(template, 0x7c) > U32(participant, 0x18) || U32(template, 0x80) > U32(participant, 0x1c)
                || U32(template, 0x84) > U32(participant, 0x20) || U32(template, 0x88) > U32(participant, 0x24)) break;
            W32(participant, 0x24, U32(participant, 0x24) - U32(template, 0x88));
            W32(participant, 0x20, U32(participant, 0x20) - U32(template, 0x84));
            W32(participant, 0x18, U32(participant, 0x18) - U32(template, 0x7c));
            W32(participant, 0x1c, U32(participant, 0x1c) - U32(template, 0x80));
            byte variant = (byte)(template[0x77] & 3);
            var probe = world.Probe(index, army, x, y, out var blocker);
            byte[]? segment = null;
            if (probe == BuilderProbe.Clear)
            {
                segment = world.Create(army, owner, index, variant, x, y);
                if (segment == null) break;
            }
            else if (probe == BuilderProbe.Blocked)
            {
                // Only a segment of the same line at exactly this point is taken over (rebuilt).
                if (!line || blocker![0] != army || blocker[0x0e] != index || U32(blocker, 4) != x || U32(blocker, 8) != y) break;
                segment = blocker;
            }
            if (segment != null) { segment[0x14] = 0x81; segment[0x19] = 1; segment[0x1a] = 1; world.Register(segment); }
            if (!line) { Finish(world, builder, index, participant); return; }
            var next = Step(template, x, y, x1, y1);
            if (next.Done) { Finish(world, builder, index, participant); return; }
            (x, y) = (next.X, next.Y);
        }
        builder[0x14] &= 0xf8;
    }

    private static void Finish(IBuilderWorld world, byte[] builder, byte index, ReadOnlySpan<byte> participant)
    {
        world.Complete(index, 1);
        world.Recount(builder);
        if (U32(participant, 0x2c) is >= 0x28 and <= 0x32) world.Complete(0, 2);
        builder[0x14] &= 0xf8;
    }
}
