using System.Buffers.Binary;
namespace Northwind;

public readonly record struct OriginalPreparedMotion(bool EnteredMotion, OriginalMotionStep Step);

/// <summary>Original direction selection, single-sector turn and DAT/SEG movement preparation.</summary>
public static class OriginalMotionPreparation
{
    // DS:19D7 in LE data object 2; FF denotes coincident coordinates in this routine.
    private static ReadOnlySpan<byte> HeadingTable => [7, 6, 5, 0, 255, 4, 1, 2, 3];

    // EXE 9688..96CC. Unsigned differences and doubling overflow are intentional.
    public static byte DirectionToward(uint x, uint y, uint targetX, uint targetY)
    {
        int column = targetX < x ? 0 : targetX == x ? 1 : 2;
        int row = targetY < y ? 0 : targetY == y ? 1 : 2;
        uint dx = targetX >= x ? targetX - x : x - targetX;
        uint dy = targetY >= y ? targetY - y : y - targetY;
        if (dx < dy)
        {
            if (unchecked(dx * 2) < dy) column = 1;
        }
        else if (unchecked(dy * 2) < dx) row = 1;
        return HeadingTable[column * 3 + row];
    }

    // EXE 9653..9687. Returns the native ZF used by the caller, including frame-addition wrap.
    public static bool TurnToward(Span<byte> record, byte target, ReadOnlySpan<byte> template)
    {
        if (record.Length < 0x26 || template.Length < 0x4b) throw new ArgumentException("Truncated turn data");
        int current = record[0x25] & 7;
        if (target == current) return true;
        int difference = (target - current) & 7;
        int next = (current + (difference <= 4 ? 1 : -1)) & 7;
        record[0x25] = unchecked((byte)((target << 3) | next));
        // MUL writes AX, but XOR AH,AH discards the high product byte before adding frame base.
        ushort frame = unchecked((ushort)((byte)(next * template[0x4a]) +
            BinaryPrimitives.ReadUInt16LittleEndian(template[0x48..])));
        BinaryPrimitives.WriteUInt16LittleEndian(record[1..], frame);
        return frame == 0;
    }

    // EXE 7180..71F5. DAT[58..5F] has a movement use as well as its placement-mask use.
    public static byte InitializeAccumulator(OriginalOccupancyMap grid, ReadOnlySpan<uint> mapCells,
        ReadOnlySpan<byte> segmentProperties, Span<byte> record, ReadOnlySpan<byte> template)
    {
        if (record.Length < 0x27 || template.Length < 0x60) throw new ArgumentException("Truncated motion data");
        uint x = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) >> 5;
        uint y = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]) >> 5;
        if (x >= grid.Width || y >= grid.Height) throw new ArgumentOutOfRangeException(nameof(record));
        int cell = (int)y * grid.Width + (int)x;
        byte property = 0;
        if ((record[0x0d] & 1) == 0 || grid.Cells[cell] == 0)
        {
            if (mapCells.Length != grid.Width * grid.Height) throw new ArgumentException("Wrong MAP size", nameof(mapCells));
            int offset = (int)(mapCells[cell] & 65535) * 8;
            if (offset >= segmentProperties.Length) throw new ArgumentException("Missing SEG property", nameof(segmentProperties));
            property = segmentProperties[offset];
        }
        byte value = template[0x58 + (property & 7)];
        if ((value & 0x80) == 0) value = unchecked((byte)(value - (property >> 4)));
        byte accumulator = unchecked((byte)(((record[0x26] & 0xc0) | (value & 0x3f)) + (record[0x16] >> 5)));
        record[0x26] = accumulator;
        return accumulator;
    }

    // EXE 7165..725C, or the pending-turn exit at 72D8, before animation/effects.
    public static OriginalPreparedMotion TurnAndAdvance(OriginalOccupancyMap grid, ReadOnlySpan<uint> mapCells,
        ReadOnlySpan<byte> segmentProperties, Span<byte> record, ReadOnlySpan<byte> template,
        uint reference, byte stepFlags)
    {
        if (record.Length < 0x2d || template.Length < 0x60) throw new ArgumentException("Truncated prepared-motion data");
        stepFlags &= 0xbf;
        if (!TurnToward(record, (byte)(record[0x25] >> 3), template))
            return new(false, new(0, false, stepFlags));
        record[0x24] = unchecked((byte)(record[0x24] + 1));
        InitializeAccumulator(grid, mapCells, segmentProperties, record, template);
        return new(true, OriginalSpatialRules.AdvanceMotion(grid, record, reference, template[3], template[4], stepFlags));
    }
}
