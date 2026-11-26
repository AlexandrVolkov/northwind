using System.Buffers.Binary;
namespace Northwind;

/// <summary>The script of the hermit Garik (ARMY__03.WRK 1F39..206B, template 19): in a war he says 59h to the participant every 32 updates (5Ah when the participant answered with 59h); with the
/// foreign buildings of the participant on his land (mask +4B) he first clears a 59h in his own queue and then says nothing; in peace he remarks on those buildings with 79h. Like Kevin
/// he counts the buildings of the participants on his land once in 256 updates. Verified against the original x86 by tests/fixtures/garik.json (tools/verify_hermit.py).</summary>
public static class OriginalGarik
{
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
            if (p == owner || w.Diplomacy.Relations[owner, p] == 0 || (w.Tick & 0x1f) != 0) continue;
            if (w.Diplomacy.Relations[owner, p] != 2) { if (OriginalRufin.Bt(r, 0x4b, p)) w.Say(0x79, (byte)p, (byte)owner); continue; }
            if (!OriginalRufin.Bt(r, 0x4b, p))
            {
                if (OriginalRufin.FindQueue(w, p, 0x59, owner, out int slot)) { OriginalRufin.Consume(w, p, slot); w.Say(0x5a, (byte)p, (byte)owner); }
                else w.Say(0x59, (byte)p, (byte)owner);
            }
            else if (OriginalRufin.FindQueue(w, owner, 0x59, owner, out int own)) OriginalRufin.Consume(w, owner, own);   // 1FFB: DL is his own owner
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
}
