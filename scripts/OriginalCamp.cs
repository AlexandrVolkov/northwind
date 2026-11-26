using System.Buffers.Binary;
namespace Northwind;

/// <summary>World of the horde's camp: AI blocks and participants, the generators, 22E46 (template of a class), 10AD2 (spot), A758/43E5 (new object),
/// 1E740 (AI character) and the alert beacons (103EC, 98DB).</summary>
public interface ICampWorld
{
    Span<byte> Block(byte participant);
    Span<byte> Participant(byte participant);
    byte Random();
    (byte Template, byte Army)? Pick(byte participant, uint mask, uint value);
    ReadOnlySpan<byte> Template(byte army, byte template);
    (uint X, uint Y)? Spot(byte template, byte army, byte variant, uint x, uint y);
    byte[]? Create(byte army, byte owner, byte template, byte variant, uint x, uint y);
    void Register(byte[] record);
    void SetCharacter(byte participant, byte character);
    /// <summary>103EC at the cell: a beacon object (the alert spawn), then 98DB removes it.</summary>
    void Beacon(byte[] record, byte cellX, byte cellY);
    (byte X, byte Y) Cursor { get; }
    IEnumerable<byte[]> Objects(byte owner);
    void Retire(byte[] record);
}

/// <summary>ARMY__00 «Лагерь», LE:1 11943..11B4D (FS 131): the camps of the horde of slot 4. State +15 = 0 waits: the timer +23 (2710h + 9E41) counts down; when
/// it passes zero and the participant has no castle-class building (AI block +5C = 0) a building of class A08h (the «Шатер») is founded near the camp
/// (22E46, 10AD2, A758, under construction, 43E5), the AI character 4 is loaded (1E740), the AI strength +19F1 becomes 7D0h and the camp goes to state 1.
/// State 1 feeds the participant every 1000h updates (+1F4 wood and metal, +3E8 gold, people 50); when the settlement is gone (+5C = 0) the camp returns to
/// state 0 with the timer 2710h — the horde comes back. Alerts (+27) spawn beacon objects in four directions when one of the participants 0..2 has strength.</summary>
public static class OriginalCamp
{
    private static readonly (sbyte X, sbyte Y)[] Directions = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];   // DS:19C7
    private static uint U32(ReadOnlySpan<byte> raw, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
    private static void W32(Span<byte> raw, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(raw[offset..], value);

    public static void Step(ICampWorld world, byte[] camp)
    {
        byte owner = camp[3];
        if (camp[0x15] == 0)
        {
            uint timer = unchecked(U32(camp, 0x23) - 1); W32(camp, 0x23, timer);
            if ((int)timer >= 0) return;
            var ai = world.Block(owner);
            if (ai[0x5c] != 0) { W32(camp, 0x23, (uint)world.Random() + 0x2710); return; }
            if (world.Pick(owner, 0xf08, 0xa08) is not var (template, army)) return;
            byte variant = (byte)(world.Template(army, template)[0x77] & 3);
            if (world.Spot(template, army, variant, U32(camp, 4), U32(camp, 8)) is not var (x, y)) return;
            if (world.Create(army, owner, template, variant, x, y) is not { } record) return;
            record[0x14] = 0x81; record[0x1a] = 1; world.Register(record);
            world.SetCharacter(ai[0], 4);
            W32(ai, 0x19f1, 0x7d0); ai[0x5c] = 1;
            camp[0x15] = 1;
        }
        else if (camp[0x15] == 1)
        {
            W32(camp, 0x23, U32(camp, 0x23) + 1);
            if ((U32(camp, 0x23) & 0xfff) != 0) return;
            var p = world.Participant(owner);
            W32(p, 0x18, U32(p, 0x18) + 0x1f4); W32(p, 0x1c, U32(p, 0x1c) + 0x1f4); W32(p, 0x20, U32(p, 0x20) + 0x3e8); W32(p, 0x24, 0x32);
            var ai = world.Block(owner);
            bool alert;
            if (ai[0x5c] == 0) { camp[0x15] = 0; W32(camp, 0x23, 0x2710); alert = true; }
            else
            {
                alert = false;
                for (byte i = 0; i < 3; i++) if (U32(world.Block(i), 0x19e9) > 0) { alert = true; break; }
                if (!alert)
                {
                    if (camp[0x27] == 0) return;
                    camp[0x27] = 0;
                }
            }
            if (alert) { if (camp[0x27] == 3) return; camp[0x27] = 3; }
            for (int edx = 6; edx >= 0; edx -= 2)
            {
                if (world.Create(camp[0], owner, camp[0x0e], 0, 0, 0) is not { } beacon) continue;
                byte cx = (byte)(Directions[edx].X + world.Cursor.X), cy = (byte)(Directions[edx].Y + world.Cursor.Y);
                beacon[0x22] = (byte)((edx + 4) & 7); beacon[0x27] = camp[0x27]; beacon[0x15] = 2;
                world.Beacon(beacon, cx, cy);
                world.Retire(beacon);
            }
        }
        else if (camp[0x15] == 2)
        {
            foreach (var other in world.Objects(owner))
                if (other[0] == camp[0] && other[0x0e] == camp[0x0e] && other[0x22] == camp[0x22]) other[0x15] = camp[0x27];
            world.Retire(camp);
        }
    }
}
