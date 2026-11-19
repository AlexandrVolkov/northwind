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

/// <summary>World seen by the squad steps: the settlement chains, the object grid (three planes of references),
/// the hostility masks of participants (+12 of the participant record) and the step-4 hook.</summary>
public interface IAiSquadWorld
{
    IReadOnlyList<(byte[] Record, byte[] Template)> Chain(byte participant);
    (byte[] Record, byte[] Template) Resolve(uint reference);
    uint Occupant(int plane, int cell);
    byte MapWidth { get; }
    byte MapHeight { get; }
    uint HostileMask(byte participant);
    /// <summary>Initial minimum of the squad bounds B977 ([18E6], [18EA]).</summary>
    (uint X, uint Y) BoundsLimit { get; }
    /// <summary>Step 4 (LE:1 1EF26, not ported yet): returns true while the step continues.</summary>
    bool StepFour(Span<byte> squad, bool started);
}

/// <summary>Squad steps of LE:1 1ED26 (table LE:3 F06A): 0 idle, 1 stand (command 40h), 2 gather in the step area
/// +4D..+50 (48h), 3 clear the area (targets 50h/58h), 4 not ported (hook), 5 march in formation (48h). The step runs
/// in +4A; a new step requested in +4B (below 80h) starts with stage +4C = 0; when a step finishes, +4B becomes 0.</summary>
public static class OriginalAiSquadSteps
{
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    /// <summary>1ED26: every squad of the block: census each 32 ticks, tick counter, step switch and the step.</summary>
    public static void Tick(Span<byte> ai, IAiSquadWorld world)
    {
        int count = ai[OriginalAiSquads.List];
        for (int k = 0; k < count; k++)
        {
            var squad = ai.Slice(OriginalAiSquads.Record(k), OriginalAiSquads.Size);
            var chain = world.Chain(squad[0]);
            if ((U32(squad, 0x1b) & 0x1f) == 0) OriginalAiSquads.Census(squad, chain);
            W32(squad, 0x1b, U32(squad, 0x1b) + 1);
            bool started = squad[0x4b] < 0x80;
            if (started) { squad[0x4a] = squad[0x4b]; squad[0x4c] = 0; squad[0x4b] = 0xff; }
            if (!Step(squad, chain, world, started)) squad[0x4b] = 0;
        }
    }

    private static IEnumerable<byte[]> Records(IReadOnlyList<(byte[] Record, byte[] Template)> chain) => chain.Select(m => m.Record);

    /// <summary>Runs step +4A; false when the step has finished.</summary>
    public static bool Step(Span<byte> squad, IReadOnlyList<(byte[] Record, byte[] Template)> chain, IAiSquadWorld world, bool started)
    {
        switch (squad[0x4a])
        {
            case 0: return !started; // 1ED77 returns the flags of the switch test
            case 1: OriginalAiSquads.Command(Records(chain), 0x40, squad[1]); return false;
            case 2: return Gather(squad, chain, world);
            case 3: return Clear(squad, chain, world);
            case 4: return world.StepFour(squad, started);
            case 5:
                if (squad[0x4c] != 0) return squad[0x43] != 0;
                var limit = world.BoundsLimit;
                OriginalAiSquads.OrderFormation(Records(chain).ToList(), 0x48, squad[1], squad[0x4d], squad[0x4e], limit.X, limit.Y, world.MapWidth, world.MapHeight);
                squad[0x4c] = 1; W32(squad, 0x1b, 0); return true;
            default: throw new InvalidDataException($"Squad step {squad[0x4a]} is outside table F06A");
        }
    }

    /// <summary>1ED87: move to the centre of the area, then wait until fewer than half the members move (+43) and all
    /// stand inside the area; up to three moves.</summary>
    private static bool Gather(Span<byte> squad, IReadOnlyList<(byte[] Record, byte[] Template)> chain, IAiSquadWorld world)
    {
        if (squad[0x4c] == 0) { squad[0x51] = 3; squad[0x4c]++; return Move(squad, chain); }
        if ((uint)squad[0x43] * 2 >= U32(squad, 0x22)) return true;
        if (--squad[0x51] == 0) return false;
        var limit = world.BoundsLimit;
        var (minX, minY, w, h) = OriginalAiSquads.Bounds(Records(chain), squad[1], limit.X, limit.Y);
        byte x0 = (byte)(minX >> 5), y0 = (byte)(minY >> 5), x1 = (byte)((w + minX) >> 5), y1 = (byte)((h + minY) >> 5);
        if (x0 < squad[0x4d] || y0 < squad[0x4e] || x1 > squad[0x4f] || y1 > squad[0x50]) return Move(squad, chain);
        return false;
    }

    private static bool Move(Span<byte> squad, IReadOnlyList<(byte[] Record, byte[] Template)> chain)
    {
        byte x = (byte)((byte)(squad[0x4f] + squad[0x4d]) >> 1), y = (byte)((byte)(squad[0x50] + squad[0x4e]) >> 1);
        OriginalAiSquads.Order(Records(chain), 0x48, squad[1], x, y);
        W32(squad, 0x1b, 0);
        return true;
    }

    /// <summary>1EE02: on every tick except each eighth look for an enemy in the area (20458). None — 207CB: everyone stands, those outside
    /// walk back to the centre, and the step ends. Otherwise up to five idle members, members with command 1 and members
    /// attacking a target outside the area get the enemy nearest to them (20408): command 58h against a DAT[40] bit 0
    /// target when the member has DAT[40] 1000h and WorkFlags bit 3, else 50h with WorkFlags bit 2.</summary>
    private static bool Clear(Span<byte> squad, IReadOnlyList<(byte[] Record, byte[] Template)> chain, IAiSquadWorld world)
    {
        if ((U32(squad, 0x1b) & 7) == 0) return true;
        var (enemy, _) = NearestEnemy(world, squad[0x3a], squad[0], squad[0x20] * 32u, squad[0x21] * 32u, squad[0x4d], squad[0x4e], squad[0x4f], squad[0x50]);
        if (enemy == 0) { HoldArea(squad, chain); return false; }
        int budget = 5;
        foreach (var (member, template) in chain)
        {
            if (member[0x1b] != squad[1]) continue;
            bool retarget = false;
            if (member[0x14] == 2 && U32(member, 0x1c) != 0)
            {
                var target = world.Resolve(U32(member, 0x1c)).Record;
                byte tx = (byte)(U32(target, 4) >> 5), ty = (byte)(U32(target, 8) >> 5);
                retarget = tx < squad[0x4d] || tx > squad[0x4f] || ty < squad[0x4e] || ty > squad[0x50];
            }
            if (!retarget) retarget = member[0x14] == 1 || (member[0x14] == 0 && member[0x15] == 0);
            if (!retarget || budget == 0) continue;
            budget--;
            var (found, _) = NearestEnemy(world, template[0x74], member[3], U32(member, 4), U32(member, 8), squad[0x4d], squad[0x4e], squad[0x4f], squad[0x50]);
            if (found == 0) return true;
            if (U32(member, 0x1c) == found) continue;
            W32(member, 0x1c, found);
            var targetTemplate = world.Resolve(found).Template;
            if ((U32(template, 0x40) & 0x1000) != 0 && (U32(targetTemplate, 0x40) & 1) != 0)
            { if ((member[0x0f] & 8) != 0) member[0x14] = (byte)((member[0x14] & 7) | 0x58); }
            else if ((member[0x0f] & 4) != 0) member[0x14] = (byte)((member[0x14] & 7) | 0x50);
        }
        return true;
    }

    /// <summary>207CB: members stand (40h); those outside the area walk to its centre (48h).</summary>
    private static void HoldArea(ReadOnlySpan<byte> squad, IReadOnlyList<(byte[] Record, byte[] Template)> chain)
    {
        uint x0 = squad[0x4d] * 32u, x1 = squad[0x4f] * 32u, y0 = squad[0x4e] * 32u, y1 = squad[0x50] * 32u;
        byte cx = (byte)((x0 + x1) >> 6), cy = (byte)((y0 + y1) >> 6);
        foreach (var (r, _) in chain)
        {
            if (r[0x1b] != squad[1]) continue;
            r[0x14] = (byte)((r[0x14] & 7) | 0x40);
            uint x = U32(r, 4), y = U32(r, 8);
            if (x < x0 || x > x1 || y < y0 || y > y1) { r[0x22] = cx; r[0x23] = cy; r[0x14] = (byte)((r[0x14] & 7) | 0x48); }
        }
    }

    /// <summary>20408/20458 with 2055C: the nearest (Chebyshev, ties keep the first) enemy object in the cell area, on the
    /// grid planes selected by bits 0..2 of <paramref name="planes"/>, visible to <paramref name="owner"/> (OBE[20] bit),
    /// alive above DAT[46], not DAT[40] 8020h, and hostile (owner bit of the mask) or DAT[40] 400000h. Also returns the sum
    /// of DAT[76] of every match (an object counts once per cell it occupies). The best distance starts at NOT origin Y.</summary>
    public static (uint Reference, uint Strength) NearestEnemy(IAiSquadWorld world, byte planes, byte owner, uint originX, uint originY,
        byte x0, byte y0, byte x1, byte y1)
    {
        byte visible = (byte)(owner < 8 ? 1 << owner : 0); uint hostile = world.HostileMask(owner); // 1 << owner by SHL AL, CL
        uint best = ~originY, strength = 0, found = 0;
        int width = world.MapWidth;
        byte columns = (byte)(x1 - x0 + 1), rows = (byte)(y1 - y0 + 1);
        int cell = width * y0 + x0;
        do
        {
            byte left = columns;
            do
            {
                for (int plane = 0; plane < 3; plane++)
                {
                    if ((planes & (1 << plane)) == 0) continue;
                    uint reference = world.Occupant(plane, cell);
                    if (reference == 0) continue;
                    var (record, template) = world.Resolve(reference);
                    if ((record[0x20] & visible) == 0) continue;
                    uint flags = U32(template, 0x40);
                    if ((flags & 0x8020) != 0 || (sbyte)record[0x19] <= (sbyte)template[0x46]) continue;
                    if ((flags & 0x400000) == 0 && (hostile >> (record[3] & 31) & 1) == 0) continue;
                    strength += template[0x76];
                    int sx = unchecked((int)(U32(record, 4) - originX)), sy = unchecked((int)(U32(record, 8) - originY));
                    uint dx = unchecked((uint)(sx < 0 ? -sx : sx)), dy = unchecked((uint)(sy < 0 ? -sy : sy));
                    uint distance = Math.Max(dx, dy);
                    if (distance < best) { best = distance; found = reference; }
                }
                cell++;
            } while (--left != 0);
            cell += width - columns;
        } while (--rows != 0);
        return (found, strength);
    }
}

/// <summary>World of the squad states (1F078): random streams 9E41/9E55, the fog map [18DA], the global byte
/// [19E9] read by state 6, and hooks for what is not ported yet: messages A4C2, the stuck-member rescue 20001 and the
/// worker states 9 and 10.</summary>
public interface IAiSquadStateWorld : IAiSquadWorld
{
    byte Global19E9 { get; }
    byte Random();
    byte Random2();
    /// <summary>[18DA]: per cell, bit n set while participant n has not seen the cell (1EBEF patrols there; 22613 sites
    /// mines only where the bit is clear).</summary>
    byte Fog(int cell);
    /// <summary>A4C2 at the squad centre with the text id from table LE:3 F02E.</summary>
    void Message(ReadOnlySpan<byte> squad, uint text);
    void Rescue(Span<byte> squad);
    void WorkerState(Span<byte> squad);
}

/// <summary>Squad states of LE:1 1F078 (table LE:3 F083), states 0..8; 9 and 10 (workers) are hooks. Requests the states
/// make go to lists +1AB (military) and +34F (orders) through 1EB5C; bytes the original fills from register leftovers
/// (+3 where noted, +5..+7, +8 when EBP is a leftover) are written as zero. See docs/ai.md, «Состояния отряда».</summary>
public sealed class OriginalAiSquadStates(IAiSquadStateWorld world, byte[] arctangent, byte[] messages)
{
    public const int Military = 0x1ab, Orders = 0x34f;
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    // 21CA0 results: [F0E0] count, [F0D0..F0DF] kinds, [F0CC] strength, [F0E4]/[F0E5] approximate centre ([F0E5] is not reset).
    public uint EnemyCount, EnemyStrength;
    public readonly byte[] EnemyKinds = new byte[16];
    public byte EnemyX = 0xff, EnemyY;

    /// <summary>1F078: runs the state of every squad; a dead target +0F is forgotten; +19E8 counts squads in state 8.</summary>
    public void Run(Span<byte> ai)
    {
        ai[0x19e8] = 0;
        int k = 0;
        while (k < ai[OriginalAiSquads.List])
        {
            int count = ai[OriginalAiSquads.List];
            var squad = ai.Slice(OriginalAiSquads.Record(k), OriginalAiSquads.Size);
            if (U32(squad, 0x0f) != 0 && (sbyte)world.Resolve(U32(squad, 0x0f)).Record[0x19] <= 0) W32(squad, 0x0f, 0);
            if (squad[2] == 8) ai[0x19e8]++;
            State(ai, OriginalAiSquads.Record(k));
            if (count > ai[OriginalAiSquads.List]) continue;
            k++;
        }
    }

