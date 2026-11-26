using System.Buffers.Binary;
namespace Northwind;

/// <summary>The services of the scripts of ARMY__05.WRK (bird, bird generator, wolf, bear, remains).</summary>
public interface IFaunaWorld
{
    uint RandomA();                                         // FS:109 (9E41)
    void Redraw();                                          // FS:1D (81D9)
    void Wounded(uint effect, uint mask);                   // FS:129 (A444): EAX, EBX
    void Vitals();                                          // FS:DD (90E9)
    void UnitStep();                                        // FS:E1 (8227): the table dispatch, the script ends here
    void Tail(int kind);                                    // FS:E9 (8273) 1, FS:ED (82C3) 2, FS:F1 (839F) 3: the jump out of a handler
    byte Tick { get; }                                      // fs:[10]
    uint PoolBase { get; }                                  // fs:[8]
    uint Mask(byte owner);                                  // participant +12 (6877 gives the participant)
    void SetMask(byte owner, uint value);
    (bool Found, uint Target) Scan(byte al, byte owner);    // FS:17B (D895): the war mask of the participant is logged
    uint TemplateFlags(uint target);                        // FS:71 (68BD): the template flags +40 of the object
    void Sound(uint eax, uint target);                      // FS:1B3 (A6AE) with ESI = the object found (EDI of the search)
    (bool None, uint Eax, uint Ebx) Spot(byte al, byte ah); // FS:16B (9064)
    uint Create(byte al, byte ah, byte bl, byte bh, uint ecx, uint edx);   // FS:45 (A758): the new object's reference
    void Occupy(uint reference);                            // FS:19B (43E5)
    IReadOnlyList<byte> Children(byte owner);               // the template (+0E) of every object of the participant's chain
    (bool Zf, uint Flags) Lookup(byte[] r);                 // FS:A1 (982B): ZF, the template flags +40
}

/// <summary>The scripts of ARMY__05 (docs/fauna.md): bird 0..5E, generator 70..E9, wolf EA..13A (state 0 handler 13F), bear 1A8 (handler 239), wolf 2A2 (handler 2F7) and the remains 313..371. Verified
/// against the original x86 with the services recorded (tools/verify_fauna.py, tests/fixtures/fauna.json).</summary>
public static class OriginalFauna
{
    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static void Put(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);

    private static void RedrawIfAsked(IFaunaWorld w, byte[] r)
    {
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
    }

    /// <summary>The bird: AH = 0 makes it rest-hovering (+F = 7, request 6 in +2D), AH = 1 asks for the redraw, the wounded effect 0, the vitals and the unit step.</summary>
    public static void Bird(IFaunaWorld w, byte ah, byte[] r)
    {
        if (ah == 0) { r[0x0f] = 7; r[0x2d] = (byte)((r[0x2d] & 0x0f) | 0x60); return; }
        if (ah != 1) return;
        RedrawIfAsked(w, r); w.Wounded(0, 0x7ff); w.Vitals(); w.UnitStep();
    }

    /// <summary>The wolves (EA, 2A2) and the bear (1A8): AH = 0 sets +F = 7 and draws the update phase +2E; AH = 1 asks for the redraw, the bear that has died calls the sound 3C8 of its killer, then the vitals
    /// and the unit step.</summary>
    public static void Beast(IFaunaWorld w, byte ah, byte[] r, bool bear)
    {
        if (ah == 0) { r[0x0f] = 7; r[0x2e] = (byte)w.RandomA(); return; }
        if (ah != 1) return;
        RedrawIfAsked(w, r);
        if (bear && r[0x19] == 0)
        {
            uint saved = w.Mask(r[3]); w.SetMask(r[3], 0xffffffff); w.SetMask(r[3], w.Mask(r[3]) & ~(1u << (r[3] & 31)));
            var (found, target) = w.Scan(0, r[3]); w.SetMask(r[3], saved);
            if (found) w.Sound(0x3c8, target);
        }
        w.Vitals(); w.UnitStep();
    }

    /// <summary>The state 0 handler of the wolf and the bear (13F, 239; the second wolf's 2F7 only draws the wound effect and goes idle): the wounded effect 2; every eighth update (+2E) it looks for an enemy of the participant (D895, the own bit out of the war mask);
    /// one that is not a thing (template flag 8) becomes the target +1C and the order 5 (+14 = 50h) before the idle action (82C3).</summary>
    public static void Hunt(IFaunaWorld w, byte[] r, bool hunts = true)
    {
        w.Wounded(2, 0x7ff);
        if (hunts && (r[0x2e] & 7) == 0)
        {
            uint saved = w.Mask(r[3]); w.SetMask(r[3], 0xffffffff); w.SetMask(r[3], w.Mask(r[3]) & ~(1u << (r[3] & 31)));
            var (found, target) = w.Scan(0, r[3]); w.SetMask(r[3], saved);
            if (found && (w.TemplateFlags(target) & 8) == 0) { Put(r, 0x1c, target - w.PoolBase); r[0x14] = (byte)((r[0x14] & 7) | 0x50); }
        }
        w.Tail(2);
    }

    /// <summary>The bird generator (70..E9): at counter 0 it creates a bird (template 0 beside it) while the settlement has fewer than four birds (+E = 0) for every generator (+E = 1).</summary>
    public static void Generator(IFaunaWorld w, byte ah, byte[] r)
    {
        if (ah != 1 || w.Tick != 0) return;
        uint birds = 0, generators = 0;
        foreach (byte template in w.Children(r[3])) { if (template == 0) birds++; if (template == 1) generators++; }
        generators <<= 2;
        if (birds >= generators) return;
        var (none, eax, ebx) = w.Spot(0, r[0]); if (none) return;
        byte bh = (byte)ebx, bl = (byte)eax, al = (byte)(eax >> 8), ah2 = r[3];
        uint reference = w.Create(al, ah2, bl, bh, birds, generators);
        w.Occupy(reference);
    }

    /// <summary>The remains (313..371): AH ≠ 1 sets them up from the creature (+3 the owner → +2, +25 the heading → the frame D4h + 2 · (heading &amp; 6), the time 258h, the stages 4) unless the template
    /// is not a corpse-leaving one and AL = 2; AH = 1 counts the time, shows the next frame and after the last stage the object ends (+0 = FFh).</summary>
    public static void Remains(IFaunaWorld w, byte al, byte ah, byte[] r, byte[] source)
    {
        if (ah != 1)
        {
            var (zf, flags) = w.Lookup(r);
            if (zf || (flags & 2) == 0) { if (al == 2) { r[0] = 0xff; return; } }
            r[2] = source[3]; BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(3), (ushort)((source[0x25] & 6) * 2 + 0xd4)); Put(r, 0x18, 0x258); r[0] = 0; r[0x17] = 4; return;
        }
        uint time = U32(r, 0x18) - 1; Put(r, 0x18, time); if (time != 0) return;
        Put(r, 0x18, 0x258); BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(3), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(3)) + 1));
        r[0x17]--; if (r[0x17] != 0) return;
        r[0] = 0xff;
    }
}
