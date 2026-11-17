using System.Buffers.Binary;
namespace Northwind;

public sealed record MovementObject(byte[] Record, byte[] Template);
public readonly record struct MovementAdmission(byte Code, bool HardObject)
{
    public bool Clear => Code == 0;
}

/// <summary>7C63/7D4C candidate footprint checks. Caller supplies a view with its own footprint removed.</summary>
public static class OriginalMovementAdmission
{
    private static readonly (int X, int Y)[] Directions = [(0,-1),(1,-1),(1,0),(1,1),(0,1),(-1,1),(-1,0),(-1,-1)];
    public static MovementAdmission ProbeStep(byte width, byte height, ReadOnlySpan<uint> cells, ReadOnlySpan<byte> properties,
        ReadOnlySpan<uint> occupancy, ReadOnlySpan<byte> mover, ReadOnlySpan<byte> template, byte direction,
        uint ownerMask, Func<uint, MovementObject> resolve, uint ignoreReference = 0)
    {
        if (direction > 7) throw new ArgumentOutOfRangeException(nameof(direction));
        var delta = Directions[direction];
        byte left = unchecked((byte)((BinaryPrimitives.ReadUInt32LittleEndian(mover[4..]) >> 5) - (uint)(template[3] >> 1) + delta.X));
        byte top = unchecked((byte)((BinaryPrimitives.ReadUInt32LittleEndian(mover[8..]) >> 5) - (uint)(template[4] >> 1) + delta.Y));
        return ProbeArea(width, height, cells, properties, occupancy, template, mover[0x0d], left, top, direction, ownerMask, resolve, ignoreReference);
    }
    public static MovementAdmission ProbeArea(byte width, byte height, ReadOnlySpan<uint> cells, ReadOnlySpan<byte> properties,
        ReadOnlySpan<uint> occupancy, ReadOnlySpan<byte> template, byte rawLayer, byte left, byte top, byte direction,
        uint ownerMask, Func<uint, MovementObject> resolve, uint ignoreReference = 0)
    {
        int fw = template[3], fh = template[4], layer = rawLayer & 3, area = width * height;
        if (width == 0 || height == 0 || fw == 0 || fh == 0 || fw > width || fh > height || layer >= 3 || direction > 7 ||
            cells.Length != area || occupancy.Length != 3 * area || properties.Length % 8 != 0 || template.Length < 0x68)
            throw new ArgumentException("Invalid movement map or template");
        if (left > width - fw || top > height - fh) return new(255, false);
        byte code = 0;
        for (int y = top; y < top + fh; y++) for (int x = left; x < left + fw; x++)
        {
            uint reference = occupancy[layer * area + y * width + x];
            if (reference == 0 || reference == ignoreReference) continue;
            var other = resolve(reference); var record = other.Record; var parameters = other.Template;
            // BT on memory can read beyond its first dword. This API intentionally supports owners 0..31.
            if (record[3] >= 32) throw new ArgumentException("Owner bit outside supported mask");
            if ((ownerMask & (1u << record[3])) != 0)
            {
                code = 3;
                int difference = template[0x3f] - parameters[0x3f];
                if (difference >= 10) continue;
            }
            if ((BinaryPrimitives.ReadUInt32LittleEndian(parameters.AsSpan(0x40)) & 8) != 0) return new(255, true);
            code = 1;
            if ((record[0x14] & 0x40) != 0) continue;
            if ((record[0x2d] >> 4) != 2 || record[0x24] == 2) return new(255, true);
            if (((record[0x25] + 4) & 7) == direction) code = 2;
        }
        for (int y = top; y < top + fh; y++) for (int x = left; x < left + fw; x++)
        {
            int index = y * width + x, property = (int)(cells[index] & 65535) * 8;
            if (property + 8 > properties.Length) throw new InvalidDataException("Missing movement SEG property");
            byte terrain = properties[property], value = template[0x58 + (terrain & 15)];
            byte adjusted = (value & 128) != 0 ? (byte)(value & 127) : unchecked((byte)(value - (terrain >> 3)));
            if ((sbyte)adjusted > 0) continue;
            if (layer != 1 || occupancy[index] == 0) return new(255, false);
            var support = resolve(occupancy[index]);
            if ((BinaryPrimitives.ReadUInt32LittleEndian(support.Template.AsSpan(0x40)) & 2) == 0 ||
                (sbyte)support.Record[0x19] <= (sbyte)support.Template[0x46] || support.Template[0x3f] < template[0x3f]) return new(255, false);
        }
        return new(code, false);
    }
}
