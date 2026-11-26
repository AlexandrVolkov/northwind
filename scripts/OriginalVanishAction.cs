using System.Buffers.Binary;
namespace Northwind;

/// <summary>State 3 of the mages and their phantoms: FS:105 (LE:1 878D), the vanishing action. At rest it starts the frame range after the death block — DAT[78] frames from
/// 8·(4A+4B) + 4·ΣDAT[4F..52] — through 8D0B (request 5 in the high nibble of +2D, the range in +22/+23): forwards when +32 = 0 (the mage vanishes, +15 = 1), backwards otherwise
/// (he returns, +15 = 2). A vanishing mage is done when the request is gone (the range has played), +32 = FF and +17 |= 200h; a returning one is done as soon as the playback has
/// begun (+2D low nibble 5): +32 = 0 and the visibility mask +20 = FF — he is visible while the animation still runs. ADAPTER: the rest of 9210 (the idle frame after the range) is
/// <see cref="Motion"/>; the whole of 878D is not verified by Unicorn.</summary>
public static class OriginalVanishAction
{
    /// <returns>true when the action is done (AL = 0), false while it goes on.</returns>
    public static bool Step(Span<byte> r, ReadOnlySpan<byte> template)
    {
        int current = r[0x2d] & 15, requested = r[0x2d] >> 4;
        switch (r[0x15])
        {
            case 0:
                if (current != 0 || requested != current) { r[0x2d] &= 0x0f; return false; }   // 8C98
                r[0x25] = 0x24;
                byte first = (byte)((byte)(template[0x4a] + template[0x4b]) << 3), shift = (byte)((byte)(template[0x4f] + template[0x50] + template[0x51] + template[0x52]) << 2);
                first += shift; byte last = (byte)(template[0x78] - 1 + first);
                if (r[0x32] != 0) { r[0x15] = 2; (first, last) = (last, first); } else r[0x15] = 1;
                r[0x22] = first; r[0x23] = last; r[0x2d] = (byte)((r[0x2d] & 0x0f) | 0x50);   // 8D0B
                return false;
            case 1:
                if (requested == 5) return false;
                r[0x32] = 0xff; r[0x18] |= 0x02; return true;   // +17 |= 200h
            case 2:
                if (current != 5) return false;
                r[0x32] = 0; r[0x20] = 0xff; return true;
            default: return true;
        }
    }

    /// <summary>The slice of the unit machine 9210 these actions need: state 0 with a request of 5 begins the frame range, state 5 plays it, and at its end the request is cleared (9275)
    /// and the standing frame of the heading is shown (92CA).</summary>
    public static class Motion
    {
        public static void Step(Span<byte> r, ReadOnlySpan<byte> template)
        {
            int current = r[0x2d] & 15, requested = r[0x2d] >> 4;
            if (current == 0 && requested == 5) { r[0x24] = 0; OriginalFrameRange.Begin(r, template); return; }
            if (current != 5) return;
            if (OriginalFrameRange.Step(r, template)) return;
            r[0x2d] = 0; r[0x24] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(r[1..], unchecked((ushort)((r[0x25] & 7) * template[0x4a] + BinaryPrimitives.ReadUInt16LittleEndian(template[0x48..]))));
        }
    }
}
