using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private void OriginalPopulationChecks()
    {
        using var doc = GameData.Read<JsonDocument>("res://tests/fixtures/population.json"); var root = doc.RootElement;
        Check(root.GetProperty("format_version").GetInt32() == 1, "Population source fixture schema");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); int count = 0;
        foreach (var c in root.GetProperty("recount").GetProperty("cases").EnumerateArray())
        {
            var objects = c.GetProperty("objects").EnumerateArray().Select(o => new PopulationObject(o.GetProperty("condition").GetByte(), o.GetProperty("threshold").GetByte(),
                o.GetProperty("flags").GetUInt32(), o.GetProperty("demand").GetUInt32(), o.GetProperty("support").GetUInt32(), o.GetProperty("x").GetUInt32(), o.GetProperty("y").GetUInt32()));
            var result = OriginalPopulationRules.Recount(c.GetProperty("width").GetByte(), c.GetProperty("height").GetByte(), objects);
            var bytes = new byte[16]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, result.Used); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), result.FarmSupport); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), result.Limit);
            bytes[12] = result.Left; bytes[13] = result.Top; bytes[14] = result.Right; bytes[15] = result.Bottom; hash.AppendData(bytes); count++;
        }
        var recount = root.GetProperty("recount");
        Check(count == recount.GetProperty("case_count").GetInt32(), "Population recount coverage");
        Check(Convert.ToHexStringLower(hash.GetHashAndReset()) == recount.GetProperty("sha256").GetString(), "Whole population recount matches original x86, including flags, signed conditions and map geometry"); count = 0;
        foreach (var c in root.GetProperty("growth").GetProperty("cases").EnumerateArray())
        {
            uint Value(string key) => c.GetProperty(key).GetUInt32();
            uint free = OriginalPopulationRules.Grow(Value("tick"), Value("free"), Value("used"), Value("support"), Value("limit"));
            var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, free); hash.AppendData(bytes); count++;
        }
        var growth = root.GetProperty("growth");
        Check(count == growth.GetProperty("case_count").GetInt32(), "Population growth coverage");
        Check(Convert.ToHexStringLower(hash.GetHashAndReset()) == growth.GetProperty("sha256").GetString(), "Population growth phase and reserve limits match original x86");
    }
    private static Vector2I FarmSite(Skirmish battle)
    {
        var cells = new List<Vector2I>();
        for (int y = -12; y <= 12; y++) for (int x = -12; x <= 12; x++) cells.Add(battle.PlayerCastle.Cell + new Vector2I(x, y));
        return cells.OrderBy(c => ((Vector2)c).DistanceSquaredTo(battle.PlayerCastle.Cell)).First(c => battle.ProbeFarm(c).Status == ConstructionPlacementStatus.Clear);
    }
    private async Task PopulationChecks()
    {
        GetTree().Root.Size = new Vector2I(1280, 800);
        OriginalPopulationChecks();
        using (var battle = CreateSkirmish())
        {
            Check(battle.Resources == new ResourceQuad(1000, 1000, 1000, 0), "Normal play starts with the original Oleg SCN balances");
            var initial = battle.Resources;
            Check(Recruit(battle, true) == null && Recruit(battle, false) == null && battle.Resources == initial,
                "Zero native free population rejects recruitment without spending any material");
            var site = FarmSite(battle); var farm = battle.BuildFarm(site)!;
            Check(farm is { ConstructionRemaining: > 0 } && farm.Record.Length == 34 && farm.Parameters[3] == 4 && farm.Parameters[4] == 2 && battle.ProbeFarm(site).Status == ConstructionPlacementStatus.Occupied,
                "Farm construction immediately occupies its real 4 by 2 area");
            Check(battle.Resources == new ResourceQuad(1000, 970, 950, 0) && battle.BuildFarm(site) == null,
                "Farm consumes the source wood and gold prices once and rejects its occupied footprint");
            battle.Paused = true; var condition = farm.Record[0x19]; battle.Advance(40);
            Check(farm.Record[0x19] == condition && battle.FreePeople == 0 && Recruit(battle, false) == null, "Pause freezes construction, population growth and recruitment");
            battle.Paused = false;
            for (int i = 0; i < 512 && farm.ConstructionRemaining > 0; i++) battle.Advance(1.0 / battle.Rules.TicksPerSecond);
            battle.Advance(32.0 / battle.Rules.TicksPerSecond);
            Check(farm.ConstructionRemaining == 0 && farm.Record[0x19] == farm.Template.InitialCondition && battle.Population.FarmSupport == 10,
                "Completed farm participates in the original owner recount");
            Check(battle.Population.Limit > battle.Population.Used && battle.PopulationGrowthSeconds is not null, "Farm provides room for free-population replenishment");
            farm.Record[0x19] = farm.Parameters[0x46]; battle.Advance(32.0 / battle.Rules.TicksPerSecond);
            Check(battle.Population.FarmSupport == 0, "Farm at the source signed threshold stops contributing support");
            farm.Record[0x19] = farm.Template.InitialCondition; battle.Advance(32.0 / battle.Rules.TicksPerSecond);
            Check(battle.Population.FarmSupport == 10, "Owner recount restores the repaired farm contribution without cumulative double credit");
            for (int i = 0; i < 1200 && battle.FreePeople == 0; i++) battle.Advance(1.0 / battle.Rules.TicksPerSecond);
            Check(battle.FreePeople > 0, "Ordinary simulation ticks replenish available people after farm construction");
            for (int i = 0; i < 2400 && battle.FreePeople < 2; i++) battle.Advance(1.0 / battle.Rules.TicksPerSecond);
            uint initialLoad = battle.Population.Used; var beforeHire = battle.Resources;
            var worker = Recruit(battle, true); var recruit = Recruit(battle, false);
            Check(worker != null && recruit is { Worker: false, Structure: false } && recruit.Record.Length == 49,
                "Source population growth enables worker and militia recruitment without a tutorial reserve");
            Check(battle.Resources == new ResourceQuad(beforeHire.First - 15, beforeHire.Second, beforeHire.Third - 80, battle.FreePeople) && battle.Population.Used == initialLoad + 2,
                "Both recruitments pay metal, gold and people directly from DAT while keeping wood unchanged");
            var destination = new[] { Vector2I.Right, Vector2I.Down, Vector2I.Left, Vector2I.Up }.Select(d => recruit!.Cell + d).First(c => battle.CanStand(recruit!, c));
            Check(battle.Move([recruit!.Id], destination) == 1, "Newly recruited militia accepts normal movement orders");
            for (int i = 0; i < 300 && (recruit.Cell != destination || recruit.Stepping); i++) battle.Advance(.1);
            Check(recruit.Cell == destination && !recruit.Stepping, "New soldier completes native motion and occupancy transfer");
            Check(battle.Attack([recruit.Id], battle.Objective.Id), "Recruited militia accepts the ordinary attack command");
            farm.Health = 0; farm.Record[0x19] = 0; battle.Advance(32.0 / battle.Rules.TicksPerSecond);
            Check(battle.Population.FarmSupport == 0, "Lost farm is removed from support on the original recount cadence");
        }
        var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.Economy.StartingResources = new(1000, 500, 1000, 12);
        using (var many = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"), GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules))
        {
            var workers = Enumerable.Range(0, 7).Select(_ => Recruit(many, true)).ToArray();
            Check(workers.All(w => w != null) && many.FreePeople == 5, "Free population replaces the old six-worker tutorial ceiling");
            var grid = many.Occupancy.Cells.ToArray(); Array.Fill(grid, many.Hero.Id); many.Occupancy.LoadCells(grid);
            uint wood = many.Wood, people = many.FreePeople;
            Check(Recruit(many, true) == null && Recruit(many, false) == null && many.Wood == wood && many.FreePeople == people,
                "An occupied spawn area rejects recruitment without losing either resource");
        }
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        scene.SelectCastle(); scene.BeginFarmPlacement(); Check(scene.PlacingFarm && !scene.PlacingMill, "Castle UI enters farm placement independently of sawmill placement");
        var cell = FarmSite(scene.Battle); scene.Camera.Zoom = Vector2.One * 2; scene.Camera.Position = (Vector2)(cell * 32) - new Vector2(140, 0);
        for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var screen = scene.GetViewport().GetCanvasTransform() * (Vector2)(cell * 32 + Vector2I.One * 16);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(!scene.PlacingBuilding && scene.Battle.Find(scene.Selection.Single())?.Source.TemplateName == "Ферма", $"Mouse places and selects a farm through the castle build flow: placing={scene.PlacingBuilding}, cell={cell}, screen={screen}, events={string.Join(";", scene.Battle.Events)}");
        for (int i = 0; i < 1600 && scene.Battle.FreePeople == 0; i++) scene.Battle.Advance(1.0 / scene.Battle.Rules.TicksPerSecond);
        scene.SelectCastle(); uint previousRecruit = scene.Battle.LastRecruitId; scene.HireMilitia();
        Check(scene.Selection.Single() == scene.Battle.PlayerCastle.Id, "Queue command keeps the castle selected for subsequent orders");
        var recruited = WaitForRecruit(scene.Battle, previousRecruit);
        scene.Selection.Clear(); scene.Selection.Add(recruited.Id);
        Check(recruited.Source.TemplateName == "Ополченец", "Castle UI queue produces a militia soldier over time");
        if (DisplayServer.GetName() != "headless")
        {
            scene.SelectCastle(); scene.Battle.Advance(10); scene.TogglePause();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            GetViewport().GetTexture().GetImage().SavePng("res://build/farms-recruitment.png");
        }
        RemoveChild(scene); scene.Free();
    }
}
