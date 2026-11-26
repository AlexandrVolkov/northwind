using System.Buffers.Binary;
namespace Northwind;

public interface IContainerServices
{
    void Redraw();                                                       // FS:1D (81D9)
    void Effect(byte army, byte type, byte layer, uint x, uint y);       // FS:19 (9727)
    void Remove();                                                       // FS:15 (98CC)
}

/// <summary>The scripts of jugs and chests of ARMY__03.WRK (docs/campaign-items.md): jug 0xE4 (and 0x2B63 / 0x2C6D, which differ only in the item), chest 0x71A (0x7CE / 0x7DF).
/// A whole object (condition 100) is broken by damage (condition below 100): the picture opens frame by frame (state 1), stays open (state 2) until a visitor takes the
/// item (AH = 8, state 2 only); a jug then falls apart, a chest stays open and empty (state 3) and loses a condition point every 50 updates. Verified against the original x86
/// by tests/fixtures/container.json (tools/verify_container.py; the FS services are recorded stubs).</summary>
public static class OriginalContainer
{
    public const byte WholeCondition = 0x64;
    public const ushort JugAnimationEnd = 0x11b, JugOpenFrame = 0x112, ChestOpenEmptyFrame = 0x127;
    public const byte JugOpenTimer = 0xce, ChestEmptyDecay = 0x32, OpenedFlag = 0x80;
    public enum Kind { Jug, Chest, Chest10, Chest11 }

    /// <summary>AH = 0 (EDI = the record): the item (+31) and the full condition.</summary>
    public static void Initialize(byte[] record, Kind kind) { record[0x31] = kind switch { Kind.Chest10 => (byte)0x0c, Kind.Chest11 => (byte)0x0e, _ => (byte)6 }; record[0x19] = WholeCondition; }

    /// <summary>AH = 1, one update. <paramref name="frameBase"/> is DAT[48] of the chest.</summary>
    public static void Step(byte[] record, ushort frameBase, bool chest, IContainerServices services)
    {
        if ((sbyte)record[0x19] <= 0)
        {
            record[0x19]--;
            if (record[0x19] > 0xfa) return;   // the object lingers six updates, then the dust effect (jugs) and the removal
            if (!chest) services.Effect(record[0], 7, 0, BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4)), BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8)));
            services.Remove(); return;
        }
        if ((record[0x14] & OpenedFlag) != 0) { record[0x14] = 0; services.Redraw(); }
        if (record[0x14] == 0)
        {
            if ((sbyte)record[0x19] >= (sbyte)WholeCondition) return;
            record[0x14] = 1; record[0x15] = 0;
            if (!chest) Frame(record, Frame(record) + 1);
        }
        if (chest)
        {
            if (record[0x14] == 1)
            {
                if ((sbyte)++record[0x15] < 2) return;
                record[0x15] = 0; Frame(record, Frame(record) + 1);
                if (Frame(record) < (ushort)(frameBase + 0x0a)) return;
                record[0x14] = 2;
            }
            if (record[0x14] == 3 && --record[0x15] == 0) { record[0x19]--; record[0x15] = ChestEmptyDecay; }
            return;
        }
        if ((sbyte)++record[0x15] < 2) return;
        record[0x15] = 0; Frame(record, Frame(record) + 1);
        if (Frame(record) >= JugAnimationEnd) { Frame(record, JugOpenFrame); record[0x15] = JugOpenTimer; record[0x14] = 2; }
    }

    /// <summary>AH = 8 (ESI = the container): AL and ZF as the script leaves them. ZF set: AL is the item code of the visitor; otherwise AL = state + 1 (1 whole: the visitor is sent to it, 2 opening).</summary>
    public static (byte Al, bool Zf) Visit(byte[] record, bool chest)
    {
        if ((byte)(record[0x14] + 1) != 3) return ((byte)(record[0x14] + 1), false);
        byte item = record[0x31]; record[0x31] = 0;
        if (chest) { record[0x14] = 3; Frame(record, ChestOpenEmptyFrame); }
        else record[0x19] = 0;
        return (item, true);
    }

    private static ushort Frame(byte[] record) => BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(1));
    private static void Frame(byte[] record, int value) => BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1), (ushort)value);
}
