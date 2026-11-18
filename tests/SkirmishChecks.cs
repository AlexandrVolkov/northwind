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
        return new(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"),
            GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
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
        using (var battle = CreateSkirmish())
        {
            uint[] ids = battle.Actors.Where(a => a.Team == 0 && !a.Structure).Select(a => a.Id).ToArray();
            Check(battle.Attack(ids, battle.Objective.Id), "Attack order accepts a distant enemy building and plans an approach");
            for (int i = 0; i < 3600 && battle.Outcome == ""; i++)
            {
                // After destroying the objective, issue ordinary player orders against surviving guards.
                if (!battle.Objective.Alive && i % 10 == 0)
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
            Check(!battle.Objective.Alive && !battle.Actors.Any(a => a.Team == 1 && !a.Structure && a.Alive), "Victory requires both the castle and its guards");
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
        var points = scene.Battle.Actors.Where(a => a.Team == 0 && !a.Structure).Select(a => scene.GetViewport().GetCanvasTransform() * a.Position).ToArray();
        var first = new Vector2(points.Min(p => p.X) - 20, points.Min(p => p.Y) - 20);
        var last = new Vector2(points.Max(p => p.X) + 20, points.Max(p => p.Y) + 20);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = first });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = last });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = last });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(scene.Selection.Count == 5, "Mouse drag in the world selects the actual army");
        scene.FocusObjective();
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var targetPoint = scene.GetViewport().GetCanvasTransform() * (scene.Battle.Objective.Position - new Vector2(0, 70));
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = targetPoint });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = targetPoint });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(scene.Selection.All(id => scene.Battle.Find(id)?.Target == scene.Battle.Objective.Id), "World right-click targets the enemy castle for the selected army");
        scene.TogglePause(); double elapsed = scene.Battle.Elapsed; scene._Process(1);
        Check(scene.Battle.Paused && scene.Battle.Elapsed == elapsed, "Scene pause button stops the realtime loop");
        scene.Restart(); scene.TogglePause(); scene.SelectArmy(); scene.FocusHero();
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
