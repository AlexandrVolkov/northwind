using Godot;
using Northwind;

public partial class TestRunner
{
    private static Skirmish CreateSkirmish() => new(GameData.Read<MapData>("res://assets/imported/map.json"),
        GameData.Read<ScenarioData>("res://assets/imported/scenario.json"), GameData.Read<TemplatesData>("res://assets/imported/templates.json"),
        GameData.Read<SkirmishRules>("res://data/skirmish.json"));

    // Isolate movement/delivery checks from population growth; gameplay uses unmodified SCN.
    private static Skirmish CreateFundedSkirmish(uint people = 12)
    {
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json");
        rules.Economy.StartingResources = new(1000, 1000, 1000, people);
        // The enemy AI hires and builds too; these player-economy checks count objects and run without it.
        return new(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules) { AiEnabled = false };
    }

    private static SkirmishActor WaitForRecruit(Skirmish battle, uint previousId)
    {
        for (int i = 0; i < 5000 && battle.LastRecruitId == previousId; i++) battle.Advance(1.0 / battle.Rules.TicksPerSecond);
        var actor = battle.Find(battle.LastRecruitId);
        if (actor == null || battle.LastRecruitId == previousId) throw new InvalidOperationException("Recruit did not leave the castle: " + battle.RecruitmentText);
        return actor;
    }
    private static SkirmishActor? Recruit(Skirmish battle, bool worker)
    {
        if (!battle.CanAfford(worker ? battle.WorkerTemplate : battle.MilitiaTemplate)) return null;
        if (battle.Paused || battle.Outcome != "") return null;
        var exit = battle.RecruitExit(worker ? battle.WorkerTemplate : battle.MilitiaTemplate);
        var gate = new Vector2I((int)(exit.X >> 5), (int)(exit.Y >> 5));
        var previousActor = battle.Find(battle.LastRecruitId);
        if (previousActor != null && previousActor.Cell == gate)
        {
            var cells = new List<Vector2I>();
            for (int y = -8; y <= 8; y++) for (int x = -8; x <= 8; x++)
                if (Math.Max(Math.Abs(x), Math.Abs(y)) >= 3) cells.Add(gate + new Vector2I(x,y));
            bool moved = false;
            foreach (var cell in cells.OrderBy(c => ((Vector2)c).DistanceSquaredTo(gate)))
            {
                if (!battle.CanStand(previousActor, cell) || battle.Move([previousActor.Id], cell) != 1 || previousActor.Goal != cell) continue;
                for (int i = 0; i < 600 && (previousActor.Cell != cell || previousActor.Stepping); i++) battle.Advance(1.0 / battle.Rules.TicksPerSecond);
                if (previousActor.Cell == cell && !previousActor.Stepping) { moved = true; break; }
            }
            if (!moved) return null;
        }
        uint previous = battle.LastRecruitId;
        if (!(worker ? battle.QueueWorker() : battle.QueueMilitia())) return null;
        return WaitForRecruit(battle, previous);
    }


    private static Vector2I BarracksSite(Skirmish battle)
    {
        var cells = new List<Vector2I>();
        for (int y = -12; y <= 12; y++) for (int x = -12; x <= 12; x++)
            cells.Add(battle.PlayerCastle.Cell + new Vector2I(x, y));
        return cells.OrderBy(c => ((Vector2)c).DistanceSquaredTo(battle.PlayerCastle.Cell))
            .First(c => battle.ProbeBarracks(c).Status == ConstructionPlacementStatus.Clear);
    }

