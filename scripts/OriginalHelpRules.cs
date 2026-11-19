namespace Northwind;

/// <summary>Bounded page transition 68FC..6956, after input update and latch admission.</summary>
public static class OriginalHelpRules
{
    public const int PageCount = 32;
    public readonly record struct Transition(byte Page, byte AcceptedInputs, bool Redraw);

    public static Transition Step(byte current, ushort scanCode, byte mouseFlags)
    {
        if (current >= PageCount) throw new ArgumentOutOfRangeException(nameof(current));
        byte page = current, accepted = 0;
        if (page != 0 && ((mouseFlags & 2) != 0 || scanCode is 0xe04b or 0xe048)) { page--; accepted++; }
        if (page != 31 && ((mouseFlags & 1) != 0 || scanCode is 0xe04d or 0xe050)) { page++; accepted++; }
        return new(page, accepted, page != current);
    }
}