    private void State(Span<byte> ai, int at)
    {
        var s = ai.Slice(at, OriginalAiSquads.Size);
        switch (s[2])
        {
            case 0: Idle(ai, at); break;
            case 1: GoToPoint(ai, at); break;
            case 2: HoldArea(ai, at); break;
            case 3: AttackArea(ai, at); break;
            case 4: AttackObject(ai, at); break;
            case 5: ChaseObject(ai, at); break;
            case 6: Retreating(ai, at); break;
            case 7: JoinSquad(ai, at); break;
            case 8: Patrol(ai, at); break;
            case 9: case 10: world.WorkerState(s); break;
            default: throw new InvalidDataException($"Squad state {s[2]} is outside table F083");
        }
    }

    private IReadOnlyList<(byte[] Record, byte[] Template)> Chain(ReadOnlySpan<byte> s) => world.Chain(s[0]);
    private void Disband(Span<byte> ai, int at) => OriginalAiSquads.Disband(ai, Chain(ai.Slice(at)).Select(m => m.Record), ai[at + 1]);
    private static byte Step(ReadOnlySpan<byte> s) => (s[0x4b] & 0x80) != 0 ? s[0x4a] : s[0x4b]; // 1F06D
    private static void ToIdle(Span<byte> s, byte priority) { s[4] = priority; s[2] = 0; s[5] = 0; } // 1F3E9
    private static void SetMode(Span<byte> s, int mode) { if (mode is 0 or 1) s[7] = 2; } // 1FE78 writes 2 for both
    private static void Counter(Span<byte> ai, int offset, byte owner) => ai[offset + 3 * owner]++; // 1ECF8 (+17C) / 1ED0F (+17D)

    /// <summary>1EB5C: add to a 20-record list unless a record with this mode and +1 exists (a 0FFh mode is always added).</summary>
    public static void Request(Span<byte> ai, int list, byte mode, byte first, byte third, byte fourth, uint parameter)
    {
        for (int k = 0; k < 20; k++)
            if (ai[list + k * 0x15] == mode && ai[list + k * 0x15 + 1] == first && mode != 0xff) return;
        for (int k = 0; k < 20; k++)
        {
            var r = ai.Slice(list + k * 0x15, 0x15);
            if (r[0] != 0xff) continue;
            r[0] = mode; r[1] = first; r[2] = 0; r[3] = third; r[4] = fourth; r[5] = r[6] = r[7] = 0;
            W32(r, 8, parameter); W32(r, 0x0c, 0); W32(r, 0x10, 0); r[0x14] = 0;
            return;
        }
    }

    private static byte Radius(ReadOnlySpan<byte> s) { uint r = (U32(s, 0x22) >> 2) + 12; return r > 0x19 ? (byte)0x19 : (byte)r; }

    private (byte, byte, byte, byte) Clip(byte x, byte y, byte w, byte h) => OriginalTargetRules.Clip(x, y, w, h, world.MapWidth, world.MapHeight);

    /// <summary>1FECE: area +17..+1A centred on the object.</summary>
    private void AreaAround(Span<byte> s, uint reference)
    {
        var o = world.Resolve(reference).Record;
        byte r = Radius(s);
        (s[0x17], s[0x18], s[0x19], s[0x1a]) = Clip((byte)(U32(o, 4) >> 5), (byte)(U32(o, 8) >> 5), r, r);
    }

    /// <summary>205E7: enemy near the squad centre (area 1F011, restored afterwards).</summary>
    private (uint Reference, uint Strength) NearCentre(Span<byte> s)
    {
        byte r = Radius(s);
        var (x0, y0, x1, y1) = Clip(s[0x20], s[0x21], r, r);
        return OriginalAiSquadSteps.NearestEnemy(world, s[0x3a], s[0], s[0x20] * 32u, s[0x21] * 32u, x0, y0, x1, y1);
    }

    private (uint Reference, uint Strength) InStepArea(ReadOnlySpan<byte> s) =>
        OriginalAiSquadSteps.NearestEnemy(world, s[0x3a], s[0], s[0x20] * 32u, s[0x21] * 32u, s[0x4d], s[0x4e], s[0x4f], s[0x50]);

    /// <summary>21CA0: hostile objects of the area (D7B1) counted into the enemy census fields.</summary>
    /// <summary>21CA0 for another caller (209B0): true when hostile objects are in the area.</summary>
    public bool AreaCensus(byte participant, byte x0, byte y0, byte x1, byte y1) => Census(participant, x0, y0, x1, y1);
    private bool Census(byte participant, byte x0, byte y0, byte x1, byte y1)
    {
        uint hostile = world.HostileMask(participant);
        EnemyStrength = 0; EnemyCount = 0; Array.Clear(EnemyKinds); EnemyX = 0xff;
        uint size = unchecked((uint)(x1 | y1 << 8) - (uint)(x0 | y0 << 8) + 0x101);
        foreach (uint reference in OriginalTargetRules.Collect(new Grid(world), x0, y0, (byte)size, (byte)(size >> 8)))
        {
            var (o, t) = world.Resolve(reference);
            if ((hostile >> (o[3] & 31) & 1) == 0) continue;
            EnemyCount++; EnemyKinds[t[0x41] & 0xf]++; EnemyStrength += t[0x76];
            if (EnemyX == 0xff) { EnemyX = (byte)(U32(o, 4) >> 5); EnemyY = (byte)(U32(o, 8) >> 5); }
            else { EnemyX = (byte)((EnemyX >> 1) + (byte)(U32(o, 4) >> 6)); EnemyY = (byte)((EnemyY >> 1) + (byte)(U32(o, 8) >> 6)); }
        }
        return EnemyCount != 0;
    }
    private bool CensusStep(ReadOnlySpan<byte> s) => Census(s[0], s[0x4d], s[0x4e], s[0x4f], s[0x50]);  // 1FF2E
    private bool CensusHome(ReadOnlySpan<byte> s) => Census(s[0], s[0x17], s[0x18], s[0x19], s[0x1a]);  // 1FF51

    private sealed class Grid(IAiSquadWorld world) : IEnemySearchWorld
    {
        public byte MapWidth => world.MapWidth;
        public byte MapHeight => world.MapHeight;
        public uint HostileMask => throw new NotSupportedException();
        public uint Occupant(int plane, int cell) => world.Occupant(plane, cell);
        public (byte[] Record, byte[] Template) Resolve(uint reference) => world.Resolve(reference);
        public bool Contact(byte owner) => throw new NotSupportedException();
        public byte TerrainClass(ReadOnlySpan<byte> record) => throw new NotSupportedException();
        public void Contacted(FirstContact contact) => throw new NotSupportedException();
    }

    /// <summary>1FF19: distance of a cell from the centre; the original compares both coordinates with +20.</summary>
    private static byte Distance(ReadOnlySpan<byte> s, byte x, byte y)
    {
        byte dx = (byte)(x >= s[0x20] ? x - s[0x20] : s[0x20] - x), dy = (byte)(y >= s[0x20] ? y - s[0x20] : s[0x20] - y);
        return Math.Max(dx, dy);
    }

    /// <summary>1FF74: enemy strength in the step area above the squad's own, minus friendly squads around the target
    /// area centre, plus block +0A; 0 when not stronger.</summary>
    private uint EnemyExcess(ReadOnlySpan<byte> ai, ReadOnlySpan<byte> s)
    {
        var (_, strength) = InStepArea(s);
        if (strength < U32(s, 0x36)) return 0;
        uint own = OriginalAiSquads.StrengthInArea(ai, (byte)((byte)(s[0x15] + s[0x13]) >> 1), (byte)((byte)(s[0x16] + s[0x14]) >> 1));
        if (strength <= own) return 0;
        return strength - own + U32(ai, 0x0a);
    }

    /// <summary>1FFB7: enemies of the area +17..+1A (21CA0, strength scaled by block +0E and shifted by +77) stronger than
    /// the friendly squads at its centre; returns the shortfall.</summary>
    private uint EnemyStronger(ReadOnlySpan<byte> ai, ReadOnlySpan<byte> s)
    {
        CensusHome(s);
        uint enemy = unchecked(EnemyStrength * U32(ai, 0x0e)) >> 5 >> (ai[0x77] & 31);
        uint own = OriginalAiSquads.StrengthInArea(ai, (byte)((byte)(s[0x19] + s[0x17]) >> 1), (byte)((byte)(s[0x1a] + s[0x18]) >> 1));
        return enemy > own ? enemy - own : 0;
    }

    /// <summary>20071: squad message by the AI character (+29) from table LE:3 F02E, when the squad has more than 10 members.</summary>
    private void Say(ReadOnlySpan<byte> ai, ReadOnlySpan<byte> s, int parameter)
    {
        if (U32(s, 0x22) <= 10) return;
        world.Message(s, U32(messages, 12 * ai[0x29] + parameter));
    }

    /// <summary>1FC6B: march in formation (step 4, kind 4) to the cell +0D/+0E; false when finished or off the map.</summary>
    private static bool MoveTo(Span<byte> s, byte mapWidth, byte mapHeight)
    {
        if (s[6] == 1 || s[6] == 0) { if (s[6] == 1 && Step(s) == 4) return true; }
        else return Step(s) == 4;
        byte x = s[0x0d], y = s[0x0e];
        if (x >= mapWidth || y >= mapHeight) return false;
        s[8] = 4; s[9] = x; s[0x0a] = y; s[0x0b] = (byte)(x + 1); s[0x0c] = y; s[0x4b] = 4; // 1EFB8
        s[6] = 2;
        return true;
    }

    /// <summary>1FC28: go to the centre of +17..+1A. Returns (moving, arrived): arrived when the centre is already inside.</summary>
    private (bool Moving, bool Arrived) GoHome(Span<byte> s)
    {
        if (s[6] == 0) { s[0x0d] = (byte)((byte)(s[0x17] + s[0x19]) >> 1); s[0x0e] = (byte)((byte)(s[0x18] + s[0x1a]) >> 1); }
        else if (s[0x20] >= s[0x17] && s[0x21] >= s[0x18] && s[0x20] <= s[0x19] && s[0x21] <= s[0x1a]) return (false, true);
        return (MoveTo(s, world.MapWidth, world.MapHeight), false);
    }

