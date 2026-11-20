using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/wind.json (tools/verify_wind.py): the wind 9E7D.</summary>
    private void OriginalWindChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/wind.json");
        var spec = reference.RootElement.GetProperty("wind");
        var constants = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json");
        var properties = Convert.FromHexString(GameData.Read<MapData>("res://assets/imported/map.json").SegmentPropertiesHex);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        IEnumerable<JsonElement> Items(string name) => spec.GetProperty(name).EnumerateArray();
        foreach (var v in Items("vectors")) foreach (var a in Items("accelerations")) foreach (var d in Items("directions"))
        foreach (var timer in Items("timers")) foreach (var indexElement in Items("random_indices"))
        {
            byte index = (byte)indexElement.GetInt32();
            var wind = new WindState { X = v[0].GetInt32(), Y = v[1].GetInt32(), AccelerationX = a[0].GetInt32(), AccelerationY = a[1].GetInt32(), Direction = (byte)d.GetInt32() };
            var random = new OriginalTerrainCatalog(properties, constants) { RandomIndex = index, SecondaryIndex = unchecked((byte)(index * 5 + 3)) };
            OriginalWindRules.Step(ref wind, (byte)timer.GetInt32(), random.NextRandomByte, random.NextSecondaryByte);
            var raw = new byte[19];
            BinaryPrimitives.WriteInt32LittleEndian(raw, wind.X); BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(4), wind.Y); raw[8] = wind.Direction;
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(9), wind.AccelerationX); BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(13), wind.AccelerationY);
            raw[17] = random.RandomIndex; raw[18] = random.SecondaryIndex;
            digest.AppendData(raw); count++;
        }
        Check(count == spec.GetProperty("case_count").GetInt32(), $"Wind reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == spec.GetProperty("sha256").GetString(), "Wind 9E7D matches original x86");
    }
}
