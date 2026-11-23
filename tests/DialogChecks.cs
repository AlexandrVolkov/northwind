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
    }
}