    /// <summary>1FBB1: retreat five cells away from the enemies of +17..+1A, then march there.</summary>
    private bool Retreat(ReadOnlySpan<byte> ai, Span<byte> s)
    {
        if (s[6] == 0)
        {
            if (!CensusHome(s)) return false;
            byte direction = (byte)((byte)(Direction(EnemyX, EnemyY, s[0x20], s[0x21]) + 0x10) >> 5);
            var (dx, dy) = Directions[direction & 7];
            byte x = (byte)(dx * 5 + s[0x20]);
            if (x >= world.MapWidth) return false;
            s[0x0d] = x;
            byte y = (byte)(dy * 5 + s[0x21]);
            if (y >= world.MapHeight) return false;
            s[0x0e] = y;
            Say(ai, s, 8);
        }
        return MoveTo(s, world.MapWidth, world.MapHeight);
    }
    private static readonly (int X, int Y)[] Directions = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)]; // DS:19C7

    /// <summary>6996: direction byte (0..255, 0 = north, clockwise) from (x1, y1) to (x2, y2) with the arctangent table DS:287E.</summary>
    public byte Direction(uint x1, uint y1, uint x2, uint y2)
    {
        byte quadrant = 0x40;
        uint ecx = x2, eax = x1, edx = y2, ebx = y1;
        if (ecx < eax) { ecx = eax - ecx; (edx, ebx) = (ebx, edx); quadrant += 0x80; } else ecx -= eax;
        if (edx < ebx) { edx = ebx - edx; (edx, ecx) = (ecx, edx); quadrant -= 0x40; } else edx -= ebx;
        if (ecx == edx) return (byte)(0x20 + quadrant);
        if (ecx > edx) return (byte)(arctangent[(byte)((edx << 8) / (ecx & 0xffff))] + quadrant);
        return (byte)(0x40 - arctangent[(byte)((ecx << 8) / (edx & 0xffff))] + quadrant);
    }

    /// <summary>1FCED: fight in the area, by mode +7 (0: chase the enemy centre, 1: march, then clear, 2: clear +17..+1A).
    /// True while the fight goes on.</summary>
    private bool Engage(Span<byte> s)
    {
        switch (s[7])
        {
            case 0:
                if (s[6] != 0 && (U32(s, 0x1b) & 0x1f) != 0) return true;
                byte r = Radius(s);
                (s[0x4d], s[0x4e], s[0x4f], s[0x50]) = Clip(s[0x20], s[0x21], r, r);
                if (!CensusStep(s)) return false;
                byte kind = Distance(s, EnemyX, EnemyY) > (byte)((byte)(s[0x4f] - s[0x4d]) >> 1) ? (byte)4 : (byte)3;
                if (s[6] != kind) { s[6] = kind; Formation(s, (byte)(kind - 1), s[0x20], s[0x21], EnemyX, EnemyY); }
                return true;
            case 1:
                for (;;)
                    switch (s[6])
                    {
                        case 0:
                            if (!CensusHome(s)) return false;
                            if (Distance(s, EnemyX, EnemyY) <= 6) return Clear(s, 3);
                            Formation(s, 2, s[0x20], s[0x21], EnemyX, EnemyY); s[6] = 1; return true;
                        case 1:
                            if (Step(s) == 4) return true;
                            if (!CensusHome(s)) return false;
                            s[0x4d] = EnemyX; s[0x4e] = EnemyY; s[0x4b] = 5; s[6] = 2; return true; // 1EFF9
                        case 2:
                            if ((U32(s, 0x1b) & 0xf) != 0) return true;
                            if (!CensusHome(s)) return false;
                            if (Distance(s, EnemyX, EnemyY) > 6) return true;
                            return Clear(s, 3);
                        case 3: return Step(s) == 3;
                        default: throw new InvalidOperationException("1FCED loops forever on this stage"); // 1FDE5 <-> 1FE2E
                    }
            case 2:
                if (s[6] != 0) return Step(s) == 3;
                return Clear(s, 1);
            default: return true;
        }
    }
    private static bool Clear(Span<byte> s, byte stage)
    {
        (s[0x4d], s[0x4e], s[0x4f], s[0x50]) = (s[0x17], s[0x18], s[0x19], s[0x1a]); // 1F054
        s[0x4b] = 3; s[8] = 0; s[6] = stage; return true; // 1EF21
    }
    private static void Formation(Span<byte> s, byte kind, byte x, byte y, byte tx, byte ty)
    { s[9] = x; s[0x0a] = y; s[0x0b] = tx; s[0x0c] = ty; s[8] = kind; s[0x4b] = 4; } // 1EFB8

    /// <summary>State 0 (1F31E): stand; fight an enemy that comes near, retreat from a stronger one.</summary>
    private void Idle(Span<byte> ai, int at)
    {
        var s = ai.Slice(at, OriginalAiSquads.Size);
        if (U32(s, 0x22) <= 1) { Disband(ai, at); return; }
        if (s[5] == 0)
        {
            var (enemy, _) = NearCentre(s);
            if (enemy != 0) { Engaged(ai, s, enemy); return; }
            s[5] = 1;
        }
        if (s[5] == 1)
        {
            if ((U32(s, 0x1b) & 0x1f) != 0) return;
            var (enemy, _) = NearCentre(s);
            if (enemy != 0) { Engaged(ai, s, enemy); return; }
            if ((U32(s, 0x1b) & 0x3ff) == 0) world.Rescue(s);
            if ((world.Random() & 0x1f) == 0) Say(ai, s, 0);
            return;
        }
        if (s[5] == 2)
        {
            if ((U32(s, 0x1b) & 0x3f) == 0 && EnemyStronger(ai, s) != 0) { s[5] = 3; s[6] = 0; return; }
            if (!Engage(s)) s[5] = 0;
            return;
        }
        if (!Retreat(ai, s)) s[5] = 0;
    }
    private void Engaged(Span<byte> ai, Span<byte> s, uint enemy)
    {
        Counter(ai, 0x17c, world.Resolve(enemy).Record[3]);
        AreaAround(s, enemy); SetMode(s, 0); s[6] = 0; s[5] = 2;
    }

    /// <summary>State 1 (1F3F5, set by 1F464): march to +0D/+0E; fight on the way; back to state 0 at the end.</summary>
    private void GoToPoint(Span<byte> ai, int at)
    {
        var s = ai.Slice(at, OriginalAiSquads.Size);
        if (s[5] == 0) { s[6] = 0; s[5] = 1; }
        if (s[5] != 1)
        {
            if (Engage(s))
            {
                if ((U32(s, 0x1b) & 0x3f) == 0 && EnemyStronger(ai, s) != 0) ToIdle(s, 0);
                return;
            }
            s[6] = 0; s[5] = 1;
        }
        if (!MoveTo(s, world.MapWidth, world.MapHeight)) { ToIdle(s, 0); return; }
        if ((U32(s, 0x1b) & 0xf) != 0) return;
        var (enemy, strength) = NearCentre(s);
        if (enemy == 0 || ((s[0x41] & 2) == 0 && strength <= 5)) return;
        Counter(ai, 0x17c, world.Resolve(enemy).Record[3]);
        AreaAround(s, enemy); SetMode(s, 1); s[6] = 0; s[5] = 2;
    }

    /// <summary>State 2 (1F1DF, set by 1F306): hold the area +13..+16.</summary>
    private void HoldArea(Span<byte> ai, int at)
    {
        var s = ai.Slice(at, OriginalAiSquads.Size);
        if (U32(s, 0x22) == 0) { Disband(ai, at); return; }
        while (true)
        {
            if (s[5] == 0) { (s[0x17], s[0x18], s[0x19], s[0x1a]) = (s[0x13], s[0x14], s[0x15], s[0x16]); s[5] = 1; s[6] = 0; return; }
            if (s[5] == 1)
            {
                if ((U32(s, 0x1b) & 0xf) == 0)
                {
                    var (enemy, strength) = NearCentre(s);
                    if (enemy != 0 && ((s[0x41] & 2) != 0 || strength > 5))
                    {
                        Counter(ai, 0x17d, world.Resolve(enemy).Record[3]);
                        AreaAround(s, enemy); SetMode(s, 1); s[6] = 0; s[5] = 3; return;
                    }
                }
                var (moving, arrived) = GoHome(s);
                if (moving) return;
                if (!arrived)
                {
                    var (x0, y0, x1, y1) = Clip(s[0x20], s[0x21], 0x14, 0x14);
                    Request(ai, Orders, 0x0a, s[1], s[4], x0, 0);
                    ToIdle(s, 0); return;
                }
                (s[0x17], s[0x18], s[0x19], s[0x1a]) = (s[0x13], s[0x14], s[0x15], s[0x16]);
                SetMode(s, 1); s[6] = 0; s[5] = 2; Say(ai, s, 4); return;
            }
            if (s[5] == 4)
            {
                if (Retreat(ai, s)) return;
                Retreat6(s, 0x20); return;
            }
            if ((U32(s, 0x1b) & 0x3f) == 0)
            {
                uint need = EnemyStronger(ai, s);
                if (need != 0) { Request(ai, Military, 3, s[1], (byte)(need >> 8), s[1], need); s[5] = 4; s[6] = 0; return; }
            }
            if (Engage(s)) return;
            if (s[5] == 3) { s[5] = 0; continue; }
            ToIdle(s, 0); return;
        }
    }
    private static void Retreat6(Span<byte> s, byte time) { s[5] = (byte)(time << 2); s[0x1f] = s[2]; s[2] = 6; } // 1F7B9

    /// <summary>State 3 (1F0D5, set by 1F1C7): gather in the area +13..+16 and clear it.</summary>
    private void AttackArea(Span<byte> ai, int at)
    {
        var s = ai.Slice(at, OriginalAiSquads.Size);
        if (U32(s, 0x22) == 0) { Disband(ai, at); return; }
        if (s[5] == 0) { (s[0x4d], s[0x4e], s[0x4f], s[0x50]) = (s[0x13], s[0x14], s[0x15], s[0x16]); s[0x4b] = 2; s[5] = 1; return; }
        if (s[5] == 1) { if (Step(s) == 2) return; s[5] = 2; }
        if (s[5] == 2)
        {
            var (enemy, _) = InStepArea(s);
            if (enemy == 0) return;
            AreaAround(s, enemy); Counter(ai, 0x17c, world.Resolve(enemy).Record[3]);
            SetMode(s, 0); s[5] = 3; s[6] = 0; return;
        }
        if (s[5] == 3)
        {
            if ((U32(s, 0x1b) & 0x3f) == 0)
            {
                uint excess = EnemyExcess(ai, s);
                if (excess != 0) Request(ai, Military, 3, s[1], (byte)(excess >> 8), s[1], 0);
            }
            if (Engage(s)) return;
            s[5] = 0;
            uint area = (uint)(byte)((byte)(s[0x15] - s[0x13] + 1) * (byte)(s[0x16] - s[0x14] + 1) >> 4) * ai[0x2a]; // 215F1
            if (area > U32(s, 0x36)) Request(ai, Military, 2, s[1], 0, s[1], area - U32(s, 0x36));
            return;
        }
        if (Step(s) == 4) return;
        (s[0x4d], s[0x4e], s[0x4f], s[0x50]) = (s[0x13], s[0x14], s[0x15], s[0x16]); s[0x4b] = 3; s[5] = 3;
    }

    /// <summary>2160B: strength wanted against an object: DAT[45] − DAT[45]/4.</summary>
    private uint ObjectStrength(uint reference) { byte v = world.Resolve(reference).Template[0x45]; return (uint)(v - (v >> 2)); }

    /// <summary>State 4 (1F8CF, set by 1F9EC): go to the object +0F and clear the area around it.</summary>
    private void AttackObject(Span<byte> ai, int at)
    {
        var s = ai.Slice(at, OriginalAiSquads.Size);
        uint target = U32(s, 0x0f);
        if (target == 0) { ToIdle(s, 0); return; }
        if (U32(s, 0x22) == 0) { Disband(ai, at); return; }
        switch (s[5])
        {
            case 0: AreaAround(s, target); s[5] = 1; s[6] = 0; return;
            case 1:
                var (moving, arrived) = GoHome(s);
                if (moving) return;
                if (!arrived) { ToIdle(s, 0); return; }
                s[5] = 2; return;
            case 2:
                if ((U32(s, 0x1b) & 0x1f) != 0) return;
                (s[0x4d], s[0x4e], s[0x4f], s[0x50]) = (s[0x17], s[0x18], s[0x19], s[0x1a]);
                var (enemy, _) = InStepArea(s);
                if (enemy == 0) return;
                Counter(ai, 0x17c, world.Resolve(enemy).Record[3]); SetMode(s, 0); s[5] = 3; s[6] = 0; return;
            case 3:
                if ((U32(s, 0x1b) & 0x3f) == 0)
                {
                    uint need = EnemyStronger(ai, s);
                    if (need != 0)
                    {
                        Request(ai, Military, 3, s[1], (byte)(need >> 8), s[1], need);
                        if (s[4] < 0x40) { s[5] = 4; s[6] = 0; return; }
                    }
                }
                if (Engage(s)) return;
                s[5] = 0;
                uint wanted = ObjectStrength(target);
                if (wanted > U32(s, 0x36)) Request(ai, Military, 2, s[1], 0, s[1], wanted - U32(s, 0x36));
                return;
            default:
                if (Retreat(ai, s)) return;
                Retreat6(s, 0x20); return;
        }
    }

    /// <summary>State 5 (1FAD9, set by 1FB9C): follow the object +0F and fight what is met on the way.</summary>
    private void ChaseObject(Span<byte> ai, int at)
    {
        var s = ai.Slice(at, OriginalAiSquads.Size);
        if (U32(s, 0x0f) == 0) { ToIdle(s, 0); return; }
        if (U32(s, 0x22) == 0) { Disband(ai, at); return; }
        while (true)
        {
            if (s[5] == 0)
            {
                if (U32(s, 0x0f) == 0) { ToIdle(s, 0); return; }
                AreaAround(s, U32(s, 0x0f)); s[5] = 1; s[6] = 0; return;
            }
            if (s[5] == 1)
            {
                if ((U32(s, 0x1b) & 0xf) == 0)
                {
                    var (enemy, strength) = NearCentre(s);
                    if (enemy != 0 && ((s[0x41] & 2) != 0 || strength > 5))
                    {
                        Counter(ai, 0x17d, world.Resolve(enemy).Record[3]);
                        AreaAround(s, enemy); SetMode(s, 1); s[6] = 0; s[5] = 3; return;
                    }
                }
                var (moving, arrived) = GoHome(s);
                if (moving) return;
                if (!arrived || U32(s, 0x0f) == 0) { ToIdle(s, 0); return; }
                SetMode(s, 1); s[6] = 0; s[5] = 2; return;
            }
            if (Engage(s)) return;
            if (s[5] == 3) { s[5] = 0; continue; }
            ToIdle(s, 0); return;
        }
    }

    /// <summary>State 6 (1F72C, entered by 1F7B9): retreat for a while, then return to the state saved in +1F.</summary>
    private void Retreating(Span<byte> ai, int at)
    {
        var s = ai.Slice(at, OriginalAiSquads.Size);
        if (world.Global19E9 == 0xff)
        {
            bool expired = s[5] < 4; s[5] -= 4;
            if (expired) { s[2] = s[0x1f]; s[5] = 0; return; }
        }
        if (U32(s, 0x22) <= 1) { Disband(ai, at); return; }
        switch (s[5] & 3)
        {
            case 0:
                if ((U32(s, 0x1b) & 0x1f) != 0) return;
                var (enemy, _) = NearCentre(s);
                if (enemy == 0) return;
                AreaAround(s, enemy); SetMode(s, 0); s[6] = 0; s[5]++; return;
            case 1:
                if ((U32(s, 0x1b) & 0x3f) == 0 && EnemyStronger(ai, s) != 0) { s[5]++; s[6] = 0; return; }
                if (!Engage(s)) s[5] &= 0xfc;
                return;
            case 2:
                if (!Retreat(ai, s)) s[5] &= 0xfc;
                return;
            default: s[2] = s[0x1f]; s[5] = 0; return;
        }
    }

    /// <summary>State 7 (1F7CA, set by 1F8C0): walk to the squad +13 and merge into it (201F6).</summary>
    private void JoinSquad(Span<byte> ai, int at)
    {
        var s = ai.Slice(at, OriginalAiSquads.Size);
        int other = OriginalAiSquads.Find(ai, s[0x13]);
        if (other < 0) { ToIdle(s, 0); return; }
        if (U32(s, 0x22) == 0) { Disband(ai, at); return; }
        if (s[5] == 0) { s[0x0d] = ai[other + 0x20]; s[0x0e] = ai[other + 0x21]; s[6] = 0; s[5] = 1; }
        if (s[5] != 1) { if (!Engage(s)) s[5] = 0; return; }
        if (MoveTo(s, world.MapWidth, world.MapHeight))
        {
            if ((U32(s, 0x1b) & 0xf) != 0) return;
            byte x = ai[other + 0x20], y = ai[other + 0x21];
            if (s[0x0d] != x || s[0x0e] != y) { s[5] = 0; return; }
            if (Distance(s, x, y) > 6)
            {
                var (enemy, strength) = NearCentre(s);
                if (enemy == 0 || ((s[0x41] & 2) == 0 && strength <= 5)) return;
                AreaAround(s, enemy); SetMode(s, 1); s[6] = 0; s[5] = 2; return;
            }
        }
        ai[other + 5] = (byte)(ai[other + 2] == 6 ? 3 : 0);
        OriginalAiSquads.Transfer(ai, Chain(s).Select(m => m.Record), s[1], ai[other + 1]);
    }

    /// <summary>State 8 (1FA01, set by 1FACD): walk to random explored places (1EBEF) and fight what is met.</summary>
    private void Patrol(Span<byte> ai, int at)
    {
        var s = ai.Slice(at, OriginalAiSquads.Size);
        if (U32(s, 0x22) == 0) { Disband(ai, at); return; }
        if (s[5] == 4) { if (!Retreat(ai, s)) s[5] = 0; return; }
        if ((U32(s, 0x1b) & 0x1f) == 0)
        {
            var (enemy, _) = NearCentre(s);
            if (enemy != 0) { AreaAround(s, enemy); SetMode(s, 0); s[6] = 0; s[5] = 2; return; }
        }
        if (s[5] == 0)
        {
            if ((U32(s, 0x1b) & 0x1f) != 0) return;
            // 1EBEF: a random cell; when the participant has not seen it yet ([18DA] bit set), an area of 20×20 around it.
            byte x = (byte)(world.Random() * world.MapWidth >> 8), y = (byte)(world.Random2() * world.MapHeight >> 8);
            byte bit = (byte)(s[0] < 8 ? 1 << s[0] : 0);
            if ((world.Fog(world.MapWidth * y + x) & bit) == 0) return;
            (s[0x17], s[0x18], s[0x19], s[0x1a]) = Clip(x, y, 0x14, 0x14);
            s[6] = 0; s[5] = 1;
        }
        if (s[5] == 1)
        {
            var (moving, arrived) = GoHome(s);
            if (moving) return;
            if (!arrived)
            {
                var (x0, y0, x1, y1) = Clip(s[0x20], s[0x21], 0x14, 0x14);
                Request(ai, Orders, 0x0a, s[1], 0x40, x0, 0);
            }
            s[5] = 0; return;
        }
        if (!Engage(s)) { s[5] = 0; return; }
        if ((U32(s, 0x1b) & 0xf) != 0) return;
        if (EnemyStronger(ai, s) == 0) return;
        s[5] = 4; s[6] = 0;
        if (!Retreat(ai, s)) s[5] = 0;
    }
}

