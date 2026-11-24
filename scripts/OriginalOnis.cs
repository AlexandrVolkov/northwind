using System.Buffers.Binary;
namespace Northwind;

/// <summary>The quest script of the hermit Onis (ARMY__03.WRK 0DBF..11F1, template 14), same services as <see cref="OriginalRufin"/> (<see cref="IRufinWorld"/>). Initialization and voice are
/// those of Rufin. The step differs in what he asks for — the participants without the crystals of the lilac and green (items 0Ch, 0Dh) —, in the manuscript he shows (template 1Ah), in
/// the walls he opens (template 5, the six of them) and in the answers (11h, 50h, 26h, 43h, 5Ah, 4Ah, 4Dh). Verified against the original x86 by tests/fixtures/onis.json
/// (tools/verify_hermit.py). ADAPTERS: as for Rufin; when wall 0 is missing the reveal point is (0, 0) — the original reads the free memory after the object list.</summary>
public static class OriginalOnis
{
    private const int Speak = 0x143;
    public static void Initialize(IRufinWorld w, byte[] r, byte appearance) => OriginalRufin.Initialize(w, r, appearance);
    public static void Voice(IRufinWorld w) => OriginalRufin.Speak(w);

    public static void Step(IRufinWorld w, byte[] r)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x17), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x17)) & 0xffe7));
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        if ((sbyte)r[0x19] <= 0) { OriginalRufin.Standard(w); return; }
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
                if (w.IsBuilding(o, out byte threshold) && (sbyte)o[0x19] > (sbyte)threshold) OriginalRufin.Bts(r, 0x4b, o[3]);
        }
        OriginalRufin.Standard(w);
    }

    /// <summary>false: the iteration ends without the answers (the exits to 1188).</summary>
    private static bool Dialog(IRufinWorld w, byte[] r, int owner, int p)
    {
        void Say(byte m) => w.Say(m, (byte)p, (byte)owner);
        bool Bt(int o) => OriginalRufin.Bt(r, o, p);
        bool Bts(int o) => OriginalRufin.Bts(r, o, p);
        if (w.Diplomacy.Relations[owner, p] == 2)   // at war (FB9)
        {
            if (!Bt(0x49))
            {
                // FBF jumps to FD5 without loading AL when he builds on the hermit's land (4B) and has not answered (45): AL is the loop's leftover, p + 1 (0 for the first participant)
                byte type = 0x59;
                if (Bt(0x45) || !Bt(0x4b)) Say(0x59); else type = (byte)(p == 0 ? 0 : p + 1);
                if (OriginalRufin.FindQueue(w, p, type, owner, out int slot)) { OriginalRufin.Consume(w, p, slot); Say(0x5a); OriginalRufin.Forgive(w, r, p); return true; }
            }
            if (r[0x19] > 0x3c) return true;
            var (found, o) = w.ScanAttacker(0); if (!found || p != o![3]) return true;
            if (!Bt(0x3f)) { if (!Bts(0x43)) Say(0x4f); return true; }
            if (BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x47)) != 0) return true;
            if (!Bts(0x41)) Say(0x25);
            return true;
        }
        if (Bt(0x4b) && !Bt(0x45)) { Say(0x28); return true; }
        if (Bt(0x49)) return true;
        if (w.HasItem(p, 9)) { if (!Bts(0x49)) Say(0x2e); return true; }
        if (w.HasItem(p, 0x0a)) { if (!Bts(0x49)) Say(0x3d); return true; }
        if (!w.HasItem(owner, 0x0c))
        {
            if (w.HasItem(p, 0x0c)) { if (!Bts(0x33)) Say(0x42); } else if (!Bts(0x35)) Say(0x41);
            return true;
        }
        if (!w.HasItem(owner, 0x0d))
        {
            if (w.HasItem(p, 0x0d)) { if (!Bts(0x39)) Say(0x46); } else if (!Bts(0x37)) Say(0x45);
            return true;
        }
        if (BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x47)) != 0) return true;
        if (!w.HasItem(p, 8)) return true;
        Say(0x2d); return OpenTheWay(w, r, p);
    }

    /// <summary>F70: the rockfall (template 2) no longer touches the participant; the manuscript 1Ah shows him the way.</summary>
    private static bool OpenTheWay(IRufinWorld w, byte[] r, int p)
    {
        OriginalRufin.Bts(r, 0x47, p);
        var trap = w.FindObject(r[3], r[0], 2, 0); if (trap == null) return false;
        OriginalRufin.Btr(trap, 0x22, p);
        var scroll = w.FindObject(r[3], r[0], 0x1a, 0); if (scroll == null) return false;
        OriginalRufin.Bts(scroll, 0x28, p);
        w.Reveal(OriginalRufin.U32(scroll, 4), OriginalRufin.U32(scroll, 8), 0xa0a);
        return true;
    }

    /// <summary>1033: the answers of the participant in the queue.</summary>
    private static void Answers(IRufinWorld w, byte[] r, int owner, int p)
    {
        while (true)
        {
            if (OriginalRufin.FindQueue(w, p, 0x11, owner, out int slot)) { OriginalRufin.Consume(w, p, slot); foreach (int o in new[] { 0x33, 0x35, 0x37, 0x39 }) OriginalRufin.Btr(r, o, p); return; }
            if (OriginalRufin.FindQueue(w, p, 0x50, owner, out slot))
            {
                OriginalRufin.Consume(w, p, slot); OriginalRufin.Forgive(w, r, p); OriginalRufin.Bts(r, 0x3f, p);
                byte[]? wall = null;
                for (int skip = 5; skip >= 0; skip--)
                {
                    wall = w.FindObject(owner, r[0], 5, skip);
                    if (wall != null) OriginalRufin.Btr(wall, 0x22, p);
                }
                w.Say(0x98, (byte)p, (byte)owner);
                // the reveal uses EDI of the last search: the wall 0, or (none) the end of the object list, whose free memory reads as zeros
                w.Reveal(wall != null ? OriginalRufin.U32(wall, 4) : 0, wall != null ? OriginalRufin.U32(wall, 8) : 0, 0xa0a);
                var (created, switchObject) = w.Create(0, (byte)owner, 1, 0, 0, 0);
                if (!created) return;
                switchObject![0x22] = 5; switchObject[0x25] = 5; switchObject[0x23] = r[0]; switchObject[0x24] = (byte)owner; switchObject[0x26] = (byte)p;
                w.Initialize(switchObject, 0, 0x102); w.Remove(switchObject);
                return;
            }
            if (OriginalRufin.FindQueue(w, p, 0x26, owner, out slot)) { OriginalRufin.Consume(w, p, slot); OriginalRufin.Forgive(w, r, p); if (!OpenTheWay(w, r, p)) return; continue; }
            if (OriginalRufin.FindQueue(w, p, 0x43, owner, out slot)) { OriginalRufin.Consume(w, p, slot); OriginalRufin.Bts(r, 0x45, p); return; }
            foreach (byte type in new byte[] { 0x5a, 0x4a, 0x4d })
                if (OriginalRufin.FindQueue(w, p, type, owner, out slot)) { OriginalRufin.Consume(w, p, slot); OriginalRufin.Forgive(w, r, p); return; }
            return;
        }
    }
}
