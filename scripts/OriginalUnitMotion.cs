using System.Buffers.Binary;
namespace Northwind;

/// <summary>The services of the unit motion machine (recorded stubs in tools/verify_unit_motion.py).</summary>
public interface IMotionWorld
{
    byte WalkStep(uint request22);                 // 70B0: one step of the walk; BL bit 40h = finished
    byte MoveStep(uint request22);                 // 12930: the move of state 4; BL bits 40h / 80h
    bool TurnStep(byte target);                    // 9653: one step of the turn; true (ZF) = turned
    byte BorderEvents();                           // D12C
    bool DeathCheck();                             // 10799: true (ZF) = the unit dies (95D3)
    void Effect(byte army, byte type, byte layer, uint x, uint y, uint selfReference, uint targetX, uint targetY);   // 9727 with its three stack arguments
    void Die();                                    // 95D3
    byte PlayerOwner { get; } byte MapWidth { get; } byte MapHeight { get; }
    byte[]? Resolve(uint reference);               // the object record of a reference (OBE[1C])
}

/// <summary>The unit motion machine, LE:1 9210..95D3: the state in the low nibble of +2D, the requested state in the high one. 0 rest (a change of request goes through 9275), 1 turn
/// (9653), 2 walk (70B0), 3 swing (frames DAT[4A]·8 + heading·DAT[4B], the impact on the frame DAT[4E] through 9727), 4 move (12930), 5 frame range (<see cref="OriginalFrameRange"/>),
/// 6 resting animation, 7 turning while at rest; 9275 ends a state and starts the requested one, 92CA is the rest with its border events. Verified against the original x86 by
/// tests/fixtures/unit-motion.json (tools/verify_unit_motion.py; the services are recorded stubs).</summary>
public sealed class OriginalUnitMotion(IMotionWorld world)
{
    private byte[] _r = []; private byte[] _t = []; private byte _al, _bl; private uint _ref;
    public byte Al => _al; public byte Bl => _bl;

    private static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private void SetFrame(int value) => BinaryPrimitives.WriteUInt16LittleEndian(_r.AsSpan(1), unchecked((ushort)value));

    /// <summary>9210: one update of the record <paramref name="record"/> with the template (EBP).</summary>
    public void Step(byte[] record, byte[] template, uint reference = 0)
    {
        _ref = reference; _r = record; _t = template; _al = 0; _bl = 0;
        byte ah = (byte)(_r[0x2d] >> 4); _al = (byte)(_r[0x2d] & 15);
        if (_al != ah) _bl = 0x40;
        if ((_r[0x17] & 0x80) != 0) return;   // 92C9
        switch (_al)
        {
            case 0: Rest(); break;
            case 1: Turn(); break;
            case 2: Walk(); break;
            case 3: Swing(); break;
            case 4: Move(); break;
            case 5: Range(); break;
            case 6: Resting(); break;
            case 7: RestingTurn(); break;
        }
    }

    private void Ret() => _al = (byte)(_r[0x2d] & 15);   // 934E
    private void Rest() { if ((_bl & 0x40) != 0) Switch(); else Ret(); }                          // 9345

    /// <summary>9275: the requested state replaces the current one.</summary>
    private void Switch()
    {
        _al = (byte)(_r[0x2d] >> 4); byte current = (byte)(_r[0x2d] & 15);
        if (_al == current) { _al = 0; _r[0x2d] = 0; }
        _r[0x24] = 0;
        switch (_al)
        {
            case 0: Idle(); break;
            case 1: _r[0x26] = 2; _r[0x2d] = (byte)((_r[0x2d] & 0xf0) | 1); Turn(); break;
            case 2: StartWalk(); break;
            case 3: StartSwing(); break;
            case 4: _r[0x24] = 0; _r[0x2d] = (byte)((_r[0x2d] & 0xf0) | 4); _bl = 0; Move(); break;
            case 5: _r[0x26] = _r[0x22]; _r[0x27] = _r[0x23]; _r[0x2c] = 1; _r[0x2d] = (byte)((_r[0x2d] & 0xf0) | 5); Range(); break;
            case 6: StartResting(); break;
            case 7: _r[0x27] = 1; _r[0x31] &= 0xfc; _r[0x2d] = (byte)((_r[0x2d] & 0xf0) | 7); _bl = 0; RestingTurn(); break;
        }
    }

    /// <summary>92CA: rest — the standing frame of the heading, the border events of the player's units.</summary>
    private void Idle()
    {
        if ((U32(_t, 0x40) & 0x100000) != 0) { StartResting(); return; }
        SetFrame((_r[0x25] & 7) * _t[0x4a] + U16(_t, 0x48));
        _r[0x2d] &= 0xf0; _al = 0;
        if (_r[3] != world.PlayerOwner) { _al = 0; return; }
        uint cl = (byte)(U32(_r, 4) >> 5), dl = (byte)(U32(_r, 8) >> 5); byte code = 0;
        bool events = false;
        if (dl == 0) events = true;
        else { code = 6; if (cl == 0) events = true; else { code = 4; dl++; if (dl >= world.MapHeight) events = true; else { code = 2; cl++; if (cl >= world.MapWidth) events = true; } } }
        if (events)
        {
            byte border = world.BorderEvents();
            if (border != 1 && world.DeathCheck()) { world.Die(); _al = code; return; }
        }
        _al = 0;
    }

    private void Turn()   // 9360
    {
        if (--_r[0x26] != 0) { Ret(); return; }
        _r[0x26] = 1;
        if (world.TurnStep((byte)((_r[0x25] >> 3) & 7))) _r[0x2d] = 0;
        Ret();
    }