/// <summary>World of the military requests 20DD0: AI blocks and participant records of every settlement, the 9E41 random
/// stream, the participant count [6C6] and the templates a settlement may build now (22E46).</summary>
public interface IAiMilitaryWorld : IAiSquadWorld
{
    byte Random();
    Span<byte> Block(byte participant);
    ReadOnlySpan<byte> Participant(byte participant);
    byte ParticipantCount { get; }
    /// <summary>223B4 for the settlement: open templates of the class (DAT[40] &amp; mask = value, DAT[9C] ≠ FFh, tree allows).</summary>
    IReadOnlyList<byte> Buildable(byte participant, uint mask, uint value);
    byte[] Template(byte participant, byte template);
    /// <summary>Pool reference (offset from [6AA]) of an object record.</summary>
    uint Reference(byte[] record);
}

/// <summary>Recruitment of squad members (LE:1 2029F, 2035A, 21660/21696, 216DF, 21DD4) and target choice (214C3, 21555, 1EC6E).</summary>
public static class OriginalAiRecruitment
{
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);
    private static uint Chebyshev(uint x1, uint y1, uint x2, uint y2)
    {
        int dx = unchecked((int)(x1 - x2)), dy = unchecked((int)(y1 - y2));
        return Math.Max(unchecked((uint)(dx < 0 ? -dx : dx)), unchecked((uint)(dy < 0 ? -dy : dy)));
    }

    /// <summary>2035A: the nearest object without a squad whose DAT class matches; ties keep the later object.</summary>
    public static (byte[] Record, byte[] Template)? NearestFree(IEnumerable<(byte[] Record, byte[] Template)> chain, uint x, uint y, uint mask, uint value)
    {
        (byte[], byte[])? found = null; uint best = uint.MaxValue;
        foreach (var (r, t) in chain)
        {
            if (r[0x1b] != 0 || (U32(t, 0x40) & mask) != value) continue;
            uint d = Chebyshev(x, y, U32(r, 4), U32(r, 8));
            if (d <= best) { best = d; found = (r, t); }
        }
        return found;
    }

    /// <summary>2029F: free objects nearest to the point join squad <paramref name="id"/> until their DAT[76] reach the wanted
    /// strength (at least one is taken). Returns the strength gathered.</summary>
    public static uint ByStrength(IEnumerable<(byte[] Record, byte[] Template)> chain, byte id, uint x, uint y, uint wanted, uint mask, uint value)
    {
        uint sum = 0;
        while (NearestFree(chain, x, y, mask, value) is { } m)
        {
            m.Record[0x1b] = id; sum += m.Template[0x76];
            if (sum >= wanted) break;
        }
        return sum;
    }

    /// <summary>21DD4: the nearest own object of the class that has no squad, or whose squad is weaker in priority than
    /// <paramref name="priority"/> (or not found); ties keep the later object.</summary>
    public static (byte[] Record, byte[] Template)? NearestForSquad(ReadOnlySpan<byte> ai, IEnumerable<(byte[] Record, byte[] Template)> chain,
        uint x, uint y, byte priority, uint mask, uint value)
    {
        (byte[], byte[])? found = null; uint best = uint.MaxValue;
        foreach (var (r, t) in chain)
        {
            if ((U32(t, 0x40) & mask) != value) continue;
            if (r[0x1b] != 0) { int s = OriginalAiSquads.Find(ai, r[0x1b]); if (s >= 0 && ai[s + 4] >= priority) continue; }
            uint d = Chebyshev(x, y, U32(r, 4), U32(r, 8));
            if (d <= best) { best = d; found = (r, t); }
        }
        return found;
    }

    /// <summary>216DF: a new squad of priority <paramref name="priority"/> takes the nearest suitable objects (also from
    /// lower-priority squads, whose counts are not updated) until its strength +36 reaches the wanted value. A squad
    /// that got nothing is disbanded. Returns the squad offset or −1.</summary>
    public static int Gather(Span<byte> ai, IReadOnlyList<(byte[] Record, byte[] Template)> chain, byte x, byte y, byte priority, uint wanted, uint mask, uint value)
    {
        int at = OriginalAiSquads.Create(ai, chain.Select(m => m.Record));
        if (at < 0) return -1;
        ai[at + 4] = priority;
        while (NearestForSquad(ai, chain, x * 32u, y * 32u, priority, mask, value) is { } m)
        {
            W32(ai, at + 0x36, U32(ai, at + 0x36) + m.Template[0x76]);
            m.Record[0x1b] = ai[at + 1];
            if (U32(ai, at + 0x36) >= wanted) break;
        }
        if (U32(ai, at + 0x36) != 0) return at;
        OriginalAiSquads.Disband(ai, chain.Select(m => m.Record), ai[at + 1]);
        return -1;
    }

    /// <summary>21660/21696: a new squad takes free objects of the class nearest to the cell (2029F); when they do not
    /// reach the wanted strength the squad is disbanded. Returns the squad offset or −1.</summary>
    /// <remarks>EBX on return: the wanted strength when no squad could be created, otherwise the strength gathered.</remarks>
    public static (int Squad, uint Ebx) GatherFree(Span<byte> ai, IReadOnlyList<(byte[] Record, byte[] Template)> chain, byte x, byte y, uint wanted, uint mask, uint value)
    {
        int at = OriginalAiSquads.Create(ai, chain.Select(m => m.Record));
        if (at < 0) return (-1, wanted);
        uint got = ByStrength(chain, ai[at + 1], x * 32u, y * 32u, wanted, mask, value);
        if (got >= wanted) return (at, got);
        OriginalAiSquads.Disband(ai, chain.Select(m => m.Record), ai[at + 1]);
        return (-1, got);
    }

    /// <summary>214C3: a building (DAT[40] bit 3, not 20h) alive above DAT[46] nearest to the home cell; each candidate
    /// passes a coin toss (9E41 bit 0) first.</summary>
    public static (byte[] Record, byte[] Template)? Building(IEnumerable<(byte[] Record, byte[] Template)> chain, ReadOnlySpan<byte> ai, Func<byte> random)
    {
        (byte[], byte[])? found = null; uint best = uint.MaxValue;
        foreach (var (r, t) in chain)
        {
            uint f = U32(t, 0x40);
            if ((f & 8) == 0 || (f & 0x20) != 0 || (sbyte)r[0x19] <= (sbyte)t[0x46] || (random() & 1) == 0) continue;
            uint d = Chebyshev(U32(r, 4), U32(r, 8), ai[0x61] * 32u, ai[0x62] * 32u);
            if (d < best) { best = d; found = (r, t); }
        }
        return found;
    }

    /// <summary>21555: a mine (DAT[40] &amp; F01h = 401h) with ore left of the kind, alive above DAT[46], nearest to the home cell.</summary>
    public static (byte[] Record, byte[] Template)? Mine(IEnumerable<(byte[] Record, byte[] Template)> chain, ReadOnlySpan<byte> ai, byte kind)
    {
        (byte[], byte[])? found = null; uint best = uint.MaxValue;
        foreach (var (r, t) in chain)
        {
            if ((U32(t, 0x40) & 0xf01) != 0x401 || U32(r, 0x24) == 0 || r[0x23] != kind || (sbyte)r[0x19] <= (sbyte)t[0x46]) continue;
            uint d = Chebyshev(U32(r, 4), U32(r, 8), ai[0x61] * 32u, ai[0x62] * 32u);
            if (d < best) { best = d; found = (r, t); }
        }
        return found;
    }

    /// <summary>1EC6E + 218A8: objects in sight of an object (its footprint DAT[3]×DAT[4] grown by 2·DAT[47]), every grid
    /// occurrence counted, without DAT[40] 20h and the dead: friends and enemies of its owner with their DAT[76].</summary>
    public static (uint Friends, uint FriendStrength, uint Enemies, uint EnemyStrength) Sight(IAiSquadWorld world, byte[] record, byte[] template)
    {
        byte grow = (byte)(template[0x47] * 2);
        var (x0, y0, x1, y1) = OriginalTargetRules.Clip((byte)(U32(record, 4) >> 5), (byte)(U32(record, 8) >> 5),
            (byte)(template[3] + grow), (byte)(template[4] + grow), world.MapWidth, world.MapHeight);
        uint hostile = world.HostileMask(record[3]);
        uint fc = 0, fs = 0, ec = 0, es = 0;
        byte rows = (byte)(y1 - y0 + 1), columns = (byte)(x1 - x0 + 1);
        int cell = world.MapWidth * y0 + x0;
        do
        {
            byte left = columns;
            do
            {
                for (int plane = 0; plane < 3; plane++)
                {
                    uint reference = world.Occupant(plane, cell);
                    if (reference == 0) continue;
                    var (r, t) = world.Resolve(reference);
                    if ((U32(t, 0x40) & 0x20) != 0 || (sbyte)r[0x19] <= (sbyte)t[0x46]) continue;
                    if ((hostile >> (r[3] & 31) & 1) != 0) { ec++; es += t[0x76]; } else { fc++; fs += t[0x76]; }
                }
                cell++;
            } while (--left != 0);
            cell += world.MapWidth - columns;
        } while (--rows != 0);
        return (fc, fs, ec, es);
    }
}

