using System.Buffers.Binary;
namespace Northwind;

/// <summary>State 5 of the unit machine (LE:1 9210; 94D1 the start, 94EA one update): the playback of the frames +22..+23 (offsets from DAT[48]); the range given backwards plays backwards.
/// +26 is the current offset, +27 the last, +2C the timer: two updates per frame. The first frame shown is the one after the first (the counter moves before it is shown), the playback
/// ends one update after the last frame has been shown. Verified against the original x86 by tests/fixtures/frame-range.json (tools/verify_frame_range.py; the finishing 9275 is a stub).</summary>
public static class OriginalFrameRange
{
    public static bool Begin(Span<byte> r, ReadOnlySpan<byte> template)
    {
        r[0x26] = r[0x22]; r[0x27] = r[0x23]; r[0x2c] = 1; r[0x2d] = (byte)((r[0x2d] & 0xf0) | 5);
        return Step(r, template);
    }

    /// <returns>true while it plays; false when the range is finished (9275 takes over).</returns>
    public static bool Step(Span<byte> r, ReadOnlySpan<byte> template)
    {
        if (--r[0x2c] != 0) return true;
        byte old = r[0x26]; r[0x26]++;
        if (old == r[0x27]) return false;
        if (old >= r[0x27]) r[0x26] -= 2;
        r[0x2c] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(r[1..], unchecked((ushort)(r[0x26] + BinaryPrimitives.ReadUInt16LittleEndian(template[0x48..]))));
        return true;
    }
}
