using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalResourceChecks()
    {
        using var doc = GameData.Read<JsonDocument>("res://tests/fixtures/resources.json");
        var root = doc.RootElement;
        Check(root.GetProperty("format_version").GetInt32() == 1, "Resource reference schema");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var bytes = new byte[17]; int count = 0;
        void Append(ResourceQuad value, int offset)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value.First);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 4), value.Second);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 8), value.Third);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 12), value.Fourth);
        }
        var stocks = root.GetProperty("stocks");
        foreach (var raw in stocks.GetProperty("raw_slots").EnumerateArray())
        {
            Append(OriginalResourceRules.ReadScenario(Convert.FromHexString(raw.GetString()!)), 0);
            digest.AppendData(bytes.AsSpan(0, 16)); count++;
        }
        Check(count == stocks.GetProperty("case_count").GetInt32() && Convert.ToHexStringLower(digest.GetHashAndReset()) == stocks.GetProperty("sha256").GetString(),
            "All eight first-map SCN slots and boundary values match native stock loading");
        var payment = root.GetProperty("payment"); count = 0;
        var values = payment.GetProperty("balance_values").EnumerateArray().Select(v => v.GetUInt32()).ToArray();
        foreach (var c in payment.GetProperty("costs").EnumerateArray())
        foreach (uint a in values) foreach (uint b in values) foreach (uint third in values) foreach (uint d in values)
        {
            var cost = c.EnumerateArray().Select(v => v.GetUInt32()).ToArray();
            var template = new byte[171];
            for (int i = 0; i < 4; i++) BinaryPrimitives.WriteUInt32LittleEndian(template.AsSpan(0x7c + 4 * i), cost[i]);
            var result = OriginalConstructionRules.TryDebitUpfront(new(a,b,third,d), OriginalResourceRules.ReadPrice(template));
            bytes[0] = result.Accepted ? (byte)1 : (byte)0; Append(result.Balances, 1); digest.AppendData(bytes); count++;
        }
        Check(count == payment.GetProperty("case_count").GetInt32() && Convert.ToHexStringLower(digest.GetHashAndReset()) == payment.GetProperty("sha256").GetString(),
            "DAT price parsing and shared four-resource payment match 32,805 native recruitment cases");
        using var normal = CreateSkirmish();
        Check(normal.Resources == new ResourceQuad(1000,1000,1000,0), "Play uses the selected source owner SCN stocks");
        Check(Skirmish.Price(normal.WorkerTemplate) == new ResourceQuad(5,0,40,1) && Skirmish.Price(normal.MilitiaTemplate) == new ResourceQuad(10,0,40,1) &&
            Skirmish.Price(normal.MillTemplate) == new ResourceQuad(30,50,80,2) && Skirmish.Price(normal.FarmTemplate) == new ResourceQuad(0,30,50,0),
            "All four castle actions expose their full ARMY02 DAT prices");
        foreach (var funds in new ResourceQuad[] { new(0,1000,1000,12), new(1000,0,1000,12), new(1000,1000,0,12), new(1000,1000,1000,0) })
        {
            var rules = GameData.Read<SkirmishRules>("res://data/skirmish.json"); rules.Economy.StartingResources = funds;
            using var battle = new Skirmish(GameData.Read<MapData>("res://assets/imported/map.json"), GameData.Read<ScenarioData>("res://assets/imported/scenario.json"), GameData.Read<TemplatesData>("res://assets/imported/templates.json"), rules);
            var site = FarmSite(battle); var millSite = EconomyMillCell(battle, battle.PlayerCastle.Cell);
            int actors = battle.Actors.Count;
            Check(!battle.CanAfford(battle.MillTemplate) && battle.BuildMill(millSite) == null && battle.Resources == funds && battle.Actors.Count == actors && battle.ProbeMill(millSite).Status == ConstructionPlacementStatus.Clear,
                "Missing any native resource rejects a sawmill without debit, actor or occupied footprint");
            if (funds.First == 0 || funds.Third == 0 || funds.Fourth == 0)
                Check(battle.TrainWorker() == null && battle.TrainMilitia() == null && battle.Resources == funds && battle.Actors.Count == actors,
                    "Recruitment rejects missing metal, gold or people without partial spending");
            if (funds.Second == 0 || funds.Third == 0)
                Check(battle.BuildFarm(site) == null && battle.Resources == funds && battle.Actors.Count == actors,
                    "Farm rejects missing wood or gold without reserving a footprint");
        }
    }
}