/// <summary>Military requests of LE:1 20DD0 (list +1AB, 20 records of 15h; see docs/ai.md, «Военные заявки»).</summary>
public sealed class OriginalAiMilitary(IAiMilitaryWorld world, byte[] gatherClasses)
{
    public const int List = 0x1ab, Orders = 0x34f, Size = 0x15;
    /// <summary>[ED39]/[ED3D]: the class filter of 2029F for mode 2, left by 209B0 (units: 808h/800h).</summary>
    public uint ClassMask = 0x808, ClassValue = 0x800;
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    /// <summary>1EB5C with every byte the call site passes (+4..+7 = CL, CH, DL, DH; +8 = EBP).</summary>
    public static void Request(Span<byte> ai, int list, byte mode, byte first, byte third, byte cl, byte ch, byte dl, byte dh, uint parameter)
    {
        for (int k = 0; k < 20; k++)
            if (ai[list + k * Size] == mode && ai[list + k * Size + 1] == first && mode != 0xff) return;
        for (int k = 0; k < 20; k++)
        {
            var r = ai.Slice(list + k * Size, Size);
            if (r[0] != 0xff) continue;
            r[0] = mode; r[1] = first; r[2] = 0; r[3] = third; r[4] = cl; r[5] = ch; r[6] = dl; r[7] = dh;
            W32(r, 8, parameter); W32(r, 0x0c, 0); W32(r, 0x10, 0); r[0x14] = 0;
            return;
        }
    }

    private IReadOnlyList<(byte[] Record, byte[] Template)> Own(ReadOnlySpan<byte> ai) => world.Chain(ai[0]);

    public void Run(Span<byte> ai)
    {
        if ((ai[0x19e4] & 1) == 0) return;
        for (int k = 0; k < 20; k++)
        {
            int at = List + k * Size;
            if (!Process(ai, at)) ai[at] = 0xff;
            W32(ai, at + 0x10, U32(ai, at + 0x10) + 1);
        }
    }

    /// <summary>One request; false drops it (+0 = FFh).</summary>
    private bool Process(Span<byte> ai, int at)
    {
        var r = ai.Slice(at, Size);
        switch (r[0])
        {
            case 1: return DefendArea(ai, r);
            case 2: return Reinforce(ai, r);
            case 3: return Support(ai, r);
            case 4: return AttackObject(ai, r);
            case 5: return War(ai, r);
            case 6:
                if ((U32(r, 0x10) & 0xff) != 0) return true;
                if (++r[0x14] > 10) return false;
                if (OriginalAiRecruitment.Building(world.Chain(r[4]), ai, world.Random) is not { } building) return true;
                return BecomeChase(ai, r, building.Record);
            case 7: return Campaign(ai, r);
            case 8: return Army(ai, r);
            case 9: return Chase(ai, r);
            case 10: return Plunder(ai, r);
            default: return false;
        }
    }

    /// <summary>Mode 1: a new squad (priority 80h) holds the area +4..+7 (state 2); the strength still missing is ordered
    /// as units (+34F mode 1).</summary>
    private bool DefendArea(Span<byte> ai, Span<byte> r)
    {
        uint wanted = (uint)(byte)((byte)(r[6] - r[4] + 1) * (byte)(r[7] - r[5] + 1) >> 4) * ai[0x2a]; // 215F1
        int s = OriginalAiRecruitment.Gather(ai, Own(ai), r[4], r[5], 0x80, wanted, 0x808, 0x800);
        if (s < 0) return false;
        ai[s + 4] = 0x80; ai[s + 0x13] = r[4]; ai[s + 0x14] = r[5]; ai[s + 0x15] = r[6]; ai[s + 0x16] = r[7]; ai[s + 2] = 2; ai[s + 5] = 0; // 1F306
        int shortfall = unchecked((int)(wanted - U32(ai, s + 0x36)));
        if (shortfall > 0) Request(ai, Orders, 1, 0x11, 0x40, r[4], r[5], 0x88, r[7], (uint)shortfall);
        return false;
    }

    /// <summary>Mode 2: squad +4 takes free units near its centre up to +8; the rest is ordered as units.</summary>
    private bool Reinforce(Span<byte> ai, Span<byte> r)
    {
        int s = OriginalAiSquads.Find(ai, r[4]);
        if (s < 0) return false;
        uint wanted = U32(r, 8);
        uint got = OriginalAiRecruitment.ByStrength(Own(ai), ai[s + 1], ai[s + 0x20] * 32u, ai[s + 0x21] * 32u, wanted, ClassMask, ClassValue);
        if (got >= wanted) return false;
        uint shortfall = wanted - got;
        Request(ai, Orders, 1, 0x11, 0x40, (byte)shortfall, (byte)(shortfall >> 8), 0x88, 0, shortfall);
        return false;
    }

    /// <summary>Mode 3: a new squad gathered at squad +4 (its priority, strength +8) walks to it and merges (state 7).</summary>
    private bool Support(Span<byte> ai, Span<byte> r)
    {
        int old = OriginalAiSquads.Find(ai, r[4]);
        if (old < 0) return false;
        int s = OriginalAiRecruitment.Gather(ai, Own(ai), ai[old + 0x20], ai[old + 0x21], ai[old + 4], U32(r, 8), 0x808, 0x800);
        if (s < 0) return false;
        ai[s + 4] = ai[old + 4]; ai[s + 0x13] = ai[old + 1]; ai[s + 2] = 7; ai[s + 5] = 0; // 1F8C0 (old offset re-read, as the original)
        return false;
    }

    /// <summary>Mode 4: a squad (priority 2·DAT[45], strength DAT[45] − DAT[45]/4) attacks object +8 (state 4).</summary>
    private bool AttackObject(Span<byte> ai, Span<byte> r)
    {
        var (target, template) = world.Resolve(U32(r, 8));
        if ((sbyte)target[0x19] <= 0) return false;
        if (r[2] != 0)
        {
            if ((U32(r, 0x10) & 7) != 0) return true;
            int q = OriginalAiSquads.Find(ai, r[0x0c]);
            return q >= 0 && ai[q + 2] == 4 && ai[q + 5] <= 1;
        }
        uint wanted = (uint)(template[0x45] - (template[0x45] >> 2));
        if (wanted == 0) return false;
        byte priority = (byte)(template[0x45] << 1);
        int s = OriginalAiRecruitment.Gather(ai, Own(ai), r[4], r[5], priority, wanted, 0xa08, 0x800);
        if (s < 0) return false;
        r[2] = 1; r[0x0c] = ai[s + 1];
        ai[s + 4] = priority; W32(ai, s + 0x0f, U32(r, 8)); ai[s + 2] = 4; ai[s + 5] = 0; // 1F9EC
        int shortfall = unchecked((int)(wanted - U32(ai, s + 0x36)));
        if (shortfall > 0) Request(ai, Orders, 1, 0x11, 0x40, (byte)(r[4] << 5), 0, 0x88, 0, (uint)shortfall);
        return false;
    }

    /// <summary>Mode 5: war on settlement +4 unless allied (+B9 relation 2) or much weaker; every 2048 turns asks for
    /// raids (mode 6), every 4096 orders a random building of class 108h (+34F mode 8).</summary>
    private bool War(Span<byte> ai, Span<byte> r)
    {
        if (world.Participant(ai[0])[0xb9 + r[4]] == 2) return false;
        if (U32(ai, 0x19e9) > unchecked(2 * U32(world.Block(r[4]), 0x19e9))) return false;
        if ((U32(r, 0x10) & 0x7ff) == 0) Request(ai, List, 6, 0x15, 0x96, r[4], 0, 0, 0, 0);
        if ((U32(r, 0x10) & 0xfff) == 0)
        {
            var open = world.Buildable(ai[0], 0xf08, 0x108);
            if (open.Count > 0)
            {
                byte t = open[(world.Random() * open.Count) >> 8];
                Request(ai, Orders, 8, 0x15, 0x50, 0, 0, 0, 0, (uint)(t | world.Participant(ai[0])[0] << 8));
            }
        }
        return true;
    }

    /// <summary>Mode 7: campaign against settlement +4: raids (mode 6) and an army gathered half-way between the homes
    /// (mode 8, class from LE:3 F0E6); when the army is ready it holds the enemy territory +63..+66 (state 2).</summary>
    private bool Campaign(Span<byte> ai, Span<byte> r)
    {
        if (r[2] == 0)
        {
            if ((U32(r, 0x10) & 0x7ff) != 0) return true;
            r[2]++;
            Request(ai, List, 6, 0x17, 0x96, r[4], 0, 0, 0, 0);
            return AddArmy(ai, r);
        }
        int army = -1;
        for (int k = 0; k < 20; k++)
            if (ai[List + k * Size] == 8 && ai[List + k * Size + 1] == 0x17) { army = List + k * Size; break; }
        if (army < 0) return AddArmy(ai, r);
        if (ai[army + 2] != 1) return true;
        ai[army] = 0xff;
        int s = OriginalAiSquads.Find(ai, ai[army + 0x14]);
        if (s < 0) return AddArmy(ai, r);
        r[2] = 0;
        var enemy = world.Block(r[4]);
        ai[s + 4] = 0xa0; ai[s + 0x13] = enemy[0x63]; ai[s + 0x14] = enemy[0x64]; ai[s + 0x15] = enemy[0x65]; ai[s + 0x16] = enemy[0x66];
        ai[s + 2] = 2; ai[s + 5] = 0; // 1F306
        return true;
    }

    private bool AddArmy(Span<byte> ai, Span<byte> r)
    {
        var enemy = world.Block(r[4]);
        byte x = (byte)((byte)(enemy[0x61] + ai[0x61]) >> 1), y = (byte)((byte)(enemy[0x62] + ai[0x62]) >> 1);
        byte kind = gatherClasses[(world.Random() * 5) >> 8];
        Request(ai, List, 8, 0x17, 0xa0, x, y, kind, 0, U32(ai, 0x45));
        return true;
    }

    /// <summary>Mode 8: gather an army of strength +8 at the cell +4/+5 (priority +3, class from +6: mask 08h | high
    /// nibble·100h, value low nibble·100h). New squads march there (state 1) or join the army squad +14 (state 7); the
    /// missing strength is ordered as units of a random open template (+34F mode 2). Ready: +2 = 1.</summary>
    private bool Army(Span<byte> ai, Span<byte> r)
    {
        if (r[2] != 0) return U32(r, 0x10) <= 1000;
        if ((U32(r, 0x10) & 0x3ff) != 0) return true;
        uint wanted = U32(r, 8);
        int existing = OriginalAiSquads.Find(ai, r[0x14]);
        if (existing < 0) r[0x14] = 0;
        else
        {
            ai[existing + 4] = r[3];
            if (wanted <= U32(ai, existing + 0x36)) { r[2] = 1; W32(r, 0x10, 0); return true; }
            wanted -= U32(ai, existing + 0x36);
        }
        uint mask = (uint)(8 | (r[6] >> 4) << 8), value = (uint)((r[6] & 0xf) << 8);
        int s = OriginalAiRecruitment.Gather(ai, Own(ai), r[4], r[5], r[3], wanted, mask, value);
        if (s < 0) return true;
        if (wanted > U32(ai, s + 0x36))
        {
            uint shortfall = wanted - U32(ai, s + 0x36);
            var open = world.Buildable(ai[0], mask, value);
            if (open.Count == 0) return false;
            byte t = open[(world.Random() * open.Count) >> 8];
            byte strength = world.Template(ai[0], t)[0x76];
            uint quotient = (shortfall & 0xffff) / strength;   // DIV r/m8: AX / DAT[76]
            if (quotient > 0xff) throw new OverflowException("20DD0 mode 8: the original faults on DIV overflow");
            Request(ai, Orders, 2, 0x18, 0x81, t, world.Participant(ai[0])[0], 0, 0, (byte)(quotient + 1));
        }
        if (r[0x14] == 0) { r[0x14] = ai[s + 1]; ai[s + 4] = r[3]; ai[s + 0x0d] = r[4]; ai[s + 0x0e] = r[5]; ai[s + 2] = 1; ai[s + 5] = 0; } // 1F464
        else { ai[s + 4] = r[3]; ai[s + 0x13] = r[0x14]; ai[s + 2] = 7; ai[s + 5] = 0; } // 1F8C0
        return true;
    }

    private bool BecomeChase(Span<byte> ai, Span<byte> r, byte[] target)
    {
        W32(r, 8, world.Reference(target)); r[0x14] = 0; r[0] = 9;
        return Chase(ai, r);
    }

