using System.Buffers.Binary;
namespace Northwind;

/// <summary>The services of the scripts of the map objects (recorded stubs in tools/verify_object_scripts.py).</summary>
public interface IObjectScriptWorld
{
    byte Random();                                    // FS:109 = 9E41
    byte Random2();                                   // FS:10D = 9E55
    (uint Count, uint Hostile, byte[]? Object) Area(uint center, uint size);   // FS:14B = 11DDE: the objects in the rectangle (CL, CH the corner, DX the size); EDI the nearest hostile
    byte Tick { get; }                                // FS:10 the update counter
    uint Reference(byte[] record);                    // ESI − FS:8
    uint Claim(uint eax, uint ebx, ushort cx);        // FS:157 = D2D1: claim or crush the square (EAX the reference or 0)
    byte Distance(uint x, uint y);                    // FS:15B: AL
    void Spawn(ushort eax, byte bl, ushort cx, uint edx);   // FS:69
    void Redraw();                                    // FS:1D
    void Remove(byte[] record);                       // FS:15
    void Vitals(); void Actions();                    // FS:DD, FS:E1
    void Effect(byte[] record, byte type, byte layer, uint x, uint y);   // FS:19 with the three stack arguments (the object, x, y)
    void Sound(uint id);                              // FS:31
    byte TemplateByte3 { get; }                       // DAT[3] of the object (EDI in AH = 1)
    ushort TemplateBase { get; }                      // DAT[48], the base frame
    void EffectAt(byte army, byte type, byte layer, uint x, uint y);   // FS:19 called with registers only
    byte[]? FindObject(byte owner, byte army, byte template, byte skip);   // FS:14F = 11BE0
    void ClearQuestField(byte[] record);              // [participant of the owner + 12h] = 0 (FS:6D)
    (bool Zf, byte Al, byte[] Object, uint Reference) ServiceA1();   // FS:A1 (ZF = nothing found)
    byte ServiceA9(ushort ax);                        // FS:A9
    byte[]? ObjectAt(uint reference);                 // the object of a reference
    (bool Zf, uint Flags) Service119(ushort ax);       // FS:119: the template of the target (EDX): ZF = none
    uint MapWidthPx { get; } uint MapHeightPx { get; }   // FS:4 and FS:0
    uint FoundFlags { get; }                          // the template flags (+40h) of the object FS:11 found (EBX)
    (bool Found, byte[]? Object) Scan17B(uint eax, uint ebx, uint ecx, uint edx, uint esi);   // FS:17B (D895) with the registers set by the script
    void Say143(byte message, byte participant, byte speakerOwner);   // FS:143
    void Jump131();                                   // the tail jump to FS:131
    void Voice();                                     // FS:D1
    void Service115();                                // FS:115
    uint TemplateFlagsOf71();                         // FS:71: DAT[40] of the template of EDI
    void Service13E();                                // FS:13E
    void Service1AF();                                // FS:1AF
    void Reshape(ushort ax, uint edx);                // FS:55 = BB8B (OriginalLineNeighbors.Reshape)
    bool BuildingDispatch();                          // FS:41 = C88C: the table of the building follows the call; returns ZF to the continuation
    void Service25(uint parent, byte bl);             // FS:25
    void Service29(byte bl);                          // FS:29
    void Sound35(uint id);                            // FS:35
    (bool Zf, byte Al) Service11(uint eax);           // FS:11
    bool ServiceA5();                                 // FS:A5: ZF
    void ServiceAD(byte al, byte bl);                 // FS:AD
    void LevelExit(byte mode);                        // FS:1A3
    void Appearance(byte army);                       // FS:4D
    (bool Found, uint Reference) ScanMasked(byte mode, uint mask);   // FS:17B = D895 with the participants of the mask excluded (the quest field +12 of the participant is lent for it)
    uint QuestField(byte[] record);                   // [participant of the owner + 12h]
    uint AiStrength(byte owner);                      // FS:1B7: the strength +19F1 of the owner's AI state
    byte ParticipantCount { get; }                    // FS:142
    uint TemplateSound { get; }                       // DAT[A3]
    void OccupyRef(uint reference);                   // FS:19B with a reference
    void Handler(byte slot);                          // the end of a state handler: a jump to FS:ED
    bool Service1BF(uint edx);                        // FS:1BF: ZF (true = skip)
    (bool Found, uint X, uint Y) FreeSpot(byte al, byte ah, byte bl, uint x, uint y);   // FS:187 = 10AD2
    (bool Found, byte Al, byte Ah, byte Bl) Beside(byte al, byte ah);   // FS:16B = 9064
    (bool Created, byte[]? Object) Create(byte army, byte owner, byte template, byte layer, uint x, uint y);   // FS:45 = A758
    void Occupy(byte[] record);                       // FS:19B = 43E5
    IEnumerable<byte> OwnTemplates(byte[] record);    // FS:6D: the templates of the objects of the owner's participant
}

