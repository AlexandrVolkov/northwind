using System.Buffers.Binary;
namespace Northwind;

public enum OreExtraction : byte { Exhausted, Idle, Poked, Refused, Granted }
public enum OreApproach : byte { Pending, Completed, Failed }
public readonly record struct OreCredit(uint Metal, uint Gold, uint MetalStatistics, uint GoldStatistics);

/// <summary>Collaborators of the ore-carrier controller 86B6 that are verified or bridged separately.</summary>
public interface IOreCarrierWorld
{
    /// <summary>Record of a live object by relative reference, or empty when absent.</summary>
    ReadOnlySpan<byte> Record(uint reference);
    /// <summary>8F70 for the reference in OBE[1C]. Failed covers BL=1 and BL=2.</summary>
    OreApproach Approach();
    /// <summary>C439 with EAX=508h, EBX=F08h over the worker's owner chain; 0 when absent.</summary>
    uint FindReceiver();
    /// <summary>8B0F: request a short move aside after a failed exchange.</summary>
    void StepAside();
}

public delegate bool OreGrant(Span<byte> visitor, byte cargo);
public delegate void OreDepletion(byte cargo, byte radius);

/// <summary>Ore extraction, carrying and crediting from LE:1 and ARMY__02.WRK. See docs/ore-extraction.md.</summary>
public static class OriginalOreRules
{
    public const byte EmptyWorker = 10, OreWorker = 8, TimberWorker = 11;

    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void Initialize(Span<byte> record, ReadOnlySpan<byte> template, byte workFlags, byte random)
    {
        record[0x20] = 255; record[0x0f] = workFlags;
        ushort frame = unchecked((ushort)(BinaryPrimitives.ReadUInt16LittleEndian(template[0x48..]) + (record[0x25] & 7) * template[0x4a]));
        BinaryPrimitives.WriteUInt16LittleEndian(record[1..], frame); record[0x2e] = random;
    }

    /// <summary>123D3 and the empty worker's WRK AH=7 (7F5/851..8FE). Returns native ZF=1.</summary>
    public static bool Grant(Span<byte> worker, ReadOnlySpan<byte> currentTemplate, ReadOnlySpan<byte> oreTemplate,
        ReadOnlySpan<byte> timberTemplate, byte cargo, Func<byte> random)
    {
        if ((sbyte)worker[0x19] <= 0 || (U32(currentTemplate, 0x40) & 0x20000) == 0) return false;
        if (worker[0x0e] != EmptyWorker) return false; // templates 8/11 ignore AH=7 with ZF=0
        if (worker[0x31] != 0) return false;
        worker[0x31] = cargo;
        if (cargo == 3)
        {
            worker[0x0e] = TimberWorker; Initialize(worker, timberTemplate, 0x13, random());
            worker[0x14] = (byte)((worker[0x14] & 7) | 0x60); return true;
        }
        if (cargo is 1 or 2) { worker[0x0e] = OreWorker; Initialize(worker, oreTemplate, 0x13, random()); return true; }
        worker[0x31] = 0; return false;
    }

    /// <summary>WRK A9B..AB3: an ore carrier without cargo becomes the empty worker; OBE[14]/[15] are kept.</summary>
    public static void ReturnEmpty(Span<byte> worker, ReadOnlySpan<byte> emptyTemplate, byte random)
    {
        if (worker[0x31] != 0) throw new ArgumentException("Dropped-cargo branch is not ported");
        worker[0x0e] = EmptyWorker; Initialize(worker, emptyTemplate, 0x3f, random);
    }

    /// <summary>B47F..B502 for one update of a working mine. Visitor is the C4CD fixed-entry result.</summary>
    public static OreExtraction Extract(Span<byte> mine, byte radius, byte tick, Span<byte> visitor, OreGrant grant, OreDepletion deplete)
    {
        uint stock = U32(mine, 0x24);
        if (stock == 0)
        {
            mine[0x15] = unchecked((byte)(mine[0x15] - 1));
            if (mine[0x15] == 0) mine[0x19] = unchecked((byte)(mine[0x19] - 1));
            return OreExtraction.Exhausted;
        }
        if ((mine[0x17] & 0x40) == 0)
        {
            if ((tick & 15) != 0 || visitor.IsEmpty) return OreExtraction.Idle;
            visitor[0x17] |= 0x20; return OreExtraction.Poked;
        }
        if (visitor.IsEmpty) { mine[0x17] &= 0xbf; return OreExtraction.Refused; }
        if (visitor[3] != mine[3]) return OreExtraction.Refused; // B49A keeps the mine's request bit.
        visitor[0x17] &= 0xbf;
        if (!grant(visitor, mine[0x23])) { mine[0x17] &= 0xbf; return OreExtraction.Refused; }
        uint left = stock >= 100 ? stock - 100 : 0;
        BinaryPrimitives.WriteUInt32LittleEndian(mine[0x24..], left);
        if ((stock >> 11) != (left >> 11)) deplete(mine[0x23], radius);
        mine[0x17] &= 0xbf; return OreExtraction.Granted;
    }

