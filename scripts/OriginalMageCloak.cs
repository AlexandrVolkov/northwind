using System.Buffers.Binary;
namespace Northwind;

public interface IMageWorld { void DropItems(byte cellX, byte cellY, byte owner); }   // FS:197 = 12588

/// <summary>The continuation of the mages' update (ARMY__03.WRK: Rufin D3D..DA6; the first mage 79..C5; Onis, Irvil and the others have the same code with their own thresholds):
/// the code that follows the table of FS:E1 (the unit step, LE:1 8227, which returns to the byte after the table). A mage hurt below <c>low</c> with the charge (+31) of at least 10
/// asks the unit machine for state 3 — FS:105 (878D), the vanishing action — by setting the pending bit and the next state in +14 (0x58); the action ends with +32 = FF (hidden)
/// and the visibility mask +20 = 0 (nobody sees or targets him); hidden, he loses one point of charge every 128 updates (the gate is the counter +2E) and asks for the return once
/// his health is above <c>high</c> (the action then runs backwards and ends with +32 = 0, +20 = FF); visible, he gains a point of charge every 128 updates. A dead Rufin drops his
/// items. Verified against the original x86 by tests/fixtures/mage-cloak.json (tools/verify_mage_cloak.py).</summary>
public static class OriginalMageCloak
{
    public const byte RufinLow = 0x3c, RufinHigh = 0x5a, FirstMageLow = 0x1e, FirstMageHigh = 0x2d;

    /// <returns>AL as the original leaves it.</returns>
    public static byte Step(IMageWorld world, byte[] r, byte low, byte high, bool dropsItems, byte al = 0x55, byte gate = 0x7f)
    {
        if (dropsItems && (sbyte)r[0x19] <= 0)
        {
            byte cellY = (byte)(BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(8)) >> 5);
            world.DropItems((byte)(BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(4)) >> 5), cellY, r[3]);
            al = cellY;   // EAX held the cell row
        }
        if (r[0x32] == 0)   // visible
        {
            if (!(r[0x31] >= 0x0a && r[0x19] < low))
            {
                if ((r[0x2e] & gate) != 0) return al;
                r[0x31]++; if ((sbyte)r[0x31] < 0) r[0x31] = 0x7f;   // inc … jns
                return al;
            }
            return Request(r);
        }
        r[0x20] = 0;        // hidden: seen by nobody
        if ((r[0x2e] & gate) != 0) return al;
        if (r[0x19] <= high) { r[0x31]--; if ((sbyte)r[0x31] >= 0) return al; r[0x31] = 0; }   // dec … jns; the exhausted charge asks for the return, too
        return Request(r);
    }

    /// <summary>The continuation of Kevin, Miron, Garik, Efil and Kochet (e.g. 1A20): the dead hermit drops his items; a hermit with the bit 0 of +17 set (hit — set by the unit machine) asks for the vanishing
    /// action, with no thresholds and no charge. Verified by tests/fixtures/mage-cloak.json.</summary>
    public static byte FlagStep(IMageWorld world, byte[] r, byte al = 0x55)
    {
        if ((sbyte)r[0x19] <= 0)
        {
            byte cellY = (byte)(BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(8)) >> 5);
            world.DropItems((byte)(BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(4)) >> 5), cellY, r[3]);
            al = cellY;
        }
        return (r[0x17] & 1) == 0 ? al : Request(r);
    }

    /// <summary>D96: ask for state 3 unless the mage is in it.</summary>
    private static byte Request(byte[] r)
    {
        byte state = (byte)(r[0x14] & 7);
        if (state == 3) return 3;
        r[0x14] = (byte)(state | 0x58); return 0;
    }
}
