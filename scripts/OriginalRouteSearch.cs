using System.Buffers.Binary;
namespace Northwind;

public readonly record struct RouteCell(byte X, byte Y);
public sealed record OriginalRouteSearchResult(bool Found, byte[] Directions, RouteCell SearchEnd);

/// <summary>73F5/7497–79BF contour search. Coordinates address the footprint's top-left cell.</summary>
public sealed class OriginalRouteSearch
{
    private static readonly (int X, int Y)[] DirectionDeltas = [(0,-1),(1,-1),(1,0),(1,1),(0,1),(-1,1),(-1,0),(-1,-1)];
    public static ReadOnlySpan<(int X, int Y)> Deltas => DirectionDeltas;
    private const int BufferStart = 0x1f45, BufferLimit = 0x2723, OutputLimit = 0x272b, StackStart = 0x2786;
    private readonly byte[] _work = new byte[0x2800];
    private Func<byte, byte, byte, bool> _admit = null!;
    private byte _x, _y, _targetX, _targetY, _direct, _direction, _minX, _maxX, _minY, _maxY;
    private int _stack, _frame, _width, _height, _footprintWidth, _footprintHeight;
    private bool _loops, _zero, _carry;
    private int _operations;
    private ushort Pair(byte x, byte y) => (ushort)(x | y << 8);
    private ushort ReadPair(int p) => BinaryPrimitives.ReadUInt16LittleEndian(_work.AsSpan(p));
    private void WritePair(int p, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(_work.AsSpan(p), value);
    private int ReadPointer(int p) => BinaryPrimitives.ReadInt32LittleEndian(_work.AsSpan(p));
    private void WritePointer(int p, int value) => BinaryPrimitives.WriteInt32LittleEndian(_work.AsSpan(p), value);
    private void SetPosition(ushort pair) { _x = (byte)pair; _y = (byte)(pair >> 8); }
    private void Guard()
    {
        // Host protection, not an original timing rule. Never silently return a different route.
        if (++_operations > 1000000) throw new InvalidDataException("Native contour search exceeded its operation guard");
    }
    private byte Toward()
    {
        int column = _targetX < _x ? 0 : _targetX == _x ? 1 : 2;
        int row = _targetY < _y ? 0 : _targetY == _y ? 1 : 2;
        ReadOnlySpan<byte> table = [7,6,5,0,255,4,1,2,3];
        return _direct = table[column * 3 + row];
    }
    private bool Probe(byte direction)
    {
        Guard();
        var d = Deltas[direction];
        byte left = unchecked((byte)(_x + d.X)), top = unchecked((byte)(_y + d.Y));
        // The callback receives every original 7D4C invocation, including out-of-map candidates.
        bool accepted = _admit(left, top, direction);
        return left <= _width - _footprintWidth && top <= _height - _footprintHeight && accepted;
    }
    private void Advance()
    {
        Guard(); var d = Deltas[_direction];
        _x = unchecked((byte)(_x + d.X)); _y = unchecked((byte)(_y + d.Y));
        if (_loops)
        {
            for (_frame = StackStart; _frame != _stack; _frame -= 7)
                if (ReadPair(_frame) == Pair(_x, _y)) { _zero = _x == _targetX && _y == _targetY; _carry = true; return; }
        }
        // 7F7D's boundary comparisons leave ZF set, even when the final target differs.
        _carry = _x == _minX || _x == _maxX || _y == _minY || _y == _maxY;
        _zero = _carry || (_x == _targetX && _y == _targetY);
    }
    private byte Distance(ushort pair)
    {
        static byte Abs(byte value) => (sbyte)value < 0 ? unchecked((byte)-value) : value;
        return Math.Max(Abs(unchecked((byte)(_targetX - (byte)pair))), Abs(unchecked((byte)(_targetY - (byte)(pair >> 8)))));
    }
    public OriginalRouteSearchResult Find(byte width, byte height, byte footprintWidth, byte footprintHeight,
        RouteCell start, RouteCell target, Func<byte, byte, byte, bool> admit, bool windowed = true)
    {
        if (width is 0 or > 128 || height is 0 or > 128 || footprintWidth == 0 || footprintHeight == 0 ||
            footprintWidth > width || footprintHeight > height || start.X > width - footprintWidth || start.Y > height - footprintHeight ||
            target.X > width - footprintWidth || target.Y > height - footprintHeight)
            throw new ArgumentException("Contour search requires a valid top-left footprint in a map up to 128 by 128");
        Array.Clear(_work); _admit = admit; _width = width; _height = height;
        _footprintWidth = footprintWidth; _footprintHeight = footprintHeight;
        _x = start.X; _y = start.Y; _targetX = target.X; _targetY = target.Y;
        _minX = unchecked((byte)(_x - 16)); _maxX = unchecked((byte)(_x + 16));
        _minY = unchecked((byte)(_y - 16)); _maxY = unchecked((byte)(_y + 16));
        if (!windowed) _minX = _maxX = _minY = _maxY = 255;   // 72FE sets the four bounds [274A..274D] to FFh
        _loops = true; _operations = 0;
        byte mask = 0, chosenMask = 0, length = 0, closestLength = 0, closestDistance = 255, closestMask = 0;
        byte a = 0, ch = 0, cl = 0, distance = 0, least = 0;
        int hand = 0, p = BufferStart, end = BufferStart, b = 0, q = 0, closestPointer = 0, cost = 65535, output = -1;
        ushort closestCell = 0, savedCell = Pair(_x, _y), waypoint = 0;
        if (start == target) goto Finish;
        for (a = 7; a != 255; a--) if (Probe(a)) goto Begin;
        goto Finish;
    Begin:
        ch = 128; _stack = StackStart; goto Direct;
    Replay: // 7529: replay branch choices encoded in mask.
        Guard(); p = BufferStart - 2; ch = 128; _stack = StackStart;
    ReplayScan:
        p += 2;
    ReplayMarker:
        if ((_work[p] & 128) == 0) goto ReplayScan;
        if (_work[p] == 128) goto Score;
        waypoint = ReadPair(p + 2); WritePair(_stack, waypoint); _work[_stack + 6] = _work[p + 1]; WritePointer(_stack + 2, p); _stack -= 7;
        a = ch; ch >>= 1; p += 8;
        if ((mask & a) == 0) goto ReplayMarker;
        q = ReadPointer(p - 4); b = p; p = q;
        if (q != 0) goto ReplayMarker;
        length = _work[b - 7]; WritePointer(b - 4, end); SetPosition(waypoint); p = end;
        hand = -1; cl = 0; a = Toward();
    Rotate: // 75A5
        a = (byte)((a + hand) & 7); if (!Probe(a)) goto Rotate;
    ContourStep:
        _direction = a; length = unchecked((byte)(length + 1)); if (length == 0) goto SaveEnd;
        Advance(); if (_carry) goto Contact; if (_zero) goto Reached;
        if (_direct == _direction) goto Corner;
        a = (byte)((a - 2 * hand) & 7);
        if (Probe(a)) goto ContourChoice;
        a = (byte)((a + hand) & 7); if (!Probe(a)) goto Rotate;
    ContourChoice:
        if (_direct == _direction || a == ((_direct - hand) & 7)) goto Corner;
        if (a != _direct) goto ContourStep;
        WritePair(p, Pair(_x, _y)); p += 2; if (p >= BufferLimit) goto FinalizeSearch;
        goto ContourStep;
    Corner:
        WritePair(p, Pair(_x, _y)); p += 2; if (p >= BufferLimit) goto FinalizeSearch;
    Direct: // 7647
        a = Toward(); if (!Probe(a)) goto Branch;
        _direction = a; length = unchecked((byte)(length + 1)); if (length == 0) goto SaveEnd;
        Advance(); if (_zero) goto Reached; goto Direct;
    Branch:
        if (ch == 0) goto DepthLimit;
        _work[p] = 129; _work[p + 1] = length; _work[_stack + 6] = length;
        WritePair(p + 2, Pair(_x, _y)); WritePair(_stack, Pair(_x, _y));
        WritePointer(p + 4, 0); WritePointer(_stack + 2, p); _stack -= 7; p += 8; ch >>= 1;
        a = _direct; hand = 1; goto Rotate;
    Contact: // 76B8: carry distinguishes a visited branch from the local boundary.
        end = p; if (!_zero) { b = _frame; goto Closest; }
        least = 255; q = 0;
        for (b = StackStart; b != _stack; b -= 7)
        {
            distance = Distance(ReadPair(b)); if (distance < least) { q = b; least = distance; }
        }
        b = q; distance = least; goto SaveClosest;
    DepthLimit:
        end = p; b = _stack + 7;
    Closest:
        distance = Distance(ReadPair(b));
    SaveClosest:
        if (distance > closestDistance || (distance == closestDistance && _work[b + 6] >= closestLength)) goto NextBranch;
        closestDistance = distance; closestPointer = ReadPointer(b + 2); closestMask = mask;
        closestLength = _work[b + 6]; closestCell = ReadPair(b); goto NextBranch;
    Reached:
        WritePair(p, Pair(_targetX, _targetY)); _work[p + 2] = 128; p += 3;
    Score:
        if (length == 0) goto SaveEnd;
        q = Distance(Pair(_x, _y)) + length;
        if (q <= cost) { cost = q; chosenMask = mask; }
    SaveEnd:
        end = p;
    NextBranch:
        q = ch << 1; ch = (byte)q; if (q > 255) goto FinalizeSearch;
        if (ch == 0) ch = 1;
        q = mask + ch; mask = (byte)q; if (q <= 255) goto Replay;
    FinalizeSearch: // 77D8
        if (cost == 65535)
        {
            if (closestLength == 0) goto Finish;
            p = closestPointer; WritePair(p, closestCell); _work[p + 2] = 128; end = p + 3; chosenMask = closestMask;
        }
        output = end; SetPosition(savedCell); _loops = false; _targetX = _targetY = 255;
        _stack = BufferStart; ch = 128;
    NextWaypoint: // 7844
        p = _stack;
    WaypointMarker:
        Guard(); waypoint = ReadPair(p);
        if ((byte)waypoint == 128) goto OutputEnd;
        if ((byte)waypoint == 129)
        {
            p += 8; bool alternate = (chosenMask & ch) != 0;
            ch = (byte)((ch >> 1) | (ch << 7)); if (alternate) p = ReadPointer(p - 4);
            goto WaypointMarker;
        }
        p += 2; _stack = p;
        if (waypoint == Pair(_targetX, _targetY)) goto WaypointMarker;
        _targetX = (byte)waypoint; _targetY = (byte)(waypoint >> 8); p = end;
    EmitDirect: // 7888
        a = Toward(); if (a == 255) goto SegmentEnd;
        if (!Probe(a)) goto EmitContour;
        _direction = a; _work[p++] = a; if (p >= OutputLimit) goto OutputFull;
        Advance(); if (!_zero) goto EmitDirect;
    SegmentEnd:
        _work[p] = 255; end = p; goto NextWaypoint;
    EmitContour: // 78BE–7971; preserve the source control flow, including scratch segments.
        end = p; savedCell = Pair(_x, _y); hand = (chosenMask & unchecked((byte)(ch * 2))) == 0 ? 1 : -1;
        length = cl; cl = 0; q = p; a = _direct;
        waypoint = Pair(_x, _y); SetPosition(savedCell); savedCell = waypoint;
    EmitRotate:
        a = (byte)((a + hand) & 7); if (!Probe(a)) goto EmitRotate;
    EmitContourStep:
        _direction = a; _work[p++] = a; cl = unchecked((byte)(cl + 1));
        if (p >= OutputLimit) goto OutputFull;
        Advance(); if (_zero || cl >= 64) goto EmitCorner;
        a = (byte)((a - 2 * hand) & 7);
        if (Probe(a)) goto EmitChoice;
        a = (byte)((a + hand) & 7); if (!Probe(a)) goto EmitRotate;
    EmitChoice:
        if (_direct != _direction && a != ((_direct - hand) & 7)) goto EmitContourStep;
    EmitCorner:
        _work[p++] = 255; SetPosition(savedCell); goto EmitDirect;
    OutputEnd:
        p = end;
    OutputFull:
        _work[p] = 255;
    Finish:
        if (output < 0) return new(false, [], new(_x, _y));
        q = output; while (q < _work.Length && (_work[q] & 8) == 0) q++;
        if (q == _work.Length) throw new InvalidDataException("Native route has no terminator");
        return new(true, _work.AsSpan(output, q - output).ToArray(), new(_x, _y));
    }

    /// <summary>72FE: the planner with explicit parameters (AL = [2741], BL = the plane, BH = the square footprint, (CL, CH) = the centre of the start, (DL, DH) = the centre of the goal, both shifted by half the size to the top-left cell): up to four contour searches without the +-16 window (the bounds are FFh); after every search the route is followed from the start; the goal reached - found; otherwise the pass counter [1F41] falls and the search starts again from the point reached. Left = the counter after the last pass; the route and SearchEnd are those of the last search.</summary>
    public (bool Found, byte[] Route, RouteCell End, byte Left) Plan72FE(byte width, byte height, byte size, RouteCell center, RouteCell goalCenter, Func<byte, byte, byte, bool> admit)
    {
        byte half = (byte)(size >> 1);
        var start = new RouteCell(unchecked((byte)(center.X - half)), unchecked((byte)(center.Y - half)));
        var target = new RouteCell(unchecked((byte)(goalCenter.X - half)), unchecked((byte)(goalCenter.Y - half)));
        byte left = 4;
        for (; ; )
        {
            var r = Find(width, height, size, size, start, target, admit, windowed: false);
            if (!r.Found) return (false, [], r.SearchEnd, left);
            byte x = start.X, y = start.Y;
            foreach (byte d in r.Directions) { x = unchecked((byte)(x + Deltas[d].X)); y = unchecked((byte)(y + Deltas[d].Y)); }
            if (x == target.X && y == target.Y) return (true, r.Directions, r.SearchEnd, left);
            left--; if (left == 0) return (false, r.Directions, r.SearchEnd, left);
            start = new RouteCell(x, y);
        }
    }
}
