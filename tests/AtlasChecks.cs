using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Frames of each atlas page sit in consecutive 128-px cells in the order of the list (a page built from a list that mixed pages once shifted every frame).</summary>
    private void AtlasChecks()
    {
        var data = GameData.Read<BattleFramesData>("res://assets/imported/battle_frames.json");
        foreach (int page in new[] { 0, 1 })
        {
            var frames = data.Frames.Where(f => f.Page == page).ToList();
            bool ordered = frames.Select((f, i) => f.X == i % 32 * 128 && f.Y == i / 32 * 128).All(ok => ok);
            Check(frames.Count > 0 && ordered, $"Atlas page {page}: {frames.Count} frames in consecutive cells");
            var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(page == 0 ? "res://assets/imported/battle_frames.png" : "res://assets/imported/battle_frames_horde.png"));
            Check(image != null && image.GetHeight() >= (frames.Count + 31) / 32 * 128, $"Atlas page {page} image holds its cells");
        }
        var castle = data.Frames.First(f => f.TemplateFile == "ARMY__02.DAT" && f.TemplateIndex == 2);
        var page0 = Image.LoadFromFile(ProjectSettings.GlobalizePath("res://assets/imported/battle_frames.png"));
        int opaque = 0; for (int y = 0; y < castle.Height; y += 4) for (int x = 0; x < castle.Width; x += 4) if (page0.GetPixel(castle.X + x, castle.Y + y).A > 0) opaque++;
        Check(castle.Width > 100 && opaque > 200, $"The castle frame is a large opaque sprite ({castle.Width}x{castle.Height}, {opaque} samples)");
    }
}
