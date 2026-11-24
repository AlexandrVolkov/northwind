using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Rufin's quest in the skirmish: he asks for the lilac stone, the player brings it and gives it away (docs/diplomacy.md).</summary>
    private void DialogChecks()
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.Fauna = true; rules.Pits = true;
        using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
        battle.GrantPeople(10); for (int i = 0; i < 3; i++) battle.QueueWorker();
        for (int t = 0; t < (int)(150 * 18.2); t++) battle.Advance(1 / 18.2);   // three workers to stand in the lightning
        var rufin = battle.SceneryObjects.First(Skirmish.IsMage); int player = rules.PlayerSlot, mage = rufin.Record[3];
        Check(Skirmish.IsHermit(rufin) && mage == 6, "Rufin is a mage of slot 6");
        battle.Diplomacy.Relations[mage, player] = 1; battle.Diplomacy.Relations[player, mage] = 1;   // they have met
        for (int t = 0; t < 200 && battle.CurrentDialog() == null; t++) battle.Advance(1 / 18.2);
        var first = battle.CurrentDialog();
        Check(first != null && first.Type == 33 && first.Text.Contains("сиреневый кристалл"), $"Rufin says where the lilac crystal lies ({first?.Type}: {first?.Text.Replace('\n', ' ')})");
        battle.PickUp(battle.Hero, 6);
        for (int t = 0; t < 200 && battle.CurrentDialog() is not { Type: 34 }; t++) battle.Advance(1 / 18.2);
        var ask = battle.CurrentDialog();
        Check(ask != null && ask.Type == 34 && ask.Answers.Count == 2 && ask.Answers[0].Type == 35, $"Rufin asks for the stone and offers two answers ({ask?.Type}, {ask?.Answers.Count})");
        battle.AnswerDialog(ask!, 35);
        Check(Array.IndexOf(battle.Inventory, (byte)6) < 0 && Array.IndexOf(battle.ItemWorldInventory(mage), (byte)6) >= 0, "The player gives the stone: it passes to Rufin");
        Check(battle.Diplomacy.Relations[mage, player] == 1 && !battle.Diplomacy.AtWar(mage, player), "and the two settle in peace");
    
        // the war: the player attacks Rufin, he and the forest answer
        battle.Attack([battle.Hero.Id], rufin.Id); battle.Attack([battle.Hero.Id], rufin.Id);
        for (int t = 0; t < 40; t++) battle.Advance(1 / 18.2);
        Check(battle.Diplomacy.AtWar(mage, player) && battle.MageFights(rufin), "The attack on Rufin is a war with him");
        var near = rufin.Cell + new Vector2I(2, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(battle.Hero.Record.AsSpan(4), (uint)(near.X * 32 + 16)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(battle.Hero.Record.AsSpan(8), (uint)(near.Y * 32 + 16));
        battle.Hero.Health = 120;   // the leader of the original has little health: one discharge hurts a lot, the test needs him alive
        int before = battle.Hero.Health; bool targeted = false;
        for (int t = 0; t < 300 && battle.Hero.Alive && battle.Hero.Health >= before; t++) { battle.Advance(1 / 18.2); targeted |= rufin.Target == battle.Hero.Id; }
        Check(battle.Hero.Health < before || battle.MageFlashes.Count > 0, $"Rufin strikes back with his discharge (hero health {before} → {battle.Hero.Health})");
        // the blast hits every unit in its square: three workers standing together all lose health (BD0F, attack 14h minus the armour)
        var group = new List<SkirmishActor>(battle.Actors.Where(x => x.Team == 0 && x.Worker && x.Alive).Take(3));
        var home = rufin.Position - new Vector2(640, 0); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(battle.Hero.Record.AsSpan(4), (uint)home.X); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(battle.Hero.Record.AsSpan(8), (uint)home.Y);   // the leader is out of his sight, at home
        var spot = rufin.Position + new Vector2(200, 0); int k = 0;
        foreach (var unit in group) { System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(unit.Record.AsSpan(4), (uint)(spot.X + 12 * k)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(unit.Record.AsSpan(8), (uint)(spot.Y + 8 * k)); k++; unit.Goal = null; unit.Route.Clear(); }
        var before3 = group.Select(u => u.Health).ToArray();
        for (int t = 0; t < 1500 && group.Count(u => u.Health < before3[group.IndexOf(u)] || !u.Alive) < 2 && battle.Hero.Alive; t++) { battle.Hero.Health = 127; battle.Advance(1 / 18.2); }
        Check(group.Count == 3 && group.Count(u => u.Health < before3[group.IndexOf(u)] || !u.Alive) >= 2, $"One discharge hurts the units standing together ({string.Join(", ", group.Select(u => u.Health))} from {string.Join(", ", before3)})");
        // the hurt mage vanishes (ARMY__03.WRK D3D, FS:105) and returns when he has recovered
        var home2 = rufin.Position - new Vector2(640, 0); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(battle.Hero.Record.AsSpan(4), (uint)home2.X); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(battle.Hero.Record.AsSpan(8), (uint)home2.Y);
        rufin.Record[0x19] = 40;
        for (int t = 0; t < 120 && !Skirmish.IsHidden(rufin); t++) battle.Advance(1 / 18.2);
        Check(Skirmish.IsHidden(rufin) && rufin.Record[0x32] == 0xff, $"A mage hurt below 3Ch vanishes (+32 = {rufin.Record[0x32]:X}, mask {rufin.Record[0x20]:X})");
        Check(battle.At(rufin.Position) != rufin && !battle.Attack([battle.Hero.Id], rufin.Id), "Nobody sees or attacks the vanished mage");
        rufin.Record[0x19] = 95;
        for (int t = 0; t < 400 && Skirmish.IsHidden(rufin); t++) battle.Advance(1 / 18.2);

        Check(!Skirmish.IsHidden(rufin) && rufin.Record[0x32] == 0, $"He returns when his health is above 5Ah (+32 = {rufin.Record[0x32]:X}, mask {rufin.Record[0x20]:X})");
    }
}
