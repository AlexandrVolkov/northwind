using System.Buffers.Binary;
namespace Northwind;

/// <summary>The services of the bridge builders (recorded stubs in tools/verify_bridge_builder.py).</summary>
public interface IBridgeBuilderWorld
{
    /// <summary>AC63: the plane check of the place (bl = 2 for raising, 1 for lowering): Free (ZF), else Limit (CF clear: the unit may not go on) with the object in the way.</summary>
    (bool Free, bool Blocked, byte[] Blocker) Place(byte template, byte army, byte mode, uint x, uint y);
    void Occupancy(uint reference);      // 43B0: 0 releases the footprint, the unit's reference registers it
    bool SearchEnemy(byte mode);         // D895: true when an enemy is found
    uint Reference { get; }              // the unit's reference (ESI − [6AA])
}

/// <summary>The actions of the units that raise and lower a bridge, LE:1 D5B1 (raise one step), D61D (lower one step) and D689 (the update). The step counter +22 runs to 20h (the height of the
/// bridge, +0C follows it); at the middle (10h) the place is checked and the footprint moves to the next plane (+0D); a blocked step waits 1Eh updates and hurts the object in the way by 10;
/// +15 is the delay between the steps. D689: the unit rests until an enemy is near (+23: 0 look every 32 updates, 1 raising, 2 lowering). Verified against the original x86 by
/// tests/fixtures/bridge-builder.json (tools/verify_bridge_builder.py).</summary>
public static class OriginalBridgeBuilder
{
    private static void Alarm(byte[] r) => BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x17), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x17)) | 0x200));

    /// <returns>BL: 0 going on, 40h done.</returns>
    public static byte Raise(byte[] r, IBridgeBuilderWorld w) => Move(r, w, 2, +1, 0x20);
    public static byte Lower(byte[] r, IBridgeBuilderWorld w) => Move(r, w, 1, -1, 0);

    private static byte Move(byte[] r, IBridgeBuilderWorld w, byte mode, int direction, int limit)
    {
        if (r[0x15] != 0) { r[0x15]--; return 0; }
        if (r[0x22] == 0x10)
        {
            var (free, blocked, blocker) = w.Place(r[0x0e], r[0], mode, BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(8)));
            if (free)
            {
                w.Occupancy(0); r[0x0d] = (byte)(r[0x0d] + direction); w.Occupancy(w.Reference); Alarm(r);
            }
            else if (!blocked) return 0x40;
            else
            {
                r[0x15] = 0x1e; blocker[0x19] = blocker[0x19] >= 0x0a ? (byte)(blocker[0x19] - 0x0a) : (byte)0; return 0;
            }
        }
        if (direction > 0 ? r[0x22] >= limit : r[0x22] == 0) return 0x40;
        r[0x0c] = (byte)(r[0x0c] + direction); r[0x22] = (byte)(r[0x22] + direction); r[0x15] = (byte)(direction > 0 ? 3 : 1);
        return 0;
    }

    /// <returns>BL after the update.</returns>
    public static byte Step(byte[] r, IBridgeBuilderWorld w, Func<bool> searchEnemy)
    {
        r[0x0f] &= 0xef; byte bl = 0;
        if (r[0x23] == 0)
        {
            r[0x15]++;
            if ((r[0x15] & 0x1f) != 0) return bl;
            if (searchEnemy()) r[0x23] = 2; else r[0x23] = 1;
            r[0x15] = 0; return bl;
        }
        if (r[0x23] == 1) { r[0x0f] = 8; bl = Raise(r, w); }
        else { r[0x0f] = 4; bl = Lower(r, w); }
        if ((bl & 0x40) != 0) r[0x23] = 0;
        return bl;
    }
}
