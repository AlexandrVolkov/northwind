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
    public byte[] Inventory { get; } = new byte[InventorySlots];
    public string ItemName(int code) => code > 0 && code < ItemTable.Count ? ItemTable[code].Name : "";
    public static bool IsContainer(SkirmishActor actor) => actor.Scenery && actor.Source.TemplateFile == "ARMY__03.DAT" && actor.Source.TemplateIndex is 1 or 9 or 10 or 11 or 30 or 33;
    private static bool IsChest(SkirmishActor actor) => actor.Source.TemplateIndex is 9 or 10 or 11;

    private void TickContainers()
    {
        if (_scenery.Count == 0) return;
        foreach (var container in _scenery.Where(IsContainer).ToArray())
        {
            if (OriginalContainer.Step(container.Record, container.Parameters, IsChest(container)) != OriginalContainer.Result.Remove) continue;
            Occupancy.ClearReference(container.Id); Actors.Remove(container); _scenery.Remove(container);
            foreach (var unit in Actors.Where(a => a.VisitTarget == container.Id)) unit.VisitTarget = 0;
        }
        foreach (var unit in Actors.Where(a => a.VisitTarget != 0 && a.Alive).ToArray())
        {
            if (Find(unit.VisitTarget) is not { } box || !IsContainer(box)) { unit.VisitTarget = 0; continue; }
            if (unit.Position.DistanceTo(box.Position) > VisitRange) continue;
            if (OriginalContainer.Visit(box.Record, IsChest(box)) is var item and not 0) { PickUp(unit, item); unit.VisitTarget = 0; }
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

    /// <summary>1243A: the handler of the item code (AH = 2) takes it (gold) or lets it go into the first free slot.</summary>
    public bool PickUp(SkirmishActor unit, byte item)
    {
        if (unit.Team != 0 || item == 0 || item >= ItemTable.Count) return false;
        switch (ItemTable[item].Handler)
        {
            case "11EE6": { uint gold = 5000u + ((uint)_terrainCatalog.NextRandomByte() << 5); Gold += gold; Log($"Найдено золото: {gold}."); return true; }   // 9E41 « 5 + 1388h
            case "11EB0": Gold += 100; return true;
            case "11ECB": Wood += 100; return true;
        }
        int slot = Array.IndexOf(Inventory, (byte)0);
        if (slot < 0) { Log("Инвентарь полон."); return false; }
        Inventory[slot] = item; Log($"Подобран предмет: {ItemName(item)}."); return true;
    }
}
