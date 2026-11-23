using System.Buffers.Binary;
namespace Northwind;

/// <summary>What the quest script of the mage needs from the world; every member is one FS service of the original (recorded stubs in tools/verify_rufin.py unless noted).</summary>
public interface IRufinWorld
{
    DiplomacyState Diplomacy { get; }
    int ParticipantCount { get; }                 // FS:142
    byte Tick { get; }                            // FS:10, the update counter
    bool HasItem(int participant, byte item);     // FS:147 = 124D8 (original code in the check)
    byte[]? FindObject(int owner, byte army, byte template, int skip);   // FS:14F = 11BE0 (original code in the check)
    void Say(byte message, byte participant, byte speakerOwner);         // FS:143 = EE38: a message of the speaker's owner to the participant
    void Reveal(uint x, uint y, uint size);       // FS:167
    void Sweep(byte participant);                 // FS:16F
    (bool Found, byte[]? Object) ScanAttacker(byte mode);                // FS:17B = D895
    (bool Created, byte[]? Object) Create(byte army, byte owner, byte template, byte layer, uint x, uint y);   // FS:45 = A758
    void Initialize(byte[] record, uint eax, uint ecx);                  // FS:18F = 103EC
    void Remove(byte[] record);                   // FS:15 = 98CC
    void Redraw();                                // FS:1D
    IReadOnlyList<byte[]> Territory();            // FS:163: the objects of the mage's territory
    bool IsBuilding(byte[] record, out byte threshold);                  // the template of the object: flag 8 and not 20h, DAT[46]
    void StandardVitals(); void StandardActions();   // FS:DD, FS:E1 (the unit step of every object)
    void Voice(ushort id);                        // FS:111 (AH = 2)
    void Appearance(byte army);                   // FS:4D (AH = 0)
    byte Random();                                // FS:109 = 9E41
}

/// <summary>The quest script of Rufin and the hermits (ARMY__03.WRK 8EC, the same code serves the other mages): every 32 updates the mage looks at each participant he has met
/// and says what the participant lacks or holds — the messages 21h..5Dh of the dialog table (docs/diplomacy.md); the answers come back through the message queue (types 11h, 23h,
/// 26h, 39h, 59h, 5Ah). The state is a set of per-participant bit masks in the record: +33, +35, +37, +39 (said the request of the lilac stone / ... ), +3F, +41, +43, +45, +49, +4B, word +47.
/// Verified against the original x86 by tests/fixtures/rufin.json (tools/verify_rufin.py). ADAPTERS: FS:167 is register-preserving; the standard unit step (DD, E1) is the world's.</summary>
public static class OriginalRufin
{
    /// <summary>AH = 0: the initial record.</summary>
    public static void Initialize(IRufinWorld w, byte[] r, byte appearance)
    {
        r[0x20] = 0xff; r[0x31] = 0x7f; r[0x32] = 0; r[0x0f] = 0x0f;
        w.Appearance(appearance); r[0x2e] = w.Random();
    }

    /// <summary>AH = 2: the mage's voice.</summary>
    public static void Speak(IRufinWorld w) => w.Voice(0x9031);

