namespace Northwind;

public readonly record struct PopulationObject(byte Condition, byte Threshold, uint Flags, uint Demand, uint Support, uint X, uint Y);
public readonly record struct PopulationTotals(uint Used, uint FarmSupport, uint Limit, byte Left, byte Top, byte Right, byte Bottom);

/// <summary>D453 owner recount and D40C free-population growth. Timing and recruitment UI are supplied by the caller.</summary>
public static class OriginalPopulationRules
{
    public static uint GrowthPeriod(uint support) => (1023u >> (int)(Math.Min(support, 100u) >> 5) | 1u) + 1u;
    public static uint Grow(uint tick, uint free, uint used, uint support, uint limit)
    {
        uint mask = GrowthPeriod(support) - 1;
        if ((tick & mask) == 0 && unchecked(used + free) < limit) return unchecked(free + 1);
        return free;
    }
    public static PopulationTotals Recount(byte width, byte height, IEnumerable<PopulationObject> objects)
    {
        if (width == 0 || height == 0) throw new ArgumentException("Population map dimensions must be nonzero");
        uint used = 0, support = 0, minX = uint.MaxValue, minY = uint.MaxValue, maxX = 0, maxY = 0;
        foreach (var obj in objects)
        {
            if ((sbyte)obj.Condition <= (sbyte)obj.Threshold) continue;
            used = unchecked(used + obj.Demand);
            if ((obj.Flags & 0xf08) == 0x108) support = unchecked(support + obj.Support);
            if ((obj.Flags & 0x68) != 8) continue;
            minX = Math.Min(minX, obj.X); minY = Math.Min(minY, obj.Y);
            maxX = Math.Max(maxX, obj.X); maxY = Math.Max(maxY, obj.Y);
        }
        if (maxX < minX || maxY < minY) return new(used, support, 0, 0, 0, 0, 0);
        byte centerX = unchecked((byte)(unchecked(maxX + minX) >> 6));
        byte centerY = unchecked((byte)(unchecked(maxY + minY) >> 6));
        ushort spans = unchecked((ushort)(((byte)((maxX - minX) >> 5) | (byte)((maxY - minY) >> 5) << 8) + 0x1414));
        static (byte Start, byte End) Clip(byte center, byte span, byte mapSize)
        {
            int half = span >> 1;
            byte start = unchecked((byte)(center - half));
            if (center < half) { span = unchecked((byte)(span + start)); start = 0; }
            byte end = unchecked((byte)(span + start));
            if (end > mapSize) end = mapSize;
            return (start, unchecked((byte)(end - 1)));
        }
        var x = Clip(centerX, (byte)spans, width); var y = Clip(centerY, (byte)(spans >> 8), height);
        uint limit = 0;
        if (x.End >= x.Start && y.End >= y.Start)
        {
            uint area = (uint)(x.End - x.Start) * (uint)(y.End - y.Start);
            limit = unchecked((unchecked(area * area) >> 14) + support);
        }
        return new(used, support, limit, x.Start, y.Start, x.End, y.End);
    }
}
