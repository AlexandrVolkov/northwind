namespace Northwind;

/// <summary>State carried between the menu screens and the mission (until the campaign and save system are ported).</summary>
public static class GameSession
{
    /// <summary>Hero chosen on the save screen; the original accepts only the names stored in the EXE (Олег, Игорь, Эйрик).</summary>
    public static string HeroName { get; set; } = "Олег";
    public static int Slot { get; set; }
}
