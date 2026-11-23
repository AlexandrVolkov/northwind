using System.Buffers.Binary;
using Godot;
namespace Northwind;

public sealed class OriginalItemData { public List<OriginalItem> Items { get; set; } = []; }
public sealed class OriginalItem { public string StaticHex { get; set; } = ""; public int Code { get; set; } public int Icon { get; set; } public string Handler { get; set; } = ""; public string Name { get; set; } = ""; public int Class { get; set; } public int Parameter { get; set; } }

/// <summary>Jugs and chests of the first map (ARMY__03 1, 9, 10, 11, 30, 33; docs/campaign-items.md): hit by anyone of the player they break open
/// (<see cref="OriginalContainer"/>), then a visiting unit takes the item (LE:1 902F → 12406 → 1243A): gold items (code 34, handler 11EE6) pay 5000 + 32·random into the
/// wallet, the others go into the 20 slots of the participant's inventory (+CD). The visit is the original 902F (OriginalVisit) on a copy of the unit record, the walk to the object is an adapter; the
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
            if (unit.Stepping) continue;
            // 902F on a copy of the unit's record: the original's movement requests (+2D, +22) belong to its own action machine, only the turn (+25) is taken over.
            var visitor = unit.Record.ToArray(); visitor[0x2d] &= 0xf0; visitor[0x2d] = 0;
            var world = new VisitWorld(this, unit, box);
            OriginalVisit.Visit(visitor, unit.Parameters, box.Record, box.Parameters, box.Id, world);
            unit.Record[0x25] = visitor[0x25];
            if (world.Taken) unit.VisitTarget = 0;
            else if (box.Record[0x14] is not (0 or 1 or 2)) unit.VisitTarget = 0;   // spent: nothing to wait for
        }
    }

    private sealed class VisitWorld(Skirmish battle, SkirmishActor unit, SkirmishActor box) : IVisitWorld
    {
        public bool Taken;
        public (byte Al, bool Zf) Service() => OriginalContainer.Visit(box.Record, IsChest(box));
        public void Pickup(byte item, byte owner) { if (item != 0) { battle.PickUp(unit, item); Taken = true; } }
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
    public OriginalItemRules ItemRules => _itemRules ??= new OriginalItemRules(code => ItemTable[code].Handler, code => code < ItemTable.Count && ItemTable[code].StaticHex.Length == 18 ? Convert.FromHexString(ItemTable[code].StaticHex) : null);
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
    private sealed class SkirmishItemWorld(Skirmish battle) : IQuestWorld
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
        // the dialog handlers (OriginalQuestRules)
        public DiplomacyState Diplomacy => battle.Diplomacy; public OriginalItemRules Items => battle.ItemRules;
        public bool Stat5(int owner) => battle.Diplomacy.Dead[owner & 7];
        public IReadOnlyList<QuestObject> QuestObjects(int owner) => battle.Actors.Where(a => a.Alive && !a.Scenery && a.Record[3] == owner)
            .Select(a => new QuestObject(a.Record, BitConverter.ToUInt32(a.Parameters, 0x40), a.Parameters[0x46])).ToList();
        public byte PoolOwnerAt(uint reference) => battle.Find(reference) is { } a ? a.Record[3] : (byte)0;
        public void Send(byte type, byte other, byte receiver, byte bh, uint parameter) => battle._diplomacyRules.Send(battle.Diplomacy, type, other, receiver, bh, parameter);
        public void React(byte bl, byte bh, uint table) => battle._diplomacyRules.React(battle.Diplomacy, bl, bh, table);
        public void Give(byte item, byte owner) => battle.ItemRules.Pickup(this, item, owner);
        // ADAPTER: the use of items needs the inventory window and a targeting mode, neither is ported; the world of the use handlers stays inert
        public ItemTarget Target(uint reference) => battle.Find(reference) is { } a ? new((byte)a.Record[0], (byte)a.Record[0x0e], a.Record[0x0d], (uint)a.Position.X, (uint)a.Position.Y, 0) : default;
        public bool PlacementFree(byte al, byte ah, byte bl, uint x, uint y) => false;
        public (bool Created, uint Reference) Create(byte army, byte owner, byte template, byte layer, uint x, uint y) => (false, 0);
        public void Setup(uint reference) { }
        public void Effect(byte al, byte type, byte layer, uint x, uint y, uint argument) { }
        public byte TemplateLayer(byte army, byte template) => 1;
    }
}
