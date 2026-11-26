using System.Buffers.Binary;
namespace Northwind;

public interface IPhantomWorld { void Appearance(byte army); void Redraw(); void Vitals(); void UnitStep(); byte Random(); }

/// <summary>The phantom (ARMY__03 3 and 32, WRK 212): a ghost of the mage's settlement. AH = 0 makes it visible to everybody (+20 = FF), gives it the life counter +31 = 7F and the
/// «hidden» mode +32 = FF, and state D8h: pending request, next state 3 — the vanishing action of the mages (FS:105) runs backwards for it, so it appears. The continuation after the
/// table of the unit step: while it lives the counter +31 drops by one every eight updates (+2E & 7 = 0); at zero the phantom's health goes to 0. It fights as any unit (its discharge is
/// the mage's effect type 0 with fire, <see cref="OriginalMageSpell"/>). Verified against the original x86 by tests/fixtures/phantom.json (tools/verify_phantom.py).</summary>
public static class OriginalPhantom
{
    public static byte Initialize(IPhantomWorld w, byte[] r, byte appearance)
    {
        r[0x20] = 0xff; r[0x31] = 0x7f; r[0x32] = 0xff; r[0x0f] = 7; r[0x14] = 0xd8;
        w.Appearance(appearance);
        return r[0x2e] = w.Random();
    }

    /// <summary>AH = 1: up to and including the unit step (the continuation is <see cref="Continuation"/>).</summary>
    public static void Update(IPhantomWorld w, byte[] r)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(0x17), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(0x17)) & 0xffe7));
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        w.Vitals(); w.UnitStep();
    }

    /// <summary>27F..29A; returns AL.</summary>
    public static byte Continuation(byte[] r, byte al = 0x55)
    {
        if ((sbyte)r[0x19] <= 0) return 0;
        if (r[0x31] == 0) r[0x19] = 0;
        if ((r[0x2e] & 7) != 0) return al;
        r[0x31]--; return 0;
    }
}