    /// <summary>Mode 9: a squad strong enough for what guards object +8 (enemies in its sight, block +13, +0E, +14·32)
    /// chases it (state 5, priority C8h); without enough free units the strength is ordered (+34F mode 1).
    /// The chase is retried up to block +52 times.</summary>
    private bool Chase(Span<byte> ai, Span<byte> r)
    {
        var (target, template) = world.Resolve(U32(r, 8));
        if ((sbyte)target[0x19] <= 0) return false;
        if (r[2] != 0)
        {
            if ((U32(r, 0x10) & 7) != 0) return true;
            int q = OriginalAiSquads.Find(ai, r[0x0c]);
            if (q >= 0 && ai[q + 2] == 5) return true;
            if (++r[0x14] >= ai[0x52]) return false;
            r[2] = 0; return true;
        }
        if ((U32(r, 0x10) & 0xff) != 0) return true;
        var sight = OriginalAiRecruitment.Sight(world, target, template);
        uint wanted = unchecked((sight.EnemyStrength + U32(ai, 0x13)) * U32(ai, 0x0e) >> 5) + (uint)(r[0x14] << 5);
        byte x = (byte)(U32(target, 4) >> 5), y = (byte)(U32(target, 8) >> 5);
        var (s, ebx) = OriginalAiRecruitment.GatherFree(ai, Own(ai), x, y, wanted, 0xa08, 0x800);
        if (s < 0) { Request(ai, Orders, 1, 0x16, 0x40, (byte)(x << 5), 0, 0xa8, 0, ebx); return true; } // EBP = EBX left by 21696
        r[2] = 1; r[0x0c] = ai[s + 1];
        ai[s + 4] = 0xc8; W32(ai, s + 0x0f, U32(r, 8)); ai[s + 2] = 5; ai[s + 5] = 0; // 1FB9C
        return true;
    }

    /// <summary>Mode 10: when the block's +1A01 reaches C0h, up to five times pick a random settlement in relation 1 or 2
    /// and chase its nearest mine of ore +8 (becomes mode 9).</summary>
    private bool Plunder(Span<byte> ai, Span<byte> r)
    {
        if (r[2] == 0) { if (ai[0x1a01] < 0xc0) return false; r[2] = 1; }
        if ((U32(r, 0x10) & 0xff) != 0) return true;
        if (++r[0x14] > 5) return false;
        byte other = 0;
        for (int tries = 10; tries > 0; tries--)
        {
            other = (byte)(world.Random() * world.ParticipantCount >> 8);
            if (other == ai[0]) continue;
            byte relation = world.Participant(ai[0])[0xb9 + other];
            if (relation is 1 or 2) break;
        }
        if (OriginalAiRecruitment.Mine(world.Chain(other), ai, r[8]) is not { } mine) return true;  // home of this block (ESI)
        return BecomeChase(ai, r, mine.Record);
    }
}

/// <summary>World of 209B0: the military world plus the second and third random streams (9E55 [19EC], 9E69 [19ED]).</summary>
public interface IAiGarrisonWorld : IAiMilitaryWorld
{
    byte Random2();
    byte Random3();
}

/// <summary>Garrison step LE:1 209B0 (docs/ai.md, «Гарнизон»), by AI block +19E4: bit 0 enables it; bit 1 — units wander
/// over the own land and attack intruders; bit 2 — units wander over the map; otherwise strength orders, guards of the
/// own buildings (request 4), areas around hostile buildings on the own land (request 1) and patrols (state 8).
/// Sets the class filter of request mode 2 ([ED39]/[ED3D] = 808h/800h).</summary>
public sealed class OriginalAiGarrison(IAiGarrisonWorld world, OriginalAiMilitary military, OriginalAiSquadStates states)
{
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);
    private const int Military = OriginalAiMilitary.List, Orders = OriginalAiMilitary.Orders;

    public void Run(Span<byte> ai)
    {
        if ((ai[0x19e4] & 1) == 0) return;
        military.ClassMask = 0x808; military.ClassValue = 0x800;
        var chain = world.Chain(ai[0]);
        uint turn = U32(ai, 1);
        if ((ai[0x19e4] & 2) != 0) { Wander(ai, chain, turn); return; }
        if ((ai[0x19e4] & 4) != 0) { Roam(ai, chain, turn); return; }

        if ((turn & 0x3ff) == 0)
        {
            // 21696 with EBX = −1 at (0, 0): gathers every free unit (808h/800h) and disbands again; EBX = their strength.
            var (_, free) = OriginalAiRecruitment.GatherFree(ai, chain, 0, 0, uint.MaxValue, 0x808, 0x800);
            uint wanted = (ai[0x1a27] & 2) != 0 ? 1000u : 100u;
            if (wanted > free) OriginalAiMilitary.Request(ai, Orders, 1, 0x1b, 0x40, 0, 0, 0x88, 0, wanted - free);
        }
        if ((turn & 0x3f) == 0)
            foreach (var (record, template) in chain.ToArray())
            {
                if (!Building(record, template)) continue;
                uint reference = world.Reference(record);
                int guard = OriginalAiSquads.FindByState(ai, 4, reference);
                if (guard < 0) { OriginalAiMilitary.Request(ai, Military, 4, 0x19, 0x80, 0, 0, 0, 0, reference); continue; }
                uint wanted = (uint)(template[0x45] - (template[0x45] >> 2));   // 2160B
                uint have = U32(ai, guard + 0x36);
                if (wanted > have) { OriginalAiMilitary.Request(ai, Military, 2, ai[guard + 1], 0x80, ai[guard + 1], 0, 0, 0, wanted - have); continue; }
                uint surplus = have - wanted;
                if (surplus <= 0x50 || ai[guard + 0x4a] != 0) continue;
                Release(chain, ai[guard + 1], surplus - 0x50);
            }
        if ((turn & 0xff) == 0 && ai[0x5c] != 0)
        {
            uint hostile = U32(world.Participant(ai[0]), 0x12);
            uint size = unchecked((uint)(ai[0x65] | ai[0x66] << 8) - (uint)(ai[0x63] | ai[0x64] << 8) + 0x101);
            foreach (uint reference in OriginalTargetRules.Collect(new Grid(world), ai[0x63], ai[0x64], (byte)size, (byte)(size >> 8)))
            {
                var (record, template) = world.Resolve(reference);
                if ((hostile >> (record[3] & 31) & 1) == 0 || !Building(record, template)) continue;
                var (x0, y0, x1, y1) = OriginalTargetRules.Clip((byte)(U32(record, 4) >> 5), (byte)(U32(record, 8) >> 5), 0x14, 0x14, world.MapWidth, world.MapHeight);
                OriginalAiMilitary.Request(ai, Military, 1, 0x1a, 0x80, x0, y0, x1, y1, 0);
            }
        }
        if ((turn & 0x3ff) == 0)
        {
            byte patrols = (byte)(((0xff - ai[0x1a01]) * ai[0x21] >> 8) >> 4);
            if (patrols > ai[0x19e8])
            {
                int s = OriginalAiRecruitment.Gather(ai, chain, ai[0x61], ai[0x62], 0xfa, U32(ai, 0x22), 0xa08, 0x800);
                if (s >= 0) { ai[s + 4] = 0xfa; ai[s + 2] = 8; ai[s + 5] = 0; } // 1FACD
            }
        }
    }

    private static bool Building(byte[] record, byte[] template) =>
        (U32(template, 0x40) & 8) != 0 && (U32(template, 0x40) & 0x20) == 0 && (sbyte)record[0x19] > (sbyte)template[0x46];

    /// <summary>202FB: members of squad <paramref name="id"/> leave it (+1B = 0) until their DAT[76] reach the limit.</summary>
    public static uint Release(IEnumerable<(byte[] Record, byte[] Template)> chain, byte id, uint limit)
    {
        uint sum = 0;
        foreach (var (r, t) in chain)
        {
            if (r[0x1b] != id) continue;
            r[0x1b] = 0; sum += t[0x76];
            if (sum >= limit) break;
        }
        return sum;
    }

    /// <summary>+19E4 bit 1: now and then a unit picked by weight DAT[88] walks to a random cell of the own land (unless it is
    /// busy there), and every 256 turns, with intruders on the land (21CA0), each unit not attacking (+14 ≠ 2) takes the
    /// nearest of them (20408, command 50h).</summary>
    private void Wander(Span<byte> ai, IReadOnlyList<(byte[] Record, byte[] Template)> chain, uint turn)
    {
        uint high = U32(ai, 0x5d) >> 4;
        uint mask = (0x3ffu >> (int)(high & 31)) | 1;
        if ((turn & mask) == 0)
        {
            uint pick = (high & 0xffffff00) | (uint)(world.Random() * ai[0x5d] >> 8);
            foreach (var (record, template) in chain)
            {
                uint weight = U32(template, 0x88);
                if (pick >= weight) { pick -= weight; continue; }
                pick -= weight;   // the original subtracts before testing; later objects never borrow again
                if ((U32(template, 0x40) & 8) != 0) continue;
                if (record[0x14] != 0)
                {
                    byte cx = (byte)(U32(record, 4) >> 5), cy = (byte)(U32(record, 8) >> 5);
                    if (cx >= ai[0x63] && cx <= ai[0x65] && cy >= ai[0x64] && cy <= ai[0x66]) continue;
                }
                uint span = unchecked((uint)(ai[0x65] | ai[0x66] << 8) - (uint)(ai[0x63] | ai[0x64] << 8));
                record[0x22] = (byte)(ai[0x63] + (world.Random() * (byte)span >> 8));
                record[0x23] = (byte)(ai[0x64] + (world.Random2() * (byte)(span >> 8) >> 8));
                record[0x14] = (byte)((record[0x14] & 7) | 0x48);
                break;
            }
        }
        if ((turn & 0xff) != 0 || !states.AreaCensus(ai[0], ai[0x63], ai[0x64], ai[0x65], ai[0x66])) return;
        foreach (var (record, template) in chain)
        {
            if ((U32(template, 0x40) & 8) != 0 || record[0x14] == 2) continue;
            var (enemy, _) = OriginalAiSquadSteps.NearestEnemy(world, template[0x74], record[3], U32(record, 4), U32(record, 8), ai[0x63], ai[0x64], ai[0x65], ai[0x66]);
            if (enemy == 0) continue;
            W32(record, 0x1c, enemy); record[0x14] = (byte)((record[0x14] & 7) | 0x50);
        }
    }

    /// <summary>+19E4 bit 2: every 2048 turns each idle unit, with a 1 in 8 chance (9E69), walks to a random cell of the map.</summary>
    private void Roam(Span<byte> ai, IReadOnlyList<(byte[] Record, byte[] Template)> chain, uint turn)
    {
        if ((turn & 0x7ff) != 0) return;
        foreach (var (record, template) in chain)
        {
            if ((world.Random3() & 7) != 0 || (U32(template, 0x40) & 8) != 0 || record[0x14] != 0) continue;
            record[0x22] = (byte)(world.Random() * world.MapWidth >> 8);
            record[0x23] = (byte)(world.Random2() * world.MapHeight >> 8);
            record[0x14] = (byte)((record[0x14] & 7) | 0x48);
        }
    }

    private sealed class Grid(IAiSquadWorld world) : IEnemySearchWorld
    {
        public byte MapWidth => world.MapWidth;
        public byte MapHeight => world.MapHeight;
        public uint HostileMask => throw new NotSupportedException();
        public uint Occupant(int plane, int cell) => world.Occupant(plane, cell);
        public (byte[] Record, byte[] Template) Resolve(uint reference) => world.Resolve(reference);
        public bool Contact(byte owner) => throw new NotSupportedException();
        public byte TerrainClass(ReadOnlySpan<byte> record) => throw new NotSupportedException();
        public void Contacted(FirstContact contact) => throw new NotSupportedException();
    }
}

/// <summary>World of the AI order executor 227C7: what each template makes (WRK AH=6, data/original_producers.json), the
/// building permission AF07 (hook: needs WRK AH=4 and the placement rules of the port), terrain classes of map cells
/// (SEG &amp; 7) and the fortification patterns (LE:3 F0F0).</summary>
public interface IAiOrdersWorld : IAiMilitaryWorld
{
    byte Random2();
    IReadOnlyList<byte> Makes(byte template);
    byte TemplateCount { get; }
    bool PlacementAllowed(byte template, byte army, byte participant, uint x, uint y);
    byte TileClass(int cell);
    byte[]? Fortification(uint index);
    /// <summary>A673: the development tree of the participant has the template with no unmet conditions.</summary>
    bool TreeAllows(byte participant, byte template);
}

