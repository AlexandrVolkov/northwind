using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>The scripts of jugs and chests (ARMY__03.WRK E4 / 71A), the item table of LE:3 and the pickup (docs/campaign-items.md).</summary>
    private void ContainerChecks()
    {
        // the automaton: a whole jug stays; damaged, it opens frame by frame; a visitor takes the item once and the jug falls apart
        var jugParameters = new byte[0x80]; jugParameters[0x48] = 0x10; jugParameters[0x49] = 1;
        var jug = new byte[50]; jug[1] = 0x10; jug[2] = 1; jug[0x14] = 0x81; jug[0x19] = 100; jug[0x31] = 6;
        for (int t = 0; t < 5; t++) OriginalContainer.Step(jug, jugParameters, false);
        Check(jug[0x14] == 0 && jug[1] == 0x10 && OriginalContainer.Visit(jug, false) == 0, "A whole jug stays closed and gives nothing");
        jug[0x19] = 70;
        OriginalContainer.Step(jug, jugParameters, false);
        Check(jug[0x14] == 1 && System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(jug.AsSpan(1)) == 0x111, "A damaged jug starts opening (frame 111h)");
        for (int t = 0; t < 40; t++) OriginalContainer.Step(jug, jugParameters, false);
        Check(jug[0x14] == 2, "The jug is open after its animation");
        byte item = OriginalContainer.Visit(jug, false);
        Check(item == 6 && jug[0x31] == 0 && jug[0x19] == 0, "A visitor of the open jug gets item 6 and the jug is spent");
        var result = OriginalContainer.Result.None; int ticks = 0;
        while (result == OriginalContainer.Result.None && ticks++ < 20) result = OriginalContainer.Step(jug, jugParameters, false);
        Check(result == OriginalContainer.Result.Remove && ticks == 6, $"The spent jug falls apart after six updates (took {ticks})");
        var chest = new byte[50]; chest[1] = 0x1c; chest[2] = 1; chest[0x19] = 100; chest[0x31] = 0x0c;
        var chestParameters = new byte[0x80]; chestParameters[0x48] = 0x1c; chestParameters[0x49] = 1;
        chest[0x19] = 50;
        for (int t = 0; t < 40; t++) OriginalContainer.Step(chest, chestParameters, true);
        Check(chest[0x14] == 2 && System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(chest.AsSpan(1)) == 0x126, $"The chest opens through ten frames ({chest[0x14]}, {System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(chest.AsSpan(1)):X})");
        Check(OriginalContainer.Visit(chest, true) == 0x0c && chest[0x14] == 3 && System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(chest.AsSpan(1)) == 0x127 && chest[0x19] == 50, "The chest gives its item and stays open and empty");
        for (int t = 0; t < 256; t++) OriginalContainer.Step(chest, chestParameters, true);
        Check(chest[0x19] == 49, "An empty chest loses its first condition point after 256 updates (the timer starts from 0)");

        // the table of items and the pickup in the skirmish
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.Fauna = true; rules.Pits = true;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        Check(battle.ItemName(6) == "Сиреневый камень" && battle.ItemName(14) == "Хрустальный шар" && battle.ItemName(35) == "Кувшин с фантомом", "The item table gives the names of the original");
        var chestActor = battle.SceneryObjects.First(a => a.Source.TemplateIndex == 9);
        var jugActor = battle.SceneryObjects.First(a => a.Source.TemplateIndex == 30);
        Check(Skirmish.IsContainer(chestActor) && Skirmish.IsContainer(jugActor), "Chests and jugs of the map are containers");
        // a hit opens the chest; the hero visits it
        chestActor.Record[0x19] = 60;
        for (int t = 0; t < 60; t++) battle.Advance(1 / 18.2);
        Check(chestActor.Record[0x14] == 2, "A damaged chest of the map opens");
        var hero = battle.Hero;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(4), (uint)chestActor.Position.X); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(8), (uint)chestActor.Position.Y + 40);
        Check(battle.Visit([hero.Id], chestActor.Id), "The hero is sent to the chest");
        for (int t = 0; t < 80 && Array.IndexOf(battle.Inventory, (byte)6) < 0; t++) battle.Advance(1 / 18.2);
        Check(Array.IndexOf(battle.Inventory, (byte)6) >= 0, "The hero takes the item into the inventory");
        uint gold = battle.Gold; battle.PickUp(hero, 34);
        Check(battle.Gold >= gold + 5000, "The gold item pays at least 5000");
        // the curse of Irvil (item 18) takes 100 gold every 256 updates while it is kept
        battle.PickUp(hero, 18); uint before = battle.Gold;
        for (int t = 0; t < 520; t++) battle.Advance(1 / 18.2);
        Check(Array.IndexOf(battle.Inventory, (byte)18) >= 0 && battle.Gold <= before - 100, $"The curse of Irvil drains the gold ({before} → {battle.Gold})");
        // the player attacks a jug: it breaks open by blows
        var jug2 = battle.SceneryObjects.First(a => a.Source.TemplateIndex == 30 && a != chestActor);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(4), (uint)jug2.Position.X); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(8), (uint)jug2.Position.Y + 40);
        Check(battle.Attack([hero.Id], jug2.Id), "The player may attack a jug (no war is declared)");
        for (int t = 0; t < 400 && jug2.Record[0x19] >= 100; t++) battle.Advance(1 / 18.2);
        Check(jug2.Record[0x19] < 100 && jug2.Record[0x14] != 0, $"Blows break the jug open (condition {jug2.Record[0x19]})");
    }
}
