using System.Buffers.Binary;
namespace Northwind;

/// <summary>What the script of Kochet needs beyond <see cref="IRufinWorld"/>.</summary>
public interface IKochetWorld : IRufinWorld
{
    byte[]? NearestUnit(byte[] hermit, uint mask, uint value);   // FS:75 = C439: the nearest living object of the participant in the hermit's +3 whose template flags &amp; mask = value
    void TakeOver(byte[] unit, byte newOwner);                    // FS:193 = D1BA: the object passes to the participant
}

/// <summary>The script of the hermit Kochet (ARMY__03.WRK 2499..266A, template 21): asks for the item 14h like Magus does (without the count of free regions), says 59h/5Ah in a war and
/// 7Ah/80h about it, thanks 7Bh; the answer 81h (90h) makes him take over the participant's army: thirty times the nearest unit of the participant (templates without the flags 4800Ch) passes
/// to the hermit's owner (his +3 is the participant's during the loop). 5Ah forgets. Verified against the original x86 by tests/fixtures/kochet.json (tools/verify_hermit.py).</summary>
public static class OriginalKochet
{
    public static void Initialize(IRufinWorld w, byte[] r, byte appearance) => OriginalRufin.Initialize(w, r, appearance);
    public static void Voice(IRufinWorld w) => OriginalRufin.Speak(w);

    public static void Step(IKochetWorld w, byte[] r)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x17), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x17)) & 0xffe7));
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        if ((sbyte)r[0x19] <= 0) { OriginalRufin.Standard(w); return; }
        int owner = r[3];
        for (int p = 0; p + 1 != w.ParticipantCount; p++)
        {
            if (p == owner || w.Diplomacy.Relations[owner, p] == 0) continue;
            if ((w.Tick & 0x1f) == 0) Dialog(w, r, owner, p);
            Answers(w, r, owner, p);
        }
        if (w.Tick == 0)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x4b), 0);
            foreach (var o in w.Territory())
            {
                uint flags = w.ObjectFlags(o, out byte threshold);
                if ((sbyte)o[0x19] > (sbyte)threshold && (flags & 0x28) != 0x28) OriginalRufin.Bts(r, 0x4b, o[3]);
            }
        }
        OriginalRufin.Standard(w);
    }

    private static void Dialog(IKochetWorld w, byte[] r, int owner, int p)
    {
        void Say(byte m) => w.Say(m, (byte)p, (byte)owner);
        if (!OriginalRufin.Bt(r, 0x4b, p) && w.Diplomacy.Relations[owner, p] == 2)
        {
            Say(0x59);
            if (OriginalRufin.FindQueue(w, p, 0x59, owner, out int slot)) { OriginalRufin.Consume(w, p, slot); Say(0x5a); }
            return;
        }
        if (w.HasItem(owner, 0x14)) return;
        if (w.HasItem(p, 0x14)) { if (!OriginalRufin.Bts(r, 0x35, p)) Say(0x80); return; }
        if (!OriginalRufin.Bt(r, 0x4b, p) || OriginalRufin.Bts(r, 0x33, p) || OriginalRufin.Bts(r, 0x37, p)) return;
        Say(0x7a);
    }

    /// <returns>true; the loop goes on.</returns>
    private static bool Answers(IKochetWorld w, byte[] r, int owner, int p)
    {
        if (OriginalRufin.FindQueue(w, p, 0x7b, owner, out int slot)) { OriginalRufin.Consume(w, p, slot); OriginalRufin.Bts(r, 0x37, p); return true; }
        if (!OriginalRufin.FindQueue(w, p, 0x81, owner, out slot))
        {
            if (OriginalRufin.FindQueue(w, p, 0x5a, owner, out slot)) { OriginalRufin.Consume(w, p, slot); OriginalRufin.Btr(r, 0x33, p); OriginalRufin.Btr(r, 0x35, p); }
            return true;
        }
        OriginalRufin.Consume(w, p, slot); w.Say(0x90, (byte)p, (byte)owner);
        byte original = r[3]; r[3] = (byte)p;
        for (int i = 0; i < 30; i++)
            if (w.NearestUnit(r, 0x4800c, 0) is { } unit) w.TakeOver(unit, original);
        r[3] = original;
        return true;
    }
}