    /// <summary>B3F7's clipped byte-arithmetic region around a raw object position.</summary>
    public static (int X, int Y, int Width, int Height) Region(byte mapWidth, byte mapHeight, uint rawX, uint rawY, byte radius)
    {
        byte bl = (byte)(rawX >> 5), dl = (byte)(rawY >> 5);
        byte cl = unchecked((byte)(radius * 2 + 1)), ch = cl;
        byte al = unchecked((byte)(dl - radius));
        if (dl < radius) { ch = unchecked((byte)(ch + al)); al = 0; }
        byte end = unchecked((byte)(al + ch));
        if (end >= mapHeight) ch = unchecked((byte)(ch - (byte)(end - mapHeight)));
        byte x0 = unchecked((byte)(bl - radius));
        if (bl < radius) { cl = unchecked((byte)(cl + x0)); x0 = 0; }
        end = unchecked((byte)(x0 + cl));
        if (end >= mapWidth) cl = unchecked((byte)(cl - (byte)(end - mapWidth)));
        return (x0, al, cl, ch);
    }

    public static int Count(ReadOnlySpan<uint> cells, byte width, byte height, uint rawX, uint rawY, byte cargo, byte radius)
    {
        var (x0, y0, w, h) = Region(width, height, rawX, rawY, radius);
        int count = 0;
        for (int y = y0; y < y0 + h; y++)
        for (int x = x0; x < x0 + w; x++)
            if ((byte)(cells[y * width + x] >> 16) == cargo) count++;
        return count;
    }

    /// <summary>B502..B5AA: remove one random ore mark and choose a class-0 surface through CE9D.</summary>
    public static int Deplete(uint[] cells, byte width, byte height, uint rawX, uint rawY, byte cargo, byte radius, OriginalTerrainCatalog catalog)
    {
        int count = Count(cells, width, height, rawX, rawY, cargo, radius);
        if (count == 0) return 0;
        int pick = (catalog.NextRandomByte() * (count & 255)) >> 8, seen = 0;
        var (x0, y0, w, h) = Region(width, height, rawX, rawY, radius);
        for (int y = y0; y < y0 + h; y++)
        for (int x = x0; x < x0 + w; x++)
        {
            int index = y * width + x;
            if ((byte)(cells[index] >> 16) != cargo) continue;
            if ((byte)seen != (byte)pick) { seen++; continue; }
            cells[index] &= 0xff00ffff;
            if (catalog.ReplaceSurface(catalog.Descriptor((int)(cells[index] & 65535)) & 0xffff00ff, out ushort tile))
                cells[index] = (cells[index] & 0xffff0000) | tile;
            return count - 1;
        }
        return count - 1;
    }

    /// <summary>CC7B: a castle with its request bit takes cargo from the worker in its fixed entry.</summary>
    public static bool CastleReceive(Span<byte> castle, byte tick, Span<byte> visitor, ReadOnlySpan<byte> visitorTemplate, ref OreCredit credit)
    {
        bool delivered = false;
        if ((castle[0x17] & 0x40) != 0)
        {
            if (visitor.IsEmpty) castle[0x17] &= 0xbf;
            else if ((U32(visitorTemplate, 0x40) & 0x20000) != 0)
            {
                visitor[0x17] &= 0xbf;
                if (Credit(visitor[0x31], ref credit)) { visitor[0x31] = 0; delivered = true; }
                castle[0x17] &= 0xbf;
            }
        }
        if ((tick & 15) == 0 && !visitor.IsEmpty) visitor[0x17] |= 0x20;
        return delivered;
    }

