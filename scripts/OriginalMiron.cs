using System.Buffers.Binary;
namespace Northwind;

/// <summary>What the script of Miron needs beyond <see cref="IRufinWorld"/> (recorded stubs in tools/verify_hermit.py).</summary>
public interface IMironWorld : IRufinWorld
{
    uint Time { get; }                                  // FS:17F, the clock of the game
    uint Dim0 { get; } uint Dim4 { get; }               // FS:0 and FS:4: the size of the map in pixels (x is taken from FS:4, y from FS:0)
    byte Random2();                                     // FS:10D = 9E55
    (bool Found, uint X, uint Y) FreeSpot(byte radius, byte army, byte flag, uint x, uint y);   // FS:187 = 10AD2 (OriginalSpotFinder)
    void Occupy(byte[] record);                         // FS:19B = 43E5: the footprint of a new object
}

/// <summary>The quest script of the hermit Miron (ARMY__03.WRK 16C8..19FC, template 16, a record of 51h bytes: +4D is the time until which he stays angry or until he stops bearing a grudge).
/// Unlike the others he answers every update (queue 50h opens the two stone faces — template 4 —, 74h, 65h/6Bh), speaks every 32 updates (war: what the participant gives or lacks —
/// items 9 and 0Bh —, then the threat chain 92h, 70h, 4Fh, 73h; peace: he forgives after the grudge time, 77h/78h) and once in 256 updates summons a phantom (template 3) at a free random
/// spot. Verified against the original x86 by tests/fixtures/miron.json (tools/verify_hermit.py).</summary>
public static class OriginalMiron
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
            if ((w.Tick & 0x1f) != 0) continue;
            Dialog(w, r, owner, p);
        }
        if (w.Tick == 0) Summon(w, r);
        OriginalRufin.Standard(w);
    }

    internal static void Grudge(IMironWorld w, byte[] r, uint ticks) => BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(0x4d), w.Time + ticks);

    /// <summary>1748..1827: the answers; false: the iteration ends (1980).</summary>
    private static bool Answers(IMironWorld w, byte[] r, int owner, int p)
    {
        if (OriginalRufin.FindQueue(w, p, 0x50, owner, out int slot))
        {
            OriginalRufin.Consume(w, p, slot); OriginalRufin.Bts(r, 0x47, p);
            byte[]? face = null;
            for (int skip = 1; skip >= 0; skip--)
            {
                face = w.FindObject(owner, r[0], 4, skip);
                if (face == null) return true;   // 176E: to the test of the clock
                OriginalRufin.Btr(face, 0x22, p);
            }
            w.Say(0x97, (byte)p, (byte)owner);
            w.Reveal(OriginalRufin.U32(face!, 4), OriginalRufin.U32(face!, 8), 0xa0a);
            var (created, sw) = w.Create(0, (byte)owner, 1, 0, 0, 0);
            if (!created) return false;
            sw![0x22] = 4; sw[0x25] = 1; sw[0x23] = r[0]; sw[0x24] = (byte)owner; sw[0x26] = (byte)p;
            w.Initialize(sw, 0, 0x101); w.Remove(sw);
            return true;
        }
        if (OriginalRufin.FindQueue(w, p, 0x74, owner, out slot)) { OriginalRufin.Consume(w, p, slot); OriginalRufin.Bts(r, 0x49, p); w.Say(0x76, (byte)p, (byte)owner); return true; }
        if (OriginalRufin.FindQueue(w, p, 0x65, owner, out slot) || OriginalRufin.FindQueue(w, p, 0x6b, owner, out slot))
        {
            OriginalRufin.Consume(w, p, slot); OriginalRufin.Bts(r, 0x45, p); Grudge(w, r, 0x2710);
        }
        return true;
    }

    private static void Dialog(IMironWorld w, byte[] r, int owner, int p)
    {
        void Say(byte m) => w.Say(m, (byte)p, (byte)owner);
        bool Bt(int o) => OriginalRufin.Bt(r, o, p);
        bool Bts(int o) => OriginalRufin.Bts(r, o, p);
        if (w.Diplomacy.Relations[owner, p] != 2)   // 1944: peace
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
        else if (!w.HasItem(owner, 0x0b))
        {
            if (w.HasItem(p, 0x0b)) { if (!Bts(0x41)) Say(0x6d); }
            else if (OriginalRufin.FindQueue(w, p, 0x59, owner, out int slot) && !Bts(0x35)) { OriginalRufin.Consume(w, p, slot); Say(0x6a); }
        }
        if (r[0x19] > 0x3c) return;
        var (found, o2) = w.ScanAttacker(0); if (!found || p != o2![3]) return;
        void Threaten(byte m) { Say(m); r[0x19] = 0x78; Grudge(w, r, 0xbb8); }
        if (!Bts(0x37)) { Threaten(0x92); return; }
        if (!Bts(0x39)) { Threaten(0x70); return; }
        if (!Bt(0x47) && !Bts(0x3b)) { Threaten(0x4f); return; }
        if (Bt(0x49) || Bts(0x3d)) return;
        Threaten(0x73);
    }

    /// <summary>1992..19FC: once in 256 updates a phantom appears at a free spot near a random point of the map.</summary>
    internal static void Summon(IMironWorld w, byte[] r)
    {
        uint x = (uint)(((w.Random() * (byte)(w.Dim4 >> 5)) >> 8) << 5);
        uint y = (uint)(((w.Random2() * (byte)(w.Dim0 >> 5)) >> 8) << 5);
        var (found, sx, sy) = w.FreeSpot(3, r[0], 1, x, y);
        if (!found) return;
        var (_, phantom) = w.Create(r[0], r[3], 3, 1, sx, sy);
        if (phantom != null) w.Occupy(phantom);
    }
}