/// <summary>The scripts of the map objects of ARMY__03.WRK. The rockfall (template 2, WRK 189): AH = 0 sets +22 = FFFFFFBF; AH = 1: every 4th random draw looks at the rectangle of 4×4 cells
/// from two cells left of the object; with something in it a stone (effect 98h) falls at a random point of the 96-pixel square around the object, and the sound 6 plays every 32 updates
/// of the counter +26. The stone face (template 4, 2B9) is the same trap with a face that blinks: frames 140h..14Bh, the states of +15 (0 waiting, 1 opening with +2A counting the frames,
/// 2 closing, 3 pause) and the sounds 5 and 6. Verified against the original x86 by tests/fixtures/obj-rockfall.json and obj-face.json (tools/verify_object_scripts.py).</summary>
public static class OriginalObjectScripts
{
    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static void Put(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);
    private static void Word(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), v);
    private static ushort Word(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));

    private static uint Corner(byte[] r)
    {
        uint cx = U32(r, 4) >> 5, cy = U32(r, 8) >> 5;
        return (cx & 0xffff00ff) | ((cy & 0xff) << 8);
    }
    private static uint CornerLeft(byte[] r) { uint c = Corner(r); return (c & 0xffffff00) | (byte)((byte)c - 2); }

    /// <summary>The stone falling at a random point of the square (the common part of 19B..211 and 31A..36D).</summary>
    private static void Stone(IObjectScriptWorld w, byte[] r, int dx, int dy)
    {
        uint eax = (uint)((w.Random() * 0x60) >> 8);
        uint x = (uint)(U32(r, 4) + eax - 0x30), y = U32(r, 8) - (uint)dy;
        eax = (uint)((w.Random2() * 0x60) >> 8);
        w.Effect(r, 0x98, 1, x, y + eax + (uint)dx);
        if ((U32(r, 0x26) & 0x1f) == 0) w.Sound(6);
    }

    public static void Rockfall(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { Put(r, 0x22, 0xffffffbf); return; }
        if (ah != 1) return;
        if ((w.Random2() & 3) != 0) return;
        var (count, _, _) = w.Area(CornerLeft(r), 0x404);
        if (count == 0) return;
        Stone(w, r, 0x32, 0);
    }

    public static void Face(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { Put(r, 0x22, 0xffffffbf); r[0x15] = 0; Put(r, 8, U32(r, 8) + 0x10); Put(r, 4, U32(r, 4) + 4); return; }
        if (ah != 1) return;
        Put(r, 0x26, U32(r, 0x26) + 1);
        if (r[0x15] == 0)
        {
            if ((w.Random2() & 3) != 0) return;
            var (count, hostile, _) = w.Area(CornerLeft(r), 0x404);
            if (hostile != 0) { r[0x15] = 1; r[0x2a] = 0; Word(r, 1, 0x140); }
            else
            {
                if (count == 0) return;
                Stone(w, r, 0x46, 0x20);
                return;
            }
        }
        if ((U32(r, 0x26) & 3) != 0) return;
        if (r[0x15] == 1)
        {
            Word(r, 1, (ushort)(Word(r, 1) + 1)); r[0x2a]++;
            if (r[0x2a] == 0x0a) w.Sound(5);
            if (r[0x2a] < 0x15) return;
            r[0x15] = 2; r[0x2a] = 0x0b; Word(r, 1, 0x14b);
        }
        if (r[0x15] == 2)
        {
            Word(r, 1, (ushort)(Word(r, 1) - 1)); r[0x2a]--;
            if (r[0x2a] != 0) return;
            r[0x15] = 3;
        }
        if ((U32(r, 0x26) & 0x1f) != 0) return;
        var (_, still, _) = w.Area(CornerLeft(r), 0x404);
        if (still != 0) return;
        r[0x15] = 0;
    }

    /// <summary>The chest with the birds (template 29, 2A6B): the chest hurt below 100 opens by its frames (+15 counts two updates per frame), then ten ravens (template 8, layer 2) fly up at free spots
    /// around it; opened it lies and its health runs down every 50 updates. DAT[3] is the owner of the birds, DAT[48] the base frame.</summary>
    public static void Chest(IObjectScriptWorld w, byte[] r, int ah)
    {
        byte dat3 = w.TemplateByte3; ushort dat48 = w.TemplateBase;
        if (ah == 0) { r[0x19] = 0x64; r[0x31] = 0x22; return; }
        if (ah != 1) return;
        if ((sbyte)r[0x19] <= 0) { r[0x19]--; if (r[0x19] > 0xfa) return; w.Remove(r); return; }
        if ((r[0x14] & 0x80) != 0) { r[0x14] = 0; w.Redraw(); }
        if (r[0x14] == 0)
        {
            if ((sbyte)r[0x19] >= 0x64) return;
            r[0x14] = 1; r[0x15] = 0;
        }
        if (r[0x14] == 1)
        {
            r[0x15]++;
            if ((sbyte)r[0x15] < 2) return;
            r[0x15] = 0; Word(r, 1, (ushort)(Word(r, 1) + 1));
            if (Word(r, 1) < (ushort)(dat48 + 0x0a)) return;
            if (!w.Service1BF(0x505))
            {
                for (int i = 0; i < 10; i++)
                {
                    var (found, x, y) = w.FreeSpot(8, r[0], 2, U32(r, 4), U32(r, 8));
                    if (!found) continue;
                    var (created, bird) = w.Create(r[0], dat3, 8, 2, x, y);
                    if (created) w.Occupy(bird!);
                }
            }
            r[0x14] = 3; r[0x31] = 0;
        }
        if (r[0x14] == 3)
        {
            r[0x15]--;
            if (r[0x15] != 0) return;
            r[0x19]--; r[0x15] = 0x32;
        }
    }

    /// <summary>The generator (template 28, 29F1): every 256 updates creates a raven (template 8) beside itself while there are fewer ravens than four times the number of the objects of template 1Ch.</summary>
    public static void Generator(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah != 1 || w.Tick != 0) return;
        uint birds = 0, others = 0;
        foreach (byte t in w.OwnTemplates(r)) { if (t == 8) birds++; if (t == 0x1c) others++; }
        if (birds >= others << 2) return;
        var (found, al, ahOut, bl) = w.Beside(8, r[0]);
        if (!found) return;
        var (_, created) = w.Create(ahOut, r[3], al, bl, birds, others << 2);   // ECX and EDX still hold the two counts
        if (created != null) w.Occupy(created);
    }

    /// <summary>The dragon (template 27, 279A): a guard that wakes when somebody comes within 4 cells (+15: 0 asleep, 1 rising, 2 breathing frames 2E5h..303h and looking 9 cells around, 3 the fire
    /// breath on frame 317h (effect 10h at x−43h, y+4Ah, sounds 7 and 8), 4 lying down after its health is below 0Bh, 5 dead). Its death switches off all the trees (template 7) and pits
    /// (template 0Ch) of its side and clears +12h of its participant (the quest of the dragon). Verified by tests/fixtures/obj-dragon.json.</summary>
    public static void Dragon(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { Put(r, 0x22, 0xffffffff); Word(r, 1, 0x2e5); Put(r, 8, U32(r, 8) - 0x10); Put(r, 4, U32(r, 4) + 0x10); return; }
        if (ah != 1) return;
        if ((r[0x14] & 0x80) != 0) { r[0x14] = 0; Word(r, 0x17, (ushort)(Word(r, 0x17) | 0x80)); Word(r, 1, 0x2bd); }
        if ((sbyte)r[0x19] <= 0)
        {
            r[0x19]--; Word(r, 1, 0x32a);
            if ((sbyte)r[0x19] > unchecked((sbyte)0xfa)) return;
            w.Sound(9); w.EffectAt(r[0], 0x11, 1, U32(r, 4), U32(r, 8)); w.Remove(r);
            Collapse(w, r); return;
        }
        if (r[0x15] == 0)
        {
            if ((w.Tick & 0x1f) != 0) return;
            var (count, _, _) = w.Area(CornerMinus(r, 4, 0), 0x405);
            if (count == 0) return;
            r[0x15]++; Word(r, 0x17, (ushort)(Word(r, 0x17) & 0xff7f)); w.Sound(7);
        }
        if (r[0x15] == 1)
        {
            if ((w.Tick & 1) == 0) return;
            Word(r, 1, (ushort)(Word(r, 1) + 1));
            uint claimed = w.Claim(w.Reference(r), 0, 0x00ff);
            if (Word(r, 1) < 0x2e5) return;
            Word(r, 1, (ushort)(Word(r, 1) - 1));
            if (claimed != 0) return;
            r[0x15]++;
        }
        if (r[0x15] == 2)
        {
            if (r[0x19] <= 0x0a) { Collapse(w, r); return; }
            if ((w.Tick & 1) == 0) return;
            Word(r, 1, (ushort)(Word(r, 1) + 1));
            if (Word(r, 1) < 0x303) return;
            Word(r, 1, 0x2e5);
            var (count, _, _) = w.Area(CornerMinus(r, 9, 0), 0xa0b);
            if (count == 0) return;
            r[0x15]++; Word(r, 1, 0x303);
        }
        if (r[0x15] == 3)
        {
            if ((w.Tick & 1) == 0) return;
            Word(r, 1, (ushort)(Word(r, 1) + 1));
            if (Word(r, 1) == 0x317) { w.EffectAt(r[0], 0x10, 1, U32(r, 4) - 0x43, U32(r, 8) + 0x4a); w.Sound(8); }
            if (Word(r, 1) < 0x32b) return;
            Word(r, 1, 0x303); r[0x15]--; return;
        }
        if (r[0x15] == 4)
        {
            if ((w.Tick & 1) == 0) return;
            Word(r, 1, (ushort)(Word(r, 1) - 1));
            if (Word(r, 1) != 0x2bd) return;
            Word(r, 0x17, (ushort)(Word(r, 0x17) | 0x280)); r[0x15]++;
            w.Claim(0, w.Reference(r), 0);
        }
    }

    /// <summary>292B: the dragon falls: state 4, the sound 7, the quest field cleared, the trees and the pits of its side switched off.</summary>
    private static void Collapse(IObjectScriptWorld w, byte[] r)
    {
        r[0x15] = 4; w.Sound(7); w.ClearQuestField(r);
        foreach (byte template in new byte[] { 7, 0x0c })
            for (byte skip = 0; ; skip++)
            {
                var o = w.FindObject(r[3], r[0], template, skip); if (o == null) break;
                Put(o, 0x22, 0);
            }
    }

    /// <summary>The phantom 32 (template 32, 2BA7): a hidden phantom (+32 = FF, state +14 = D8) that every 8 updates looks for an enemy among the participants other than its own and
    /// attacks him (state request 50h, the target +1C). AH = 0: init; AH = 1: the redraw and the standard step; <see cref="Phantom32Idle"/> is its idle handler (2C1D).</summary>
    public static void Phantom32(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { r[0x20] = 0xff; r[0x31] = 0x7f; r[0x32] = 0xff; r[0x0f] = 7; r[0x14] = 0xd8; w.Appearance(4); r[0x2e] = w.Random(); return; }
        if (ah != 1) return;
        Word(r, 0x17, (ushort)(Word(r, 0x17) & 0xffe7));
        Redraw(w, r); w.Vitals(); w.Actions();
    }

    public static void Phantom32Idle(IObjectScriptWorld w, byte[] r)
    {
        if ((r[0x2e] & 7) == 0)
        {
            var (found, reference) = w.ScanMasked(0, ~(1u << (r[3] & 31)));
            if (found) { Put(r, 0x1c, reference); r[0x14] = (byte)((r[0x14] & 7) | 0x50); }
        }
        w.Handler(0xed);
    }

    /// <summary>The jug with the phantom (template 33, 2C6D): a whole jug stays; broken it releases a phantom of the last participant (template 20h) at a free spot and dust (effect 7), then goes.</summary>
    public static void JugPhantom(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { r[0x31] = 0x22; r[0x19] = 0x64; return; }
        if (ah != 1) return;
        if ((sbyte)r[0x19] > 0) { if ((r[0x14] & 0x80) != 0) { r[0x14] = 0; w.Redraw(); } return; }
        r[0x19]--;
        if (r[0x19] > 0xfa) return;
        Word(r, 0x17, (ushort)(Word(r, 0x17) | 0x80));
        w.OccupyRef(0);
        var (found, x, y) = w.FreeSpot(0x20, r[0], 1, U32(r, 4), U32(r, 8));
        if (found)
        {
            var (created, phantom) = w.Create(r[0], (byte)(w.ParticipantCount - 1), 0x20, 1, x, y);
            if (created) w.Occupy(phantom!);
        }
        w.EffectAt(r[0], 7, 0, U32(r, 4), U32(r, 8)); w.Remove(r);
    }

    /// <summary>The talking object (2D08): when somebody is near (FS:1BF), says its sound DAT[A3] once.</summary>
    public static void Talking(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah != 1 || (w.Tick & 0x3f) != 0) return;
        if (r[0x15] == 0)
        {
            if (w.Service1BF(0x505)) return;
            r[0x15] = 1; w.Sound(w.TemplateSound);
        }
        if (w.Service1BF(0x505)) r[0x15] = 0;
    }

    /// <summary>The second face (2D52): wakes when somebody is near (+15: 1 plays the frames 140h..154h with the counter +0F, the sound DAT[A3] at 10, up to 3Ch; 2 plays back; 3 waits for the place to empty).</summary>
    public static void FaceB(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { r[0x15] = 0; Put(r, 8, U32(r, 8) + 0x10); Put(r, 4, U32(r, 4) + 4); return; }
        if (ah != 1) return;
        if (r[0x15] == 0)
        {
            if ((w.Tick & 0x3f) != 0) return;
            if (w.Service1BF(0x505)) return;
            r[0x15] = 1; r[0x0f] = 0; Word(r, 1, 0x140);
        }
        if ((w.Tick & 3) != 0) return;
        if (r[0x15] == 1)
        {
            Word(r, 1, (ushort)(Word(r, 1) + 1));
            if (Word(r, 1) > 0x154) Word(r, 1, 0x14b);
            r[0x0f]++;
            if (r[0x0f] == 0x0a) w.Sound(w.TemplateSound);
            if (r[0x0f] < 0x3c) return;
            r[0x15] = 2; r[0x0f] = 0x0b; Word(r, 1, 0x14b);
        }
        if (r[0x15] == 2)
        {
            Word(r, 1, (ushort)(Word(r, 1) - 1)); r[0x0f]--;
            if (r[0x0f] != 0) return;
            r[0x15] = 3;
        }
        if ((w.Tick & 0x3f) != 0) return;
        if (w.Service1BF(0x505)) r[0x15] = 0;
    }

    /// <summary>The rockfall of a participant's quest (2E19): the trap's mask +22 is the participant's quest field +12 while his strength is above 7CFh (zero otherwise); then the rockfall.</summary>
    public static void RockfallQuest(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 1)
        {
            uint field = w.QuestField(r); uint strength = w.AiStrength(r[3]);
            if (strength <= 0x7cf) field = 0;
            Put(r, 0x22, field);
        }
        Rockfall(w, r, ah);
    }

    /// <summary>The manuscripts (templates 24..26, 2710 / 2780 / 278D differ in the item +31 they hold: 18h, 19h, 1Bh): AH = 0 sets the item and the health 64h; AH = 8 (a visitor takes the page): the
    /// strength +19F1 of the owner's AI above 7CFh lets only the participants whose bit is not set in +28 take it; AH = 1 the redraw and the fade after the health is gone. Returns AX as left by the
    /// original.</summary>
    public static ushort Manuscript(IObjectScriptWorld w, byte[] r, int ah, byte item, byte visitorOwner)
    {
        if (ah == 0) { r[0x31] = item; r[0x19] = 0x64; return 0; }
        if (ah == 8)
        {
            bool takes = w.AiStrength(r[3]) <= 0x7cf || ((U32(r, 0x28) >> (visitorOwner & 31)) & 1) != 0;
            if (!takes) return 0xff00;
            byte al = r[0x31]; r[0x31] = 0; r[0x19] = 0; return al;
        }
        if (ah != 1) return (ushort)(ah << 8);
        if ((sbyte)r[0x19] > 0) { if ((r[0x14] & 0x80) != 0) { r[0x14] = 0; w.Redraw(); } return 0x100; }
        r[0x19]--;
        if (r[0x19] > 0xfa) return 0x100;
        w.Remove(r); return 0x100;
    }

    /// <summary>The switch object of ARMY__00 (template 1, WRK 52) that a hermit creates when he opens a way: +22 the template, +23 the army, +24 the owner, +25 the count, +26 the participant. Within
    /// one update it visits the objects of that template and army (count + 1 of them) and clears the participant's bit in their mask +22 (the trap no longer touches him); then it removes itself.</summary>
    public static void Switch(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah != 1) return;
        RunSwitch(w.FindObject, r); w.Remove(r);
    }

    /// <summary>The body of the switch: the objects it opens are looked up through <paramref name="find"/> (owner, army, template, skip).</summary>
    public static void RunSwitch(Func<byte, byte, byte, byte, byte[]?> find, byte[] r)
    {
        do
        {
            var o = find(r[0x24], r[0x23], r[0x22], r[0x25]);
            if (o != null) Put(o, 0x22, U32(o, 0x22) & ~(1u << (r[0x26] & 31)));
            r[0x25]--;
        } while ((sbyte)r[0x25] >= 0);
        Word(r, 0x17, (ushort)(Word(r, 0x17) | 0x80));
    }

    /// <summary>The second switch (template 23, WRK 371): every 32 updates it blanks +31 of the objects 16h and 17h of army 3 and owner 4.</summary>
    public static void Switch2(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah != 1 || (w.Tick & 0x1f) != 0) return;
        foreach (byte template in new byte[] { 0x16, 0x17 })
            if (w.FindObject(4, 3, template, 0) is { } o) o[0x31] = 0;
    }

    /// <summary>The exit (template 24, WRK 3A9): every 64 updates, when somebody stands in the square of 15×15 cells around, the level ends (FS:1A3, mode 3).</summary>
    public static void Exit(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { Put(r, 0x22, 7); return; }
        if (ah != 1 || (w.Tick & 0x3f) != 0) return;
        var (count, _, _) = w.Area(Corner(r) - 0x707, 0xe0e);
        if (count != 0) w.LevelExit(3);
    }

    /// <summary>The map objects that hold an item (ARMY__00.WRK 86..4F3): the staffs of the hermits (86, F1..153: the item +31, the frame shifted by BL/2 of the owner), the stones (164, 1F2, 203),
    /// the crystal ball (214, 488), the disease (29B), the manuscripts (311), the lightnings (3F0, 46A, 477) and the jug of the phantom (499). AH = 0 gives the item and the health 64h; AH = 8 (a
    /// visitor) hands the item over and empties the object; AH = 1 releases the square after the redraw (FS:157), animates the object and fades it after the health is gone (the removal after FAh).
    /// <paramref name="entry"/> numbers the entry points of tests/fixtures/obj-itemobj.json. Returns AX as the original leaves it.</summary>
    public static readonly byte[] PickupItems = [0x0a, 0x0b, 0x07, 0x08, 0x09, 0x15, 0x16, 0x17, 0x0d, 0x14, 0x06, 0x0c, 0x0e, 0x10, 0x13, 0, 0x1f, 0x20, 0x21, 0x23];
    public static ushort Pickup(IObjectScriptWorld w, byte[] r, int ah, int entry, byte bl)
    {
        bool staff = entry <= 8, stone = entry is 9 or 10 or 11, ball = entry is 12 or 13, disease = entry == 14, lightning = entry is 16 or 17 or 18;
        if (ah == 0)
        {
            r[0x31] = entry == 15 ? (byte)((byte)(r[0x0e] - 0x10) + 0x18) : PickupItems[entry];
            if (staff) Word(r, 1, (ushort)(Word(r, 1) + ((bl & 7) >> 1)));
            r[0x19] = 0x64; return 0;
        }
        if (ah == 8) { byte al = r[0x31]; r[0x31] = 0; r[0x19] = 0; return al; }
        if (ah != 1) return (ushort)(ah << 8);
        if ((r[0x14] & 0x80) != 0)
        {
            if (staff) r[0x20] = 0;
            uint claimed = w.Claim(w.Reference(r), 0, 0);
            if (claimed != 0) return 0x100;
            r[0x14] = 0; if (staff) r[0x20] = 0xff;
        }
        bool alive = (sbyte)r[0x19] > 0;
        if (alive)
        {
            if (stone)
            {
                ushort bx = w.TemplateBase;
                if (Word(r, 1) == bx && (w.Tick & 0x7f) != 0) return 0x100;
                if ((w.Tick & 1) != 0) return 0x100;
                Word(r, 1, (ushort)(Word(r, 1) + 1));
                if (Word(r, 1) == (ushort)(bx + 6)) Word(r, 1, bx);
            }
            else if (ball)
            {
                if (Word(r, 1) == 0x1bb && (w.Tick & 0x7f) != 0) return 0x100;
                if ((w.Tick & 1) != 0) return 0x100;
                Word(r, 1, (ushort)(Word(r, 1) + 1));
                if (Word(r, 1) == 0x1c0) Word(r, 1, 0x1bb);
            }
            else if (disease)
            {
                if ((w.Tick & 1) != 0) return 0x100;
                Word(r, 1, (ushort)(Word(r, 1) + 1));
                if (Word(r, 1) == 0x1c5) Word(r, 1, 0x1c0);
            }
            else if (lightning)
            {
                if ((w.Tick & 1) != 0) return 0x100;
                Word(r, 1, (ushort)(Word(r, 1) + 1)); r[0x15]++;
                if (r[0x15] == 8) { r[0x15] = 0; Word(r, 1, (ushort)(Word(r, 1) - 8)); }
            }
            return 0x100;
        }
        r[0x19]--;
        if (r[0x19] > 0xfa) return 0x100;
        w.Remove(r); return 0x100;
    }

    /// <summary>The effect scripts of ARMY__00.WRK (records of 22h bytes: +0 the kind, +3 the frame, +16/+17 counters, +12 the lifetime). 54A (kind 1, a flame at the point of its creator, lives 2BCh updates
    /// or until FS:11 says it is over), 6FE and 735 (the kinds 4 and 5, sparks of 8 frames of 2 updates, 6FE starts on 62h or 6Ah), 7FE (kind 7, a smoke whose frame runs between 39h and 48h
    /// and back by the parity of its random +16; it ends when FS:A5 says so). <paramref name="parent"/> is the creator (EBX in AH = 0), <paramref name="bl"/> EBX.</summary>
    public static void Fx54a(IObjectScriptWorld w, byte[] r, int ah, byte[] parent)
    {
        if (ah == 1) { Put(r, 0x12, U32(r, 0x12) - 1); if (U32(r, 0x12) == 0) r[0] = 0xff; return; }
        Word(r, 3, (ushort)((parent[0x25] & 7) + 9)); Put(r, 0x12, 0x2bc); r[0] = 1;
        w.Sound35(0x10); var (zf, al) = w.Service11(0x10);
        if (!zf || al != 4) r[0] = 0xff;
    }

    public static void Spark(IObjectScriptWorld w, byte[] r, int ah, bool kind5)
    {
        if (ah == 1)
        {
            r[0x16]--; if (r[0x16] != 0) return;
            r[0x16] = 2; Word(r, 3, (ushort)(Word(r, 3) + 1)); r[0x17]--; if (r[0x17] == 0) r[0] = 0xff; return;
        }
        if (kind5) { Word(r, 3, 0x7d); r[0] = 5; }
        else { Word(r, 3, (ushort)((w.Random() & 8) + 0x62)); r[0] = 4; }
        r[0x16] = 2; r[0x17] = 8;
    }

    public static void Fx7fe(IObjectScriptWorld w, byte[] r, int ah, byte bl)
    {
        if (ah == 1)
        {
            if ((r[0x16] & 1) == 0) { Word(r, 3, (ushort)(Word(r, 3) + 1)); if (Word(r, 3) == 0x49) Word(r, 3, 0x39); }
            else { Word(r, 3, (ushort)(Word(r, 3) - 1)); if (Word(r, 3) == 0x38) Word(r, 3, 0x48); }
            if (w.ServiceA5()) r[0] = 0xff;
            return;
        }
        Word(r, 3, (ushort)((bl & 0xf) + 0x39)); r[0] = 7; r[0x16] = w.Random(); w.ServiceAD(3, 0x0e);
    }

    public static void FxKind17(IObjectScriptWorld w, byte[] r, int ah, byte[] parent)   // FD1
    {
        if (ah == 1) { Put(r, 0x12, U32(r, 0x12) - 1); if (U32(r, 0x12) == 0) r[0] = 0xff; return; }
        Word(r, 3, (ushort)((parent[0x25] & 7) + 0x11)); Put(r, 0x12, 0x2bc); r[0] = 0x17;
        w.Sound35(0x11); var (zf, al) = w.Service11(0x11);
        if (!zf || al != 4) r[0] = 0xff;
    }

    /// <summary>Kind 18h (1012): a burst of 5 frames (18Ch + 5·k) played 3 updates each at the point of the creator, who is told twice (FS:25, FS:29); after the frames it looks what stands there (FS:A5, FS:A9) and burns out.</summary>
    public static void Fx1012(IObjectScriptWorld w, byte[] r, int ah, uint parentReference)
    {
        if (ah == 0)
        {
            int k = (w.Random() * 5) >> 8; Word(r, 3, (ushort)(5 * k + 0x18c)); r[0] = 0x18; r[0x16] = 3; r[0x17] = 5;
            w.Service25(parentReference, 0); w.Service29(8); return;
        }
        if (r[0x16] < 0x0a)
        {
            if ((w.Tick & 1) == 0)
            {
                Word(r, 3, (ushort)(Word(r, 3) + 1)); r[0x17]--;
                if (r[0x17] == 0) { Word(r, 3, (ushort)(Word(r, 3) - 5)); r[0x17] = 5; }
            }
            if (!w.ServiceA5()) return;
            byte al = w.ServiceA9(0x010a);
            if (al == 1 || al == 2) { r[0] = 0xff; return; }
            r[0x16]--;
            if (r[0x16] != 0) { w.ServiceAD(1, 2); return; }
            r[0x16] = 0x0a; r[0x17] = 0; r[5] = 0;
        }
        r[0x17]--;
        if (r[0x17] == 0) r[0] = 0xff;
    }

    /// <summary>10B3: the creation of the spark of a hit: the target of the creator (+1C) is looked up (FS:119); without one the effect is removed; the target's template flag 10h picks the hard spark
    /// (kind 5) over the soft one (kind 4); the sound 17h plays.</summary>
    public static void FxHit(IObjectScriptWorld w, byte[] r, byte[] parent)
    {
        if (U32(parent, 0x1c) == 0) { r[0] = 0xff; return; }
        var (zf, flags) = w.Service119(3);
        if (zf) { r[0] = 0xff; return; }
        w.Sound35(0x17);
        Spark(w, r, 0, (flags & 0x10) != 0);
    }

    private static readonly int[] Around = [0, -32, 32, -32, 32, 0, 32, 32, 0, 32, -32, 32, -32, 0, -32, -32];

    /// <summary>The wandering charm (1105): +12 the referenced target (EBX − pool in AH = 0), +18 its life (1388h updates). Every 8 updates the effect moves one cell around (the table of eight steps of
    /// 32 px at 1227) and, when FS:11 finds something there that is no building and the target is not already marked, throws effect 9Ah at the target; always it follows the target, marks it
    /// (+17 bit 100h) and, every 32 updates, one time in 4 by FS:10D and one in 4 by FS:109 makes the target wander: it forgets its state (clears the request +2D), turns by half a circle or is sent to
    /// a random cell (+22, +23, state 48h). The effect ends when its life is over (the target's health set to 0) or the target is dead.</summary>
    public static void Fx1105(IObjectScriptWorld w, byte[] r, int ah, uint parentPointer, uint poolBase)
    {
        if (ah != 1) { Put(r, 0x12, parentPointer - poolBase); Put(r, 0x18, 0x1388); return; }
        byte[]? target = w.ObjectAt(U32(r, 0x12));
        if ((w.Tick & 7) == 0)
        {
            int k = w.Random() & 7;
            uint x = (uint)(Around[2 * k] + (int)U32(r, 7)), y = (uint)(Around[2 * k + 1] + (int)U32(r, 0xc));
            if (x < w.MapWidthPx && y < w.MapHeightPx)
            {
                Put(r, 7, x); Put(r, 0xc, y);
                var (zf, al) = w.Service11(x);   // EAX still holds the new x
                if (!zf && (w.FoundFlags & 0x18) == 0 && (Word(r, 0x17) & 0x100) == 0)   // EDI: the effect record itself
                    w.EffectAt(al, 0x9a, (byte)(r[0x0d] & 3), U32(r, 4), U32(r, 8));
            }
        }
        var t = w.ObjectAt(U32(r, 0x12))!;
        if ((sbyte)t[0x19] <= 0) { r[0] = 0xff; return; }
        Word(t, 0x17, (ushort)(Word(t, 0x17) | 0x100)); Put(r, 7, U32(t, 4)); Put(r, 0xc, U32(t, 8));
        if ((w.Tick & 0x1f) == 0 && (w.Random2() & 3) == 0)
        {
            if ((t[0x2d] & 0xf) != 0) t[0x2d] &= 0x0f;
            else if ((w.Random() & 3) == 0) { t[0x22] = (byte)(w.Random() & 0x7f); t[0x23] = (byte)(w.Random2() & 0x7f); t[0x14] = (byte)((t[0x14] & 7) | 0x48); }
            else { byte turn = (byte)(((t[0x25] + 4) & 7) << 3); t[0x25] = (byte)((t[0x25] & 7) | turn); t[0x2d] = (byte)((t[0x2d] & 0x0f) | 0x10); }
        }
        Put(r, 0x18, U32(r, 0x18) - 1);
        if (U32(r, 0x18) != 0) return;
        t[0x19] = 0; r[0] = 0xff;
    }

    // The effects of the mages (ARMY__03.WRK 2F4E..3464): small scripts of the flashes and clouds that the mages' spells leave.
    public static void MageFx2f4e(IObjectScriptWorld w, byte[] r, int ah, byte[] parent)
    {
        if (ah == 1) { Put(r, 0x18, U32(r, 0x18) - 1); if (U32(r, 0x18) == 0) r[0] = 0xff; return; }
        Word(r, 3, Word(parent, 1)); r[2] = parent[3]; Put(r, 0x18, 1000); r[0] = 1;
    }

    public static void MageFx30a0(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 1) { Put(r, 0x18, U32(r, 0x18) - 1); if (U32(r, 0x18) == 0) r[0] = 0xff; return; }
        var (_, al, _, _) = w.ServiceA1();
        if (al == 2 || al == 1) { r[0] = 0xff; return; }
        Word(r, 3, 0x11b); r[0] = 7; Put(r, 0x18, 1000);
    }

    public static void MageFx31e7(IObjectScriptWorld w, byte[] r, int ah, byte[] parent)
    {
        if (ah == 1)
        {
            Put(r, 0x18, U32(r, 0x18) - 1); if (U32(r, 0x18) != 0) return;
            Put(r, 0x18, 0x258); Word(r, 3, (ushort)(Word(r, 3) + 1)); r[0x17]--; if (r[0x17] == 0) r[0] = 0xff; return;
        }
        var (zf, al, _, _) = w.ServiceA1();
        if (!((!zf) && (w.FoundFlags & 2) != 0) && al == 2) { r[0] = 0xff; return; }
        r[2] = parent[3]; Word(r, 3, (ushort)(((parent[0x25] & 6) * 2) + 0x29d)); Put(r, 0x18, 0x258); r[0] = 0x0f; r[0x17] = 4;
    }

    public static void MageFx333a(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah != 1) { Word(r, 3, 0x3a3); Put(r, 0x18, 0x400); r[0] = 0x11; return; }
        if (Word(r, 3) != 0x3bc && (w.Tick & 3) == 0) Word(r, 3, (ushort)(Word(r, 3) + 1));
        Put(r, 0x18, U32(r, 0x18) - 1); if (U32(r, 0x18) == 0) r[0] = 0xff;
    }

    public static void MageFx336e(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah != 1) { r[0] = 0x12; Word(r, 3, (ushort)(0x2ad + (w.Random() & 7))); w.Service29(5); Word(r, 0x20, 0); return; }
        Put(r, 0x18, U32(r, 0x18) + 0x0a); if ((int)U32(r, 0x18) >= 0) Put(r, 0x18, 0);
        Put(r, 0x1c, U32(r, 0x1c) - 0x0a); if ((int)U32(r, 0x1c) < 0) Put(r, 0x1c, 0);
        if (w.ServiceA9(0x308) != 0 || w.ServiceA5()) { r[0] = 0xff; return; }
        if ((w.Tick & 3) != 0) return;
        Word(r, 3, (ushort)(Word(r, 3) + 1));
        if (Word(r, 3) >= 0x2bc) r[0] = 0xff;
    }

    public static void MageFx33e0(IObjectScriptWorld w, byte[] r, int ah, byte bl, byte bh)
    {
        if (ah != 1) { r[0x16] = bh; Word(r, 3, (ushort)(bl + 0x33f)); r[0] = 0x13; return; }
        if ((w.Tick & 1) != 0) return;
        Word(r, 3, (ushort)(Word(r, 3) + 1)); r[0x16]--; if (r[0x16] == 0) r[0] = 0xff;
    }

    public static void MageFx340f(IObjectScriptWorld w, byte[] r, int ah, byte bl)
    {
        if (ah == 1)
        {
            r[0x16]--; if (r[0x16] != 0) return;
            r[0x16] = 2; Word(r, 3, (ushort)(Word(r, 3) + 1)); r[0x17]--; if (r[0x17] == 0) r[0] = 0xff; return;
        }
        Word(r, 3, (ushort)(((bl & 7) << 2) + 0xf0)); r[0] = 0x14; r[0x16] = 2; r[0x17] = 4;
        w.EffectAt(r[1], 0x93, r[5], 0, 0);   // ECX and EDX are not set by the script (zero in the check)
        w.Sound35(0);
    }

    /// <summary>The blasts of the mages (2F75, 2FA7, 2FD9: BL = 2, 3, 6): the effect disappears at once and the object hit at the creator's point (FS:17B) hears the message 5Eh, 62h or 63h from the creator's owner.</summary>
    public static void MageBlast(IObjectScriptWorld w, byte[] r, byte[] parent, uint parentPointer, int k)
    {
        r[0] = 0xff;
        byte bl = new byte[] { 2, 3, 6 }[k];
        var (found, o) = w.Scan17B(0, (parentPointer & 0xffff0000) | 0x100 | bl, U32(parent, 4), U32(parent, 8), parentPointer - 0x400000);
        if (!found) return;
        w.Say143(new byte[] { 0x5e, 0x62, 0x63 }[k], o![3], parent[3]);
    }

    /// <summary>The spray of the mages (300B and the preludes 307F, 3118, 3139, 315A, 317B): kind 5 for 14h strokes at the creator's point; every 4th update it throws an effect (14h) at a random point
    /// within ±256 px (FS:19) and counts its life down.</summary>
    public static void MageSpray(IObjectScriptWorld w, byte[] r, int ah, byte[] parent, int k)
    {
        if (k != 0 || ah != 1) { r[0] = 5; r[0x16] = 0x14; Put(r, 7, U32(parent, 4)); Put(r, 0xc, U32(parent, 8)); }
        if ((w.Tick & 3) != 0) return;
        uint x = U32(r, 7) + (uint)((sbyte)w.Random() * 2);
        if (x < w.MapWidthPx)
        {
            uint y = U32(r, 0xc) + (uint)((sbyte)w.Random2() * 2);
            if (y < w.MapHeightPx) { w.Random2(); w.EffectAt(r[1], 0x14, 1, x, y); }
        }
        r[0x16]--; if (r[0x16] == 0) r[0] = 0xff;
    }

    /// <summary>The wall clearing of the mages (319C and 31D0): the effect vanishes; when nothing of the template 17h (16h) of the creator's army and owner exists, the four walls (template 5) of the owner 6 and
    /// the army of the creator lose their mask +22.</summary>
    public static void MageClear(IObjectScriptWorld w, byte[] r, byte[] parent, int k)
    {
        r[0] = 0xff;
        if (w.FindObject(parent[3], parent[0], (byte)(k == 0 ? 0x17 : 0x16), 0) != null) return;
        for (int skip = 3; skip >= 0; skip--)
            if (w.FindObject(6, parent[0], 5, (byte)skip) is { } wall) Put(wall, 0x22, 0);
    }

    /// <summary>The camp (template 0, ARMY__00.WRK 0): the direction from the middle of the map (800h, 800h) to the object, in eighths (+22), and a random time +23 of 2710h and more; the update is FS:131.</summary>
    public static void Camp(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 1) { w.Jump131(); return; }
        if (ah != 0) return;
        uint cl = 0, ch = 0; int ax = (int)(U32(r, 4) - 0x800), bx = (int)(U32(r, 8) - 0x800);
        if (ax < 0) { ax = -ax; cl = 4; }
        if (bx < 0) { bx = -bx; ch = 4; }
        byte dl = 2;
        if ((uint)ax > (uint)bx) dl = (byte)(dl + cl); else dl = (byte)(4 + ch);
        r[0x22] = (byte)(dl & 7);
        Put(r, 0x23, (uint)(w.Random() + 0x2710));
    }

    /// <summary>The siege weapons (the catapult WRK F86 and the ballista 1462 of ARMY__02.WRK; the same code): AH = 0 the first state, the appearance BL, the random +2E and the army in +32; AH = 1 the debris
    /// (effects 16h, 17h, 18h at its point) when its health is gone while it is dying (+17 bits 18h = 10h), the redraw, the vitals, FS:115 and — unless +31 = FF — the unit step; AH = 2 the voice;
    /// AH = 5 (can it be loaded?): with +31 = FF the template flag 1000h of the object. Returns ZF of AH = 5.</summary>
    public static bool Siege(IObjectScriptWorld w, byte[] r, int ah, byte bl, bool ballista = false)
    {
        if (ah == 0) { r[0x0f] = 7; w.Appearance(bl); r[0x2e] = w.Random(); r[0x32] = r[0]; return false; }
        if (ah == 2) { w.Voice(); return false; }
        if (ah == 5) { if (r[0x31] != 0xff) return false; return (~w.TemplateFlagsOf71() & 0x1000) == 0; }
        if (ah != 1) return false;
        if (r[0x19] == 0 && (r[0x17] & 0x18) == 0x10)
            foreach (byte kind in ballista ? new byte[] { 0x16, 0x18 } : new byte[] { 0x16, 0x17, 0x18 }) w.EffectAt(r[0], kind, 1, U32(r, 4), U32(r, 8));   // the ballista throws two kinds of debris
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        w.Vitals(); w.Service115();
        if (r[0x31] == 0xff) return false;
        r[0x0f] = 7; w.Actions(); return false;
    }

    /// <summary>The shackled warrior (ARMY__01.WRK F64, template 20): a prisoner that shifts through the frames 907h..911h and, when his health is gone, falls apart and frees a warrior (template 13h of the owner 4,
    /// +31 = 8).</summary>
    public static void Shackled(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { Word(r, 1, 0x907); return; }
        if (ah != 1) return;
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        if ((w.Tick & 3) == 0) { Word(r, 1, (ushort)(Word(r, 1) + 1)); if (Word(r, 1) >= 0x912) Word(r, 1, 0x907); }
        if ((sbyte)r[0x19] > 0) return;
        r[0x19]--; if (r[0x19] > 0xfa) return;
        w.Remove(r);
        var (created, freed) = w.Create(r[0], 4, 0x13, 1, U32(r, 4), U32(r, 8));
        if (created) freed![0x31] = 8;
    }

    public const int LeaderTable = 0x11a8;
    /// <summary>The leader (ARMY__02.WRK 10F1, template 20): the unit script of the horseman with FS:13E before the step; AH = 6 returns the table at 11A8. Returns EDX of AH = 6.</summary>
    public static uint Leader(IObjectScriptWorld w, byte[] r, int ah, byte bl)
    {
        if (ah == 0) { r[0x0f] = 7; w.Appearance(bl); r[0x2e] = w.Random(); return 0; }
        if (ah == 2) { w.Voice(); return 0; }
        if (ah == 6) return LeaderTable;
        if (ah != 1) return 0;
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        w.Vitals(); w.Service13E(); w.Actions(); return 0;
    }

    /// <summary>The continuation of the leader's step (116E): a dead leader not yet marked asks FS:1AF.</summary>
    public static void LeaderContinuation(IObjectScriptWorld w, byte[] r)
    {
        if ((sbyte)r[0x19] > 0 || (Word(r, 0x17) & 0x80) != 0) return;
        w.Service1AF();
    }

    public static readonly int[] FenceTables = [0x481, 0x6ca, 0x13ff];
    /// <summary>The fences and bridges (ARMY__01.WRK 569 and 61E, ARMY__02.WRK 454, 6A1 and 13D6): AH = 0 redraws and shapes the section (FS:55, EDX = 3, AX = ah·100h with ah = FFh for the first
    /// fence of ARMY__01, 55h for the others); AH = 1 redraws and goes through the action table of the building (FS:41: only the state 1 does something, FS:3D, the construction) — a continuation
    /// with ZF clears AL; AH = 9 (ARMY__02) returns the table of the shapes (EDX). <paramref name="reshapeAh"/> and <paramref name="table"/> belong to the entry. Returns EDX of AH = 9.</summary>
    public static uint Fence(IObjectScriptWorld w, byte[] r, int ah, byte reshapeAh, uint table)
    {
        if (ah == 0) { w.Redraw(); w.Reshape((ushort)(reshapeAh << 8), 3); return 0; }
        if (ah == 9 && table != 0) return table;
        if (ah != 1) return 0;
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        w.BuildingDispatch(); return 0;
    }

    public static void Fx54aKind16(IObjectScriptWorld w, byte[] r, int ah, byte[] parent)   // B62
    {
        if (ah == 1) { Put(r, 0x12, U32(r, 0x12) - 1); if (U32(r, 0x12) == 0) r[0] = 0xff; return; }
        Word(r, 3, (ushort)((parent[0x25] & 7) + 1)); Put(r, 0x12, 0x2bc); r[0] = 0x10; Put(r, 7, U32(r, 7) + 1); Put(r, 0xc, U32(r, 0xc) + 0x0b);
        w.Sound35(0x0f); var (zf, al) = w.Service11(0x0f);
        if (!zf || al != 4) r[0] = 0xff;
    }

    /// <summary>Kind 9 (8DA): a burning flame of two frame sets (17Ah and 183h) with its own life: on layer 0 it only burns down; otherwise it plays 9 frames, asks FS:A5 and FS:A9 what stands there and goes out.</summary>
    public static void Fx8da(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0)
        {
            Word(r, 3, (w.Random() & 1) != 0 ? (ushort)0x17a : (ushort)0x183); r[0] = 9; r[0x16] = 9; r[0x17] = 1; w.ServiceAD(5, 4); return;
        }
        if (r[5] == 0) { r[0x16]--; if (r[0x16] == 0) r[0] = 0xff; return; }
        r[0x17] = (byte)(r[0x17] + 0x80);
        if ((r[0x17] & 0x80) != 0)
        {
            Word(r, 3, (ushort)(Word(r, 3) + 1)); r[0x16]--;
            if (r[0x16] == 0) { Word(r, 3, (ushort)(Word(r, 3) - 9)); r[0x16] = 9; }
        }
        if (!w.ServiceA5()) return;
        w.Sound35(0x13);
        byte al = w.ServiceA9(4); al--;
        if (al == 0) { r[0] = 0xff; return; }
        al--;
        if (al == 0) { w.Sound35(0x12); r[0] = 0xff; return; }
        if ((r[0x17] & 1) != 0) { r[0x17] &= 0xfe; w.ServiceAD(1, 2); return; }
        r[0x16] = 0; r[5] = 0;
    }

    /// <summary>Kinds 0Dh and 0Eh (A6C, AB4): an explosion — the frames from 97h (0A9h), two updates each, 12h of them; at BL ≥ 4 it throws 3 (5) fragments (effect 9).</summary>
    public static void Explosion(IObjectScriptWorld w, byte[] r, int ah, byte bl, bool large)
    {
        if (ah == 1)
        {
            r[0x16]--; if (r[0x16] != 0) return;
            r[0x16] = 2; Word(r, 3, (ushort)(Word(r, 3) + 1)); r[0x17]--; if (r[0x17] == 0) r[0] = 0xff; return;
        }
        Word(r, 3, large ? (ushort)0xa9 : (ushort)0x97); r[0] = large ? (byte)0x0e : (byte)0x0d; w.Sound35(large ? 0x0bu : 0x0au);
        r[0x16] = 2; r[0x17] = 0x12;
        if (bl < 4) return;
        for (int i = large ? 5 : 3; i > 0; i--) w.EffectAt(r[1], 9, 1, U32(r, 7), U32(r, 0xc));
    }

    /// <summary>Kind 0Fh (AEB): a mark that follows an object (FS:A1): it takes the frame and the health of the found object and disappears when the object is gone, changed or dead, or after +16 updates.</summary>
    public static void Fxaeb(IObjectScriptWorld w, byte[] r, int ah, byte bl)
    {
        if (ah == 1)
        {
            uint reference = U32(r, 0x18);
            if (reference != 0)
            {
                var o = w.ObjectAt(reference)!;
                if ((sbyte)o[0x19] <= 0) { r[0] = 0xff; return; }
                if (o[0x19] != r[0x17] && Word(o, 1) != U32(r, 0x1c)) { r[0] = 0xff; return; }
            }
            r[0x16]--; if (r[0x16] == 0) r[0] = 0xff; return;
        }
        Word(r, 3, (ushort)((bl & 0xf) + 0x39)); r[0] = 0x0f; r[0x16] = 0; Put(r, 0x18, 0);
        var (zf, al, obj, reference2) = w.ServiceA1();
        if (!zf) { Put(r, 0x1c, Word(obj, 1)); r[0x17] = obj[0x19]; Put(r, 0x18, reference2); return; }
        if (al == 1 || al == 2) r[0] = 0xff; else r[5] = 1;
    }

    private static void Redraw(IObjectScriptWorld w, byte[] r) { if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); } }
    private static uint CornerMinus(byte[] r, int dx, int dy)
    {
        uint c = Corner(r); return (c & 0xffff0000) | (byte)((byte)c - dx) | ((uint)(byte)((byte)(c >> 8) - dy) << 8);
    }

    /// <summary>The stone wall (template 5, 3F8): a wall that sinks into the ground (frame 15Fh + 11·k, +2A counts to 10) while nobody is near and rises when somebody comes within 3 cells.</summary>
    public static void Wall(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0)
        {
            Put(r, 0x22, 0xffffffbf); r[0x15] = 0;
            int k = (w.Random() * 3) >> 8; Word(r, 1, (ushort)(11 * k + 0x15f)); r[0x2a] = 0x0a; return;
        }
        if (ah != 1) return;
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); r[0x0d]++; w.Redraw(); r[0x0d]--; }
        if (r[0x15] == 0)
        {
            if (r[0x2a] != 0x0a && (w.Tick & 1) == 0)
            {
                r[0x2a]++; Word(r, 1, (ushort)(Word(r, 1) + 1));
                r[0x0d]++; uint claimed = w.Claim(w.Reference(r), 0, 0x02ff); r[0x0d]--;
                if (claimed != 0 && r[0x2a] >= 0x0a) { r[0x2a]--; Word(r, 1, (ushort)(Word(r, 1) - 1)); }
            }
            if ((w.Tick & 0x1f) != 0) return;
            var (_, hostile, _) = w.Area(CornerMinus(r, 3, 3), 0x606);
            if (hostile == 0) return;
            r[0x15] = 1; return;
        }
        if (r[0x2a] != 0 && (w.Tick & 1) == 0)
        {
            Word(r, 1, (ushort)(Word(r, 1) - 1)); r[0x2a]--;
            if (r[0x2a] == 0) { r[0x0d]++; w.Claim(0, w.Reference(r), 0); r[0x0d]--; }
        }
        if ((w.Tick & 0x1f) != 0) return;
        var (_, still, _) = w.Area(CornerMinus(r, 3, 3), 0x606);
        if (still == 0) r[0x15] = 0;
    }

    /// <summary>The knight's head (template 6, 522): turns toward the nearest hostile in the rectangle of 8×8 cells (frames 15Eh + the turn, +27 the current and +28 the wanted turn) and, when it
    /// already looks that way, blinks (frames 187h..); a dead head is removed after 5 updates.</summary>
    public static void Head(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { Put(r, 0x22, 0xffffffbf); r[0x27] = 0x20; Word(r, 1, 0x17e); return; }
        if (ah != 1) return;
        Redraw(w, r);
        if ((sbyte)r[0x19] <= 0)
        {
            r[0x19]--; if (r[0x19] > 0xfa) return;
            w.Remove(r); return;
        }
        if (r[0x15] == 0)
        {
            if ((w.Tick & 7) != 0) return;
            var (_, _, target) = w.Area(CornerMinus(r, 4, 0), 0x408);
            if (target == null) return;
            byte al = w.Distance(U32(target, 4), U32(target, 8));
            if (al < 3) al = 3; if (al > 5) al = 5;
            al = (byte)(al << 3);
            if (al == r[0x27]) { r[0x15] = 2; r[0x28] = 0; }
            else { r[0x28] = al; r[0x15] = 1; }
        }
        if (r[0x15] == 1)
        {
            byte a = r[0x27]; if (!(a < r[0x28])) a -= 2; a++;
            r[0x27] = a; Word(r, 1, (ushort)(a + 0x15e));
            if (a == r[0x28]) r[0x15] = 0;
            return;
        }
        if ((w.Tick & 1) != 0) return;
        byte hi = (byte)((byte)(r[0x27] >> 3) - 3); int product = 0x0c * hi;
        int ax = (product & 0xff00) | (byte)((byte)product + r[0x28]);
        Word(r, 1, (ushort)(ax + 0x187));
        r[0x28]++;
        if (r[0x28] == 0x0c) r[0x15] = 0;
    }

    /// <summary>The living fir (template 7, 624): when somebody comes within 3 cells it sways through the frames 1ABh..1B6h and strikes at frame 1B3h (FS:69).</summary>
    public static void Tree(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { Put(r, 0x22, 0xffffffbf); return; }
        if (ah != 1) return;
        Redraw(w, r);
        if (r[0x15] == 0)
        {
            if ((w.Tick & 0x1f) != 0) return;
            var (count, _, _) = w.Area(CornerMinus(r, 3, 3), 0x505);
            if (count == 0) return;
            r[0x15] = 1;
        }
        Word(r, 1, (ushort)(Word(r, 1) + 1));
        if (Word(r, 1) == 0x1b3) w.Spawn(8, 2, (ushort)Corner(r), 0x505);
        if (Word(r, 1) < 0x1b7) return;
        Word(r, 1, 0x1ab); r[0x15] = 0;
    }

    /// <summary>The raven (template 8, 6BA): a bird — AH = 0 asks for the resting animation; AH = 1 the standard unit step.</summary>
    public static void Raven(IObjectScriptWorld w, byte[] r, int ah)
    {
        if (ah == 0) { r[0x0f] = 7; r[0x2d] = (byte)((r[0x2d] & 0x0f) | 0x60); return; }
        if (ah != 1) return;
        Redraw(w, r); w.Vitals(); w.Actions();
    }
}

