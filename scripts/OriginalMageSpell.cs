using System.Buffers.Binary;
namespace Northwind;

public interface IMageSpellWorld
{
    void Effect(byte army, byte type, byte layer, uint x, uint y);   // FS:19 = 9727
    void Sound(uint id);                                             // FS:35 = A47E
    void Blast(byte attack, byte kind, byte planes, byte cellX, byte cellY, byte width, byte height);   // FS:69 = BD0F
    (bool Found, byte Layer, uint X, uint Y) FindSpot(byte template, byte army); // FS:16B = 9064 (<see cref="OriginalSpotFinder"/>)
    byte[]? CreateObject(byte army, byte owner, byte template, byte layer, uint x, uint y);   // FS:45 = A758
    void Setup(byte[] created);                                      // FS:19B = 43E5
    byte Random();                                                   // FS:109 = 9E41
    byte[] Inventory(int owner);                                     // participant +CD
}

/// <summary>The discharge of the mages (ARMY__03 effect types 0 and 8; WRK 2E4C and 30D1). Type 0 is what the strike of an attacker of army 3 creates (9727 with BL = DAT[70] = 0 takes
/// the effect table of the army): the flash, 72×72, four frames of two updates, frame F0h + 4·heading; on creation it chooses what comes next from the items of the attacker's settlement
/// (not counting 10h and 13h): one — fire (ARMY__00 type 13h); two — the blast; none or three and more — the blast, one time in eight a phantom (template 3) is summoned next to the
/// target; the two hostile mages 16h/17h and the phantoms use fire. Type 8 hits the square around its point with BD0F (attack 14h, kind 2, all planes), the side of the square is
/// the distance to the attacker in cells (1..3). Verified against the original x86 by tests/fixtures/mage-spell.json (tools/verify_mage_spell.py).</summary>
public static class OriginalMageSpell
{
    public const byte Fire = 0x93, Blast = 8, BlastAttack = 0x14, BlastKind = 2, FlashFrames = 4, FlashFirstFrame = 0xf0;

    /// <summary>AH = 0 of type 0: <paramref name="effect"/> is the new effect record, <paramref name="attacker"/> the striking mage, (x, y) the target point.</summary>
    public static void Create(IMageSpellWorld w, byte[] effect, byte[] attacker, uint x, uint y)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(effect.AsSpan(3), (ushort)(FlashFirstFrame + ((attacker[0x25] & 7) << 2)));
        effect[0] = 0; effect[0x16] = 2; effect[0x17] = FlashFrames;
        int items = 0;
        foreach (byte item in w.Inventory(attacker[3])) if (item != 0 && item != 0x10 && item != 0x13) items++;
        int count = items; byte type = Fire;
        bool decided = false;
        if (attacker[0x0e] >= 0x16)
        {
            if (attacker[0x31] <= 0x0a) decided = true; else count = 3;
        }
        if (!decided && (attacker[0x0e] == 3 || attacker[0x0e] == 0x20 || count == 1)) decided = true;
        if (!decided)
        {
            type = Blast;
            if (count != 2 && (w.Random() & 7) == 0)
            {
                var (found, layer, spotX, spotY) = w.FindSpot(3, attacker[0]);
                if (found)
                {
                    var created = w.CreateObject(attacker[0], attacker[3], 3, layer, spotX, spotY);
                    if (created != null) w.Setup(created);
                }
                type = Fire;   // 2F1A
            }
        }
        w.Effect(effect[1], type, effect[5], x, y);
        w.Sound(0);
    }

    /// <summary>AH = 1 of type 0: the frame advances every two updates; after four frames the effect is gone.</summary>
    public static void Step(byte[] effect)
    {
        if (--effect[0x16] != 0) return;
        effect[0x16] = 2; BinaryPrimitives.WriteUInt16LittleEndian(effect.AsSpan(3), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(effect.AsSpan(3)) + 1));
        if (--effect[0x17] == 0) effect[0] = 0xff;
    }

    /// <summary>Type 8 (30D1): the blast around the effect's point; <paramref name="attacker"/> gives the distance.</summary>
    public static void BlastStep(IMageSpellWorld w, byte[] effect, byte[] attacker)
    {
        uint x = BinaryPrimitives.ReadUInt32LittleEndian(effect.AsSpan(7)), y = BinaryPrimitives.ReadUInt32LittleEndian(effect.AsSpan(0xc));
        int dx = unchecked((int)(BinaryPrimitives.ReadUInt32LittleEndian(attacker.AsSpan(4)) - x)); if (dx < 0) dx = unchecked(-dx);
        int dy = unchecked((int)(BinaryPrimitives.ReadUInt32LittleEndian(attacker.AsSpan(8)) - y)); if (dy < 0) dy = unchecked(-dy);
        uint distance = (uint)dx > (uint)dy ? (uint)dx : (uint)dy;
        byte side = (byte)(distance >> 5); if (side == 0) side = 1; if (side > 3) side = 3;
        effect[0] = 0xff;
        w.Blast(BlastAttack, BlastKind, 7, (byte)(x >> 5), (byte)(y >> 5), side, side);
    }
}
