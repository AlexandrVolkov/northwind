using System.Buffers.Binary;
namespace Northwind;

/// <summary>What the visit of an object needs from the world: the target's script (WRK AH = 8, via 12406) and the pickup of the item it gives (1243A).</summary>
public interface IVisitWorld
{
    (byte Al, bool Zf) Service(); // the target's WRK called with AH = 8: ZF set means AL is an item for the visitor
    void Pickup(byte item, byte owner);
}

/// <summary>The action «visit» of a unit (LE:1 902F): when the footprints are at most 8 apart (9B86) and the unit already faces the target (960B in 8C1E), the target's
/// script (12406 → WRK AH = 8) either hands over an item (1243A) or, answering 1, sends the visitor to the object (8C7C: a move request with the target kept in +1C).
/// Verified against the original x86 by tests/fixtures/visit.json (tools/verify_visit.py; the WRK call and the pickup are recorded stubs).</summary>
public static class OriginalVisit
{
    public const uint Reach = 8;

    /// <summary>960B: turn the unit one step (the facing +25 low bits) towards the point and set its frame; returns the ZF of the original: set when the unit already faces the point
    /// (or, after a step, when the new frame is 0).</summary>
    public static bool Turn(Span<byte> unit, ReadOnlySpan<byte> template, uint x, uint y)
    {
        byte direction = OriginalTowerFire.Direction(BinaryPrimitives.ReadUInt32LittleEndian(unit[4..]), BinaryPrimitives.ReadUInt32LittleEndian(unit[8..]), x, y);
        byte facing = (byte)(unit[0x25] & 7), step = (byte)(direction - facing);
        if (step == 0) return true;
        step &= 7; facing++;
        if (step > 4) facing -= 2;
        facing &= 7;
        unit[0x25] = (byte)((byte)(direction << 3) | facing);
        ushort frame = unchecked((ushort)((byte)(facing * template[0x4a]) + BinaryPrimitives.ReadUInt16LittleEndian(template[0x48..])));
        BinaryPrimitives.WriteUInt16LittleEndian(unit[1..], frame);
        return frame == 0;
    }

    /// <summary>902F for the visitor (ESI) and the target (EDI); <paramref name="targetReference"/> is the target's offset in the object pool.</summary>
    public static void Visit(Span<byte> visitor, ReadOnlySpan<byte> visitorTemplate, ReadOnlySpan<byte> target, ReadOnlySpan<byte> targetTemplate, uint targetReference, IVisitWorld world)
    {
        if (OriginalWorkerApproach.FootprintGap(visitor, target, visitorTemplate, targetTemplate) > Reach) return;
        // 8C1E: the unit must be at rest (state 0) and face the target
        if ((visitor[0x2d] & 15) != 0) { visitor[0x2d] &= 0x0f; return; }
        if (!Turn(visitor, visitorTemplate, BinaryPrimitives.ReadUInt32LittleEndian(target[4..]), BinaryPrimitives.ReadUInt32LittleEndian(target[8..])))
        { visitor[0x2d] = (byte)((visitor[0x2d] & 15) | 0x10); return; }
        // 12406: only a living object whose template has the visitor flag (20000h) answers
        if ((sbyte)target[0x19] <= 0 || (BinaryPrimitives.ReadUInt32LittleEndian(targetTemplate[0x40..]) & 0x20000) == 0) return;
        var (al, zf) = world.Service();
        if (zf) { world.Pickup(al, visitor[3]); return; }
        if (al != 1) return;
        BinaryPrimitives.WriteUInt32LittleEndian(visitor[0x1c..], targetReference);
        visitor[0x22] = 0xff; visitor[0x2d] = (byte)((visitor[0x2d] & 15) | 0x30);   // 8C7C
    }
}
