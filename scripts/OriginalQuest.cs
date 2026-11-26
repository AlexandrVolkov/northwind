namespace Northwind;

public readonly record struct QuestObject(byte[] Record, uint Flags, byte Threshold);

/// <summary>The world the dialog handlers act in (see <see cref="OriginalQuestRules"/>): the diplomacy state, the inventories and wallets (<see cref="IItemWorld"/>), the objects.</summary>
public interface IQuestWorld : IItemWorld
{
    DiplomacyState Diplomacy { get; }
    OriginalItemRules Items { get; }
    bool Stat5(int owner);                       // [stats + 5] of a participant (nonzero: the participant takes no part, 0F0F3 treats it as dead)
    IReadOnlyList<QuestObject> QuestObjects(int owner);
    byte PoolOwnerAt(uint reference);            // the owner byte (+3) of the pool object at the reference (23BEE reads it for a target of 0, too)
    void Send(byte type, byte other, byte receiver, byte bh, uint parameter);   // EDD5
    void React(byte bl, byte bh, uint parameter);                               // EF3F
    void Give(byte item, byte owner);                                           // 1243A
}

/// <summary>The handlers of the message types that are dialogs (LE:1 F072..F5CE): peace, alliance, the trades of the hermits' staffs and crystals, the gifts. Each is called as the
/// message kernel EDD5 does: BL the participant that gets the message, [record + 9] the other one. Verified against the original x86 by tests/fixtures/quest.json
/// (tools/verify_quest.py; EDD5, 1243A and EF3F are recorded stubs). Not ported: F09C and F154 (vassalage: they read the stack's leftovers or call D1BA), F250 is in
/// <see cref="OriginalDiplomacyRules"/>.</summary>
public static class OriginalQuestRules
{
    /// <summary>Runs the handler; returns the type the stack slot [ESP+4] holds afterwards (F28B/F29C/F2AD replace the type of the message).</summary>
    public static byte Run(IQuestWorld w, uint handler, byte bl, byte bh, byte other, uint parameter, byte type)
    {
        switch (handler)
        {
            case 0xf072: Settle(w, bl, other, 3); w.React(bl, other, 0xe23); break;   // alliance joined: relation 3
            case 0xf0f3:   // F0F3: peace, relation 1 (a dead participant gets the plain peace of F12B)
                if (w.Stat5(other)) { Peace(w, bl, other); break; }
                Settle(w, bl, other, 1); w.React(bl, other, 0xeb3); break;
            case 0xf12b: Peace(w, bl, other); break;
            case 0xf14b: ClearQueues(w.Diplomacy, bl, other); break;
            case 0xf28b: return Word(w, 6) != 0xff01 ? (byte)0x32 : type;
            case 0xf29c: return Word(w, 12) != 0xff09 ? (byte)0x49 : type;
            case 0xf2ad: return Word(w, 14) != 0xff07 ? (byte)0x56 : type;
            case 0xf2be: Trade(w, 6, bl, other, true); break;
            case 0xf2de: Trade(w, 0x0c, bl, other, true); break;
            case 0xf2fe: Trade(w, 0x14, bl, other, true); break;
            case 0xf31e: Trade(w, 0x0e, bl, other, true); break;
            case 0xf33e: if (Take(w, 7, bl)) { Give(w, 7, other); w.SetGold(bl, w.Gold(bl) + 10000); Peace(w, bl, other); } break;
            case 0xf427: if (Take(w, 0x0d, bl)) { Give(w, 0x0d, other); w.SetGold(bl, w.Gold(bl) + 10000); Peace(w, bl, other); } break;
            case 0xf370: GiveAndAnswer(w, 9, bl, other, bh, parameter); break;
            case 0xf3ad: GiveAndAnswer(w, 0x0b, bl, other, bh, parameter); break;
            case 0xf3ea: GiveAndAnswer(w, 0x0a, bl, other, bh, parameter); break;
            case 0xf395: if (Take(w, 9, bl)) Give(w, 9, other); break;
            case 0xf3d2: if (Take(w, 0x0b, bl)) Give(w, 0x0b, other); break;
            case 0xf40f: if (Take(w, 0x0a, bl)) Give(w, 0x0a, other); break;
            case 0xf4bc: TradeBack(w, 0x10, bl, other); break;
            case 0xf4dd: TradeBack(w, 7, bl, other); break;
            case 0xf4fe: TradeBack(w, 0x0d, bl, other); break;
            case 0xf53b: TradeBack(w, 0x13, bl, other); break;
            case 0xf51f: w.SetGold(bl, w.Gold(bl) + 10000); Peace(w, bl, other); break;
            case 0xf55c:
            {
                ushort word = Word(w, 20);
                byte message = word == 0x606 ? (byte)0x7d : word == 0x602 ? (byte)0x7e : (byte)0x7f;
                w.Send(message, bl, other, bh, parameter); Peace(w, bl, other); break;
            }
            case 0xf58e: Give(w, 0x0f, other); break;
            case 0xf599: Give(w, 0x11, other); break;
            case 0xf5a4: Give(w, 0x12, other); break;
            case 0xf5af: Give(w, 0x1f, other); Give(w, 0x20, other); Give(w, 0x21, other); break;
        }
        return type;
    }

