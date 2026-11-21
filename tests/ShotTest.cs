using Godot;
using Northwind;
public partial class TestRunner
{
    private async System.Threading.Tasks.Task Shots()
    {
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Root.Size = new Vector2I(1280, 720);
        for (int t = 0; t < (int)(240 * 18.2); t++) scene.Battle.Advance(1 / 18.2);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
        foreach (var g in scene.Battle.Actors.Where(x => x.Alive && x.Team == 2 && !x.Structure).GroupBy(x => x.Source.TemplateName + "/" + x.Source.TemplateIndex)) GD.Print("RIVAL " + g.Key + " ×" + g.Count() + " frames " + string.Join(",", g.Take(3).Select(x => x.Frame)));
        var chopper = scene.Battle.Actors.FirstOrDefault(x => x.Alive && x.Worker && x.JobText == "Рубит дерево");
        for (int t = 0; t < (int)(120 * 18.2) && chopper == null; t++) { scene.Battle.Advance(1 / 18.2); chopper = scene.Battle.Actors.FirstOrDefault(x => x.Alive && x.Worker && x.JobText == "Рубит дерево"); }
        if (chopper != null) { var seq = new List<int>(); for (int t = 0; t < 60; t++) { scene.Battle.Advance(1 / 18.2); seq.Add(chopper.Frame); } GD.Print("CHOP " + chopper.Source.TemplateIndex + " frames " + string.Join(",", seq)); }
        { var victim = scene.Battle.Actors.First(x => x.Alive && !x.Structure && !x.Worker && x.Team == 1);
          typeof(Skirmish).GetMethod("Kill", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)!.Invoke(scene.Battle, [victim]);
          var seq = new List<string>(); for (int t = 0; t < 120; t++) { scene.Battle.Advance(1 / 18.2); seq.Add(victim.Frame + (victim.Dying ? "d" : "") + (victim.Alive ? "a" : "")); }
          scene.FocusWorld(victim.Position); for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng("res://build/shot-death.png"); GD.Print("CORPSES " + scene.Battle.Corpses.Count);
          GD.Print("DEATH " + victim.Source.TemplateName + " " + string.Join(",", seq.Distinct())); }
        { var site = scene.Battle.Actors.FirstOrDefault(x => x.Structure && x.Alive && x.ConstructionRemaining > 0);
          for (int t = 0; t < (int)(300 * 18.2) && site == null; t++) { scene.Battle.Advance(1 / 18.2); site = scene.Battle.Actors.FirstOrDefault(x => x.Structure && x.Alive && x.ConstructionRemaining > 0); }
          if (site != null) { var seq = new List<string>(); for (int t = 0; t < (int)(200 * 18.2) && site.ConstructionRemaining > 0; t++) { scene.Battle.Advance(1 / 18.2); var f = site.Frame - BitConverter.ToUInt16(site.Parameters, 0x48); if (seq.Count == 0 || seq[^1] != f + "/" + site.Record[0x19]) seq.Add(f + "/" + site.Record[0x19]); } GD.Print("BUILD " + site.Source.TemplateName + " frame/cond " + string.Join(" ", seq.Take(40))); } }
        var names = new[] { "Лесопилка", "Шахта", "Рабочий" };
        int k = 0;
        foreach (var name in names)
        {
            var a = scene.Battle.Actors.FirstOrDefault(x => x.Alive && x.Source.TemplateName == name && (name != "Рабочий" || x.Record[0x31] != 0)) ?? scene.Battle.Actors.First(x => x.Alive && x.Source.TemplateName == name);
            scene.FocusWorld(a.Position);
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng($"res://build/shot-{k++}.png");
            GD.Print($"SHOT {name} cell {a.Cell} frame {a.Frame} tpl {a.Source.TemplateIndex} cargo {(a.Record.Length > 0x31 ? a.Record[0x31] : 0)}");
        }
        GetTree().Quit();
    }
}
