using Godot;
using Northwind;
public partial class TestRunner
{
    private async System.Threading.Tasks.Task RivalShots()
    {
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
        async System.Threading.Tasks.Task Shot(string name)
        {
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng($"res://build/{name}.png");
        }
        var castle = scene.Battle.Actors.First(a => a.Team == 2 && a.Source.TemplateName == "Замок");
        scene.FocusWorld(castle.Position); await Shot("rival-start");
        for (int minute = 0; minute < 40; minute++)
        {
            for (int t = 0; t < 60 * 18; t++) scene.Battle.Advance(1 / 18.2);
            var fighters = scene.Battle.Actors.Where(a => a.Alive && !a.Structure && a.Team != 0 && a.Target != 0 && a.Cell.X > 75).ToList();
            if (fighters.Count >= 6)
            {
                var centre = fighters.Aggregate(Vector2.Zero, (s, a) => s + a.Position) / fighters.Count;
                scene.FocusWorld(centre); await Shot("rival-battle"); GD.Print($"battle at minute {minute + 1}, {fighters.Count} fighting");
                break;
            }
        }
        scene.FocusWorld(castle.Position); await Shot("rival-later");
        GetTree().Quit();
    }
}
