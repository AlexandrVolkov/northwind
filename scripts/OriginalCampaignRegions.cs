using System.Buffers.Binary;
namespace Northwind;

/// <summary>The services of the campaign regions (recorded stubs in tools/verify_campaign_regions.py).</summary>
public interface ICampaignRegionWorld
{
    /// <summary>1ECA0: the first living object of the army with the flags (ECX mask, EDX value) of its template; null = none.</summary>
    (uint X, uint Y)? FindObject(byte army, uint mask, uint value);
}

/// <summary>The regions of the campaign layer, LE:1 10BD4..10E19 and 110C8..11150. The table at DS:3E87 holds <see cref="Region"/> bytes per region (byte 0 bit 0: the region exists, then one
/// <see cref="Record"/>-byte record per army: +0 the army, +1..10 its relations to the armies (2 = enemy, 3/4 = ally), +11 value (state +19E9), +17 the population (state +19F1), +57/+58 growth and war flags, +59 settled).
/// Every 1024 updates the populations grow and the war flag costs value (10BD4); the snapshot (10CA0) writes the running mission into its region; 110C8 decides whether the campaign has
/// ended. Field names are descriptive guesses from the use of the values; the code is verified against the original x86 by tests/fixtures/campaign-regions.json.</summary>
public static class OriginalCampaignRegions
{
    public const int Region = 0x841, Record = 0x84, Participant = 0xf5, State = 0x1a35;

    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static void Put(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);

    /// <summary>10BD4: the counter advances; on every 1024th update every settled army grows by 10 and an army at war loses strength to the strength of its enemies in the region.</summary>
    public static void Tick(byte[] regions, int regionCount, int armies, ref uint counter, bool campaign)
    {
        if (!campaign) return;
        counter++;
        if ((counter & 0x3ff) != 0) return;
        for (int r = 0; r < regionCount; r++)
        {
            int region = r * Region;
            if ((regions[region] & 1) == 0) continue;
            for (int a = 0; a < armies; a++)
            {
                int rec = region + 1 + a * Record;
                if (U32(regions, rec + 0x17) <= 0x7cf) continue;
                if ((regions[rec + 0x58] & 1) != 0 && U32(regions, rec + 0x17) < 0x5573) Put(regions, rec + 0x17, U32(regions, rec + 0x17) + 10);
                if ((regions[rec + 0x57] & 1) == 0) continue;
                uint sum = 0; int other = region + 1;
                for (int k = 0; k < armies; k++, other += Record)
                    if (regions[other + regions[rec] + 1] == 2 && U32(regions, other + 0x17) > 0x7cf) sum += U32(regions, other + 0x11);
                uint loss = (uint)(((ulong)0x79e * sum) >> 16), strength = U32(regions, rec + 0x11);
                Put(regions, rec + 0x11, strength >= loss ? strength - loss : 0);
            }
        }
    }

    /// <summary>10CA0: the running mission (current region = [1CC8]·[1CC5] + [1CC7]) is written into its region, record by record, from the participants (stride F5) and the army states (stride 1A35).</summary>
    public static void Snapshot(byte[] regions, byte[] states, byte[] participants, int armies, byte row, byte columns, byte column, ICampaignRegionWorld world)
    {
        int product = row * columns; int index = (product & 0xff00) | (byte)((byte)product + column);
        int region = index * Region;
        regions[region] |= 1;
        int rec = region + 1;
        for (int a = 0; a < armies; a++, rec += Record)
        {
            int p = a * Participant, s = a * State;
            regions[rec] = (byte)a;
            for (int j = 0; j < 0x10; j++) regions[rec + 1 + j] = participants[p + 0xb9 + j];
            for (int j = 0; j < 0x14; j++) regions[rec + 0x23 + j] = participants[p + 0xa4 + j];
            for (int j = 0; j < 0x14; j++) regions[rec + 0x37 + j] = participants[p + 0xe1 + j];
            for (int j = 0; j < 0x14; j++) regions[rec + 0x70 + j] = participants[p + 0xcd + j];
            Put(regions, rec + 0x11, U32(states, s + 0x19e9)); regions[rec + 0x15] = states[s + 0x61]; regions[rec + 0x16] = states[s + 0x62];
            Put(regions, rec + 0x17, U32(states, s + 0x19f1)); regions[rec + 0x1b] = states[s + 0x5c]; Put(regions, rec + 0x1c, U32(participants, p + 0x24));
            Put(regions, rec + 0x4b, U32(participants, p + 0x18)); Put(regions, rec + 0x4f, U32(participants, p + 0x20)); Put(regions, rec + 0x53, U32(participants, p + 0x1c));
            regions[rec + 0x22] = participants[p + 0xb8]; regions[rec + 0x57] = states[s + 0x19e4]; regions[rec + 0x58] = states[s + 0x1a34]; regions[rec + 0x59] = states[s + 5];
            Put(regions, rec + 0x5e, U32(states, s + 0x19f9)); Put(regions, rec + 0x5a, U32(states, s + 0x19f5));
            Put(regions, rec + 0x64, U32(states, s + 0x1a08)); Put(regions, rec + 0x68, U32(states, s + 0x1a0c)); Put(regions, rec + 0x6c, U32(states, s + 0x1a10));
            regions[rec + 0x62] = states[s + 0x19fd]; regions[rec + 0x63] = states[s + 0x19fe]; regions[rec + 0x20] = 0; regions[rec + 0x21] = 0;
            if (world.FindObject(states[s], 0xf08, 0xf00) is { } found) { regions[rec + 0x20] = (byte)(found.X >> 5); regions[rec + 0x21] = (byte)(found.Y >> 5); }
        }
    }

