using Godot;
using Northwind;

public partial class TestRunner
{
    private void OriginalCampaignChecks()
    {
        var catalog = OriginalEpisodeCatalog.Load();
        Check(catalog.FormatVersion == 1 && catalog.Episodes.Count == 3, "Original EPI catalog is bundled");
        var game = catalog.Episodes["GAME.EPI"];
        Check(game.Width == 4 && game.Height == 3 && game.ParticipantCount == 7, "Original campaign dimensions and participant count");
        Check(game.StartArea(0) == new Vector2I(0, 0) && game.StartArea(1) == new Vector2I(0, 2)
            && game.StartArea(2) == new Vector2I(2, 2), "Original hero starting areas");
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++) Check(game.AreaId(new(x, y)) == 1 + x + 3 * y, "Nine original campaign area IDs");
            Check(game.AreaId(new(3, y)) == 10, "Repeated tenth campaign area ID");
        }
        Check(game.TryDestination(new(2, 0), 2, out var east) && east == new Vector2I(3, 0)
            && game.AreaId(east) == 10, "Eastern border enters the tenth area");
        Check(!game.TryDestination(new(3, 0), 2, out _), "EPI bounds reject a step beyond the fourth column");
        Check(!game.TryDestination(new(0, 0), 0, out _), "EPI bounds reject a step north of the world");
        var sourceDirections = new[] { new Vector2I(0, -1), new(1, -1), new(1, 0), new(1, 1),
            new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1) };
        for (int i = 0; i < 8; i++) Check(OriginalEpisode.DirectionOffset(i) == sourceDirections[i], "LE:2 19C7 compass delta");
        Check(OriginalEpisode.ArrivalPixel(new(4064, 120), 2) == new Vector2I(32, 120), "East arrival follows LE:1 10799");
        Check(OriginalEpisode.ArrivalPixel(new(0, 120), 6) == new Vector2I(4032, 120), "West arrival follows LE:1 10799");
        Check(OriginalEpisode.ArrivalPixel(new(120, 0), 0) == new Vector2I(120, 4032), "North arrival follows LE:1 10799");
        Check(OriginalEpisode.ArrivalPixel(new(120, 4064), 4) == new Vector2I(120, 32), "South arrival follows LE:1 10799");
        Check(OriginalEpisode.ArrivalPixel(new(208, 80), 2) == new Vector2I(48, 80), "Subcell parity is kept on arrival");
        var demo = catalog.Episodes["DEMO.EPI"];
        Check(demo.Width == 1 && demo.Height == 2 && demo.AreaId(new(0, 0)) == 21 && demo.AreaId(new(0, 1)) == 22,
            "Original demo topology");
        Check(catalog.Episodes["DEMO2.EPI"].AreaId(Vector2I.Zero) == 22, "Second demo area ID");
    }
}