/// <summary>The services of the standard unit scripts (recorded stubs in tools/verify_unit_scripts.py).</summary>
public interface IUnitScriptWorld
{
    byte Random();                    // FS:109
    void Appearance(byte army);       // FS:4D
    void Voice();                     // FS:D1
    void Redraw();                    // FS:1D
    void Vitals(); void Actions();    // FS:DD, FS:E1
    void Effect(byte al, byte bl, byte bh, uint ecx, uint edx);    // FS:19 with registers
    void Service1BB();                // FS:1BB
}

/// <summary>The standard unit script of ARMY__01.WRK and ARMY__02.WRK (the swordsman 0, the archer 7F, the horseman 4F0 / 11B4 and their kin): AH = 0 the first frame state (+0F = 0Fh, the horseman
/// 07h), the appearance of the army BL and the first random update counter +2E; AH = 1 the redraw, the vitals and the unit step; AH = 2 the voice. Verified against the original x86 by
/// tests/fixtures/unit-scripts.json (tools/verify_unit_scripts.py).</summary>
public static class OriginalUnitScript
{
    /// <summary>The worker of ARMY__01.WRK (a77 the peasant, b4f the carrier of 3, c1c the carrier of 1 and 2): AH = 0 the init (+0F = 1Fh for the worker, 13h for the carriers), AH = 1 the step —
    /// the carriers go back to the worker script (BE9 / CB0) and may release the carried cargo with the effect 1Bh / 1Ch —, AH = 7 takes the cargo AL: 3 switches to b4f (+0E = 10h, +2D = (+2D and 7) or 60h),
    /// 1 and 2 to c1c (+0E = 11h). kind 0, 1, 2 select the script.</summary>
    public static void Worker(IUnitScriptWorld w, byte[] r, int ah, byte bl, int kind, byte cargo = 0)
    {
        if (ah == 0) { r[0x20] = 0xff; r[0x0f] = kind == 0 ? (byte)0x1f : (byte)0x13; w.Appearance(bl); r[0x2e] = w.Random(); return; }
        if (ah == 2) { w.Voice(); return; }
        if (ah == 7 && kind == 0)
        {
            if (r[0x31] != 0) return;
            r[0x31] = cargo;
            if (cargo == 3) { r[0x0e] = 0x10; Worker(w, r, 0, r[0x25], 1); r[0x14] = (byte)((r[0x14] & 7) | 0x60); }
            else if (cargo is 1 or 2) { r[0x0e] = 0x11; Worker(w, r, 0, r[0x25], 2); }
            else r[0x31] = 0;
            return;
        }
        if (ah != 1) return;
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        if (kind != 0 && ((sbyte)r[0x19] <= 0 || r[0x31] == 0))
        {
            r[0x0e] = 0x0f; Worker(w, r, 0, r[0x25], 0);
            if (r[0x31] != 0) { r[0x31] = 0; w.Effect(r[0], kind == 1 ? (byte)0x1b : (byte)0x1c, 1, BitConverter.ToUInt32(r, 4), BitConverter.ToUInt32(r, 8)); }
        }
        w.Vitals(); w.Actions();
    }

