namespace Northwind;

public sealed class OriginalDiplomacyType
{
    public int Type { get; set; }
    public uint Handler { get; set; }
    public uint Duration { get; set; }
    public List<byte> Parameters { get; set; } = [];
    public string Text { get; set; } = "";
}
public sealed class OriginalDiplomacyData
{
    public int FormatVersion { get; set; }
    public List<OriginalDiplomacyType> Types { get; set; } = [];
    public List<byte> Reactions { get; set; } = [];
}

/// <summary>One 0Eh message record of a participant queue (+34): type FF is free.</summary>
public struct DiplomacyMessage
{
    public byte Type; public uint Time; public uint Deadline; public byte Other; public uint Parameter;
    public readonly byte[] Encode()
    {
        var raw = new byte[14]; raw[0] = Type;
        BitConverter.TryWriteBytes(raw.AsSpan(1), Time); BitConverter.TryWriteBytes(raw.AsSpan(5), Deadline);
        raw[9] = Other; BitConverter.TryWriteBytes(raw.AsSpan(10), Parameter); return raw;
    }
}

/// <summary>Settlement relations, war masks and message queues (participant +12, +B9, +34; statistics +5, +73).</summary>
public sealed class DiplomacyState
{
    public int Count { get; }
    public uint[] Hostile { get; }
    public byte[,] Relations { get; }
    public DiplomacyMessage[,] Queue { get; }
    public bool[] Dead { get; }
    public int[] Reputation { get; }
    public uint Time { get; set; }
    public DiplomacyState(int count)
    {
        Count = count; Hostile = new uint[count]; Relations = new byte[count, 16]; Queue = new DiplomacyMessage[count, 8];
        Dead = new bool[count]; Reputation = new int[count];
        for (int p = 0; p < count; p++) for (int s = 0; s < 8; s++) Queue[p, s].Type = 0xff;
    }
    public bool AtWar(int a, int b) => a != b && (Hostile[a] & (1u << (b & 31))) != 0;
}

/// <summary>Message kernel of LE:1: EDD5/EE69 sending, F250 first contact, F000/F057 war, EEFA, 21459, 23A6E, EF3F reactions.
/// Handlers of other message types are reported through <see cref="Unhandled"/>. See docs/diplomacy.md.</summary>
public sealed class OriginalDiplomacyRules(OriginalDiplomacyData data, uint reactionBase = 0xddb)
{
    public const uint FirstContactHandler = 0xf250, WarHandler = 0xf000, SilentWarHandler = 0xf057, NoHandler = 0xe65a;
    /// <summary>Called for handlers outside the kernel: handler address, BL|BH, the stored record.</summary>
    public Action<uint, ushort, DiplomacyMessage>? Unhandled { get; set; }
    private bool _reacting;
    private readonly List<(byte Me, byte Them)> _deferred = [];

    /// <summary>EDD5: queue a message of <paramref name="type"/> for <paramref name="owner"/> about <paramref name="other"/>. Returns false (AL=FF) when refused.</summary>
    public bool Send(DiplomacyState state, byte type, byte other, byte owner, byte bh, uint parameter)
    {
        for (int s = 0; s < 8; s++)
            if (state.Queue[owner, s].Type == type && state.Queue[owner, s].Other == other) return false;
        int slot = -1;
        for (int s = 0; s < 8 && slot < 0; s++) if (state.Queue[owner, s].Type == 0xff) slot = s;
        if (slot < 0) return false;
        var info = data.Types[type];
        ulong deadline = (ulong)state.Time + info.Duration;
        state.Queue[owner, slot] = new DiplomacyMessage { Type = type, Other = other, Parameter = parameter, Time = state.Time,
            Deadline = deadline > uint.MaxValue ? uint.MaxValue : (uint)deadline };
        Handle(state, info.Handler, owner, bh, slot);
        state.Queue[owner, slot].Type = type; state.Queue[owner, slot].Other = other;
        return true;
    }

    private void Handle(DiplomacyState state, uint handler, byte bl, byte bh, int slot)
    {
        var record = state.Queue[bl, slot];
        switch (handler)
        {
            case NoHandler: return;
            case FirstContactHandler:
                state.Relations[bl, bh] = 1;
                if (state.Relations[bh, bl] == 0)
                    Send(state, 3, bh, bh, bl, ((uint)bl << 16) | (ushort)record.Parameter);
                return;
            case WarHandler:
                if (!state.Dead[record.Other])
                {
                    byte them = record.Other;
                    state.Reputation[them] -= 2500;
                    if (state.Reputation[them] <= 0) state.Reputation[them] = 0;
                    War(state, bl, them);
                    React(state, bl, them);
                    return;
                }
                War(state, bl, record.Other);
                return;
            case SilentWarHandler:
                War(state, bl, record.Other);
                return;
            default:
                Unhandled?.Invoke(handler, (ushort)(bl | (bh << 8)), record);
                return;
        }
    }

    private static void War(DiplomacyState state, byte me, byte them)
    {
        for (int s = 0; s < 8; s++) if (state.Queue[me, s].Other == them) state.Queue[me, s].Type = 0xff;   // EEFA
        for (int s = 0; s < 8; s++) if (state.Queue[them, s].Other == me) state.Queue[them, s].Type = 0xff;
        if (me != them) { state.Hostile[me] |= 1u << (them & 31); state.Hostile[them] |= 1u << (me & 31); }   // 21459
        state.Relations[me, them] = 2; state.Relations[them, me] = 2;   // 23A6E
    }

    /// <summary>EF3F: every other living settlement reacts by its relations to both sides. Re-entry is deferred and replayed in order.</summary>
    private void React(DiplomacyState state, byte me, byte them)
    {
        if (_reacting) { _deferred.Add((me, them)); return; }
        _reacting = true;
        for (int d = 0; d < state.Count; d++)
        {
            if (state.Dead[d] || d == me || d == them) continue;
            int index = state.Relations[d, them] * 12 + state.Relations[d, me] * 2;
            byte first = data.Reactions[index], second = data.Reactions[index + 1];
            uint parameter = reactionBase + (uint)index;
            if (first != 0) Send(state, first, (byte)d, me, 0, parameter);
            if (second != 0) Send(state, second, (byte)d, them, 0, parameter);
        }
        _reacting = false;
        var pending = _deferred.ToArray(); _deferred.Clear();
        foreach (var (m, t) in pending) React(state, m, t);
    }
}
