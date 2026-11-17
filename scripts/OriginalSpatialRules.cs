using System.Buffers.Binary;
namespace Northwind;

public readonly record struct OriginalMotionStep(uint Distance, bool OccupancyTransferred, byte StepFlags);
public readonly record struct OriginalObjectChain(uint BaseReference, uint EndReference, uint CursorReference);
public readonly record struct OriginalRemovalCounters(uint Counter19F5, uint Counter19F9);
public readonly record struct OriginalDetachment(OriginalObjectChain Chain, OriginalRemovalCounters Counters);

/// <summary>Bounded motion phase and record detachment, recovered from the original executable.</summary>
public static class OriginalSpatialRules
{
    // LE data object 2, DS:19C7. Y grows down the map.
    private static readonly (int X, int Y)[] Directions =
        [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    // EXE 71F5..725C, stopping before animation/effects. Route selection and speed lookup are upstream.
    public static OriginalMotionStep AdvanceMotion(OriginalOccupancyMap grid, Span<byte> record,
        uint reference, byte footprintWidth, byte footprintHeight, byte stepFlags)
    {
        if (record.Length < 0x2d) throw new ArgumentException("Motion record is truncated", nameof(record));
        if (reference == 0) throw new ArgumentOutOfRangeException(nameof(reference));
        uint x = Read(record, 4), y = Read(record, 8);
        int sum = record[0x26] + ((record[0x26] << 6) & 255);
        uint distance = (uint)(((record[0x26] >> 2) + (sum > 255 ? 1 : 0)) & 15);
        byte remaining = record[0x2c];
        if (distance > remaining) distance = remaining;
        byte nextRemaining = (byte)(remaining - distance);
        bool transfer = remaining >= 16 && nextRemaining < 16;
        if (transfer)
        {
            // 9B0D uses the upper direction bits to locate both reservation rectangles;
            // coordinate motion below uses the lower three bits. They need not be equal.
            var direction = Directions[(record[0x25] >> 3) & 7];
            uint oldX = Add(x, -direction.X * (32 - nextRemaining));
            uint oldY = Add(y, -direction.Y * (32 - nextRemaining));
            uint newX = Add(x, direction.X * nextRemaining);
            uint newY = Add(y, direction.Y * nextRemaining);
            // Validate both before changing either; native writes have no bounds checks.
            grid.ValidateFootprint(footprintWidth, footprintHeight, record[0x0d], oldX, oldY);
            grid.ValidateFootprint(footprintWidth, footprintHeight, record[0x0d], newX, newY);
            grid.WriteFootprint(footprintWidth, footprintHeight, record[0x0d], oldX, oldY, 0);
            grid.WriteFootprint(footprintWidth, footprintHeight, record[0x0d], newX, newY, reference);
        }
        record[0x26] = unchecked((byte)sum);
        record[0x2c] = nextRemaining;
        var movement = Directions[record[0x25] & 7];
        Write(record, 4, Add(x, movement.X * (int)distance));
        Write(record, 8, Add(y, movement.Y * (int)distance));
        return new(distance, transfer, (byte)(stepFlags & 0xbf));
    }

    // EXE 98CC..996E. Detaches from the owner's forward chain without moving/freeing record bytes.
    // BaseReference replaces the native absolute pointer at owner+5; end/cursor are already relative.
    public static OriginalDetachment DetachRecord(OriginalOccupancyMap grid, Span<byte> pool,
        uint reference, OriginalObjectChain chain, byte footprintWidth, byte footprintHeight,
        uint templateFlags, OriginalRemovalCounters counters)
    {
        if (chain.BaseReference == 0 || chain.CursorReference < chain.BaseReference ||
            chain.CursorReference >= chain.EndReference || chain.EndReference > pool.Length)
            throw new ArgumentException("Invalid active object chain", nameof(chain));
        uint current = chain.CursorReference, previous = 0;
        while (current != reference)
        {
            uint next = Next(pool, current, chain.EndReference);
            if (next >= chain.EndReference || next > reference)
                throw new ArgumentException("Object is outside the active forward chain", nameof(reference));
            previous = current; current = next;
        }
        uint after = Next(pool, reference, chain.EndReference);
        var record = pool.Slice((int)reference, 34);
        if ((BinaryPrimitives.ReadUInt16LittleEndian(record[0x17..]) & 0x80) == 0)
            grid.WriteFootprint(footprintWidth, footprintHeight, record[0x0d], Read(record, 4), Read(record, 8), 0);
        if ((templateFlags & 8) == 0)
            counters = counters with { Counter19F9 = unchecked(counters.Counter19F9 + 1) };
        else if ((templateFlags & 0x20) == 0)
            counters = counters with { Counter19F5 = unchecked(counters.Counter19F5 + 1) };
        if (previous == 0)
            chain = after == chain.EndReference
                ? chain with { EndReference = chain.BaseReference, CursorReference = chain.BaseReference }
                : chain with { CursorReference = after };
        else if (after == chain.EndReference)
            chain = chain with { EndReference = reference };
        else
            Write(pool, (int)previous + 0x10, after - previous);
        return new(chain, counters);
    }

    private static uint Next(ReadOnlySpan<byte> pool, uint reference, uint end)
    {
        if (reference >= end || (ulong)reference + 34 > end)
            throw new ArgumentException("Truncated object prefix", nameof(pool));
        uint link = Read(pool, (int)reference + 0x10);
        ulong next = (ulong)reference + link;
        if (link < 34 || next > end) throw new ArgumentException("Invalid forward object link", nameof(pool));
        return (uint)next;
    }

    private static uint Add(uint value, int delta) => unchecked(value + (uint)delta);
    private static uint Read(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static void Write(Span<byte> bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes[offset..], value);
}
