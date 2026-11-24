using System.Buffers.Binary;
namespace Northwind;

/// <summary>The script of the hermit Kevin (ARMY__03.WRK 1D72..1EC7, template 18): the shortest of the hermits. He asks nothing: in a war every 32 updates he says 59h to the participant
/// (5Ah when the participant has answered with 59h), in peace he remarks on the foreign buildings on his land once (83h, the mask +33), and the answer 85h is thanked with 86h. Once in
/// 256 updates he counts the buildings (not fortresses: flags &amp; 28h ≠ 28h) of the participants on his land into +4B. Verified against the original x86 by tests/fixtures/kevin.json
/// (tools/verify_hermit.py).</summary>
public static class OriginalKevin
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
            if (p == owner || w.Diplomacy.Relations[owner, p] == 0) continue;
            if ((w.Tick & 0x1f) == 0)
            {
                if (w.Diplomacy.Relations[owner, p] == 2)
                {
                    if (OriginalRufin.FindQueue(w, p, 0x59, owner, out int slot)) { OriginalRufin.Consume(w, p, slot); w.Say(0x5a, (byte)p, (byte)owner); }
                    else w.Say(0x59, (byte)p, (byte)owner);
                }
                else if (OriginalRufin.Bt(r, 0x4b, p)) { if (!OriginalRufin.Bts(r, 0x33, p)) w.Say(0x83, (byte)p, (byte)owner); }
                else OriginalRufin.Btr(r, 0x33, p);
            }
            if (OriginalRufin.FindQueue(w, p, 0x85, owner, out int thanks)) { OriginalRufin.Consume(w, p, thanks); w.Say(0x86, (byte)p, (byte)owner); }
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
