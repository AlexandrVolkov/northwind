using System.Buffers.Binary;
namespace Northwind;

public enum WorkerApproachStatus : byte { Completed, Blocked, Invalid, Pending }
public readonly record struct WorkerApproachResult(WorkerApproachStatus Status, bool MoveRequested = false, bool StopRequested = false);

/// <summary>8F70..902E with the lumbermill's WRK AH=5 validator. Movement execution remains external.</summary>
public static class OriginalWorkerApproach
{
    // ARMY__02.WRK 363..382: validates the visitor's DAT, not the building's condition.
    public static bool AcceptsVisitor(ReadOnlySpan<byte> visitor)
    {
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(visitor[0x40..]);
        return (flags & 0x1000) != 0 || (flags & 0xf00) == 0x300;
    }

    public static uint FootprintGap(ReadOnlySpan<byte> visitor, ReadOnlySpan<byte> receiver,
        ReadOnlySpan<byte> visitorTemplate, ReadOnlySpan<byte> receiverTemplate)
    {
        return Math.Max(AxisGap(visitor, receiver, visitorTemplate, receiverTemplate, 4, 3), AxisGap(visitor, receiver, visitorTemplate, receiverTemplate, 8, 4));
    }
    private static uint AxisGap(ReadOnlySpan<byte> visitor, ReadOnlySpan<byte> receiver, ReadOnlySpan<byte> visitorTemplate,
        ReadOnlySpan<byte> receiverTemplate, int coordinate, int dimension)
    {
        uint d = unchecked(BinaryPrimitives.ReadUInt32LittleEndian(receiver[coordinate..]) - BinaryPrimitives.ReadUInt32LittleEndian(visitor[coordinate..]));
        if ((d & 0x80000000) != 0) d = unchecked(0u - d);
        d = unchecked(d - (byte)(receiverTemplate[dimension] << 4) - (byte)(visitorTemplate[dimension] << 4));
        return (d & 0x80000000) != 0 ? 0 : d;
    }
    private static WorkerApproachResult Invalid(Span<byte> visitor)
    { visitor[0x17] &= 0xbf; return new(WorkerApproachStatus.Invalid); }
    public static WorkerApproachResult Step(Span<byte> visitor, Span<byte> receiver,
        ReadOnlySpan<byte> visitorTemplate, ReadOnlySpan<byte> receiverTemplate, uint widthPixels, uint heightPixels, bool? receiverAccepts = null)
    {
        // receiverAccepts carries another receiver's verified WRK AH=5 result (mine/castle: OriginalOreRules).
        if (BinaryPrimitives.ReadUInt32LittleEndian(visitor[0x1c..]) == 0 || receiver.IsEmpty || !(receiverAccepts ?? AcceptsVisitor(visitorTemplate))) return Invalid(visitor);
        int current = visitor[0x2d] & 15, requested = visitor[0x2d] >> 4;
        int phase = visitor[0x30] & 15;
        if (phase == 3)
        {
            return new(OriginalWorkerDelivery.WaitHandoff(visitor) switch {
                DeliveryWait.Completed => WorkerApproachStatus.Completed,
                DeliveryWait.Pending => WorkerApproachStatus.Pending, _ => WorkerApproachStatus.Invalid });
        }
        if (phase == 2)
        {
            if (current != 0) { visitor[0x2d] &= 15; return new(WorkerApproachStatus.Pending, StopRequested: true); }
            byte heading = OriginalMotionPreparation.DirectionToward(BinaryPrimitives.ReadUInt32LittleEndian(visitor[4..]), BinaryPrimitives.ReadUInt32LittleEndian(visitor[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(receiver[4..]), BinaryPrimitives.ReadUInt32LittleEndian(receiver[8..]));
            if (!OriginalMotionPreparation.TurnToward(visitor, heading, visitorTemplate))
            { visitor[0x2d] = (byte)((visitor[0x2d] & 15) | 0x10); return new(WorkerApproachStatus.Pending); }
            OriginalWorkerDelivery.RequestHandoff(visitor, receiver); return new(WorkerApproachStatus.Pending);
        }
        if (phase > 3) return new(WorkerApproachStatus.Pending);
        if (phase == 0)
        {
            if (current == 2) { visitor[0x2d] &= 15; return new(WorkerApproachStatus.Pending, StopRequested: true); }
            visitor[0x30] = 0x91;
        }
        else if (current != requested || current != 0) return new(WorkerApproachStatus.Pending);
        var point = OriginalWorkerDelivery.Approach(visitorTemplate, receiverTemplate, BinaryPrimitives.ReadUInt32LittleEndian(receiver[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(receiver[8..]), widthPixels, heightPixels);
        if (!point.Valid) return Invalid(visitor);
        if (phase == 1)
        {
            bool arrived = point.IsCenter ? FootprintGap(visitor, receiver, visitorTemplate, receiverTemplate) < 32 :
                point.X == BinaryPrimitives.ReadUInt32LittleEndian(visitor[4..]) && point.Y == BinaryPrimitives.ReadUInt32LittleEndian(visitor[8..]);
            if (arrived) { visitor[0x30] = 2; return new(WorkerApproachStatus.Pending); }
            byte before = visitor[0x30]; visitor[0x30] = unchecked((byte)(before - 0x10));
            if (before < 0x10) return new(WorkerApproachStatus.Blocked);
        }
        visitor[0x22] = (byte)(point.X >> 5); visitor[0x23] = (byte)(point.Y >> 5);
        visitor[0x2d] = (byte)((visitor[0x2d] & 15) | 0x20);
        return new(WorkerApproachStatus.Pending, MoveRequested: true);
    }
}