    private async Task SkirmishChecks()
    {
        GetTree().Root.Size = new Vector2I(1280, 800);
        using (var battle = CreateSkirmish())
        {
            var friends = battle.Actors.Where(a => a.Team == 0 && !a.Structure).ToArray();
            Check(friends.Length == 5 && battle.Hero.Id == 99 && battle.Objective.Id == 196609,
                "Playable mission uses five original Oleg combatants and Ilya's source castle");
            var hero = battle.Hero; var before = hero.Position;
            var next = new[] { Vector2I.Right, Vector2I.Down, Vector2I.Left, Vector2I.Up }.Select(d => hero.Cell + d).First(c => battle.CanStand(hero, c));
            Check(battle.Move([hero.Id], next) == 1, "Move order accepts a reachable cell");
            battle.Paused = true; battle.Advance(5);
            Check(battle.Elapsed == 0 && hero.Position == before && battle.Move([hero.Id], next) == 0 && !battle.Attack([hero.Id], battle.Objective.Id),
                "Pause freezes time, movement and new orders");
            battle.Paused = false; battle.Advance(3);
            Check(hero.Position != before && hero.Cell == next && !hero.Stepping,
                "Visible source record completes its native turn and 32-unit movement step");
            int cell = next.Y * battle.Map.Width + next.X + (hero.Record[0x0d] & 3) * battle.Map.Width * battle.Map.Height;
            Check(battle.Occupancy.Cells[cell] == hero.Id, "Completed visible movement updates the source occupancy plane");
            var ids = friends.Select(a => a.Id).ToArray();
            Check(battle.Move(ids, next + new Vector2I(3, 0)) == 5 && friends.Select(a => a.Goal).Distinct().Count() == 5,
                "Group move assigns distinct reachable destinations");
        }
        // Movement and combat flow against the scenario's own defenders: the enemy AI (which now hires an army) is off here;
        // its behaviour is checked by mode "skirmish-ai".
        using (var battle = CreateSkirmish())
        {
            battle.AiEnabled = false;
            uint[] ids = battle.Actors.Where(a => a.Team == 0 && !a.Structure).Select(a => a.Id).ToArray();
            Check(!battle.AtWar(battle.Hero, battle.Objective) && !battle.Attack(ids, battle.Objective.Id),
                "Settlements start without war; the first attack order on them only warns (4F04)");
            Check(battle.Attack(ids, battle.Objective.Id) && battle.AtWar(battle.Hero, battle.Objective),
                "Repeating the order declares war (message 5, F000) and plans an approach");
            // Up to 12 minutes of game time (about 13 100 object updates at the original 18.2 per second).
            for (int i = 0; i < 7200 && battle.Outcome == ""; i++)
            {
                // After destroying the objective, issue ordinary player orders against surviving guards.
                if (!battle.Objective.Standing && i % 10 == 0)
                {
                    var guard = battle.Actors.Where(a => a.Team == 1 && a.Alive && !a.Structure)
                        .OrderBy(a => a.Position.DistanceSquaredTo(battle.Hero.Position)).FirstOrDefault();
                    if (guard != null) battle.Attack(ids, guard.Id);
                }
                battle.Advance(.1);
            }
            Check(battle.Outcome.StartsWith("Победа") && battle.Hero.Alive,
                "The default mission can be won through normal approach, combat and destruction: " + battle.Outcome +
                " | " + string.Join("; ", battle.Actors.Select(a => $"{a.Source.TemplateName}:{a.Team} hp{a.Health} {a.Position} route{a.Route.Count} step{a.Stepping}")));
            Check(battle.GuidedRouteSearches > 0, "Distant assault retains explicit provisional guidance across the first-map river");
            Check(!battle.Objective.Standing && !battle.Actors.Any(a => a.Team == 1 && !a.Structure && a.Alive), "Victory requires both the castle and its guards");
            var positions = battle.Actors.Select(a => a.Position).ToArray(); battle.Advance(5);
            Check(positions.SequenceEqual(battle.Actors.Select(a => a.Position)), "A finished battle stops simulation");
        }
        using (var battle = CreateSkirmish())
        {
            battle.Hero.Health = 0; battle.Advance(.1);
            Check(battle.Outcome.StartsWith("Поражение") && battle.Move([], new(5, 5)) == 0, "Hero death ends the mission");
        }
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(scene.ReadyToPlay, "Default realtime scene loads bundled textures and scenario");
        scene.SelectArmy(); Check(scene.Selection.Count == 5, "Select-army UI command selects all five live combatants");
        scene.Selection.Clear();
        var army = scene.Battle.Actors.Where(a => a.Team == 0 && !a.Structure).ToArray();
        scene.FocusWorld(army.Aggregate(Vector2.Zero, (sum, a) => sum + a.Position) / army.Length);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // The original shows 448×448 world pixels through the map window; only units inside it can be framed.
        var visible = army.Where(a => scene.VisibleWorld().Grow(-8).HasPoint(a.Position)).ToArray();
        GD.Print($"Army spread {army.Length}, visible {visible.Length}");
        var points = visible.Select(a => scene.GetViewport().GetCanvasTransform() * a.Position).ToArray();
        var first = new Vector2(points.Min(p => p.X) - 20, points.Min(p => p.Y) - 20);
        var last = new Vector2(points.Max(p => p.X) + 20, points.Max(p => p.Y) + 20);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = first });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = last });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = last });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(visible.Length > 0 && scene.Selection.Count == visible.Length && visible.All(a => scene.Selection.Contains(a.Id)), "Mouse drag in the map window selects the framed army");
        scene.FocusObjective();
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var targetPoint = scene.GetViewport().GetCanvasTransform() * (scene.Battle.Objective.Position - new Vector2(0, 70));
        // Without war the first right-click only warns (4F04); the repeated order declares war.
        for (int click = 0; click < 2; click++)
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = targetPoint });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = targetPoint });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (click == 0) Check(scene.Selection.All(id => scene.Battle.Find(id)?.Target == 0), "First right-click on a settlement not at war only warns");
        }
        Check(scene.Selection.All(id => scene.Battle.Find(id)?.Target == scene.Battle.Objective.Id), "World right-click targets the enemy castle for the selected army");
        scene.TogglePause(); double elapsed = scene.Battle.Elapsed; scene._Process(1);
        Check(scene.Battle.Paused && scene.Battle.Elapsed == elapsed, "Scene pause button stops the realtime loop");
        scene.Restart();
        Check(scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.MenuButton.GetCenter()), MouseButton.Left) && scene.Hud.MenuOpen, "«Меню» opens the original menu panel");
        scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.PauseButton.GetCenter()), MouseButton.Left);
        Check(scene.Battle.Paused, "«Пауза» of the menu panel pauses the battle"); scene.TogglePause();
        scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.GameFaster.GetCenter()), MouseButton.Left);
        scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.GameFaster.GetCenter()), MouseButton.Left);
        Check(scene.GameSpeed == 0xfe && scene.Speed == 2, "«Игра» + raises [908E] up to FEh: a world step every tick (11364, A10B)");
        for (int i = 0; i < 20; i++) scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.GameSlower.GetCenter()), MouseButton.Left);
        Check(scene.GameSpeed == 0xf0 && Math.Abs(scene.Speed - 2f / 15) < 1e-6, "«Игра» − stops at F0h: a step every 15 ticks");
        while (scene.GameSpeed < 0xfd) scene.ChangeSpeed(1);
        scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.ScreenButton.GetCenter()), MouseButton.Left);
        Check(DisplaySettings.WindowOpen, "«Экран» on the menu panel opens the screen settings");
        DisplaySettings.Close();
        scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.BackButton.GetCenter()), MouseButton.Left);
        Check(!scene.Hud.MenuOpen, "«Назад» closes the menu panel");
        Check(!scene.Hud.Click(scene.Hud.MapRect.GetCenter(), MouseButton.Left), "Clicks inside the map window reach the world");
        scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.Minimap.Position + new Vector2(100, 100)), MouseButton.Left);
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(scene.VisibleWorld().HasPoint(new Vector2(100, 100) / 128 * new Vector2(scene.Battle.Map.Width, scene.Battle.Map.Height) * 32), "A minimap click moves the map window there");
        var archer = scene.Battle.Actors.First(a => a.Team == 0 && a.Source.TemplateName == "Лучник");
        scene.Selection.Clear(); scene.Selection.Add(archer.Id);
        Check(scene.Hud.Template(archer)?.Slots.Count(s => s.Sprite != 0) == 4, "The archer panel has the four order buttons of its DAT template");
        scene.Hud.Click(scene.Hud.FromClassic(OriginalHud.Slot(1).GetCenter()), MouseButton.Left);
        Check(scene.PendingOrder == 2, "The arrows button waits for a move target");
        scene.FocusWorld(archer.Position); for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var moveTo = scene.GetViewport().GetCanvasTransform() * (archer.Position + new Vector2(96, 0));
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = moveTo });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = moveTo });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(scene.PendingOrder == 0 && archer.Route.Count > 0, "The next map click gives the move order");
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            GetViewport().GetTexture().GetImage().SavePng("res://build/hud-archer.png");
        }
        scene.Battle.Stop(scene.Selection);
        scene.Selection.Clear(); scene.Selection.Add(scene.Battle.Hero.Id); scene.FocusHero();
        if (DisplayServer.GetName() != "headless")
        {
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            GetViewport().GetTexture().GetImage().SavePng("res://build/hud-hero.png");
            scene.Hud.MenuOpen = true; scene.Hud.QueueRedraw();
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng("res://build/hud-menu.png");
            var shot = GetViewport().GetTexture().GetImage(); var original = Image.LoadFromFile(ProjectSettings.GlobalizePath("res://docs/reference/original-menu-panel.png"));
            double sum = 0; int n = 0;
            foreach (int row in new[] { 103, 108, 151, 156 })
                for (int x = 491; x < 615; x++)
                {
                    var p = shot.GetPixelv((Vector2I)scene.ClassicToScreen(new Vector2(x + .5f, row + .5f))); var q = original.GetPixel(x, row);
                    sum += Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B); n++;
                }
            Check(sum / n * 255 < 30, $"Menu sliders «Панорама» 0Dh and «Игра» FDh look like the original ({sum / n * 255:0.0})");
            scene.Hud.MenuOpen = false;
        }
        scene.TogglePause(); scene.SelectArmy(); scene.FocusHero();
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            GetViewport().GetTexture().GetImage().SavePng("res://build/skirmish.png");
            scene.FocusObjective();
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng("res://build/skirmish-objective.png");
        }
        RemoveChild(scene); scene.Free();
    }
}
