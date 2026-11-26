using System.Buffers.Binary;
namespace Northwind;

/// <summary>The script of the hermit Efil (ARMY__03.WRK 2111..2427, template 20, a record of 51h bytes), a relative of Miron's (<see cref="OriginalMiron"/>): the same grudge timer +4D, the same
/// threats and the same summoning of a phantom once in 256 updates, but his speeches are not gated by the clock (the masks do the gating), he opens eight walls of template 7 (answer
/// 50h), asks for no answer 74h, and wants the item 0Ah instead of 0Bh and 13h for the last threat (8Dh). Verified against the original x86 by tests/fixtures/efil.json
/// (tools/verify_hermit.py).</summary>
public static class OriginalEfil
{
    public static void Initialize(IRufinWorld w, byte[] r, byte appearance) => OriginalRufin.Initialize(w, r, appearance);
    public static void Voice(IRufinWorld w) => OriginalRufin.Speak(w);

    public static void Step(IMironWorld w, byte[] r)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x17), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x17)) & 0xffe7));
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        if ((sbyte)r[0x19] <= 0) { OriginalRufin.Standard(w); return; }
        int owner = r[3];
        for (int p = 0; p + 1 != w.ParticipantCount; p++)
        {
            if (p == owner || w.Diplomacy.Relations[owner, p] == 0) continue;
            if (!Answers(w, r, owner, p)) continue;
            Dialog(w, r, owner, p);
        }
        if (w.Tick == 0) OriginalMiron.Summon(w, r);
        OriginalRufin.Standard(w);
    }

    private static bool Answers(IMironWorld w, byte[] r, int owner, int p)
    {
        if (OriginalRufin.FindQueue(w, p, 0x50, owner, out int slot))
        {
            OriginalRufin.Consume(w, p, slot); OriginalRufin.Bts(r, 0x47, p);
            byte[]? wall = null;
            for (int skip = 7; skip >= 0; skip--)
            {
                wall = w.FindObject(owner, r[0], 7, skip);
                if (wall == null) return true;
                OriginalRufin.Btr(wall, 0x22, p);
            }
            w.Say(0x97, (byte)p, (byte)owner);
            w.Reveal(OriginalRufin.U32(wall!, 4), OriginalRufin.U32(wall!, 8), 0xa0a);
            var (created, sw) = w.Create(0, (byte)owner, 1, 0, 0, 0);
            if (!created) return false;
            sw![0x22] = 7; sw[0x25] = 7; sw[0x23] = r[0]; sw[0x24] = (byte)owner; sw[0x26] = (byte)p;
            w.Initialize(sw, 0, 0x101); w.Remove(sw);
            return true;
        }
        if (OriginalRufin.FindQueue(w, p, 0x65, owner, out slot) || OriginalRufin.FindQueue(w, p, 0x6b, owner, out slot))
        {
            OriginalRufin.Consume(w, p, slot); OriginalRufin.Bts(r, 0x45, p); OriginalMiron.Grudge(w, r, 0x2710);
        }
        return true;
    }

    private static void Dialog(IMironWorld w, byte[] r, int owner, int p)
    {
        void Say(byte m) => w.Say(m, (byte)p, (byte)owner);
        bool Bt(int o) => OriginalRufin.Bt(r, o, p);
        bool Bts(int o) => OriginalRufin.Bts(r, o, p);
        if (w.Diplomacy.Relations[owner, p] != 2)
        {
            foreach (int o in new[] { 0x3f, 0x41, 0x37, 0x3b, 0x3d }) OriginalRufin.Btr(r, o, p);
            if (r[0x19] < 0x78 || w.Time < OriginalRufin.U32(r, 0x4d)) return;
            bool had = Bt(0x45); OriginalRufin.Btr(r, 0x45, p);
            Say(had ? (byte)0x77 : (byte)0x78);
            return;
        }
        if (!w.HasItem(owner, 9))
        {
            if (w.HasItem(p, 9)) { if (!Bts(0x3f)) Say(0x67); }
            else if (OriginalRufin.FindQueue(w, p, 0x59, owner, out int slot) && !Bts(0x33)) { OriginalRufin.Consume(w, p, slot); Say(0x64); }
        }
        else if (!w.HasItem(owner, 0x0a))
        {
            if (w.HasItem(p, 0x0a)) { if (!Bts(0x41)) Say(0x8a); }
            else if (OriginalRufin.FindQueue(w, p, 0x59, owner, out int slot) && !Bts(0x35)) { OriginalRufin.Consume(w, p, slot); Say(0x87); }
        }
        if (r[0x19] > 0x3c) return;
        var (found, o2) = w.ScanAttacker(0); if (!found || p != o2![3]) return;
        void Threaten(byte m) { Say(m); r[0x19] = 0x78; OriginalMiron.Grudge(w, r, 0xbb8); }
        if (!Bts(0x37)) { Threaten(0x92); return; }
        if (!Bts(0x39)) { Threaten(0x70); return; }
        if (!Bt(0x47) && !Bts(0x3b)) { Threaten(0x4f); return; }
        if (!w.HasItem(owner, 0x13) || Bts(0x3d)) return;
        Threaten(0x8d);
    }
}
