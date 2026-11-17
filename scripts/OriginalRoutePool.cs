using System.Buffers.Binary;
namespace Northwind;

/// <summary>802C–809B / 7FA7–7FC6 / 8120: per-owner packed routes and record +28 nibble cursor.</summary>
public sealed class OriginalRoutePool
{
    public const int OwnerBytes = 8192, BlockBytes = 64, BlocksPerOwner = 128;
    public const uint NoRoute = uint.MaxValue;
    private readonly byte[] _bytes;
    public ReadOnlySpan<byte> Bytes => _bytes;
    public OriginalRoutePool(int owners = 32)
    {
        if (owners is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(owners));
        _bytes = new byte[owners * OwnerBytes];
        // 2480 initializes the first byte only; block tails survive reuse.
        for (int i = 0; i < _bytes.Length; i += BlockBytes) _bytes[i] = 255;
    }
    public void LoadBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != _bytes.Length) throw new ArgumentException("Route pool size mismatch");
        bytes.CopyTo(_bytes);
    }
    public static uint Cursor(ReadOnlySpan<byte> record) => BinaryPrimitives.ReadUInt32LittleEndian(record[0x28..]);
    public static void SetCursor(Span<byte> record, uint cursor) => BinaryPrimitives.WriteUInt32LittleEndian(record[0x28..], cursor);
    public bool Allocate(Span<byte> record, ReadOnlySpan<byte> directions)
    {
        int start = record[3] * OwnerBytes;
        if (start >= _bytes.Length) throw new ArgumentOutOfRangeException(nameof(record));
        // Input is the byte-per-direction result of the planner, including its terminator.
        int terminal = -1;
        for (int i = 0; i < directions.Length; i++) if ((directions[i] & 8) != 0) { terminal = i; break; }
        if (terminal < 0) throw new ArgumentException("Route needs a terminator");
        for (int block = start; block < start + OwnerBytes; block += BlockBytes)
        {
            if (_bytes[block] != 255) continue;
            SetCursor(record, (uint)(block * 2));
            for (int i = 0; i < BlockBytes - 1; i++)
            {
                int source = i * 2;
                // Native code reads a dword even at the terminator. Bytes beyond it have no semantic effect.
                byte low = directions[source], high = source + 1 < directions.Length ? directions[source + 1] : (byte)0;
                byte packed = (byte)((low & 15) | (high << 4));
                _bytes[block + i] = packed;
                if ((packed & 0x88) != 0) return true;
            }
            // Deliberately preserves the old low nibble, exactly as 8095 does.
            _bytes[block + 63] |= 128;
            return true;
        }
        return false;
    }
    public byte Peek(ReadOnlySpan<byte> record) => Read(Cursor(record));
    public byte Read(uint cursor)
    {
        if (cursor == NoRoute || cursor / 2 >= _bytes.Length) throw new InvalidOperationException("No valid route cursor");
        return (byte)((_bytes[cursor / 2] >> ((int)(cursor & 1) * 4)) & 15);
    }
    public void Consume(Span<byte> record)
    {
        if ((Peek(record) & 8) != 0) throw new InvalidOperationException("Cannot consume a terminator");
        SetCursor(record, Cursor(record) + 1);
    }
    public void Release(Span<byte> record)
    {
        uint cursor = Cursor(record);
        if (cursor == NoRoute) return;
        if (cursor / 2 >= _bytes.Length) throw new InvalidOperationException("Invalid route cursor");
        _bytes[(cursor / 2) & ~63u] = 255;
        SetCursor(record, NoRoute);
    }
}
