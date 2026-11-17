using System.Collections;
using Godot;
namespace Northwind;

/// <summary>Prototype adapter: native search prefixes in the original shared route pool.</summary>
public sealed class SkirmishRoute : IEnumerable<Vector2I>
{
    private static readonly Vector2I[] Deltas = [new(0,-1),new(1,-1),new(1,0),new(1,1),new(0,1),new(-1,1),new(-1,0),new(-1,-1)];
    // A terminal high nibble in byte 62 avoids the source writer's stale byte-63 low nibble.
    public const int PrefixSteps = 125;
    private OriginalRoutePool? _pool;
    private byte[] _record = [];
    private Vector2I _head;
    public int Count { get; private set; }
    public void Attach(OriginalRoutePool pool, byte[] record)
    {
        if (_pool != null) throw new InvalidOperationException("Route already attached");
        _pool = pool; _record = record; OriginalRoutePool.SetCursor(record, OriginalRoutePool.NoRoute);
    }
    public bool Set(IReadOnlyList<Vector2I> path)
    {
        Clear();
        if (path.Count < 2) return false;
        if (_pool == null) throw new InvalidOperationException("Route is not attached");
        int count = Math.Min(PrefixSteps, path.Count - 1);
        var directions = new byte[count + 1]; directions[count] = 8;
        for (int i = 0; i < count; i++)
        {
            int direction = Array.IndexOf(Deltas, path[i + 1] - path[i]);
            if (direction < 0) throw new ArgumentException("Route contains a nonadjacent step");
            directions[i] = (byte)direction;
        }
        if (!_pool.Allocate(_record, directions)) return false;
        _head = path[0]; Count = count; return true;
    }
    public Vector2I Peek()
    {
        if (Count == 0) throw new InvalidOperationException("Route is empty");
        return _head + Deltas[_pool!.Peek(_record)];
    }
    public Vector2I Dequeue()
    {
        var next = Peek(); _pool!.Consume(_record); _head = next;
        if (--Count == 0) _pool.Release(_record);
        return next;
    }
    public void Clear()
    {
        // +28 is a union: forestry uses it as two ushort target coordinates after motion ends.
        // Only a live adapter lease owns the field as a route cursor.
        if (Count > 0) _pool!.Release(_record);
        Count = 0;
    }
    public IEnumerator<Vector2I> GetEnumerator()
    {
        var cell = _head;
        uint cursor = Count == 0 ? OriginalRoutePool.NoRoute : OriginalRoutePool.Cursor(_record);
        for (int i = 0; i < Count; i++) { cell += Deltas[_pool!.Read(cursor + (uint)i)]; yield return cell; }
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
