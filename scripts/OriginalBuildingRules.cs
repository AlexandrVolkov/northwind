namespace Northwind;

// Object offsets: Condition=0x19, Counter=0x16, WorkFlags=0x0f, ObjectFlags=0x17.
// Raw condition bytes are not yet identified with displayed hit points.
public readonly record struct BuildingCondition(byte Condition, byte Counter, byte WorkFlags, ushort ObjectFlags);
public readonly record struct ResourceTriple(uint First, uint Second, uint Third);
public readonly record struct MaintenanceParameters(byte Limit, byte Interval, ResourceTriple Costs);
public readonly record struct MaintenanceResult(BuildingCondition State, ResourceTriple Balances, bool ContinueAction, byte Signal);

/// <summary>
/// Two bounded operations recovered from the shipped EXE, not a full building update.
/// Parameters come from the true DAT record (after its two-byte file header).
/// This module is separate from the experimental battle simulation.
/// </summary>
public static class OriginalBuildingRules
{
    // EXE code object C80D..C88B. Interval counts invocations, not seconds.
    public static MaintenanceResult StepMaintenance(BuildingCondition state, MaintenanceParameters parameters,
        ResourceTriple balances, bool stateBit40)
    {
        MaintenanceResult Finish(byte signal) => new(state with { WorkFlags = 0x20 }, balances, false, signal);
        if (stateBit40 || ((state.ObjectFlags & 2) != 0 && (state.ObjectFlags & 0x1a) != 0x1a))
            return Finish(1);

        state = state with { Counter = unchecked((byte)(state.Counter + 1)) };
        if (state.Counter < parameters.Interval)
            return new(state, balances, true, 0);

        if (state.WorkFlags != 0)
        {
            state = state with { WorkFlags = 1 };
            var costs = parameters.Costs;
            // All three comparisons happen before any balance changes.
            if (balances.First < costs.First || balances.Second < costs.Second || balances.Third < costs.Third)
                return Finish(1);
            balances = new(balances.First - costs.First, balances.Second - costs.Second, balances.Third - costs.Third);
        }

        state = state with { Counter = 0 };
        // The original checks completion after the possible debit, not before it.
        if (state.Condition == parameters.Limit)
            return Finish(0);
        state = state with { Condition = unchecked((byte)(state.Condition + 1)) };
        return new(state, balances, true, 1);
    }

    // EXE code object BE04..BE2D. Target selection, strength generation and
    // deduplication precede this tail and are not implemented by this method.
    public static BuildingCondition ApplyDamageTail(BuildingCondition state, byte strength, byte protection, byte reaction)
    {
        if (unchecked((sbyte)state.Condition) > 0 && strength > protection)
        {
            state = state with { Condition = (byte)Math.Max(0, state.Condition - (strength - protection)) };
            reaction |= 2;
        }
        return state with { ObjectFlags = (ushort)((state.ObjectFlags & 0xffe0) | reaction) };
    }
}
