using System.Buffers.Binary;
namespace Northwind;

/// <summary>The scripts of jugs and chests of ARMY__03.WRK (docs/campaign-items.md): jug 0xE4 (and 0x2B63 / 0x2C6D, which differ only in the item), chest 0x71A (0x7CE / 0x7DF).
/// A whole object (condition 100) is broken by damage (condition below 100): the picture opens frame by frame (state 1), stays open (state 2) until a visitor takes the
/// item (AH = 8, state 2 only); a jug then falls apart, a chest stays open and empty (state 3) and loses a condition point every 50 updates.
/// FS services used by the scripts are only the pointer set-up (6877), the redraw (81D9) and the removal (98CC) — the removal is the caller's job (<see cref="Result.Remove"/>).</summary>
public static class OriginalContainer
{
    public const byte WholeCondition = 0x64;
    public const ushort JugAnimationEnd = 0x11b, JugOpenFrame = 0x112, ChestOpenEmptyFrame = 0x127;
    public const byte JugOpenTimer = 0xce, ChestEmptyDecay = 0x32, OpenedFlag = 0x80;
    public enum Result { None, Remove }

    /// <summary>AH = 0: the item (+31) and the full condition.</summary>
    public static void Initialize(byte[] record, byte item) { record[0x31] = item; record[0x19] = WholeCondition; }

    /// <summary>AH = 1, one update.</summary>
    public static Result Step(byte[] record, byte[] parameters, bool chest)
    {
        if ((sbyte)record[0x19] <= 0)
        {
            record[0x19]--;
            return record[0x19] > 0xfa ? Result.None : Result.Remove;   // the object lingers six updates, then 9727 (a dust effect, type 7) and 98CC
        }
        if ((record[0x14] & OpenedFlag) != 0) record[0x14] = 0;   // 1D is the redraw
        if (record[0x14] == 0)
        {
            if (record[0x19] >= WholeCondition) return Result.None;
            record[0x14] = 1; record[0x15] = 0;
            if (!chest) Frame(record, Frame(record) + 1);
        }
        if (chest)
        {
            if (record[0x14] == 1)
            {
                if (++record[0x15] < 2) return Result.None;
                record[0x15] = 0; Frame(record, Frame(record) + 1);
                if (Frame(record) < BinaryPrimitives.ReadUInt16LittleEndian(parameters.AsSpan(0x48)) + 0x0a) return Result.None;
                record[0x14] = 2;
            }
            if (record[0x14] == 3 && --record[0x15] == 0) { record[0x19]--; record[0x15] = ChestEmptyDecay; }
            return Result.None;
        }
        if (++record[0x15] < 2) return Result.None;
        record[0x15] = 0; Frame(record, Frame(record) + 1);
        if (Frame(record) >= JugAnimationEnd) { Frame(record, JugOpenFrame); record[0x15] = JugOpenTimer; record[0x14] = 2; }
        return Result.None;
    }

    /// <summary>AH = 8: the item code the visitor receives (0: none). Only an open object (state 2) gives it.</summary>
    public static byte Visit(byte[] record, bool chest)
    {
        if (record[0x14] != 2) return 0;
        byte item = record[0x31]; record[0x31] = 0;
        if (chest) { record[0x14] = 3; Frame(record, ChestOpenEmptyFrame); }
        else record[0x19] = 0;
        return item;
    }
    /// <summary>The result of the call for a visitor: 1 when the object is whole (the visitor is sent to it), 2 while it opens, 0 otherwise (902F → 8C7C).</summary>
    public static int VisitCode(byte[] record) => record[0x14] == 0 ? 1 : record[0x14] == 1 ? 2 : 0;

    private static ushort Frame(byte[] record) => BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(1));
    private static void Frame(byte[] record, int value) => BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1), (ushort)value);
}
