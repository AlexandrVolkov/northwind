using Godot;

namespace Northwind;

public sealed class OriginalEpisodeCatalog
{
    public int FormatVersion { get; set; }
    public Dictionary<string, OriginalEpisode> Episodes { get; set; } = [];
    public static OriginalEpisodeCatalog Load() => GameData.Read<OriginalEpisodeCatalog>("res://data/original_episodes.json");
}

public sealed class OriginalEpisodePerson
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public int[] StartArea { get; set; } = [];
}

public sealed class OriginalEpisode
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int ParticipantCount { get; set; }
    public List<OriginalEpisodePerson> People { get; set; } = [];
    public List<List<int>> AreaIds { get; set; } = [];

    public bool Contains(Vector2I area) => area.X >= 0 && area.X < Width && area.Y >= 0 && area.Y < Height;

    public int AreaId(Vector2I area)
    {
        if (!Contains(area)) throw new ArgumentOutOfRangeException(nameof(area));
        return AreaIds[area.Y][area.X];
    }

    public Vector2I StartArea(int participant)
    {
        var value = People[participant].StartArea;
        if (value.Length != 2) throw new InvalidDataException("EPI start area must have two coordinates");
        var area = new Vector2I(value[0], value[1]);
        if (!Contains(area)) throw new InvalidDataException("EPI participant has no playable starting area");
        return area;
    }

    /// <summary>LE:2 19C7: eight compass offsets, with 0=N, 2=E, 4=S, 6=W.</summary>
    public static Vector2I DirectionOffset(int direction) => direction switch
    {
        0 => new(0, -1), 1 => new(1, -1), 2 => new(1, 0), 3 => new(1, 1),
        4 => new(0, 1), 5 => new(-1, 1), 6 => new(-1, 0), 7 => new(-1, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };

    public bool TryDestination(Vector2I current, int direction, out Vector2I destination)
    {
        destination = current + DirectionOffset(direction);
        return Contains(destination);
    }

    /// <summary>LE:1 107AA..107D7: arrival position for a cardinal border transition.</summary>
    public static Vector2I ArrivalPixel(Vector2I previousPixel, int direction)
    {
        var delta = DirectionOffset(direction);
        if (delta.X != 0 && delta.Y != 0) throw new ArgumentOutOfRangeException(nameof(direction), "Border arrival uses a cardinal direction");
        if (delta.X == 0)
            return new(previousPixel.X, (previousPixel.Y & 0x10) + 0x20 + (delta.Y == 1 ? 0 : 0xfa0));
        return new((previousPixel.X & 0x10) + 0x20 + (delta.X == 1 ? 0 : 0xfa0), previousPixel.Y);
    }
}
