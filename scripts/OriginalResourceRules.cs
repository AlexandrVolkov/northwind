using System.Buffers.Binary;
namespace Northwind;

/// <summary>Four resource slots in native order: metal, wood, gold, free people.</summary>
public static class OriginalResourceRules
{
    // EXE 2F03..2F1B copies SCN +15,+19,+1D,+21 into owner +18..+24.
    public static ResourceQuad ReadScenario(ReadOnlySpan<byte> slot)
    {
        if (slot.Length != 114) throw new InvalidDataException("Expected a complete 114-byte SCN slot");
        return Read(slot, 0x15);
    }

    // EXE A8BB..A90F (recruitment) and B083..B0E9 (construction).
    public static ResourceQuad ReadPrice(ReadOnlySpan<byte> template)
    {
        if (template.Length != 171) throw new InvalidDataException("Expected a complete 171-byte DAT template");
        return Read(template, 0x7c);
    }

    private static ResourceQuad Read(ReadOnlySpan<byte> bytes, int offset) => new(
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]),
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]),
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 8)..]),
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 12)..]));
}
