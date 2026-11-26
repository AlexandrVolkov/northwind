using System.Buffers.Binary;
namespace Northwind;

/// <summary>The quest script of the hermit Irvil (ARMY__03.WRK 1297..163F, template 15), same services and skeleton as <see cref="OriginalOnis"/>: he asks for the crystals 0Eh and 0Dh, shows
/// the manuscript 19h after the rockfall (template 2) is switched off, and in a war says 5Fh when his own side holds the item 10h (otherwise 25h). Answers 11h, 60h, 26h, 54h, 5Ah. Verified
/// against the original x86 by tests/fixtures/irvil.json (tools/verify_hermit.py).</summary>
public static class OriginalIrvil
{
    public static void Initialize(IRufinWorld w, byte[] r, byte appearance) => OriginalRufin.Initialize(w, r, appearance);
    public static void Voice(IRufinWorld w) => OriginalRufin.Speak(w);

    public static void Step(IRufinWorld w, byte[] r) => OriginalHermit.Step(w, r, Dialog, Answers);

    private static bool Dialog(IRufinWorld w, byte[] r, int owner, int p)
    {
        void Say(byte m) => w.Say(m, (byte)p, (byte)owner);
        bool Bt(int o) => OriginalRufin.Bt(r, o, p);
        bool Bts(int o) => OriginalRufin.Bts(r, o, p);
        if (w.Diplomacy.Relations[owner, p] == 2)
        {
            if (!Bt(0x49))
            {
                byte type = 0x59;
                if (Bt(0x45) || !Bt(0x4b)) Say(0x59); else type = OriginalHermit.Leftover(p);
                if (OriginalRufin.FindQueue(w, p, type, owner, out int slot)) { OriginalRufin.Consume(w, p, slot); Say(0x5a); OriginalRufin.Forgive(w, r, p); return true; }
            }
            if (r[0x19] > 0x3c) return true;
            var (found, o) = w.ScanAttacker(0); if (!found || p != o![3]) return true;
            if (w.HasItem(owner, 0x10)) { if (!Bts(0x43)) Say(0x5f); return true; }
            if (BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x47)) != 0) return true;
            if (!Bts(0x41)) Say(0x25);
            return true;
        }
        if (Bt(0x4b) && !Bt(0x45)) { Say(0x28); return true; }
        if (Bt(0x49)) return true;
        if (w.HasItem(p, 0x0b)) { if (!Bts(0x49)) Say(0x3b); return true; }
        if (w.HasItem(p, 0x0a)) { if (!Bts(0x49)) Say(0x3d); return true; }
        if (!w.HasItem(owner, 0x0e))
        {
            if (w.HasItem(p, 0x0e)) { if (!Bts(0x33)) Say(0x53); } else if (!Bts(0x35)) Say(0x52);
            return true;
        }
        if (!w.HasItem(owner, 0x0d))
        {
            if (w.HasItem(p, 0x0d)) { if (!Bts(0x39)) Say(0x46); } else if (!Bts(0x37)) Say(0x45);
            return true;
        }
        if (BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x47)) != 0) return true;
        if (!w.HasItem(p, 8)) return true;
        Say(0x2d); return OriginalHermit.OpenTheWay(w, r, p, 0x19);
    }

    private static void Answers(IRufinWorld w, byte[] r, int owner, int p)
    {
        while (true)
        {
            if (OriginalRufin.FindQueue(w, p, 0x11, owner, out int slot)) { OriginalRufin.Consume(w, p, slot); foreach (int o in new[] { 0x33, 0x35, 0x37, 0x39 }) OriginalRufin.Btr(r, o, p); return; }
            if (OriginalRufin.FindQueue(w, p, 0x60, owner, out slot)) { OriginalRufin.Consume(w, p, slot); OriginalRufin.Forgive(w, r, p); return; }
            if (OriginalRufin.FindQueue(w, p, 0x26, owner, out slot)) { OriginalRufin.Consume(w, p, slot); OriginalRufin.Forgive(w, r, p); if (!OriginalHermit.OpenTheWay(w, r, p, 0x19)) return; continue; }
            if (OriginalRufin.FindQueue(w, p, 0x54, owner, out slot)) { OriginalRufin.Consume(w, p, slot); OriginalRufin.Bts(r, 0x45, p); return; }
            if (OriginalRufin.FindQueue(w, p, 0x5a, owner, out slot)) { OriginalRufin.Consume(w, p, slot); OriginalRufin.Forgive(w, r, p); }
            return;
        }
    }
}

/// <summary>The skeleton shared by the hermits' quest scripts (the code at the start of AH = 1 of Onis, Irvil and the others): the update of a living hermit visits the participants, speaks
/// every 32 updates, reads the answers, and once in 256 updates rescans the foreign buildings on his land.</summary>
public static class OriginalHermit
{
    /// <summary>AL at the jump into a queue search that skips its own load of AL: the loop's leftover (p + 1; 0 for the first participant).</summary>
    public static byte Leftover(int p) => (byte)(p == 0 ? 0 : p + 1);

    public static void Step(IRufinWorld w, byte[] r, Func<IRufinWorld, byte[], int, int, bool> dialog, Action<IRufinWorld, byte[], int, int> answers)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x17), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x17)) & 0xffe7));
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        if ((sbyte)r[0x19] <= 0) { OriginalRufin.Standard(w); return; }
        int owner = r[3];
        for (int p = 0; p + 1 != w.ParticipantCount; p++)
        {
            if (p == owner || w.Diplomacy.Relations[owner, p] == 0) continue;
            if ((w.Tick & 0x1f) == 0 && !dialog(w, r, owner, p)) continue;
            answers(w, r, owner, p);
        }
        if (w.Tick == 0)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x4b), 0);
            foreach (var o in w.Territory())
                if (w.IsBuilding(o, out byte threshold) && (sbyte)o[0x19] > (sbyte)threshold) OriginalRufin.Bts(r, 0x4b, o[3]);
        }
        OriginalRufin.Standard(w);
    }

    /// <summary>The way opens for the participant: the rockfall (template 2) no longer touches him and the manuscript shows him the way.</summary>
    public static bool OpenTheWay(IRufinWorld w, byte[] r, int p, byte manuscript)
    {
        OriginalRufin.Bts(r, 0x47, p);
        var trap = w.FindObject(r[3], r[0], 2, 0); if (trap == null) return false;
        OriginalRufin.Btr(trap, 0x22, p);
        var scroll = w.FindObject(r[3], r[0], manuscript, 0); if (scroll == null) return false;
        OriginalRufin.Bts(scroll, 0x28, p);
        w.Reveal(OriginalRufin.U32(scroll, 4), OriginalRufin.U32(scroll, 8), 0xa0a);
        return true;
    }
}
