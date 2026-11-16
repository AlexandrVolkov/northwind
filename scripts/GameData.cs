using System.Text.Json;
using Godot;
namespace Northwind;

public sealed class MapData
{
    public int FormatVersion { get; set; }
    public int MapNumber { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public uint[] Cells { get; set; } = [];
    public string[] Terrain { get; set; } = [];
}
public sealed class UnitRules
{
    public string Name { get; set; } = "Дружинник";
    public int MaxHp { get; set; } = 10;
    public int Movement { get; set; } = 6;
    public int Damage { get; set; } = 3;
    public int Retaliation { get; set; } = 1;
}
public sealed class MovementRules
{
    public int ForestCost { get; set; } = 2;
    public int GroundCost { get; set; } = 1;
}
public sealed class Spawn
{
    public int Id { get; set; }
    public int Team { get; set; }
    public int[] Offset { get; set; } = [0, 0];
}
public sealed class GameRules
{
    public UnitRules Unit { get; set; } = new();
    public MovementRules Movement { get; set; } = new();
    public int[] SpawnCenter { get; set; } = [0, 0];
    public Spawn[] Units { get; set; } = [];
}
public sealed class UnitState
{
    public int Id { get; init; }
    public int Team { get; set; }
    public Vector2I Cell { get; set; }
    public int Hp { get; set; }
    public int Moves { get; set; }
    public bool Attacked { get; set; }
}
public sealed class ScenarioData
{
    public string MapFile { get; set; } = "";
    public int SlotCount { get; set; }
    public ScenarioSlot[] Slots { get; set; } = [];
}
public sealed class ScenarioSlot
{
    public int SlotIndex { get; set; }
    public int FileOffset { get; set; }
    public string LeaderName { get; set; } = "";
    public string SettlementName { get; set; } = "";
    public string ObjectFile { get; set; } = "";
    public int ObjectBytes { get; set; }
}
public static class GameData
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public static T Read<T>(string path) where T : class =>
        JsonSerializer.Deserialize<T>(Godot.FileAccess.GetFileAsString(path), Options)
        ?? throw new InvalidDataException($"Invalid JSON: {path}");
}