/// <summary>AI order executor LE:1 227C7 over list +34F (docs/ai.md, «Заказы»): only the orders of the highest priority +3
/// run. Units are ordered from a producer (command 60h, template in +22), buildings from their maker with a site
/// (command 58h, area +29..+2F); sites are chosen at random in an area (mode 4), near a building (5), around the home
/// (8), for fortifications (6, 7) and across water (10). A missing producer is ordered as a building (mode 5).</summary>
public sealed class OriginalAiOrders(IAiOrdersWorld world, OriginalAiMilitary military)
{
    public const int List = 0x34f, Size = 0x15;
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);
    private byte _x0, _y0, _x1, _y1;   // [1F41..1F44] of mode 10: kept between calls

    public void Run(Span<byte> ai)
    {
        if ((ai[0x1a15] & 1) == 0) return;
        byte top = 0;
        for (int k = 0; k < 20; k++) if (ai[List + k * Size] != 0xff && ai[List + k * Size + 3] > top) top = ai[List + k * Size + 3];
        for (int k = 0; k < 20; k++)
        {
            int at = List + k * Size;
            if (ai[at + 3] >= top && !Process(ai, at)) ai[at] = 0xff;
            W32(ai, at + 0x10, U32(ai, at + 0x10) + 1);
        }
    }

    /// <summary>22E85: the first object of a producing type (makes the template) alive or under construction (+14 = 1)
    /// and allowed to work (WorkFlags bit 2). Otherwise: 1 — producers exist but none can work; 0 — a random producer
    /// type the tree allows (to be built); 2 — none allowed. Returns the producer type in either case.</summary>
    public (byte[]? Producer, byte Status, byte Type) Producer(ReadOnlySpan<byte> ai, byte template, byte army)
    {
        var types = new List<byte>();
        for (int t = 0; t < world.TemplateCount; t++)
            foreach (byte made in world.Makes((byte)t)) if (made == template) types.Add((byte)t);
        if (types.Count == 0) return (null, 2, 0xff);
        byte status = 0, type = 0;
        foreach (var (r, tm) in world.Chain(ai[0]))
        {
            if (r[0] != army || !types.Contains(r[0x0e])) continue;
            if (r[0x14] != 1 && (sbyte)r[0x19] <= (sbyte)tm[0x46]) continue;
            status = 1; type = r[0x0e];
            if ((r[0x0f] & 4) != 0) return (r, 0, type);
        }
        if (status == 1) return (null, 1, type);
        type = types[(world.Random() * types.Count) >> 8];
        return (null, world.TreeAllows(ai[0], type) ? (byte)0 : (byte)2, type);   // A673
    }

    /// <summary>22FE4: the site for a template at a cell: AF07 allows it, the footprint grown by one cell on every side lies
    /// on the map, has no building in plane 1 and only terrain the template accepts (DAT[58 + class]).</summary>
    public bool Site(byte template, byte army, byte participant, byte x, byte y)
    {
        var t = world.Template(participant, template);
        uint px = x * 32u + (uint)((t[3] & 1) << 4), py = y * 32u + (uint)((t[4] & 1) << 4);
        if (!world.PlacementAllowed(template, army, participant, px, py)) return false;
        byte w = (byte)(t[3] + 2), h = (byte)(t[4] + 2);
        byte left = (byte)(x - (w >> 1)), top = (byte)(y - (h >> 1));
        if (left > (byte)(world.MapWidth - w) || top > (byte)(world.MapHeight - h)) return false;
        for (int row = 0; row < h; row++)
            for (int column = 0; column < w; column++)
            {
                int cell = (top + row) * world.MapWidth + left + column;
                uint reference = world.Occupant(1, cell);
                if (reference != 0 && (U32(world.Resolve(reference).Template, 0x40) & 8) != 0) return false;
                if (t[0x58 + (world.TileClass(cell) & 7)] == 0) return false;
            }
        return true;
    }

    private static void Produce(byte[] producer, byte template) { producer[0x22] = template; producer[0x14] = (byte)((producer[0x14] & 7) | 0x60); } // 22F76

    /// <summary>22F82: the builder gets template +22, command 58h and the site +29..+2F (fixed point, centred for odd sizes).</summary>
    private void Build(byte[] builder, byte template, byte x0, byte y0, byte x1, byte y1)
    {
        builder[0x22] = template; builder[0x14] = (byte)((builder[0x14] & 7) | 0x58);
        var t = world.Template(0, template);
        ushort ox = (ushort)((t[3] & 1) << 4), oy = (ushort)((t[4] & 1) << 4);
        BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x29), (ushort)(x0 * 32 + ox));
        BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x2d), (ushort)(x1 * 32 + ox));
        BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x2b), (ushort)(y0 * 32 + oy));
        BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x2f), (ushort)(y1 * 32 + oy));
    }

    private void OrderProducer(Span<byte> ai, Span<byte> r, byte type, byte army) =>
        OriginalAiMilitary.Request(ai, List, 5, 0x10, r[3], 0, 0, 0, 0, (uint)(type | army << 8));

    private bool Process(Span<byte> ai, int at)
    {
        var r = ai.Slice(at, Size);
        while (true)
            switch (r[0])
            {
                case 1:
                    {
                        if ((U32(r, 0x10) & 0x3f) != 0) return true;
                        uint mask = (uint)(8 | (r[6] >> 4) << 8), value = (uint)((r[6] & 0xf) << 8);
                        var open = world.Buildable(ai[0], mask, value);
                        if (open.Count == 0) return false;
                        byte t = open[(world.Random() * open.Count) >> 8];
                        byte army = world.Participant(ai[0])[0];
                        var (producer, status, type) = Producer(ai, t, army);
                        if (producer != null)
                        {
                            Produce(producer, t);
                            uint before = U32(r, 8), strength = world.Template(ai[0], t)[0x76];
                            W32(r, 8, before - strength);
                            return before > strength;   // SUB, JA
                        }
                        if (++r[0x14] >= 0x14) return false;
                        if (status == 0) OrderProducer(ai, r, type, army);
                        return true;
                    }
                case 2:
                    {
                        if ((U32(r, 0x10) & 0x3f) != 0) return true;
                        var (producer, status, type) = Producer(ai, r[4], r[5]);
                        if (producer != null) { Produce(producer, r[4]); W32(r, 8, U32(r, 8) - 1); return U32(r, 8) != 0; }
                        if (status == 0) OrderProducer(ai, r, type, r[5]);
                        return true;
                    }
                case 3:
                    {
                        if ((U32(r, 0x10) & 0x3f) != 0) return true;
                        if (++r[0x14] > 0x28) return false;
                        var t = world.Template(ai[0], r[8]);
                        byte n = t[3];
                        if ((U32(t, 0x40) & 0x80) != 0)
                        {
                            byte dx = (byte)(r[4] - r[6]); if ((sbyte)dx < 0) dx = (byte)-dx;
                            byte dy = (byte)(r[5] - r[7]); if ((sbyte)dy < 0) dy = (byte)-dy;
                            n = Math.Max(dx, dy);
                        }
                        uint count = (byte)(n / t[3]);   // DIV r/m8
                        var p = world.Participant(ai[0]);
                        for (int i = 0; i < 4; i++)
                        {
                            uint cost = U32(t, 0x7c + 4 * i);
                            uint total = (cost & 0xffff0000) | (((cost & 0xffff) * count) & 0xffff);   // MUL BX
                            if (total > U32(p, 0x18 + 4 * i)) return true;
                        }
                        var (builder, status, type) = Producer(ai, r[8], r[9]);
                        if (builder == null) { if (status == 0) OrderProducer(ai, r, type, r[9]); return true; }
                        Build(builder, r[8], r[4], r[5], r[6], r[7]);
                        return false;
                    }
                case 4:
                    {
                        if ((U32(r, 0x10) & 0x1f) != 0) return true;
                        if (++r[0x14] > 0x14) return false;
                        uint span = unchecked((uint)(r[6] | r[7] << 8) + 0x101 - (uint)(r[4] | r[5] << 8));
                        byte x = (byte)(r[4] + (world.Random() * (byte)span >> 8));
                        byte y = (byte)(r[5] + (world.Random2() * (byte)(span >> 8) >> 8));
                        if (!Site(r[8], r[9], ai[0], x, y)) return true;
                        Convert(r, x, y);
                        continue;
                    }
                case 5:
                    {
                        if ((U32(r, 0x10) & 0x1f) != 0) return true;
                        if (++r[0x14] > 0x14) return false;
                        uint px = (uint)(world.Random() * world.MapWidth >> 8) << 5, py = (uint)(world.Random2() * world.MapHeight >> 8) << 5;
                        military.ClassMask = 0x68; military.ClassValue = 8;
                        if (OriginalAiRecruitment.NearestFree(world.Chain(ai[0]), px, py, 0x68, 8) is { } near)
                        {
                            byte x = (byte)(U32(near.Record, 4) >> 5), y = (byte)(U32(near.Record, 8) >> 5);
                            byte bx = 5, by = 0;
                            byte coin = world.Random();
                            if ((coin & 1) != 0) bx = unchecked((byte)-5);
                            if ((coin & 2) != 0) (bx, by) = (by, bx);
                            x += bx; y += by;
                            if (!Site(r[8], r[9], ai[0], x, y)) return true;
                            Convert(r, x, y);
                            continue;
                        }
                        byte size = (byte)((ai[0x5c] << 2) + 0x0e);
                        (r[4], r[5], r[6], r[7]) = OriginalTargetRules.Clip(ai[0x61], ai[0x62], size, size, world.MapWidth, world.MapHeight);
                        r[0] = 4; r[2] = 0; W32(r, 0x10, 0); r[0x14] = 0;
                        continue;
                    }
                case 6:
                    OriginalAiMilitary.Request(ai, List, 7, 0x46, 0x80, ai[0x61], ai[0x62], 0, 0, 0);
                    return false;
                case 7:
                    {
                        if ((U32(r, 0x10) & 0xf) != 0) return true;
                        var pattern = world.Fortification(U32(r, 8));
                        int e = 5 * r[0x14];
                        if (pattern == null || pattern[e] == 0xff) return false;
                        var open = world.Buildable(ai[0], 0xf08, (uint)(8 | pattern[e] << 8));
                        if (open.Count == 0) { r[0x14]++; return true; }
                        byte t = open[(world.Random() * open.Count) >> 8];
                        if (!AddFree(ai, 3, 0x47, r[3], (byte)(r[4] + pattern[e + 1]), (byte)(r[5] + pattern[e + 2]),
                            (byte)(r[4] + pattern[e + 3]), (byte)(r[5] + pattern[e + 4]), (uint)(t | world.Participant(ai[0])[0] << 8))) return true;
                        r[0x14]++; return true;
                    }
                case 8:
                    {
                        if ((U32(r, 0x10) & 0x1f) != 0) return true;
                        if (++r[0x14] > 0x14) return false;
                        byte a = (byte)((world.Random() * 10 >> 8) - 5);
                        byte x = (byte)(((sbyte)a < 0 ? unchecked((byte)-10) : (byte)10) + a);
                        byte y = (byte)((world.Random2() * 0x3c >> 8) - 0x1e);
                        if ((world.Random() & 1) == 0) (x, y) = (y, x);
                        x += ai[0x61]; y += ai[0x62];
                        if (!Site(r[8], r[9], ai[0], x, y)) return true;
                        Convert(r, x, y);
                        continue;
                    }
                case 10: return Crossing(ai, r);
                default: return false;
            }
    }

    private static void Convert(Span<byte> r, byte x, byte y) { r[4] = x; r[5] = y; r[0] = 3; r[2] = 0; W32(r, 0x10, 0); r[0x14] = 0; } // 22A2B

    /// <summary>1EAED: the first free record of list +34F, without the duplicate check of 1EB5C. False when the list is full.</summary>
    private static bool AddFree(Span<byte> ai, byte mode, byte first, byte third, byte cl, byte ch, byte dl, byte dh, uint parameter)
    {
        for (int k = 0; k < 20; k++)
        {
            var q = ai.Slice(List + k * Size, Size);
            if (q[0] != 0xff) continue;
            q[0] = mode; q[1] = first; q[2] = 0; q[3] = third; q[4] = cl; q[5] = ch; q[6] = dl; q[7] = dh;
            W32(q, 8, parameter); W32(q, 0x0c, 0); W32(q, 0x10, 0); q[0x14] = 0;
            return true;
        }
        return false;
    }

    /// <summary>Mode 10 (22C53): unless a bridge-class object (DAT[40] &amp; 0Ah = 0Ah) is in the area +4..+7, find the
    /// narrowest water crossing (terrain class 2) along rows and then columns, and order a class-0Ah template there.</summary>
    private bool Crossing(Span<byte> ai, Span<byte> r)
    {
        uint size = unchecked((uint)(r[6] | r[7] << 8) - (uint)(r[4] | r[5] << 8) + 0x101);
        foreach (uint reference in OriginalTargetRules.Collect(new Grid(world), r[4], r[5], (byte)size, (byte)(size >> 8)))
            if ((U32(world.Resolve(reference).Template, 0x40) & 0xa) == 0xa) return false;
        byte best = 0xff, counter = 0, mark = 0;
        byte y = r[5];
        do   // rows: INC DH / CMP DH,[+7] / JBE, bytes wrap
        {
            byte state = 0xff, x = r[4];
            do { Step(world.TileClass(y * world.MapWidth + x), x, ref state, ref counter, ref mark); x++; } while (x <= r[6]);
            if (state == 2 && counter < best) { best = counter; _x0 = (byte)(mark - counter); _y0 = y; _x1 = (byte)(mark + 1); _y1 = y; }
            y++;
        } while (y <= r[7]);
        byte cx = r[4];
        do   // columns
        {
            byte state = 0xff; y = r[5];
            do { Step(world.TileClass(y * world.MapWidth + cx), y, ref state, ref counter, ref mark); y++; } while (y <= r[7]);
            if (state == 2 && counter < best) { best = counter; _x0 = cx; _y0 = (byte)(mark - counter); _x1 = cx; _y1 = (byte)(mark + 1); }
            cx++;
        } while (cx <= r[6]);
        if (best == 0xff) return false;
        (r[4], r[5], r[6], r[7]) = (_x0, _y0, _x1, _y1);
        var open = world.Buildable(ai[0], 0xa, 0xa);
        if (open.Count == 0) return false;
        byte t = open[(world.Random() * open.Count) >> 8];
        r[8] = t; r[9] = world.Participant(ai[0])[0]; r[0] = 3;
        return true;
    }

    /// <summary>One cell of the crossing scan: land (state 0) → water run (1, counted) → land after it (2, marked);
    /// water again joins the land gap to the run.</summary>
    private static void Step(byte terrain, byte position, ref byte state, ref byte counter, ref byte mark)
    {
        bool water = (terrain & 7) == 2;
        if (state == 0xff) { if (!water) state = 0; return; }
        if (state == 0) { if (!water) return; state = 1; counter = 0; }
        if (state == 1)
        {
            counter++;
            if (water) return;
            state = 2; mark = position;
        }
        if (water) { state--; counter = (byte)(counter + position - mark); }
    }

    private sealed class Grid(IAiSquadWorld world) : IEnemySearchWorld
    {
        public byte MapWidth => world.MapWidth;
        public byte MapHeight => world.MapHeight;
        public uint HostileMask => throw new NotSupportedException();
        public uint Occupant(int plane, int cell) => world.Occupant(plane, cell);
        public (byte[] Record, byte[] Template) Resolve(uint reference) => world.Resolve(reference);
        public bool Contact(byte owner) => throw new NotSupportedException();
        public byte TerrainClass(ReadOnlySpan<byte> record) => throw new NotSupportedException();
        public void Contacted(FirstContact contact) => throw new NotSupportedException();
    }
}