    private void StartWalk()   // 937F
    {
        if ((U32(_t, 0x40) & 0x100000) != 0) { _r[0x24] = 0; _r[0x2d] = (byte)((_r[0x2d] & 0xf0) | 4); _bl = 0; Move(); return; }
        _r[0x24] = 0; _r[0x2d] = (byte)((_r[0x2d] & 0xf0) | 2); _bl = 0; Walk();
    }
    private void Walk()   // 939A
    {
        _bl = world.WalkStep(U32(_r, 0x22));
        if ((_bl & 0x40) != 0) Switch(); else Ret();
    }

    private void StartSwing()   // 93AD
    {
        _r[0x2d] = (byte)((_r[0x2d] & 0xf0) | 3);
        if (_r[0x22] == 0xff)
        {
            uint reference = U32(_r, 0x1c);
            if (reference == 0) { Switch(); return; }
            var target = world.Resolve(reference) ?? new byte[0x40];
            BinaryPrimitives.WriteUInt16LittleEndian(_r.AsSpan(0x28), (ushort)U32(target, 4)); BinaryPrimitives.WriteUInt16LittleEndian(_r.AsSpan(0x2a), (ushort)U32(target, 8));
            _r[0x27] = (byte)(target[0x0d] & 3);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_r.AsSpan(0x28), (ushort)((uint)_r[0x22] * 32 + 0x10)); BinaryPrimitives.WriteUInt16LittleEndian(_r.AsSpan(0x2a), (ushort)((uint)_r[0x23] * 32 + 0x10));
            _r[0x27] = 1;
        }
        _bl = _t[0x4a]; SetFrame((_r[0x25] & 7) * _t[0x4b] + _t[0x4a] * 8 + U16(_t, 0x48));
        _r[0x26] = _t[0x4c]; Ret();
    }
    private void Swing()   // 9429
    {
        if (--_r[0x26] != 0) { Ret(); return; }
        _r[0x24]++; byte ah = _t[0x4c], al = _r[0x24];
        if (al >= _t[0x4e]) ah = _t[0x4d];
        _r[0x26] = ah;
        if (al >= _t[0x4b]) { Switch(); return; }
        BinaryPrimitives.WriteUInt16LittleEndian(_r.AsSpan(1), (ushort)(U16(_r, 1) + 1));
        if (al != _t[0x4e]) { Ret(); return; }
        _r[0x2f] = 0; int o = 0x60 + (_r[0x25] & 7) * 2;
        _bl = _t[0x70]; world.Effect(_r[0], _t[0x70], _r[0x27], (uint)(U32(_r, 4) + (sbyte)_t[o]), (uint)(U32(_r, 8) + (sbyte)_t[o + 1]), _ref, U16(_r, 0x28), U16(_r, 0x2a));
        Ret();
    }

    private void Move()   // 94A6
    {
        _bl = world.MoveStep(U32(_r, 0x22));
        if ((_bl & 0x40) == 0) { Ret(); return; }
        _r[0x2d] = 0x60;
        if ((_bl & 0x80) == 0) { StartResting(); return; }
        _r[0x25] += 0x10; _r[0x25] &= 0x3f; _r[0x2d] = 0x70;
        _r[0x27] = 1; _r[0x31] &= 0xfc; _r[0x2d] = (byte)((_r[0x2d] & 0xf0) | 7); _bl = 0; RestingTurn();
    }

    private void Range()   // 94EA
    {
        if (--_r[0x2c] != 0) { Ret(); return; }
        byte old = _r[0x26]; _r[0x26]++;
        if (old == _r[0x27]) { Switch(); return; }
        if (old >= _r[0x27]) _r[0x26] -= 2;
        _r[0x2c] = 2; SetFrame(_r[0x26] + U16(_t, 0x48)); Ret();
    }

    private void StartResting()   // 951D
    {
        _r[0x24] = 0; _r[0x27] = 0; _r[0x31] &= 0xfc; _r[0x2d] = 0x46; _bl = 0; Resting();
    }
    private void Resting()   // 952F
    {
        if ((_bl & 0x40) != 0) { Switch(); return; }
        _r[0x27]++;
        if (_r[0x27] >= _t[0x78]) _r[0x27] = 0;
        int ax = (_r[0x31] >> 2) * _t[0x78]; byte al = (byte)ax; al += _r[0x27]; int eax = (ax & 0xff00) | al;
        int ebx = (byte)(_t[0x4a] + _t[0x4b]);
        eax = eax + ebx * 8; ebx = _t[0x4f]; _bl = (byte)ebx; eax = eax + ebx * 4 + 0x20;
        SetFrame(eax + U16(_t, 0x48)); Ret();
    }
    private void RestingTurn()   // 9584
    {
        if ((_bl & 0x40) != 0) { Switch(); return; }
        if ((_r[0x2e] & 1) != 0) { Resting(); return; }
        byte al = (byte)(_r[0x25] & 7), ah = (byte)((_r[0x25] >> 3) & 7);
        ah = (byte)(ah - al);
        if (ah == 0) { _r[0x2d] = (byte)((_r[0x2d] & 0x0f) | 0x60); Switch(); return; }
        al++; ah &= 7; if (ah > 4) al -= 2; al &= 7;
        _r[0x25] = (byte)((_r[0x25] & 0xf8) | al); _r[0x31] = (byte)(al << 2); _bl = 0; Resting();
    }
}