    /// <summary>The offset of the table returned by the AH = 6 of the worker 7F5 of ARMY__02.WRK (the bytes stay in the module).</summary>
    public const uint Worker2Table = 0x841;

    /// <summary>The worker of ARMY__02.WRK (7F5 the peasant, 8FE the carrier of 3, 9ED the carrier of 1 and 2): as <see cref="Worker"/>, but the init gives +0F = 3Fh to the worker, AH = 2 first calls FS:1BB,
    /// AH = 6 returns the table <see cref="Worker2Table"/>, the cargo 3 sets +0E = 0Bh and 1 or 2 +0E = 08h; the carrier of 3 sets +14 = 58h and +15 = 0 on every return to the worker, the effects are 1Ah and 26h.</summary>
    public static void Worker2(IUnitScriptWorld w, byte[] r, int ah, byte bl, int kind, byte cargo = 0)
    {
        if (ah == 0) { r[0x20] = 0xff; r[0x0f] = kind == 0 ? (byte)0x3f : (byte)0x13; w.Appearance(bl); r[0x2e] = w.Random(); return; }
        if (ah == 2) { if (kind != 0) w.Service1BB(); w.Voice(); return; }
        if (ah == 7 && kind == 0)
        {
            if (r[0x31] != 0) return;
            r[0x31] = cargo;
            if (cargo == 3) { r[0x0e] = 0x0b; Worker2(w, r, 0, r[0x25], 1); r[0x14] = (byte)((r[0x14] & 7) | 0x60); }
            else if (cargo is 1 or 2) { r[0x0e] = 0x08; Worker2(w, r, 0, r[0x25], 2); }
            else r[0x31] = 0;
            return;
        }
        if (ah != 1) return;
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        if (kind != 0 && ((sbyte)r[0x19] <= 0 || r[0x31] == 0))
        {
            r[0x0e] = 0x0a; Worker2(w, r, 0, r[0x25], 0);
            if (kind == 1) { r[0x14] = 0x58; r[0x15] = 0; }
            if (r[0x31] != 0) { r[0x31] = 0; w.Effect(r[0], kind == 1 ? (byte)0x1a : (byte)0x26, 1, BitConverter.ToUInt32(r, 4), BitConverter.ToUInt32(r, 8)); }
        }
        w.Vitals(); w.Actions();
    }

    public static void Run(IUnitScriptWorld w, byte[] r, int ah, byte bl, bool horseman)
    {
        if (ah == 0) { r[0x0f] = horseman ? (byte)7 : (byte)0x0f; w.Appearance(bl); r[0x2e] = w.Random(); return; }
        if (ah == 2) { w.Voice(); return; }
        if (ah != 1) return;
        if ((r[0x14] & 0x80) != 0) { r[0x14] &= 0x7f; w.Redraw(); }
        w.Vitals(); w.Actions();
    }
}
