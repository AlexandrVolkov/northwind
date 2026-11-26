using System.Buffers.Binary;
namespace Northwind;

/// <summary>What the script of Magus needs beyond <see cref="IRufinWorld"/>.</summary>
public interface IMagusWorld : IRufinWorld
{
    /// <summary>FS:18B = 11CC9: the number of regions of the campaign that hold no enemy of the army (the loop of 110C8). The original passes the leftover of AL as the army.</summary>
    byte CountFreeRegions(byte army);
}

/// <summary>The script of the hermit Magus (ARMY__03.WRK 1A6E..1CCC, template 17): he asks the participants for the item 14h (the mask +35 asks, +33 and +37 say that the participant has been
/// told); the answers 7Bh (+37), 81h (he lets two switch objects rise: templates 1 and 17h with +22 = 5 and the way of the hermit's items, 94h) and 5Ah (forget). The participant who builds
/// on his land (+4B) is warned with 79h (peace) or 59h; with few free regions (FS:18B) he says 7Ah. Verified against the original x86 by tests/fixtures/magus.json (tools/verify_hermit.py).</summary>
public static class OriginalMagus
{
    public static void Initialize(IRufinWorld w, byte[] r, byte appearance) => OriginalRufin.Initialize(w, r, appearance);
    public static void Voice(IRufinWorld w) => OriginalRufin.Speak(w);

    public static void Step(IMagusWorld w, byte[] r)
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

    private static void Dialog(IMagusWorld w, byte[] r, int owner, int p)
    {
        void Say(byte m) => w.Say(m, (byte)p, (byte)owner);
        bool war = w.Diplomacy.Relations[owner, p] == 2;
        if (!OriginalRufin.Bt(r, 0x4b, p))
        {
            if (war)
            {
                if (OriginalRufin.FindQueue(w, p, 0x59, owner, out int slot)) { OriginalRufin.Consume(w, p, slot); Say(0x5a); return; }   // 1B20: to the answers
                Say(0x59);
            }
        }
        else
        {
            if (w.CountFreeRegions(OriginalHermit.Leftover(p)) >= 5) { if (OriginalRufin.FindQueue(w, owner, 0x59, owner, out int own)) OriginalRufin.Consume(w, owner, own); }
            else if (!war) Say(0x79);
        }
        if (w.HasItem(owner, 0x14)) return;
        if (w.HasItem(p, 0x14)) { if (!OriginalRufin.Bts(r, 0x35, p)) Say(0x80); return; }
        if (!OriginalRufin.Bt(r, 0x4b, p) || OriginalRufin.Bts(r, 0x33, p) || OriginalRufin.Bts(r, 0x37, p)) return;
        if (w.CountFreeRegions(0x14) >= 7) Say(0x7a);
    }

    private static void Answers(IMagusWorld w, byte[] r, int owner, int p)
    {
        if (OriginalRufin.FindQueue(w, p, 0x7b, owner, out int slot)) { OriginalRufin.Consume(w, p, slot); OriginalRufin.Bts(r, 0x37, p); return; }
        if (!OriginalRufin.FindQueue(w, p, 0x81, owner, out slot))
        {
            if (OriginalRufin.FindQueue(w, p, 0x5a, owner, out slot)) { OriginalRufin.Consume(w, p, slot); OriginalRufin.Btr(r, 0x33, p); OriginalRufin.Btr(r, 0x35, p); }
            return;
        }
        OriginalRufin.Consume(w, p, slot); w.Say(0x94, (byte)p, (byte)owner);
        var (created, sw) = w.Create(0, (byte)owner, 1, 0, 0, 0);
        if (!created) return;
        sw![0x22] = 5; sw[0x25] = 3; sw[0x23] = r[0]; sw[0x24] = (byte)owner; sw[0x26] = (byte)p;
        w.Initialize(sw, 0, 2); w.Remove(sw);
        (created, sw) = w.Create(0, (byte)owner, 0x17, 0, 0, 0);
        if (!created) return;
        w.Initialize(sw!, 0, 2); w.Remove(sw!);
    }
}
