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
        { var seq = new List<string>(); for (int t = 0; t < 2800; t++) { scene.Battle.Advance(1 / 18.2); var c = scene.Battle.Corpses.FirstOrDefault(x => x[1] == 2); string f = c == null ? "-" : BitConverter.ToUInt16(c, 3).ToString(); if (seq.Count == 0 || seq[^1] != f) seq.Add(f); } GD.Print("DECAY " + string.Join(" ", seq)); }
        { var b = scene.Battle.Actors.First(x => x.Alive && x.Structure && x.Source.TemplateName == "Ферма");
          b.Record[0x19] = 3; typeof(Skirmish).GetMethod("Kill", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(scene.Battle, [b]);
          scene.FocusWorld(b.Position); for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
          GetViewport().GetTexture().GetImage().SavePng("res://build/shot-ruin.png"); GD.Print("RUINS " + scene.Battle.Ruins.Count); }
        { var e = new byte[0x22]; var hero = scene.Battle.Hero.Position; System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(7), (uint)hero.X + 64); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(0xc), (uint)hero.Y);
          OriginalCatapultRules.CreateBigExplosion(e); e[0x17] = 12; System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(3), 0x8d); scene.Battle.Stones.Add(e); scene.Battle.Paused = true;
          scene.FocusWorld(hero); for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
          GetViewport().GetTexture().GetImage().SavePng("res://build/shot-explosion.png"); }
        { var b = scene.Battle; var hero = b.Hero; int n = 0, ok = 0; Godot.Vector2I? any = null;
          for (int y = 0; y < b.Map.Height; y++) for (int x = 0; x < b.Map.Width; x++) { var c = new Godot.Vector2I(x, y); if (!b.Harvestable(c)) continue; n++; if (b.CanStand(hero, c)) { ok++; any ??= c; } }
          GD.Print($"TIMBER cells {n} standable by hero {ok} first {any}"); }
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
public partial class TestRunner
{
    private async System.Threading.Tasks.Task HordeShot()
    {
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Root.Size = new Vector2I(1280, 720);
        foreach (var c in scene.Battle.Camps) System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(0x23), 3);
        for (int t = 0; t < (int)(150 * 18.2); t++) scene.Battle.Advance(1 / 18.2);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
        var tent = scene.Battle.Actors.First(a => a.Team == 3 && a.Source.TemplateName == "Шатер");
        Array.Fill(scene.Battle.FogMap, (byte)0);
        scene.FocusWorld(tent.Position);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng("res://build/shot-horde.png");
        GD.Print($"HORDE tent {tent.Cell} units {scene.Battle.Actors.Count(a => a.Team == 3 && !a.Structure)}");
        GetTree().Quit();
    }
}
public partial class TestRunner
{
    private async System.Threading.Tasks.Task BridgeShot()
    {
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Root.Size = new Vector2I(1280, 720);
        scene.SelectCastle(); scene.BuildMenuOpen = true; scene.BeginBridgePlacement();
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng("res://build/shot-bridge.png");
        GetTree().Quit();
    }
}