    private static ushort Word(IQuestWorld w, int item) => (ushort)(w.Items.Entries[item][0x15] | w.Items.Entries[item][0x16] << 8);   // the entry's +15 (word)
    private static bool Take(IQuestWorld w, byte item, int owner)   // 124A5: ZF = the item was found and removed
    {
        int slot = Array.IndexOf(w.Inventory(owner), item); if (slot < 0) return false;
        w.Inventory(owner)[slot] = 0; return true;
    }
    private static void Give(IQuestWorld w, byte item, int owner) => w.Give(item, (byte)owner);   // 1243A
    private static void Trade(IQuestWorld w, byte item, byte bl, byte other, bool peace)
    { if (Take(w, item, bl)) { Give(w, item, other); if (peace) Peace(w, bl, other); } }
    private static void TradeBack(IQuestWorld w, byte item, byte bl, byte other)
    { if (Take(w, item, other)) { Give(w, item, bl); Peace(w, bl, other); } }
    private static void GiveAndAnswer(IQuestWorld w, byte item, byte bl, byte other, byte bh, uint parameter)
    { if (Take(w, item, bl)) { Give(w, item, other); w.Send(0x31, bl, other, bl, parameter); } }   // 124A5 leaves BH = the participant it searched

    /// <summary>F12B: the war ends, the relations become 1 (peace) and the units of the two sides stop attacking each other.</summary>
    public static void Peace(IQuestWorld w, byte bl, byte other) => Settle(w, bl, other, 1);
    private static void Settle(IQuestWorld w, byte bl, byte other, byte relation)
    {
        ClearQueues(w.Diplomacy, bl, other);                                                                       // EEFA
        w.Diplomacy.Hostile[bl] &= ~(1u << (other & 31)); w.Diplomacy.Hostile[other] &= ~(1u << (bl & 31));       // 21490
        w.Diplomacy.Relations[bl, other] = relation; w.Diplomacy.Relations[other, bl] = relation;                  // 23A6E
        for (int pass = 0; pass < 2; pass++)                                                                       // 23B8E (twice over the same participant)
            foreach (var o in w.QuestObjects(bl))
            {
                if ((sbyte)o.Record[0x19] <= (sbyte)o.Threshold) continue;
                if ((o.Flags & 8) != 0) continue;   // buildings: the healing branch needs a byte of the type table that is 0
                uint target = BitConverter.ToUInt32(o.Record, 0x1c);
                if (w.PoolOwnerAt(target) == other) BitConverter.GetBytes(0u).CopyTo(o.Record, 0x1c);
            }
    }
    private static void ClearQueues(DiplomacyState d, byte a, byte b)
    {
        for (int s = 0; s < 8; s++) if (d.Queue[a, s].Other == b) d.Queue[a, s].Type = 0xff;
        for (int s = 0; s < 8; s++) if (d.Queue[b, s].Other == a) d.Queue[b, s].Type = 0xff;
    }
}
