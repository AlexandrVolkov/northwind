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

/// <summary>World of the squad states (1F078): random streams 9E41/9E55, the explored-cells map [18DA], the global byte
/// [19E9] read by state 6, and hooks for what is not ported yet: messages A4C2, the stuck-member rescue 20001 and the
/// worker states 9 and 10.</summary>
public interface IAiSquadStateWorld : IAiSquadWorld
{
    byte Global19E9 { get; }
    byte Random();
    byte Random2();
    byte Explored(int cell);
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
            // 1EBEF: a random cell; when the participant has explored it ([18DA] bit), an area of 20×20 around it.
            byte x = (byte)(world.Random() * world.MapWidth >> 8), y = (byte)(world.Random2() * world.MapHeight >> 8);
            byte bit = (byte)(s[0] < 8 ? 1 << s[0] : 0);
            if ((world.Explored(world.MapWidth * y + x) & bit) == 0) return;
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
