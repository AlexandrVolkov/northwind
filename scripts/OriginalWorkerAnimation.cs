using System.Buffers.Binary;
namespace Northwind;

// Arguments at the native 9727 effect-dispatch boundary; effect simulation is external.
public readonly record struct WorkerImpact(uint X, uint Y, ushort TargetX, ushort TargetY, byte Army, byte Code, byte Layer);

/// <summary>9210's stationary worker states 0, 1 and 3, excluding general upkeep and border events.</summary>
public static class OriginalWorkerAnimation
{
    public static bool Pending(ReadOnlySpan<byte> record) => (record[0x2d] & 15) is 1 or 3 || (record[0x2d] >> 4) is 1 or 3;

    public static WorkerImpact? Step(Span<byte> record, ReadOnlySpan<byte> template)
    {
        if (record.Length < 0x30 || template.Length < 0x71) throw new ArgumentException("Truncated worker animation data");
        int state = record[0x2d] & 15, requested = record[0x2d] >> 4;
        if ((state != 0 && state != 1 && state != 3) || (requested != 0 && requested != 1 && requested != 3) ||
            (BinaryPrimitives.ReadUInt32LittleEndian(template[0x40..]) & 0x100000) != 0)
            throw new ArgumentException("Unsupported stationary worker state");
        if ((record[0x17] & 128) != 0) return null;
        if (state == 0 && requested == 0) return null;
        if (state == 1) { AdvanceTurn(record, template); return null; }
        if (state == 3)
        {
            record[0x26] = unchecked((byte)(record[0x26] - 1));
            if (record[0x26] != 0) return null;
            record[0x24] = unchecked((byte)(record[0x24] + 1));
            byte progress = record[0x24];
            record[0x26] = progress < template[0x4e] ? template[0x4c] : template[0x4d];
            if (progress < template[0x4b])
            {
                BinaryPrimitives.WriteUInt16LittleEndian(record[1..], unchecked((ushort)(BinaryPrimitives.ReadUInt16LittleEndian(record[1..]) + 1)));
                if (progress != template[0x4e]) return null;
                record[0x2f] = 0;
                int offset = 0x60 + (record[0x25] & 7) * 2;
                return new(unchecked(BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) + (uint)(sbyte)template[offset]),
                    unchecked(BinaryPrimitives.ReadUInt32LittleEndian(record[8..]) + (uint)(sbyte)template[offset + 1]),
                    BinaryPrimitives.ReadUInt16LittleEndian(record[0x28..]), BinaryPrimitives.ReadUInt16LittleEndian(record[0x2a..]),
                    record[0], template[0x70], record[0x27]);
            }
        }
        // 9275: matching completed/current requests cancel to idle, rather than loop.
        if (requested == state) { requested = 0; record[0x2d] = 0; }
        record[0x24] = 0;
        record[0x2d] = (byte)((record[0x2d] & 0xf0) | requested);
        int heading = record[0x25] & 7;
        ushort baseFrame = BinaryPrimitives.ReadUInt16LittleEndian(template[0x48..]);
        if (requested == 0)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(record[1..], unchecked((ushort)(baseFrame + heading * template[0x4a])));
            return null;
        }
        if (requested == 1)
        {
            record[0x26] = 2; AdvanceTurn(record, template); return null;
        }
        // Only the tile-target branch 93AD..93DD is included; FF denotes an object target.
        if (record[0x22] == 255) throw new ArgumentException("Worker swing requires a tile target");
        BinaryPrimitives.WriteUInt16LittleEndian(record[0x28..], (ushort)(record[0x22] * 32 + 16));
        BinaryPrimitives.WriteUInt16LittleEndian(record[0x2a..], (ushort)(record[0x23] * 32 + 16));
        record[0x27] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(record[1..], unchecked((ushort)(baseFrame + 8 * template[0x4a] + heading * template[0x4b])));
        record[0x26] = template[0x4c];
        return null;
    }
    // 9354..937D: the first two-tick period is decremented immediately on entry.
    private static void AdvanceTurn(Span<byte> record, ReadOnlySpan<byte> template)
    {
        record[0x26] = unchecked((byte)(record[0x26] - 1));
        if (record[0x26] != 0) return;
        record[0x26] = 1;
        if (OriginalMotionPreparation.TurnToward(record, (byte)((record[0x25] >> 3) & 7), template)) record[0x2d] = 0;
    }
}