    /// <summary>1243A with the goods handlers 11EB0 (code 1) and 11ECB (code 2).</summary>
    public static bool Credit(byte cargo, ref OreCredit credit)
    {
        switch (cargo)
        {
            case 0: return true;
            case 1: credit = credit with { Gold = unchecked(credit.Gold + 100), GoldStatistics = unchecked(credit.GoldStatistics + 100) }; return true;
            case 2: credit = credit with { Metal = unchecked(credit.Metal + 100), MetalStatistics = unchecked(credit.MetalStatistics + 100) }; return true;
            default: throw new NotSupportedException("Other goods use the item handlers and inventory slots");
        }
    }

    /// <summary>Mine WRK 593..5B3 and castle WRK 13B..157 AH=5 visitor checks.</summary>
    public static bool MineAccepts(ReadOnlySpan<byte> visitorTemplate)
    {
        uint flags = U32(visitorTemplate, 0x40);
        return (flags & 0x1000) != 0 || (flags & 0xf00) == 0x300;
    }
    public static bool CastleAccepts(ReadOnlySpan<byte> castle, ReadOnlySpan<byte> visitor, ReadOnlySpan<byte> visitorTemplate)
        => visitor[3] == castle[3] && (U32(visitorTemplate, 0x40) & 0x20000) != 0;

    /// <summary>86B6..877F: mine/receiver phases in OBE[15]. Returns native ZF.</summary>
    public static bool CarrierStep(Span<byte> worker, bool entryFlag, IOreCarrierWorld world)
    {
        if (worker[0x15] == 0) return PhaseZero(worker, entryFlag, world);
        uint saved = U32(worker, 0x32);
        if (saved != 0)
        {
            var mine = world.Record(saved);
            if (mine.IsEmpty || mine[3] != worker[3] || (sbyte)mine[0x19] <= 0) BinaryPrimitives.WriteUInt32LittleEndian(worker[0x32..], 0);
        }
        switch (worker[0x15] & 15)
        {
            case 1: return Approach(worker, entryFlag, world, false);
            case 2: return Approach(worker, entryFlag, world, true);
            case 3: return Wait(worker, entryFlag, world);
            default: return true;
        }
    }
    private static bool PhaseZero(Span<byte> worker, bool entryFlag, IOreCarrierWorld world)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(worker[0x32..], U32(worker, 0x1c));
        return worker[0x31] != 0 ? FindReceiver(worker, entryFlag, world) : BackToMine(worker, entryFlag, world);
    }
    // 8707..8717 falls through into the phase-1 approach in the same update.
    private static bool BackToMine(Span<byte> worker, bool entryFlag, IOreCarrierWorld world)
    {
        worker[0x15] = 1; worker[0x30] = 0;
        uint mine = U32(worker, 0x32);
        BinaryPrimitives.WriteUInt32LittleEndian(worker[0x1c..], mine);
        return mine == 0 || Approach(worker, entryFlag, world, false);
    }
    private static bool FindReceiver(Span<byte> worker, bool entryFlag, IOreCarrierWorld world)
    {
        uint receiver = world.FindReceiver();
        if (receiver == 0) return true;
        BinaryPrimitives.WriteUInt32LittleEndian(worker[0x1c..], receiver);
        worker[0x15] = 2; worker[0x30] = 0;
        return Approach(worker, entryFlag, world, true);
    }
    private static bool Approach(Span<byte> worker, bool entryFlag, IOreCarrierWorld world, bool receiver)
    {
        var result = world.Approach();
        if (result == OreApproach.Pending) return entryFlag;
        if (result == OreApproach.Failed)
        {
            worker[0x15] = 3; worker[0x30] = 0; world.StepAside();
            return Wait(worker, entryFlag, world);
        }
        if (!receiver) return worker[0x31] == 0 || FindReceiver(worker, entryFlag, world);
        return worker[0x31] == 0 ? BackToMine(worker, entryFlag, world) : entryFlag;
    }
    private static bool Wait(Span<byte> worker, bool entryFlag, IOreCarrierWorld world)
    {
        worker[0x30] = unchecked((byte)(worker[0x30] + 1));
        if (worker[0x30] != 0) return entryFlag;
        return worker[0x31] != 0 ? FindReceiver(worker, entryFlag, world) : BackToMine(worker, entryFlag, world);
    }
}
