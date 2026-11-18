using System.Buffers.Binary;
namespace Northwind;

public readonly record struct DeliveryCandidate(uint Reference, uint X, uint Y, uint Flags, byte Condition, byte Threshold);
public readonly record struct DeliveryPoint(uint X, uint Y, bool IsCenter, bool Valid);
public enum DeliveryWait : byte { Completed = 0, Expired = 2, Pending = 255 }

public static class OriginalWorkerDelivery
{
    // C439..C4CC: owner-chain order is supplied by the caller. Equal distances select the later record.
    // Callers pass EAX/EBX: 200h/F00h for the lumbermill (8659), 508h/F08h for ore receivers (872A).
    public static uint FindReceiver(uint x, uint y, IEnumerable<DeliveryCandidate> ownerChain, uint kind = 0x200, uint mask = 0xf00)
    {
        uint result = 0, best = uint.MaxValue;
        foreach (var candidate in ownerChain)
        {
            if ((candidate.Flags & mask) != kind || (sbyte)candidate.Condition <= (sbyte)candidate.Threshold) continue;
            uint dx = SignedAbs(unchecked(candidate.X - x)), dy = SignedAbs(unchecked(candidate.Y - y));
            uint distance = Math.Max(dx, dy);
            if (distance > best) continue;
            best = distance; result = candidate.Reference;
        }
        return result;
    }
    private static uint SignedAbs(uint value) => (value & 0x80000000) == 0 ? value : unchecked(0u - value);
    // B008..B02D: even footprints use a grid corner; odd footprints add sixteen.
    public static uint BuildingAnchor(byte cell, byte footprint) => (uint)(cell * 32 + ((footprint & 1) << 4));
    // 8DB0..8E0D: entry offsets depend on the visitor's DAT width, not the owner's army.
    public static DeliveryPoint Approach(ReadOnlySpan<byte> visitor, ReadOnlySpan<byte> target, uint x, uint y, uint widthPixels, uint heightPixels)
    {
        bool center = true;
        if ((BinaryPrimitives.ReadUInt32LittleEndian(target[0x40..]) & 8) != 0)
        {
            int offset = (visitor[3] & 0xfe) == 0 ? 0x53 : 0x55;
            int dx = (sbyte)target[offset], dy = (sbyte)target[offset + 1];
            center = (dx | dy) == 0; x = unchecked(x + (uint)dx); y = unchecked(y + (uint)dy);
        }
        return new(x, y, center, x < widthPixels && y < heightPixels);
    }
    // C4CD's fixed-entry branch through C591; the generic perimeter branch is excluded.
    public static uint WaitingWorker(byte width, byte height, ReadOnlySpan<uint> occupancy, byte layer,
        uint x, uint y, ReadOnlySpan<byte> template)
    {
        if ((BinaryPrimitives.ReadUInt32LittleEndian(template[0x40..]) & 8) == 0 || (template[0x53] | template[0x54]) == 0)
            throw new ArgumentException("Only fixed-entry buildings are supported");
        if (occupancy.Length != 3 * width * height || (layer & 3) >= 3) throw new ArgumentException("Invalid occupancy");
        x = unchecked(x + (uint)(sbyte)template[0x53]) >> 5; y = unchecked(y + (uint)(sbyte)template[0x54]) >> 5;
        // C591 uses the low byte of both tile coordinates.
        x &= 255; y &= 255;
        if (x >= width || y >= height) return 0;
        uint reference = occupancy[(layer & 3) * width * height + (int)y * width + (int)x];
        return reference; // Fixed-entry C4FC tests the pointer, overriding C591's flag test.
    }
    // 8FFD..900B, after alignment and turn have succeeded.
    public static void RequestHandoff(Span<byte> worker, Span<byte> receiver)
    {
        worker[0x17] |= 0x40; receiver[0x17] |= 0x40; worker[0x30] = 0xf3;
    }
    // 900D..902E: byte timeout and acknowledgement, independent of the receiver's processing queue.
    public static DeliveryWait WaitHandoff(Span<byte> worker)
    {
        if ((worker[0x30] >> 4) == 0) { worker[0x17] &= 0xbf; return DeliveryWait.Expired; }
        worker[0x30] = unchecked((byte)(worker[0x30] - 0x10));
        if ((worker[0x17] & 0x40) != 0) return DeliveryWait.Pending;
        worker[0x17] &= 0xbf; return DeliveryWait.Completed;
    }
    private static void Initialize(Span<byte> record, ReadOnlySpan<byte> template, byte workFlags, byte random)
    {
        record[0x20] = 255; record[0x0f] = workFlags;
        ushort frame = unchecked((ushort)(BinaryPrimitives.ReadUInt16LittleEndian(template[0x48..]) + (record[0x25] & 7) * template[0x4a]));
        BinaryPrimitives.WriteUInt16LittleEndian(record[1..], frame); record[0x2e] = random;
    }
    public static void InitializeEmpty(Span<byte> record, ReadOnlySpan<byte> template, byte random) => Initialize(record, template, 0x3f, random);
    // 123D3 and WRK 851..8E8, restricted to empty workers receiving timber.
    public static bool GrantTimber(Span<byte> record, ReadOnlySpan<byte> emptyTemplate, ReadOnlySpan<byte> loadedTemplate, byte random)
    {
        if ((sbyte)record[0x19] <= 0 || (BinaryPrimitives.ReadUInt32LittleEndian(emptyTemplate[0x40..]) & 0x20000) == 0) return false;
        if (record[0x31] != 0) throw new ArgumentException("Grant requires empty cargo");
        record[0x31] = 3; record[0x0e] = 11; Initialize(record, loadedTemplate, 0x13, random);
        record[0x14] = (byte)((record[0x14] & 7) | 0x60); return true;
    }
    // WRK 9B2..9D0: empty-cargo return; the dropped-cargo/effect branch is excluded.
    public static void ReturnEmpty(Span<byte> record, ReadOnlySpan<byte> emptyTemplate, byte random)
    {
        if (record[0x31] != 0) throw new ArgumentException("Return requires empty cargo");
        record[0x0e] = 10; InitializeEmpty(record, emptyTemplate, random); record[0x14] = 0x58; record[0x15] = 0;
    }
}