    private static bool Bt(byte[] r, int offset, int bit) => (BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(offset)) & (1u << (bit & 31))) != 0;
    private static bool Bts(byte[] r, int offset, int bit) { bool was = Bt(r, offset, bit); BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(offset), BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(offset)) | (1u << (bit & 31))); return was; }
    private static void Btr(byte[] r, int offset, int bit) => BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(offset), BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(offset)) & ~(1u << (bit & 31)));
    private static uint U32(byte[] r, int o) => BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(o));

    /// <summary>AH = 1, one update.</summary>
    public static void Step(IRufinWorld w, byte[] r)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x17), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x17)) & 0xffe7));
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        if ((sbyte)r[0x19] <= 0) { Standard(w); return; }
        int owner = r[3];
        for (int p = 0; p + 1 != w.ParticipantCount; p++)
        {
            if (p == owner || w.Diplomacy.Relations[owner, p] == 0) continue;
            if ((w.Tick & 0x1f) == 0 && !Dialog(w, r, owner, p)) continue;
            Answers(w, r, owner, p);
        }
        if (w.Tick == 0)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x4b), 0);
            foreach (var o in w.Territory())
                if (w.IsBuilding(o, out byte threshold) && (sbyte)o[0x19] > (sbyte)threshold) Bts(r, 0x4b, o[3]);
        }
        Standard(w);
    }
    private static void Standard(IRufinWorld w) { w.StandardVitals(); w.StandardActions(); }

    private static bool FindQueue(IRufinWorld w, int participant, byte type, int speaker, out int slot)
    {
        for (slot = 0; slot < 8; slot++) if (w.Diplomacy.Queue[participant, slot].Type == type && w.Diplomacy.Queue[participant, slot].Other == speaker) return true;
        return false;
    }
    private static void Consume(IRufinWorld w, int participant, int slot) => w.Diplomacy.Queue[participant, slot].Type = 0xff;

    /// <summary>The speech part (every 32 updates); false: the script goes to the next participant without the answers (the CB0 exits).</summary>
    private static bool Dialog(IRufinWorld w, byte[] r, int owner, int p)
    {
        void Say(byte m) => w.Say(m, (byte)p, (byte)owner);
        void Alarm() => BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x17), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x17)) | 0x200));
        if (w.Diplomacy.Relations[owner, p] == 2)   // at war (AE9)
        {
            if (!Bt(r, 0x49, p))
            {
                if (Bt(r, 0x45, p) || !Bt(r, 0x4b, p)) Say(0x59);
                if (FindQueue(w, p, 0x59, owner, out int slot)) { Consume(w, p, slot); Say(0x5a); Forgive(w, r, p); return true; }
            }
            if (r[0x19] > 0x3c) return true;
            var (found, o) = w.ScanAttacker(0); if (!found || p != o![3]) return true;
            if (!Bt(r, 0x3f, p)) { if (Bts(r, 0x43, p)) return true; Say(0x38); Alarm(); return true; }
            if (BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x47)) != 0) return true;
            if (Bts(r, 0x41, p)) return true;
            Say(0x25); Alarm(); return true;
        }
        if (Bt(r, 0x4b, p) && !Bt(r, 0x45, p)) { Say(0x28); return true; }   // he builds on my land
        if (Bt(r, 0x49, p)) return true;
        if (w.HasItem(p, 9)) { if (!Bts(r, 0x49, p)) Say(0x2e); return true; }
        if (w.HasItem(p, 0x0b)) { if (!Bts(r, 0x49, p)) Say(0x3b); return true; }
        if (!w.HasItem(owner, 6))
        {
            if (!w.HasItem(p, 6)) { if (!Bts(r, 0x35, p)) Say(0x21); } else if (!Bts(r, 0x33, p)) Say(0x22);
            return true;
        }
        if (!w.HasItem(owner, 7))
        {
            if (!w.HasItem(p, 7)) { if (!Bts(r, 0x37, p)) Say(0x29); } else if (!Bts(r, 0x39, p)) Say(0x2a);
            return true;
        }
        if (BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x47)) != 0) return true;
        if (!w.HasItem(p, 8)) return true;
        Say(0x2d); return OpenTheWay(w, r, p);
    }

    /// <summary>A9D: Kochet's staff is in the hands of the participant: the rockfall trap is switched off for him and a manuscript shown. False: no trap or manuscript (CB0).</summary>
    private static bool OpenTheWay(IRufinWorld w, byte[] r, int p)
    {
        Bts(r, 0x47, p);
        var trap = w.FindObject(r[3], r[0], 2, 0); if (trap == null) return false;
        Btr(trap, 0x22, p);
        var scroll = w.FindObject(r[3], r[0], 0x18, 0); if (scroll == null) return false;
        Bts(scroll, 0x28, p);
        w.Reveal(U32(scroll, 4), U32(scroll, 8), 0xa0a);
        return true;
    }

    /// <summary>C83: the answer was given: the mage stops asking and his health is set to 78h.</summary>
    private static void Forgive(IRufinWorld w, byte[] r, int p)
    {
        if (!Bt(r, 0x45, p))
        {
            bool had = Bt(r, 0x4b, p); Btr(r, 0x4b, p);
            if (had) { w.Say(0x5d, (byte)p, r[3]); w.Sweep((byte)p); }
        }
        Btr(r, 0x33, p); Btr(r, 0x39, p); r[0x19] = 0x78;
    }

    /// <summary>B6F: the answers of the participant in the queue; the way to the hermit jumps back here.</summary>
    private static void Answers(IRufinWorld w, byte[] r, int owner, int p)
    {
        while (true)
        {
            if (FindQueue(w, p, 0x11, owner, out int slot)) { Consume(w, p, slot); Btr(r, 0x33, p); Btr(r, 0x35, p); Btr(r, 0x37, p); Btr(r, 0x39, p); return; }
            if (FindQueue(w, p, 0x39, owner, out slot))
            {
                Consume(w, p, slot); Forgive(w, r, p); Bts(r, 0x3f, p);
                var pit = w.FindObject(owner, r[0], 0x0c, 0); if (pit == null) return; Btr(pit, 0x22, p);
                pit = w.FindObject(owner, r[0], 0x0c, 1); if (pit == null) return; Btr(pit, 0x22, p);
                w.Say(0x96, (byte)p, (byte)owner);
                w.Reveal(U32(pit, 4), U32(pit, 8), 0xa0a);
                var (created, switchObject) = w.Create(0, (byte)owner, 1, 0, 0, 0);
                if (!created) return;
                switchObject![0x22] = 0x0c; switchObject[0x25] = 1; switchObject[0x23] = r[0]; switchObject[0x24] = (byte)owner; switchObject[0x26] = (byte)p;
                w.Initialize(switchObject, 0, 1); w.Remove(switchObject);
                return;
            }
            if (FindQueue(w, p, 0x26, owner, out slot)) { Consume(w, p, slot); Forgive(w, r, p); if (!OpenTheWay(w, r, p)) return; continue; }
            if (FindQueue(w, p, 0x23, owner, out slot)) { Consume(w, p, slot); Bts(r, 0x45, p); return; }
            if (FindQueue(w, p, 0x5a, owner, out slot)) { Consume(w, p, slot); Forgive(w, r, p); }
            return;
        }
    }
}
