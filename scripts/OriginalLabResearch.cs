using System.Buffers.Binary;
namespace Northwind;

/// <summary>World of the laboratory's research: the researchable items of the settlement's tree (A716: id and cost), the random
/// byte (9E41) and the completion of an item (A6AE).</summary>
public interface ILabResearchWorld
{
    IReadOnlyList<(byte Id, uint Cost)> Researchable(byte owner, byte army);
    byte Random();
    void Complete(byte item);
}

/// <summary>LE:1 CCCB (FS 81), the laboratory's research executor. With no research running it picks a random researchable item
/// (record +28 the item, +24 the remaining cost, +15 the flag). Every 256th update (low byte of the global counter [19EA] is zero)
/// it pays 10 · +23 · +22 (byte arithmetic) from the participant's third resource (+20), the unpaid rest reduces the remaining cost
/// as the original's wrapped subtraction does, and at zero the item is completed, +15 drops and the settlement's counter +73 loses 271h.</summary>
public static class OriginalLabResearch
{
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    public static void Tick(ILabResearchWorld world, Span<byte> lab, Span<byte> participant, Span<byte> settlement, byte tickLow)
    {
        if (lab[0x15] == 0)
        {
            var items = world.Researchable(lab[3], lab[0]);
            if (items.Count == 0) return;
            byte count = (byte)items.Count;
            var item = items[(byte)(world.Random() * count >> 8)];
            lab[0x28] = item.Id; W32(lab, 0x24, item.Cost); lab[0x15]++;
        }
        if (tickLow != 0) return;
        uint amount = (ushort)((byte)(10 * lab[0x23]) * lab[0x22]);
        uint gold = U32(participant, 0x20), paid = amount;
        if (gold >= amount) W32(participant, 0x20, gold - amount);
        else { paid = gold - amount; W32(participant, 0x20, 0); lab[0x23] = 1; }
        uint remaining = U32(lab, 0x24);
        W32(lab, 0x24, remaining - paid);
        if (remaining > paid) return;
        world.Complete(lab[0x28]);
        lab[0x15]--;
        if (settlement.Length >= 0x77) W32(settlement, 0x73, U32(settlement, 0x73) - 0x271);
        if (lab.Length >= 0x77 && (int)U32(lab, 0x73) <= 0) W32(lab, 0x73, 0);
    }
}
