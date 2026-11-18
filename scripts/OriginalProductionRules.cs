using System.Buffers.Binary;
namespace Northwind;

public readonly record struct ProductionExit(uint X, uint Y, byte Layer);

/// <summary>Bounded native queue, timing, exit and refund operations; not the complete WRK dispatcher.</summary>
public static class OriginalProductionRules
{
    public const int Capacity = 5;
    // EXE 62FE and 6330..6341. Valid queues are packed, with FF for unused slots.
    public static bool Enqueue(Span<byte> producer, byte template)
    {
        if (template == 0xff) throw new ArgumentOutOfRangeException(nameof(template));
        if (producer[0x26] != 0xff) return false;
        int slot = producer.Slice(0x22, Capacity).IndexOf((byte)0xff);
        producer[0x22 + slot] = template; return true;
    }
    // A90F..A91F, entered after a successful four-slot debit.
    public static void StartPaid(Span<byte> producer)
    { producer[0x15] = unchecked((byte)(producer[0x15] + 1)); producer[0x28] = producer[0x27] = 0; producer[0x0f] = 1; }

    // A934..A95C, AA5A..AA64. Ready means attempt the exit now, not unconditional creation.
    public static bool Tick(Span<byte> producer, byte interval, byte speedShift, byte limit)
    {
        int threshold = interval >> (speedShift & 31);
        producer[0x28] = unchecked((byte)(producer[0x28] + 1));
        bool ready = false;
        if (producer[0x28] >= threshold)
        {
            producer[0x28] = 0;
            if (producer[0x27] < limit) producer[0x27]++;
            else ready = true;
        }
        // A962 is a caller boundary. AA5D writes this only on a waiting return.
        if (!ready) producer[0x0f] = 1;
        return ready;
    }
    // AA36..AA64: shift the four following slots and reset phase for the next payment.
    public static void Complete(Span<byte> producer)
    {
        producer.Slice(0x23, 4).CopyTo(producer[0x22..]); producer[0x26] = 0xff; producer[0x15] = 0;
        if (producer[0x22] != 0xff) producer[0x0f] = 1;
        else { producer[0x0f] = 0x24; producer[0x14] &= 0xf8; producer[0x27] = 0; }
    }
    // A962..A99F. Large footprint uses the second offset pair; coordinates wrap as u32.
    public static ProductionExit Exit(ReadOnlySpan<byte> producer, ReadOnlySpan<byte> building, ReadOnlySpan<byte> unit)
    {
        int offset = Math.Max(unit[3], unit[4]) < 2 ? 0x53 : 0x55;
        return new(unchecked(BinaryPrimitives.ReadUInt32LittleEndian(producer[4..]) + (uint)(sbyte)building[offset]),
            unchecked(BinaryPrimitives.ReadUInt32LittleEndian(producer[8..]) + (uint)(sbyte)building[offset + 1]), (byte)(unit[0x77] & 3));
    }
    // AA76..AAB1. Native byte MUL/DIV preserve the upper half of each dword cost.
    // Supported domain excludes native #DE (zero divisor or quotient > 255).
    public static ResourceQuad Refund(ResourceQuad balances, ResourceQuad costs, byte progress, byte limit)
    {
        uint Part(uint cost)
        {
            if (limit == 0) throw new DivideByZeroException("Native production refund has a zero divisor");
            uint quotient = (cost & 255) * progress / limit;
            if (quotient > 255) throw new OverflowException("Native production refund would raise #DE");
            return (cost & 0xffff0000) | quotient;
        }
        return new(unchecked(balances.First + Part(costs.First)), unchecked(balances.Second + Part(costs.Second)),
            unchecked(balances.Third + Part(costs.Third)), unchecked(balances.Fourth + costs.Fourth));
    }
}