    /// <summary>110C8: counts the regions that hold no enemy of the player (every populated army of the region is the player, settled, or allied with him); with seven of them and the third region
    /// empty the campaign state [1EC6] becomes 3 (won). Returns the new state.</summary>
    public static byte End(byte[] regions, int regionCount, int armies, byte player, byte state)
    {
        int free = 0;
        for (int r = 0; r < regionCount; r++)
        {
            int region = r * Region;
            if ((regions[region] & 1) == 0) continue;
            bool clear = true; int rec = region + 1;
            for (int a = 0; a < armies && clear; a++, rec += Record)
            {
                if (U32(regions, rec + 0x17) <= 0x7cf) continue;
                if (regions[rec + 0x59] != 0 || regions[rec] == player) continue;
                byte relation = regions[rec + player + 1];
                if (relation != 3 && relation != 4) clear = false;
            }
            if (clear) free++;
        }
        if ((byte)free < 7) return state;
        int third = 2 * Region;
        if ((third >= regions.Length ? 0 : regions[third] & 1) == 0) return state;
        return U32(regions, third + 1 + 0x17) <= 0x7cf ? (byte)3 : state;
    }
}

/// <summary>10FDE: casualties of an army taken while it was away from its mission. The units of the army are weakened to the minimum condition of their template (DAT[46]) until the
/// strength budget is spent: the value of the army (state +19E9, the sum of DAT[76] of its units, docs/ai.md) shrinks by <c>loss</c>; units that carry weight (DAT[76]) count against the new value, soldiers (flags &amp; 28h = 8) against the
/// share of strength lost. Verified by tests/fixtures/campaign-losses.json (tools/verify_campaign_losses.py; 68BD is a stub).</summary>
public static class OriginalCampaignLosses
{
    public static void Apply(byte[] state, uint loss, IEnumerable<(byte[] Unit, byte[] Template)> units)
    {
        if (loss == 0) return;
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(0x19e9)); byte strength = state[0x5c];
        uint remaining = value >= loss ? value - loss : 0;
        uint half = (value >> 1) + 1, lost = 0;
        if (loss > half) lost = (uint)(((ulong)(loss - half) * strength) / half);
        uint budget = strength >= lost ? strength - lost : 0, accumulated = 0, counter = 0;
        foreach (var (unit, template) in units)
        {
            sbyte minimum = (sbyte)template[0x46];
            if ((sbyte)unit[0x19] <= minimum) continue;
            if (accumulated >= remaining && template[0x76] != 0) { unit[0x19] = (byte)minimum; continue; }
            accumulated += template[0x76];
            if ((BinaryPrimitives.ReadUInt32LittleEndian(template.AsSpan(0x40)) & 0x28) != 8) continue;
            if (counter >= budget) { unit[0x19] = (byte)minimum; continue; }
            counter++;
        }
    }
}

/// <summary>The services of the catch-up (recorded stubs in tools/verify_campaign_catchup.py).</summary>
public interface ICampaignCatchupWorld
{
    void Casualties(uint loss, int army);        // 10FDE
    void ResetMission();                         // 1E6C2
    void System(byte army, byte mode);           // 1E740
    void RunWorld(uint updates);                 // 11087: that many updates of the world
}

/// <summary>10E19 and 10ECE: the time the campaign has spent elsewhere is applied when a region is entered. <see cref="Away"/> takes 800h from the budget [3E83], adds the rest to the global time,
/// restores the relations of the participants from the region record and gives the army the casualties it took (state value +19E9 above the recorded +11); <see cref="Budget"/> runs the
/// budget as world updates in chunks of 400h. Verified by tests/fixtures/campaign-catchup.json.</summary>
public static class OriginalCampaignCatchup
{
    public static void Away(byte[] regions, byte[] states, byte[] participants, int armies, ref uint budget, ref uint time, byte row, byte columns, byte column, byte player, ICampaignCatchupWorld world, byte[]? padded = null)
    {
        uint old = budget; budget -= 0x800;
        if (old > 0x800)
        {
            time += budget;
            int product = row * columns; int index = (product & 0xff00) | (byte)((byte)product + column); int region = index * OriginalCampaignRegions.Region;
            if (region < regions.Length && (regions[region] & 1) != 0)
            {
                int rec = region + 1;
                for (int a = 0; a < armies; a++, rec += OriginalCampaignRegions.Record)
                {
                    for (int j = 0; j < 0x10; j++) participants[a * OriginalCampaignRegions.Participant + 0xb9 + j] = regions[rec + 1 + j];
                    uint value = BitConverter.ToUInt32(states, a * OriginalCampaignRegions.State + 0x19e9), recorded = BitConverter.ToUInt32(regions, rec + 0x11);
                    if (value > recorded) world.Casualties(value - recorded, a);
                }
            }
        }
        world.ResetMission(); world.System(player, 1); world.RunWorld(0x800);
    }

    public static void Budget(ref uint budget, ICampaignCatchupWorld world)
    {
        uint remaining = budget;
        do
        {
            uint chunk = Math.Min(remaining, 0x400u); budget = chunk; world.RunWorld(chunk);
            if (remaining <= 0x400) break;
            remaining -= 0x400;
        } while (remaining > 0);
    }
}
