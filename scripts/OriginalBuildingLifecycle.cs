using System.Buffers.Binary;
namespace Northwind;

public readonly record struct BuildingActionState(byte EncodedState, byte Auxiliary, byte WorkFlags);

/// <summary>Bounded original command transitions and the fresh 49-byte castle shape.</summary>
public static class OriginalBuildingLifecycle
{
    public const int CastleRecordBytes = 49;

    // EXE 61C7..61D3, 623D..624F: six command slots, single object, mode 0.
    public static byte QueueDirectCommand(byte encodedState, int slot)
    {
        if (slot is < 0 or >= 6) throw new ArgumentOutOfRangeException(nameof(slot));
        return (byte)((encodedState & 7) | 0x40 | (slot << 3));
    }

    public static byte QueueDirectCommand(byte encodedState, TemplateCommand command)
    {
        if (!command.Present || command.Mode != 0)
            throw new ArgumentException("Only a present direct-mode command can be queued", nameof(command));
        return QueueDirectCommand(encodedState, command.Slot);
    }

    // EXE C8C9..C8DF, entered only when the current handler returned ZF=1.
    public static BuildingActionState CompleteCurrentAction(BuildingActionState state)
    {
        byte next = (byte)((state.EncodedState >> 3) & 7);
        return new(next, 0, next == 1 ? (byte)1 : state.WorkFlags);
    }

    // Empty-chain path of A758..A86F plus the real castle WRK AH=0 callback.
    // Allocation ownership, placement and upfront payment belong to the caller.
    public static byte[] CreateFreshCastleRecord(byte armySlot, byte owner, byte templateIndex,
        uint x, uint y, byte layer, ushort initialFrame, byte initialCondition)
    {
        var record = new byte[CastleRecordBytes];
        record[0] = armySlot;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1), initialFrame);
        record[3] = owner;
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), x);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), y);
        record[0x0d] = layer;
        record[0x0e] = templateIndex;
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x10), CastleRecordBytes);
        record[0x14] = 0x80;
        record[0x19] = initialCondition;
        record[0x1a] = initialCondition;
        record[0x20] = 0xff;
        // WRK FE..125, AH=0: fields 22..27 are sentinel FF bytes.
        record.AsSpan(0x22, 6).Fill(0xff);
        return record;
    }

    // EXE B16A..B175. Leaves WorkFlags unchanged; a fresh record has zero there.
    public static void MarkConstruction(Span<byte> record)
    {
        if (record.Length < 0x1b) throw new ArgumentException("Object prefix is truncated", nameof(record));
        record[0x14] = 0x81;
        record[0x19] = 1;
        record[0x1a] = 1;
    }
}
