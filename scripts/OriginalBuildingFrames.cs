using System.Buffers.Binary;
namespace Northwind;

/// <summary>The frame and condition tail of the generic building update (LE:1 C88C from C8E0; the WRK entry's dispatch before it is not
/// part of it). Frames of a template: stages base .. base+4A follow the condition; the working animation is [base+4A, base+4A+4B) (shifted
/// by 4A·2+4B times +22 for line templates) and advances every second update (low bit of the global counter). A building whose condition is not
/// above DAT[46] drops its work flags and loses 1 condition per 256 updates; below zero it collapses over a few updates (debris, then removal).</summary>
public static class OriginalBuildingFrames
{
    private static ushort U16(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(raw[offset..]);
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);

    /// <summary>Runs the tail. <paramref name="effect"/> gets (AL, BX, x, y) of the debris call 9727; <paramref name="remove"/> is 98CC.
    /// Returns the native ZF of the return (true: the building stands idle and ruined or healthy as asked by the caller's loop).
    /// Division faults of the original (a zero DAT[45]+1, a quotient over 255) throw <see cref="DivideByZeroException"/> / <see cref="OverflowException"/>.</summary>
    public static bool Update(Span<byte> rec, ReadOnlySpan<byte> t, byte tick, Action<byte, ushort, uint, uint> effect, Action remove)
    {
        byte b4a = t[0x4a], b4b = t[0x4b]; ushort baseFrame = U16(t, 0x48);
        uint ecx = (ushort)(b4a + baseFrame); byte dl = (byte)(b4a + b4b); uint edx = (ushort)(dl + baseFrame); byte al = (byte)(b4a + dl);
        if ((U32(t, 0x40) & 0x80) != 0) { uint product = (ushort)(al * rec[0x22]); edx += product; ecx += product; }
        if (rec[0x19] <= t[0x46] && rec[0x0f] != 0)
        {
            rec[0x0f] = 0; rec[0x14] = 0; rec[0x15] = 0;
            if (tick == 0 && (sbyte)rec[0x19] > 0) rec[0x19]--;
        }
        if ((sbyte)rec[0x19] < 0)
        {
            rec[0x19]--;
            if (rec[0x19] > 0xfa) return Tail(rec, t);
            if ((U16(rec, 0x17) & 0x80) == 0 && t[0x50] != 0xff) effect(rec[0], t[0x50], U32(rec, 4), U32(rec, 8));
            remove();
            return true;
        }
        ushort frame = U16(rec, 1);
        if (frame < (ushort)edx && frame >= (ushort)ecx && (tick & 1) == 0)
        {
            frame++; if (frame >= (ushort)edx) frame = (ushort)ecx;
            BinaryPrimitives.WriteUInt16LittleEndian(rec[1..], frame);
        }
        if (rec[0x19] == rec[0x1a]) return Tail(rec, t);
        byte bl = (byte)(b4a + 1); ushort ax = (ushort)(rec[0x19] * bl); byte bh = (byte)(t[0x45] + 1);
        if (bh == 0) throw new DivideByZeroException();
        int quotient = ax / bh; if (quotient > 255) throw new OverflowException();
        byte q = (byte)quotient;
        frame = U16(rec, 1);
        if (rec[0x19] < rec[0x1a])
        {
            if (q == 0) rec[0x19] = 0xff;
            bl = (byte)(bl - q);
            uint ebx = edx + bl - 2;
            if (frame == (ushort)ebx) return Tail(rec, t);
            if (ebx < edx && frame >= (ushort)ecx && frame < (ushort)edx) return Tail(rec, t);
            BinaryPrimitives.WriteUInt16LittleEndian(rec[1..], (ushort)ebx);
            byte kind = t[0x4f];
            if (kind != 0xff) effect(rec[0], (ushort)(kind | 2 << 8), U32(rec, 4), U32(rec, 8));
            return Tail(rec, t);
        }
        else
        {
            bl = (byte)(bl - q);
            uint ebx = edx + bl - 2;
            if (frame == (ushort)ebx) return Tail(rec, t);
            byte shifted = (byte)(q - b4a);
            ebx = (uint)(int)(sbyte)shifted; ebx = (ebx & 0xffff0000) | (ushort)((ushort)ebx + (ushort)ecx);
            if (ebx < ecx || frame < (ushort)ecx || frame >= (ushort)edx) BinaryPrimitives.WriteUInt16LittleEndian(rec[1..], (ushort)ebx);
            return Tail(rec, t);
        }
    }

    private static bool Tail(Span<byte> rec, ReadOnlySpan<byte> t)
    {
        if ((rec[0x14] & 7) == 1) return false;
        return (sbyte)rec[0x19] <= (sbyte)t[0x46];
    }
}
