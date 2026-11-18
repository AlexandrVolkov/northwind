using System.Buffers.Binary;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private async Task MineChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/mine-deposits.json");
        var map = GameData.Read<MapData>("res://assets/imported/map.json");
        var cases = reference.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Vector2I previewSite = default;
        foreach (var item in cases)
        {
            int x = item.GetProperty("x").GetInt32(), y = item.GetProperty("y").GetInt32();
            var deposit = OriginalMineRules.Initialize(map.Cells, map.Width, map.Height, (uint)(x * 32), (uint)(y * 32));
            Check(deposit.Type == item.GetProperty("type").GetByte() && deposit.Remaining == item.GetProperty("remaining").GetUInt32()
                && deposit.Count == Math.Max(item.GetProperty("first").GetInt32(), item.GetProperty("second").GetInt32()),
                $"Mine deposit at {x},{y} matches bounded original LE:1 B3F7");
        }
        using (var battle = CreateFundedSkirmish())
        {
            var center = battle.PlayerCastle.Cell;
            var sites = from y in Enumerable.Range(center.Y - 20, 41)
                        from x in Enumerable.Range(center.X - 20, 41)
                        let cell = new Vector2I(x, y)
                        where battle.ProbeMine(cell).Status == ConstructionPlacementStatus.Clear
                        orderby battle.MineDepositAt(cell).Count descending
                        select cell;
            var site = sites.First(); previewSite = site; var deposit = battle.MineDepositAt(site); var before = battle.Resources;
            Check(deposit.Count > 0, $"Reachable mine site covers source ore: {site}, count {deposit.Count}");
            var mine = battle.BuildMine(site) ?? throw new InvalidOperationException("Mine placement failed");
            Check(mine.Record.Length == 40 && mine.Parameters[3] == 3 && mine.Parameters[4] == 2 && mine.Record[0x23] == deposit.Type
                && BinaryPrimitives.ReadUInt32LittleEndian(mine.Record.AsSpan(0x24)) == deposit.Remaining,
                "Mine uses DAT footprint and original WRK deposit fields");
            var price = Skirmish.Price(battle.MineTemplate);
            Check(battle.Resources == new ResourceQuad(before.First - price.First, before.Second - price.Second, before.Third - price.Third, before.Fourth - price.Fourth)
                && battle.ProbeMine(site).Status == ConstructionPlacementStatus.Occupied,
                "Mine charges the four DAT costs once and occupies its source footprint");
            for (int i = 0; i < 700 && mine.ConstructionRemaining > 0; i++) battle.Advance(1.0 / battle.Rules.TicksPerSecond);
            Check(mine.ConstructionRemaining == 0 && mine.Record[0x19] == mine.Template.InitialCondition
                && BinaryPrimitives.ReadUInt32LittleEndian(mine.Record.AsSpan(0x24)) == deposit.Remaining,
                "Mine reaches completed condition while retaining its source deposit state");
            var worker = Recruit(battle, true) ?? throw new InvalidOperationException("Could not hire a miner");
            for (int i = 0; i < 300 && battle.Find(worker.Id) != null && worker.Cell == battle.PlayerCastle.Cell; i++) battle.Advance(1.0 / battle.Rules.TicksPerSecond);
            uint metal = battle.Metal, gold = battle.Gold, stock = BinaryPrimitives.ReadUInt32LittleEndian(mine.Record.AsSpan(0x24));
            Check(!battle.WorkMine([battle.Hero.Id], mine.Id) && battle.WorkMine([worker.Id], mine.Id) && worker.MineJob,
                "Only workers accept the mine order");
            bool carried = false;
            for (int i = 0; i < 9000 && (battle.Metal == metal && battle.Gold == gold); i++)
            {
                battle.Advance(1.0 / battle.Rules.TicksPerSecond);
                if (worker.Source.TemplateIndex == OriginalOreRules.OreWorker) carried = true;
            }
            uint after = BinaryPrimitives.ReadUInt32LittleEndian(mine.Record.AsSpan(0x24));
            bool goldMine = deposit.Type == 1;
            Check(carried && after == stock - 100, $"Mine B47F hands one bag to the worker and debits 100 ({stock} -> {after})");
            Check(goldMine ? battle.Gold == gold + 100 && battle.Metal == metal : battle.Metal == metal + 100 && battle.Gold == gold,
                $"Castle CC7B credits exactly one bag of {Skirmish.OreName(deposit.Type)} (metal {metal}->{battle.Metal}, gold {gold}->{battle.Gold})");
            Check(worker.Record[0x31] == 0 && (goldMine ? battle.ProducedGold : battle.ProducedMetal) == 100, "Delivered cargo is cleared and counted once in statistics");
            for (int i = 0; i < 9000 && (battle.Metal == metal && battle.Gold == gold || after == BinaryPrimitives.ReadUInt32LittleEndian(mine.Record.AsSpan(0x24))); i++)
                battle.Advance(1.0 / battle.Rules.TicksPerSecond);
            Check(BinaryPrimitives.ReadUInt32LittleEndian(mine.Record.AsSpan(0x24)) == stock - 200 && worker.MineJob,
                "The worker returns to the mine for the next bag");
        }
        var scene = GD.Load<PackedScene>("res://scenes/skirmish.tscn").Instantiate<SkirmishMain>(); AddChild(scene);
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        scene.SelectCastle(); scene.BeginMinePlacement();
        Check(scene.PlacingMine && scene.PlacingBuilding, "Castle UI enters mine placement");
        if (DisplayServer.GetName() != "headless")
        {
            scene.Camera.Position = (Vector2)(previewSite * 32);
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var screen = scene.GetViewport().GetCanvasTransform() * (Vector2)(previewSite * 32 + Vector2I.One * 16);
            Input.WarpMouse(screen);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            scene._Process(0); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://build"));
            GetViewport().GetTexture().GetImage().SavePng("res://build/mine-placement.png");
        }
        scene.SelectCastle();
        var farm = scene.Battle.BuildFarm(FarmSite(scene.Battle)) ?? throw new InvalidOperationException("Farm placement failed");
        for (int i = 0; i < 9000 && scene.Battle.FreePeople < 4; i++) scene.Battle.Advance(1.0 / scene.Battle.Rules.TicksPerSecond);
        var placed = scene.Battle.BuildMine(previewSite) ?? throw new InvalidOperationException("Mine placement failed after population growth");
        for (int i = 0; i < 700 && placed.ConstructionRemaining > 0; i++) scene.Battle.Advance(1.0 / scene.Battle.Rules.TicksPerSecond);
        Check(farm.Alive && placed.ConstructionRemaining == 0, "Normal SCN start can grow four people and finish a mine");
        scene.Selection.Clear(); scene.Selection.Add(placed.Id);
        if (DisplayServer.GetName() != "headless")
        {
            scene.Camera.Position = placed.Position;
            scene._Process(0); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng("res://build/mine-built.png");
        }
        RemoveChild(scene); scene.Free();
    }
}
