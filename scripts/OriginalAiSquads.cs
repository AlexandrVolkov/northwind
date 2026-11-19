using System.Buffers.Binary;
namespace Northwind;

/// <summary>AI squads of a settlement (LE:1 20095..20275, 2098B, 205FB) and the group orders they give to their members
/// (B765..B9EA). Squads live in the AI block: a number pool at +7B (256 bytes, 1 = in use), the count at +9DF and up to
/// 50 records of 52h bytes from +9E0. Members are the settlement's objects whose OBE +1B equals the squad number +1.
/// Verified against the original x86 by tools/verify_ai_squads.py; see docs/ai.md, «Отряды».</summary>
public static class OriginalAiSquads
{
    public const int Ids = 0x7b, List = 0x9df, Size = 0x52, Capacity = 50;
    public static int Record(int index) => List + 1 + Size * index;
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    /// <summary>20095: the highest free squad number 255..1. When none is free, byte +7B (number 0) is marked and 0 returned.</summary>
    public static byte Allocate(Span<byte> ai)
    {
        int n = 0xff;
        while (n != 0 && ai[Ids + n] != 0) n--;
        ai[Ids + n] = 1;
        return (byte)n;
    }

    /// <summary>200AB: releases pool byte <paramref name="index"/> (its own AH is cleared first, so it always writes 0).</summary>
    public static void Release(Span<byte> ai, byte index) => ai[Ids + index] = 0;

    /// <summary>20232: offset of the squad with this number, or −1. Number 0 is never reported as found.</summary>
    public static int Find(ReadOnlySpan<byte> ai, byte id)
    {
        for (int k = 0; k < ai[List]; k++)
            if (ai[Record(k) + 1] == id) return id != 0 ? Record(k) : -1;
        return -1;
    }

    /// <summary>2010B: reuses (and clears) the record of this number or appends one; −1 when 50 squads exist.</summary>
    public static int Add(Span<byte> ai, byte participant, byte id)
    {
        int at = Find(ai, id);
        if (at < 0)
        {
            if (ai[List] == Capacity) return -1;
            at = Record(ai[List]++);
        }
        ai.Slice(at, Size).Clear(); ai[at] = participant; ai[at + 1] = id;
        return at;
    }

    /// <summary>20143: removes the first squad with this number, releases the number and closes the gap.</summary>
    public static void Remove(Span<byte> ai, byte id)
    {
        int count = ai[List];
        for (int k = 0; k < count; k++)
        {
            if (ai[Record(k) + 1] != id) continue;
            Release(ai, id);
            int tail = (count - k - 1) * Size;
            ai.Slice(Record(k + 1), tail).CopyTo(ai.Slice(Record(k), tail));
            ai[List]--;
            return;
        }
    }

    /// <summary>20189: the last squad with the lowest priority byte +4, or −1 when there are none.</summary>
    public static int Weakest(ReadOnlySpan<byte> ai)
    {
        int found = -1; byte lowest = 0xff;
        for (int k = 0; k < ai[List]; k++)
            if (ai[Record(k) + 4] <= lowest) { found = Record(k); lowest = ai[Record(k) + 4]; }
        return found;
    }

    /// <summary>201B8: members leave the squad (+1B = 0), then the squad is removed.</summary>
    public static void Disband(Span<byte> ai, IEnumerable<byte[]> chain, byte id) => Transfer(ai, chain, id, 0);

    /// <summary>201F6: members move to squad <paramref name="to"/>, then the squad is removed.</summary>
    public static void Transfer(Span<byte> ai, IEnumerable<byte[]> chain, byte id, byte to)
    {
        foreach (var record in chain) if (record[0x1b] == id) record[0x1b] = to;
        Remove(ai, id);
    }

    /// <summary>200B5: a new empty squad of the settlement (participant +0 of the block); with 50 squads the weakest is
    /// disbanded first. Returns the record offset or −1. When the list is full after all, the original releases pool byte
    /// «participant» instead of the new number (kept as is).</summary>
    public static int Create(Span<byte> ai, IEnumerable<byte[]> chain)
    {
        if (ai[List] == Capacity)
        {
            int weakest = Weakest(ai);
            if (weakest < 0) return -1;
            Disband(ai, chain, ai[weakest + 1]);
        }
        byte id = Allocate(ai);
        if (id == 0) return -1;
        int at = Add(ai, ai[0], id);
        if (at < 0) Release(ai, ai[0]);
        return at;
    }

    /// <summary>2024D: the first squad whose target area +13..+16 contains the cell.</summary>
    public static int FindInArea(ReadOnlySpan<byte> ai, byte x, byte y)
    {
        for (int k = 0; k < ai[List]; k++)
        {
            int r = Record(k);
            if (x >= ai[r + 0x13] && y >= ai[r + 0x14] && x <= ai[r + 0x15] && y <= ai[r + 0x16]) return r;
        }
        return -1;
    }

    /// <summary>20275: total strength +36 of the squads whose area +17..+1A contains the cell.</summary>
    public static uint StrengthInArea(ReadOnlySpan<byte> ai, byte x, byte y)
    {
        uint sum = 0;
        for (int k = 0; k < ai[List]; k++)
        {
            int r = Record(k);
            if (x >= ai[r + 0x17] && y >= ai[r + 0x18] && x <= ai[r + 0x19] && y <= ai[r + 0x1a]) sum += U32(ai, r + 0x36);
        }
        return sum;
    }

