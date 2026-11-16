using Godot;
namespace Northwind;

/// <summary>Deterministic experimental rules, not reconstructed original gameplay.</summary>
public sealed class Simulation : IDisposable
{
    public MapData Map { get; private set; } = new();
    public GameRules Rules { get; private set; } = new();
    public List<UnitState> Units { get; } = [];
    public List<string> Events { get; } = [];
    public int Turn { get; private set; } = 1;
    public AStarGrid2D Grid { get; } = new();
    private static readonly Vector2I[] Directions = [Vector2I.Left, Vector2I.Right, Vector2I.Up, Vector2I.Down];

    public void Setup(MapData map, GameRules rules)
    {
        Map = map; Rules = rules;
        Grid.Region = new Rect2I(0, 0, map.Width, map.Height);
        Grid.DiagonalMode = AStarGrid2D.DiagonalModeEnum.Never;
        Grid.DefaultComputeHeuristic = AStarGrid2D.Heuristic.Manhattan;
        Grid.DefaultEstimateHeuristic = AStarGrid2D.Heuristic.Manhattan;
        Grid.Update();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var cell = new Vector2I(x, y);
            Grid.SetPointSolid(cell, !Passable(cell));
            Grid.SetPointWeightScale(cell, MovementCost(cell));
        }
        Units.Clear(); Events.Clear(); Turn = 1;
        var center = new Vector2I(rules.SpawnCenter[0], rules.SpawnCenter[1]);
        foreach (var specification in rules.Units)
        {
            var cell = FindSpawn(center + new Vector2I(specification.Offset[0], specification.Offset[1]));
            if (cell.X < 0) continue;
            Units.Add(new UnitState { Id = specification.Id, Team = specification.Team, Cell = cell,
                Hp = rules.Unit.MaxHp, Moves = rules.Unit.Movement });
        }
        Log("Экспериментальные правила. Ваш ход.");
    }
    public bool Inside(Vector2I c) => c.X >= 0 && c.Y >= 0 && c.X < Map.Width && c.Y < Map.Height;
    public string TerrainAt(Vector2I c) => Inside(c) ? Map.Terrain[Map.Cells[c.Y * Map.Width + c.X] & 0xffff] : "outside";
    public bool Passable(Vector2I c) => Inside(c) && TerrainAt(c) is not ("water" or "rock");
    public int MovementCost(Vector2I c) => TerrainAt(c) == "forest" ? Rules.Movement.ForestCost : Rules.Movement.GroundCost;
    public UnitState? UnitAt(Vector2I c) => Units.FirstOrDefault(u => u.Hp > 0 && u.Cell == c);
    public UnitState? GetUnit(int id) => Units.FirstOrDefault(u => u.Hp > 0 && u.Id == id);
    private Vector2I FindSpawn(Vector2I desired)
    {
        for (int radius = 0; radius < Math.Max(Map.Width, Map.Height); radius++)
        for (int y = desired.Y - radius; y <= desired.Y + radius; y++)
        for (int x = desired.X - radius; x <= desired.X + radius; x++)
        {
            var cell = new Vector2I(x, y);
            if (Passable(cell) && UnitAt(cell) == null) return cell;
        }
        return new(-1, -1);
    }
    public List<Vector2I> PathFor(UnitState? unit, Vector2I target)
    {
        if (unit == null || !Passable(target) || UnitAt(target) != null) return [];
        var obstacles = Units.Where(u => u.Hp > 0 && u.Id != unit.Id).ToArray();
        foreach (var other in obstacles) Grid.SetPointSolid(other.Cell, true);
        try { return Grid.GetIdPath(unit.Cell, target).ToList(); }
        finally { foreach (var other in obstacles) Grid.SetPointSolid(other.Cell, !Passable(other.Cell)); }
    }
    public int RouteCost(IReadOnlyList<Vector2I> path) => path.Skip(1).Sum(MovementCost);
    public List<Vector2I> MoveUnit(int id, Vector2I target)
    {
        var unit = GetUnit(id); var path = PathFor(unit, target);
        if (unit == null || path.Count < 2 || RouteCost(path) > unit.Moves) return [];
        unit.Moves -= RouteCost(path); unit.Cell = target;
        Log($"Отряд {id}: переход, осталось {unit.Moves} ОД.");
        return path;
    }
    public static bool Adjacent(Vector2I a, Vector2I b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;
    public bool Attack(int attackerId, int defenderId)
    {
        var a = GetUnit(attackerId); var d = GetUnit(defenderId);
        if (a == null || d == null || a.Team == d.Team || a.Attacked || a.Moves < 1 || !Adjacent(a.Cell, d.Cell)) return false;
        d.Hp = Math.Max(0, d.Hp - Rules.Unit.Damage); a.Attacked = true; a.Moves = 0;
        if (d.Hp > 0) a.Hp = Math.Max(0, a.Hp - Rules.Unit.Retaliation);
        Log($"Отряд {attackerId} → {defenderId}: урон {Rules.Unit.Damage}; ответ {(d.Hp > 0 ? Rules.Unit.Retaliation : 0)}.");
        return true;
    }
    public string Outcome() => !Units.Any(u => u.Hp > 0 && u.Team == 0) ? "Поражение. R — начать заново."
        : !Units.Any(u => u.Hp > 0 && u.Team == 1) ? "Победа. R — начать заново." : "";
    public List<(int Id, List<Vector2I> Path)> EndTurn()
    {
        List<(int, List<Vector2I>)> actions = [];
        if (Outcome() != "") return actions;
        foreach (var enemy in Units.Where(u => u.Team == 1 && u.Hp > 0))
        {
            enemy.Moves = Rules.Unit.Movement; enemy.Attacked = false;
            var candidates = Units.Where(u => u.Team == 0 && u.Hp > 0)
                .OrderBy(u => ((Vector2)enemy.Cell).DistanceSquaredTo(u.Cell)).ToArray();
            List<Vector2I> best = [];
            foreach (var friend in candidates)
            {
                if (Adjacent(enemy.Cell, friend.Cell)) { Attack(enemy.Id, friend.Id); break; }
                foreach (var direction in Directions)
                {
                    var route = PathFor(enemy, friend.Cell + direction);
                    if (route.Count > 1 && (best.Count == 0 || RouteCost(route) < RouteCost(best))) best = route;
                }
            }
            if (!enemy.Attacked && best.Count > 1)
            {
                var affordable = new List<Vector2I> { best[0] }; int spent = 0;
                foreach (var cell in best.Skip(1)) { spent += MovementCost(cell); if (spent > enemy.Moves) break; affordable.Add(cell); }
                if (affordable.Count > 1) actions.Add((enemy.Id, MoveUnit(enemy.Id, affordable[^1])));
                foreach (var friend in candidates)
                    if (friend.Hp > 0 && Adjacent(enemy.Cell, friend.Cell)) { Attack(enemy.Id, friend.Id); break; }
            }
        }
        Turn++;
        foreach (var friend in Units.Where(u => u.Team == 0 && u.Hp > 0)) { friend.Moves = Rules.Unit.Movement; friend.Attacked = false; }
        Log($"Ход {Turn}. Ваши отряды готовы."); return actions;
    }
    public void Log(string message) { Events.Add(message); if (Events.Count > 6) Events.RemoveAt(0); }
    public void Dispose() => Grid.Dispose();
}
