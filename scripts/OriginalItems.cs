using System.Buffers.Binary;
namespace Northwind;

/// <summary>The world the item handlers act in: the participants (owner slots) with their inventory, wallet and statistics, the objects of an owner, the random streams and the
/// update counters. Test and game implement it (tests/OriginalItemChecks.cs, SkirmishContainers.cs).</summary>
public interface IItemWorld
{
    byte[] Inventory(int owner);                 // participant +CD, 20 slots
    uint Gold(int owner); void SetGold(int owner, uint value);   // participant +20
    uint Wood(int owner); void SetWood(int owner, uint value);   // participant +18
    void AddGoldStatistics(int owner, uint value); void AddWoodStatistics(int owner, uint value);   // 1EAD8 + 1A08 / 1A0C
    byte Random1(); byte Random2(); byte Random3();              // 9E41, 9E55, 9E69
    byte Tick19E9 { get; } byte Tick19EA { get; } byte Global1EC5 { get; }
    int PlayerOwner { get; } int ParticipantCount { get; } bool ItemsActive { get; }   // [1972], [6C6], [1C4E]
    IReadOnlyList<ItemObject> Objects(int owner);                // the owner's object records (participant +0E .. +09)
    void FleeRequest(byte[] record);             // 8E0E
    void Message(); void Sound(byte id);         // 5593, A4A9
}
public sealed record ItemObject(byte[] Record, bool Structure);

/// <summary>The item machinery of LE:1 (docs/campaign-items.md): the per-update loop 12383, the pickup 1243A, the search 124D8 and the handlers of the item table (LE:3 +4191).
/// Verified against the original x86 by tests/fixtures/items.json (tools/verify_items.py). Handlers with AH = 3 (use), the stones 12/16/19/20/35, the staff 8 and the lightnings
/// 31..33 are not ported yet.</summary>
public sealed class OriginalItemRules
{
    public const int Slots = 20, TableEntries = 36, EntrySize = 0x19, StateOffset = 0x15;
    /// <summary>The runtime part of the item table entries: +15 (dword: the countdown of a curse; stones keep +15 = [1EC5] and +16 = the owner).</summary>
    public byte[][] Entries { get; } = Enumerable.Range(0, TableEntries).Select(_ => new byte[EntrySize]).ToArray();
    private readonly Func<int, string> _handler;
    public OriginalItemRules(Func<int, string> handlerOfItem) => _handler = handlerOfItem;

    /// <summary>12383: every update each held item gets AH = 1.</summary>
    public void Update(IItemWorld world)
    {
        if (!world.ItemsActive) return;
        for (int owner = 0; owner != world.ParticipantCount; owner++)
        {
            var inventory = world.Inventory(owner);
            for (int slot = 0; slot < Slots; slot++) if (inventory[slot] != 0) Handle(world, inventory[slot], 1, owner, slot, 0);
        }
    }

    /// <summary>1243A: the handler of the item (AH = 2) consumes it (returns false, ZF) or lets it be kept in the first free slot. Result: AL and ZF as the original leaves them.</summary>
    public (byte Al, bool Zf) Pickup(IItemWorld world, byte item, int owner)
    {
        if (item == 0) return (0, true);
        var (al, zf) = Handle(world, item, 2, owner, 0, item);
        if (zf) return (0, true);
        var inventory = world.Inventory(owner);
        int slot = Array.IndexOf(inventory, (byte)0);
        if (slot < 0) return (0xff, false);
        inventory[slot] = item;
        if (owner == world.PlayerOwner) { world.Message(); world.Sound(0x13); }
        return (0, true);
    }

    /// <summary>124D8: the slot holding the item (or −1).</summary>
    public static int Find(IItemWorld world, byte item, int owner) => Array.IndexOf(world.Inventory(owner), item);

    private (byte Al, bool Zf) Handle(IItemWorld world, byte item, int ah, int owner, int slot, byte incoming)
    {
        var entry = Entries[item];
        switch (_handler(item))
        {
            case "11EB0" when ah == 2: world.SetGold(owner, world.Gold(owner) + 100); world.AddGoldStatistics(owner, 100); return (0, true);
            case "11ECB" when ah == 2: world.SetWood(owner, world.Wood(owner) + 100); world.AddWoodStatistics(owner, 100); return (0, true);
            case "11EE6" when ah == 2: world.SetGold(owner, world.Gold(owner) + ((uint)world.Random1() << 5) + 0x1388); return (0, true);
            case "11EFF" when ah == 2:   // manuscripts: the numbers of the pages (code − 17h) are merged into one slot
            {
                var inventory = world.Inventory(owner);
                for (int i = 0; i < Slots; i++)
                    if (inventory[i] is >= 0x18 and <= 0x1e)
                    {
                        inventory[i] = (byte)(((byte)(inventory[i] - 0x17) | (byte)(incoming - 0x17)) + 0x17); return (0, true);
                    }
                return (0xff, false);
            }
            case "11F5B" when ah == 2: return (0xff, false);
            case "11F64" or "11FC8" or "12022":
                if (ah == 2) { BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(StateOffset), 10000); return (0xff, false); }
                if (ah != 1) return (0, true);
                uint timer = unchecked(BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(StateOffset)) - 1);
                BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(StateOffset), timer);
                if (timer == 0) { world.Inventory(owner)[slot] = 0; return (0, true); }
                switch (_handler(item))
                {
                    case "11F64":   // the curse of Rufin: every 256 updates each unit of the owner has a one in four chance to run to a random cell
                        if (world.Tick19E9 != 0) break;
                        foreach (var o in world.Objects(owner)) if (!o.Structure && (world.Random3() & 3) == 0) world.FleeRequest(o.Record);
                        break;
                    case "11FC8":   // of Onis: every 256 updates the experience (+16) of the units drops to zero
                        if (world.Tick19EA != 0) break;
                        foreach (var o in world.Objects(owner)) if (!o.Structure) o.Record[0x16] = 0;
                        break;
                    default:        // of Irvil: every 256 updates 100 gold are lost
                        if (world.Tick19EA != 0) break;
                        world.SetGold(owner, world.Gold(owner) < 100 ? 0 : world.Gold(owner) - 100); break;
                }
                return (0, true);
            case "1217E" or "121E5":   // stones 6 and 14: the table remembers the update counter and the owner
                if (ah == 1) { entry[0x15] = world.Global1EC5; entry[0x16] = (byte)owner; }
                else if (ah == 2) return (0xff, false);
                return (0, true);
        }
        return (0, true);
    }
}