/// <summary>World of the AI economy (21E99, 221AE): the turn world for messages, the participant record to change, the
/// fog map [18DA] (bit set = cell not yet seen by the participant), map tile bytes and the mine placement check D363.</summary>
public interface IAiEconomyWorld : IAiMilitaryWorld
{
    byte Random2();
    Span<byte> ParticipantRecord(byte participant);
    /// <summary>[18DA]: bit n set while participant n has not seen the cell.</summary>
    byte Fog(int cell);
    /// <summary>Byte 2 of the map tile word ([18EE] + 4·cell + 2): the ore kind of the cell.</summary>
    byte TileOre(int cell);
    /// <summary>D363: the template may be placed at the cell (WRK AH=4 and occupancy; hook).</summary>
    bool SiteAllowed(byte template, byte army, byte participant, byte x, byte y);
    /// <summary>EDD5: message type, other, owner, BH, parameter.</summary>
    void Send(byte type, byte other, byte owner, byte bh, uint parameter);
}

/// <summary>AI economy LE:1 21E99 (income, upkeep, economic requests in list +4F3, farm and building orders) and 221AE
/// (worker squads for mines and lumbermills, mine sites, lumbermill orders). See docs/ai.md, «Экономика».</summary>
public sealed class OriginalAiEconomy(IAiEconomyWorld world, OriginalAiMilitary military)
{
    public const int Economy = 0x4f3, Orders = 0x34f, Military = 0x1ab, Size = 0x15;
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);
    private static void Request(Span<byte> ai, int list, byte mode, byte first, byte third, uint parameter, byte cl = 0, byte ch = 0) =>
        OriginalAiMilitary.Request(ai, list, mode, first, third, cl, ch, 0, 0, parameter);

    /// <summary>21E99 (the turn counter is +0Dh for the duration of the step).</summary>
    public void Plan(Span<byte> ai)
    {
        W32(ai, 1, U32(ai, 1) + 0xd);
        uint turn = U32(ai, 1);
        var p = world.ParticipantRecord(ai[0]);
        if ((turn & 0x1fff) == 0)
        {
            uint income = U32(p, 0x24) << 1;
            W32(p, 0x18, U32(p, 0x18) + income); W32(p, 0x1c, U32(p, 0x1c) + income);
            income <<= 2; W32(p, 0x20, U32(p, 0x20) + income);
            world.Send(9, ai[0], ai[0], 0, income);
            uint upkeep = U32(ai, 0x19ed) << 4;
            if (U32(p, 0x20) < upkeep)
            {
                W32(p, 0x20, 0);
                if ((ai[0x1a27] & 2) == 0 && U32(ai, 0x19ed) > 0x1e) W32(ai, 0x73, 0x2711);
            }
            else W32(p, 0x20, U32(p, 0x20) - upkeep);
        }
        if ((ai[0x19ff] & 1) != 0)
        {
            if ((turn & 0xff) == 0)
            {
                if ((byte)((byte)U32(ai, 0x5d) * ai[0x1b] >> 8) + 1 is var wanted && (byte)wanted > ai[0x1a00])
                    Request(ai, Economy, 4, 0x38, 0xc8, 0);
                if (ai[0x1a02] < ai[0x27] >> 4) Request(ai, Economy, 2, 0x39, Urgency(ai[0x1a02], U32(p, 0x20)), 1);
                if (ai[0x1a03] < ai[0x28] >> 4) Request(ai, Economy, 2, 0x3a, Urgency(ai[0x1a03], U32(p, 0x18)), 2);
            }
            if ((turn & 0x1ff) == 0)
                foreach (var (r, t) in world.Chain(ai[0]))
                    if ((sbyte)r[0x19] > 0 && (U32(t, 0x40) & 0xf08) == 0x408 && U32(r, 0x24) != 0
                        && OriginalAiSquads.FindByState(ai, 0x0a, world.Reference(r)) < 0)
                        Request(ai, Economy, 3, 0x3b, 0xc0, world.Reference(r));
            if ((turn & 0x1ff) == 0 && U32(ai, 0x4e) > U32(p, 0x2c))
            {
                byte urgency = (byte)((ulong)(U32(ai, 0x4e) - U32(p, 0x2c)) * 200 / U32(ai, 0x4e));
                if (Random(ai, 0xf08, 0x108) is { } farm) Request(ai, Orders, 8, 0x3c, urgency, farm);
            }
            if (((turn + 0x100) & 0x1ff) == 0)
                foreach (var (r, t) in world.Chain(ai[0]))
                    if ((sbyte)r[0x19] > 0 && (U32(t, 0x40) & 0xf08) == 0x208 && OriginalAiSquads.FindByState(ai, 9, world.Reference(r)) < 0)
                        Request(ai, Economy, 5, 0x3d, 0xc0, world.Reference(r));
            if ((turn & 0x1ff) == 0 && ai[0x20] > ai[0x1a26])
            {
                byte urgency = (byte)((ai[0x20] - ai[0x1a26]) * 0x96 / ai[0x20]);
                if (Random(ai, 0xf08, 0xb08) is { } building) Request(ai, Orders, 5, 0x3e, urgency, building);
            }
        }
        W32(ai, 1, U32(ai, 1) - 0xd);
    }

    private static byte Urgency(byte count, uint stock) => (byte)(0x82 + (count == 0 ? 0x50 : 0) + (stock <= 0x96 ? 0x1e : 0));

    /// <summary>22E46: a random open template of the class as template | army &lt;&lt; 8.</summary>
    private uint? Random(ReadOnlySpan<byte> ai, uint mask, uint value)
    {
        var open = world.Buildable(ai[0], mask, value);
        if (open.Count == 0) return null;
        byte t = open[(world.Random() * open.Count) >> 8];
        return (uint)(t | world.Participant(ai[0])[0] << 8);
    }

    /// <summary>221AE over list +4F3: mode 5 — workers (class F08h/300h) for a lumbermill (state 9), 3 — for a mine
    /// (state 10), 2 — a mine site near the home on seen ore of kind +8, 4 — a lumbermill order.</summary>
    public void Execute(Span<byte> ai)
    {
        if ((ai[0x19ff] & 1) == 0) return;
        military.ClassMask = 0xf08; military.ClassValue = 0x300;
        for (int k = 0; k < 20; k++)
        {
            int at = Economy + k * Size;
            if (!Process(ai, at)) ai[at] = 0xff;
            W32(ai, at + 0x10, U32(ai, at + 0x10) + 1);
        }
    }

    private bool Process(Span<byte> ai, int at)
    {
        var r = ai.Slice(at, Size);
        switch (r[0])
        {
            case 5:
            case 3:
                {
                    bool lumber = r[0] == 5;
                    uint reference = U32(r, 8);
                    int squad = Workers(ai, lumber ? U32(ai, 0x1c) : 0x1e);
                    if (squad >= 0)
                    {
                        var s = ai.Slice(squad, OriginalAiSquads.Size);
                        W32(s, 0x0f, reference);
                        if (lumber)
                        {
                            var o = world.Resolve(reference).Record;
                            (s[0x4d], s[0x4e], s[0x4f], s[0x50]) = OriginalTargetRules.Clip((byte)(U32(o, 4) >> 5), (byte)(U32(o, 8) >> 5), 10, 10, world.MapWidth, world.MapHeight);
                            s[0x4b] = 2; s[2] = 9; s[5] = 1; s[0x1f] = 0;   // 1F5F1
                        }
                        else { s[2] = 10; s[5] = 0; }                        // 1F71A
                        return false;
                    }
                    var open = world.Buildable(ai[0], 0xf08, 0x300);
                    if (open.Count == 0) return false;
                    byte t = open[(world.Random() * open.Count) >> 8];
                    byte capacity = world.Template(ai[0], t)[0x9d];
                    uint quotient = (U32(ai, 0x1c) & 0xffff) / capacity;   // DIV r/m8
                    if (quotient > 0xff) throw new OverflowException("221AE: the original faults on DIV overflow");
                    Request(ai, Orders, 2, lumber ? (byte)0x35 : (byte)0x33, lumber ? (byte)0xc0 : (byte)0xf0, (byte)quotient, t, world.Participant(ai[0])[0]);
                    return false;
                }
            case 2:
                {
                    if (r[2] == 0)
                    {
                        r[2]++;
                        var open = world.Buildable(ai[0], 0xf08, 0x408);
                        if (open.Count == 0) return false;
                        r[9] = open[(world.Random() * open.Count) >> 8]; r[0x0a] = world.Participant(ai[0])[0];
                    }
                    if ((U32(r, 0x10) & 0x1f) != 0) return true;
                    byte radius = (byte)(++r[0x14] + 0x0f);
                    if (radius > 0x40)
                    {
                        if (r[3] > 0x80) Request(ai, Military, 0x0a, 0x32, r[3], U32(r, 8));
                        return false;
                    }
                    if (Site(r[8], ai[0], ai[0x61], ai[0x62], radius) is not { } cell) return true;
                    if (!world.SiteAllowed(r[9], r[0x0a], ai[0], cell.X, cell.Y)) return true;
                    Request(ai, Orders, 3, 0x32, r[3], (uint)(r[9] | r[0x0a] << 8), cell.X, cell.Y);
                    return false;
                }
            case 4:
                if (Random(ai, 0xf08, 0x208) is { } mill) Request(ai, Orders, 8, 0x34, r[3], mill);
                return false;
            default: return false;
        }
    }

    /// <summary>22435: a new squad takes free workers (class [ED39]/[ED3D]) until their DAT[9D] cover the wanted amount;
    /// otherwise it is disbanded. Returns the squad offset or −1.</summary>
    private int Workers(Span<byte> ai, uint wanted)
    {
        var chain = world.Chain(ai[0]);
        int at = OriginalAiSquads.Create(ai, chain.Select(m => m.Record));
        if (at < 0) return -1;
        uint left = wanted;
        foreach (var (r, t) in chain)
        {
            if (r[0x1b] != 0 || (U32(t, 0x40) & military.ClassMask) != military.ClassValue) continue;
            r[0x1b] = ai[at + 1];
            if (left <= t[0x9d]) return at;
            left -= t[0x9d];
        }
        OriginalAiSquads.Disband(ai, chain.Select(m => m.Record), ai[at + 1]);
        return -1;
    }

    /// <summary>22613: up to five random cells around the home (radius grows with the attempts) that the participant has
    /// seen ([18DA] bit clear) and whose tile carries ore of the kind.</summary>
    private (byte X, byte Y)? Site(byte kind, byte participant, byte x, byte y, byte radius)
    {
        byte bit = (byte)(participant < 8 ? 1 << participant : 0);
        var (x0, y0, x1, y1) = OriginalTargetRules.Clip(x, y, radius, radius, world.MapWidth, world.MapHeight);
        uint size = unchecked((uint)(x1 | y1 << 8) + 0x101 - (uint)(x0 | y0 << 8));
        for (int tries = 5; tries > 0; tries--)
        {
            byte cx = (byte)(x0 + (world.Random() * (byte)size >> 8)), cy = (byte)(y0 + (world.Random2() * (byte)(size >> 8) >> 8));
            int cell = world.MapWidth * cy + cx;
            if ((world.Fog(cell) & bit) != 0) continue;
            if (world.TileOre(cell) == kind) return (cx, cy);
        }
        return null;
    }
}
