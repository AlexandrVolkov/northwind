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
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
        var b = scene.Battle; var hero = b.Hero;
        Vector2I? h0 = null, h1 = null, v0 = null, v1 = null;
        for (int y = 2; y < b.Map.Height - 8 && (h0 == null || v0 == null); y++)
            for (int x = 2; x < b.Map.Width - 8; x++)
            {
                int run = 0; while (x + run < b.Map.Width && b.BridgeCellFree(new(x + run, y)) && !b.CanStand(hero, new(x + run, y))) run++;
                if (h0 == null && run >= 4 && run <= 7) { h0 = new(x, y); h1 = new(x + run - 1, y); }
                int vr = 0; while (y + vr < b.Map.Height && b.BridgeCellFree(new(x, y + vr)) && !b.CanStand(hero, new(x, y + vr))) vr++;
                if (v0 == null && vr >= 4 && vr <= 7) { v0 = new(x, y); v1 = new(x, y + vr - 1); }
            }
        BuildTestFactory(b);
        int ha = b.BuildBridge(h0!.Value, h1!.Value), va = b.BuildBridge(v0!.Value, v1!.Value);
        for (int t = 0; t < (int)(120 * 18.2); t++) b.Advance(1 / 18.2);
        GD.Print($"BRIDGE h {h0}-{h1} {ha} v {v0}-{v1} {va}");
        { var seg = b.Actors.First(x => x.Index02 == 7 && x.Cell.Y == h0!.Value.Y + 1 || x.Index02 == 7);
          System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(4), (uint)(seg.Position.X)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(hero.Record.AsSpan(8), (uint)(seg.Position.Y)); }
        Array.Fill(b.FogMap, (byte)0);
        foreach (var (name, at) in new[] { ("h", h0.Value), ("v", v0.Value) })
        {
            scene.FocusWorld(new Vector2(at.X * 32 + 100, at.Y * 32 + 100));
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng($"res://build/shot-bridge-{name}.png");
        }
        { var f = b.Actors.First(Skirmish.IsFactory); scene.Selection.Clear(); scene.Selection.Add(f.Id); scene.FocusWorld(f.Position);
          for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
          await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
          GetViewport().GetTexture().GetImage().SavePng("res://build/shot-factory.png"); }
        GD.Print("SHAPES " + string.Join(",", b.Actors.Where(a => a.Index02 == 7).Select(a => a.Record[0x22])));
        GetTree().Quit();
    }
}
public partial class TestRunner
{
    private async System.Threading.Tasks.Task CastleShot()
    {
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Root.Size = new Vector2I(1280, 720);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
        scene.FocusWorld(scene.Battle.PlayerCastle.Position);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng("res://build/shot-castle.png");
        GetTree().Quit();
    }
}
public partial class TestRunner
{
    private async System.Threading.Tasks.Task DrawPerf()
    {
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Root.Size = new Vector2I(1280, 720);
        scene.Battle.FrameBudgetMs = 0;
        for (int t = 0; t < (int)(1080 * 18.2); t++) scene.Battle.Advance(1 / 18.2);
        scene.Battle.FrameBudgetMs = 14;
        var b = scene.Battle;
        GD.Print($"DRAWPERF units {b.Actors.Count(a => a.Alive && !a.Structure)} actors {b.Actors.Count}");
        scene.FocusWorld(b.Hero.Position); Array.Fill(b.FogMap, (byte)0);
        scene.ProcessMs = scene.DrawMs = 0; scene.ProfiledFrames = 0;
        for (int i = 0; i < 400; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"DRAWPERF max {scene.ProcessMaxMs:0.0} ms, game seconds advanced {scene.Battle.Elapsed:0}");
        GD.Print($"DRAWPERF frames {scene.ProfiledFrames}: process {scene.ProcessMs / scene.ProfiledFrames:0.00} ms, draw {scene.DrawMs / scene.ProfiledFrames:0.00} ms");
        GetTree().Quit();
    }
}
public partial class TestRunner
{
    private async System.Threading.Tasks.Task TowerShot()
    {
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Root.Size = new Vector2I(1280, 720);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
        var b = scene.Battle; b.GrantPeople(10); SkirmishActor? tower = null;
        for (int r = 4; r < 12 && tower == null; r++) for (int dy = -r; dy <= r && tower == null; dy++) for (int dx = -r; dx <= r && tower == null; dx++)
        { var cell = b.PlayerCastle.Cell + new Vector2I(dx, dy); if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && b.ProbeIndex(13, cell).Status == ConstructionPlacementStatus.Clear) tower = b.BuildIndex(13, cell); }
        for (int t = 0; t < (int)(300 * 18.2) && tower!.ConstructionRemaining > 0; t++) b.Advance(1 / 18.2);
        Array.Fill(b.FogMap, (byte)0);
        scene.FocusWorld(tower!.Position);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng("res://build/shot-tower.png");
        GetTree().Quit();
    }
}
public partial class TestRunner
{
    private async System.Threading.Tasks.Task FenceShot()
    {
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Root.Size = new Vector2I(1280, 720);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
        var b = scene.Battle; BuildTestFactory(b); b.LineIndex = 5; var hero = b.Hero; Vector2I? start = null;
        for (int r = 6; r < 16 && start == null; r++) for (int dx = -r; dx <= r && start == null; dx++)
        { var c = b.PlayerCastle.Cell + new Vector2I(dx, r); if (Enumerable.Range(0, 6).All(k => b.BridgeCellFree(c + new Vector2I(k, 0)) && b.CanStand(hero, c + new Vector2I(k, 0)))) start = c; }
        b.BuildBridge(start!.Value, start.Value + new Vector2I(5, 0)); b.BuildBridge(start.Value + new Vector2I(2, 1), start.Value + new Vector2I(2, 3)); b.BuildBridge(start.Value + new Vector2I(5, 1), start.Value + new Vector2I(5, 2));
        for (int t = 0; t < (int)(200 * 18.2); t++) b.Advance(1 / 18.2);
        Array.Fill(b.FogMap, (byte)0); scene.FocusWorld(new Vector2(start.Value.X * 32 + 96, start.Value.Y * 32 + 64));
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng("res://build/shot-fence.png");
        GetTree().Quit();
    }
}
public partial class TestRunner
{
    private async System.Threading.Tasks.Task EffectsShot()
    {
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Root.Size = new Vector2I(1280, 720);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
        var b = scene.Battle; var hero = b.Hero.Position;
        void Add(byte type, ushort frame, float dx)
        {
            var e = new byte[0x22]; e[0] = type; System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(3), frame); e[0x17] = 5; e[0x16] = 3;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(7), (uint)(hero.X + dx)); System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(0xc), (uint)(hero.Y + 40));
            b.Stones.Add(e);
        }
        Add(0x13, 0xce, 60); Add(0x14, 0x104, 140); Add(0x15, 0x13a, 240); Add(0x1d, 0x226, 340); Add(0x11, 0x19, 420); Add(0x12, 0x49, 480);
        b.Paused = true; Array.Fill(b.FogMap, (byte)0); scene.FocusWorld(hero + new Vector2(200, 0));
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng("res://build/shot-effects.png");
        GetTree().Quit();
    }
}
public partial class TestRunner
{
    private async System.Threading.Tasks.Task CollapseShot()
    {
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetTree().Root.Size = new Vector2I(1280, 720);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
        var b = scene.Battle; BuildTestFactory(b); var f = b.Actors.First(Skirmish.IsFactory);
        Array.Fill(b.FogMap, (byte)0); scene.FocusWorld(f.Position);
        int full = f.Template.InitialCondition;
        // The staged destruction by condition (frames after the animation block), then the fall at death, then the ruin.
        foreach (var (name, share) in new[] { ("100", 1.0), ("70", .7), ("40", .4), ("15", .15) })
        {
            f.Record[0x19] = (byte)(full * share); for (int t = 0; t < 8; t++) b.Advance(1 / 18.2);
            b.Paused = true; for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng($"res://build/shot-damage-{name}.png"); b.Paused = false;
            GD.Print($"DAMAGE {name}: cond {f.Record[0x19]} prev {f.Record[0x1a]} frame {f.Frame - f.Template.InitialFrame} dat45 {f.Parameters[0x45]} 46 {f.Parameters[0x46]} 4a {f.Parameters[0x4a]} 4b {f.Parameters[0x4b]} init {f.Template.InitialCondition} base {f.Template.InitialFrame}");
        }
        b.Demolish(f);
        for (int t = 0; t < 400; t++) b.Advance(1 / 18.2);
        b.Paused = true; for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng("res://build/shot-ruin2.png");
        GD.Print("RUIN2 ruins " + b.Ruins.Count + " " + string.Join(";", b.Ruins.Select(r => r.Template + "/" + r.Frame + "/" + r.Left)) + " alive " + f.Alive);
        GetTree().Quit();
    }
}