    /// <summary>2098B: the first squad in state +2 whose target object +0F is <paramref name="reference"/>.</summary>
    public static int FindByState(ReadOnlySpan<byte> ai, byte state, uint reference)
    {
        for (int k = 0; k < ai[List]; k++)
            if (ai[Record(k) + 2] == state && U32(ai, Record(k) + 0x0f) == reference) return Record(k);
        return -1;
    }

    /// <summary>205FB: recounts the squad from its members: +22 count, +36 strength (DAT[76]), +3A DAT[74] bits,
    /// +41 bit 1 (a member with OBE[17] bit 0), +42..+49 members per command group, +26..+35 members per DAT[41] kind,
    /// +20/+21 centre cell.</summary>
    public static void Census(Span<byte> squad, IEnumerable<(byte[] Record, byte[] Template)> chain)
    {
        W32(squad, 0x22, 0); W32(squad, 0x36, 0); squad[0x3a] = 0; squad[0x41] &= 0xfd;
        squad.Slice(0x42, 8).Clear(); squad.Slice(0x26, 16).Clear();
        uint sumX = 0, sumY = 0, count = 0;
        foreach (var (record, template) in chain)
        {
            if (record[0x1b] != squad[1]) continue;
            squad[0x41] |= (byte)((record[0x17] << 1) & 2);
            count++;
            W32(squad, 0x36, U32(squad, 0x36) + template[0x76]);
            int command = record[0x14];
            if ((command & 0x40) != 0) command >>= 3;
            squad[0x42 + (command & 7)]++;
            squad[0x3a] |= template[0x74];
            squad[0x26 + (template[0x41] & 0xf)]++;
            sumX += U32(record, 4); sumY += U32(record, 8);
        }
        W32(squad, 0x22, count);
        if (count == 0) return;
        squad[0x20] = (byte)((sumX >> 5) / count); squad[0x21] = (byte)((sumY >> 5) / count);
    }

    private static IEnumerable<byte[]> Members(IEnumerable<byte[]> chain, byte squad, byte command)
    {
        int bit = 1 << ((command >> 3) & 7);
        foreach (var record in chain) if (record[0x1b] == squad && (record[0x0f] & bit) != 0) yield return record;
    }
    private static void SetCommand(byte[] record, byte command) => record[0x14] = (byte)((record[0x14] & 7) | command);

    /// <summary>B765: members allowed the command (OBE[0F] bit (command >> 3) &amp; 7) get it with target cell +22/+23.</summary>
    public static void Order(IEnumerable<byte[]> chain, byte command, byte squad, byte x, byte y)
    {
        foreach (var r in Members(chain, squad, command)) { r[0x22] = x; r[0x23] = y; SetCommand(r, command); }
    }

    /// <summary>B7B9: as <see cref="Order"/>, keeping the formation: each member is sent by the offset from the squad
    /// centre to the target; a cell beyond the map width/height falls back to the target itself.</summary>
    public static void OrderFormation(IEnumerable<byte[]> chain, byte command, byte squad, byte x, byte y, uint limitX, uint limitY, byte mapWidth, byte mapHeight)
    {
        var members = chain as IReadOnlyCollection<byte[]> ?? chain.ToList();
        var (minX, minY, w, h) = Bounds(members, squad, limitX, limitY);
        byte dx = (byte)(x - (byte)((w + minX * 2) >> 6)), dy = (byte)(y - (byte)((h + minY * 2) >> 6));
        foreach (var r in Members(members, squad, command))
        {
            byte cx = (byte)((U32(r, 4) >> 5) + dx), cy = (byte)((U32(r, 8) >> 5) + dy);
            r[0x22] = cx < mapWidth ? cx : x; r[0x23] = cy < mapHeight ? cy : y;
            SetCommand(r, command);
        }
    }

    /// <summary>B851: command with a 32-bit parameter in +1C.</summary>
    public static void OrderParameter(IEnumerable<byte[]> chain, byte command, byte squad, uint parameter)
    {
        foreach (var r in Members(chain, squad, command)) { W32(r, 0x1c, parameter); SetCommand(r, command); }
    }

    /// <summary>B8A2: command without arguments.</summary>
    public static void Command(IEnumerable<byte[]> chain, byte command, byte squad)
    {
        foreach (var r in Members(chain, squad, command)) SetCommand(r, command);
    }

    /// <summary>B8EE: command with a second cell in +32/+33.</summary>
    public static void OrderPoint(IEnumerable<byte[]> chain, byte command, byte squad, byte x, byte y)
    {
        foreach (var r in Members(chain, squad, command)) { r[0x32] = x; r[0x33] = y; SetCommand(r, command); }
    }

    /// <summary>B942: every member drops its command (+14 keeps the low three bits).</summary>
    public static void Clear(IEnumerable<byte[]> chain, byte squad)
    {
        foreach (var r in chain) if (r[0x1b] == squad) r[0x14] &= 7;
    }

    /// <summary>B977: minimum corner and size of the members' positions (fixed point, 32 per cell); the minimum starts at
    /// the limits [18E6]/[18EA]. All zero when there are no members.</summary>
    public static (uint MinX, uint MinY, uint Width, uint Height) Bounds(IEnumerable<byte[]> chain, byte squad, uint limitX, uint limitY)
    {
        uint minX = limitX, minY = limitY, maxX = 0, maxY = 0;
        foreach (var r in chain)
        {
            if (r[0x1b] != squad) continue;
            uint x = U32(r, 4), y = U32(r, 8);
            if (minX > x) minX = x;
            if (maxX < x) maxX = x;
            if (minY > y) minY = y;
            if (maxY < y) maxY = y;
        }
        if (maxX < minX || maxY < minY) return (0, 0, 0, 0);
        return (minX, minY, maxX - minX, maxY - minY);
    }
}
