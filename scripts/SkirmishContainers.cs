using System.Buffers.Binary;
using Godot;
namespace Northwind;

public sealed class OriginalItemData { public List<OriginalItem> Items { get; set; } = []; }
public sealed class OriginalItem { public int Code { get; set; } public int Icon { get; set; } public string Handler { get; set; } = ""; public string Name { get; set; } = ""; public int Class { get; set; } public int Parameter { get; set; } }

/// <summary>Jugs and chests of the first map (ARMY__03 1, 9, 10, 11, 30, 33; docs/campaign-items.md): hit by anyone of the player they break open
/// (<see cref="OriginalContainer"/>), then a visiting unit takes the item (LE:1 902F → 12406 → 1243A): gold items (code 34, handler 11EE6) pay 5000 + 32·random into the
/// wallet, the others go into the 20 slots of the participant's inventory (+CD). ADAPTERS: the visit is a walk to the object and a range of 56 pixels instead of 9B86/8C1E; the
/// effects of the stones, staffs and manuscripts (handlers 11F5B…122D6) are not ported — the items are only kept; the dust effect of a broken jug is not drawn.</summary>
public sealed partial class Skirmish
{
    public const int InventorySlots = 20, VisitRange = 56;
    private OriginalItemData? _itemData;
    public IReadOnlyList<OriginalItem> ItemTable => (_itemData ??= GameData.Read<OriginalItemData>("res://data/original_items.json")).Items;
    /// <summary>The player's inventory (participant +CD, 20 bytes of item codes; 0 is free).</summary>
    public byte[] Inventory { get; } = new byte[InventorySlots];   // participant +CD
    public string ItemName(int code) => code > 0 && code < ItemTable.Count ? ItemTable[code].Name : "";
    public static bool IsContainer(SkirmishActor actor) => actor.Scenery && actor.Source.TemplateFile == "ARMY__03.DAT" && actor.Source.TemplateIndex is 1 or 9 or 10 or 11 or 30 or 33;
    private sealed class ContainerServices : IContainerServices { public bool Removed; public void Redraw() { } public void Remove() => Removed = true; public void Effect(byte army, byte type, byte layer, uint x, uint y) { } }   // the dust effect (type 7) is not drawn
    private static bool IsChest(SkirmishActor actor) => actor.Source.TemplateIndex is 9 or 10 or 11;

    private void TickContainers()
    {
        if (_scenery.Count == 0) return;
        foreach (var container in _scenery.Where(IsContainer).ToArray())
        {
            var services = new ContainerServices(); OriginalContainer.Step(container.Record, BinaryPrimitives.ReadUInt16LittleEndian(container.Parameters.AsSpan(0x48)), IsChest(container), services);
            if (!services.Removed) continue;
            Occupancy.ClearReference(container.Id); Actors.Remove(container); _scenery.Remove(container);
            foreach (var unit in Actors.Where(a => a.VisitTarget == container.Id)) unit.VisitTarget = 0;
        }
        foreach (var unit in Actors.Where(a => a.VisitTarget != 0 && a.Alive).ToArray())
        {
            if (Find(unit.VisitTarget) is not { } box || !IsContainer(box)) { unit.VisitTarget = 0; continue; }
            if (unit.Position.DistanceTo(box.Position) > VisitRange) continue;
            if (OriginalContainer.Visit(box.Record, IsChest(box)) is { Zf: true, Al: not 0 } taken) { PickUp(unit, taken.Al); unit.VisitTarget = 0; }
            else if (box.Record[0x14] is not (0 or 1)) unit.VisitTarget = 0;   // empty or spent: nothing to wait for
        }
    }

    /// <summary>A right click of the player on a jug or chest: the selected units walk to it and take its item once it is open.</summary>
    public bool Visit(IEnumerable<uint> ids, uint containerId)
    {
        if (Paused || Outcome != "" || Find(containerId) is not { } box || !IsContainer(box)) return false;
        var units = ids.Distinct().Select(Find).OfType<SkirmishActor>().Where(a => a.Team == 0 && !a.Structure && !a.Worker && a.Alive).ToArray();
        if (units.Length == 0) return false;
        foreach (var unit in units) unit.VisitTarget = containerId;
        Move(units.Select(u => u.Id), box.Cell);
        foreach (var unit in units) unit.VisitTarget = containerId;   // Move clears the unit's orders
        return true;
    }

    private OriginalItemRules? _itemRules; private SkirmishItemWorld? _itemWorld;
    public OriginalItemRules ItemRules => _itemRules ??= new OriginalItemRules(code => ItemTable[code].Handler);
    private SkirmishItemWorld ItemWorld => _itemWorld ??= new SkirmishItemWorld(this);
    private void TickItems() { if (Inventory.Any(i => i != 0)) ItemRules.Update(ItemWorld); }

    /// <summary>1243A (<see cref="OriginalItemRules.Pickup"/>): the handler of the item takes it (gold, cargo) or the item goes into the first free slot of the participant.</summary>
    public bool PickUp(SkirmishActor unit, byte item)
    {
        if (unit.Team != 0 || item == 0 || item >= ItemTable.Count) return false;
        uint gold = Gold; var before = Inventory.ToArray();
        var (al, _) = ItemRules.Pickup(ItemWorld, item, Rules.PlayerSlot);
        if (Gold > gold) Log($"Найдено золото: {Gold - gold}.");
        else if (al == 0xff) Log("Инвентарь полон.");
        else if (!Inventory.SequenceEqual(before)) Log($"Подобран предмет: {ItemName(item)}.");
        return al != 0xff;
    }

    /// <summary>The participants as the item handlers see them: the player's wallet and inventory, the objects of an owner, the streams of random numbers.</summary>
    private sealed class SkirmishItemWorld(Skirmish battle) : IItemWorld
    {
        private readonly byte[][] _inventories = Enumerable.Range(0, 8).Select(_ => new byte[InventorySlots]).ToArray();
        public byte[] Inventory(int owner) => owner == battle.Rules.PlayerSlot ? battle.Inventory : _inventories[owner & 7];
        public uint Gold(int owner) => owner == battle.Rules.PlayerSlot ? battle.Gold : 0;
        public void SetGold(int owner, uint value) { if (owner == battle.Rules.PlayerSlot) battle.Gold = value; }
        public uint Wood(int owner) => owner == battle.Rules.PlayerSlot ? battle.Wood : 0;
        public void SetWood(int owner, uint value) { if (owner == battle.Rules.PlayerSlot) battle.Wood = value; }
        public void AddGoldStatistics(int owner, uint value) { }   // the statistics screen is not ported
        public void AddWoodStatistics(int owner, uint value) { }
        public byte Random1() => battle._terrainCatalog.NextRandomByte(); public byte Random2() => battle._terrainCatalog.NextSecondaryByte(); public byte Random3() => battle.SoundRandom();
        public byte Tick19E9 => (byte)battle._globalTick; public byte Tick19EA => (byte)battle._globalTick; public byte Global1EC5 => 0;   // ADAPTER: both counters are the update count, [1EC5] is unknown
        public int PlayerOwner => battle.Rules.PlayerSlot; public int ParticipantCount => 8; public bool ItemsActive => true;
        public IReadOnlyList<ItemObject> Objects(int owner) => battle.Actors.Where(a => a.Alive && !a.Scenery && a.Record[3] == owner).Select(a => new ItemObject(a.Record, a.Structure)).ToList();
        public void FleeRequest(byte[] record) => OriginalCombatRules.Flee(record, (byte)battle.Map.Width, (byte)battle.Map.Height, battle._terrainCatalog);
        public void Message() { }
        public void Sound(byte id) { }
    }
}
