using System.Buffers.Binary;
namespace Northwind;

/// <summary>Services the pit trap calls: FS:14B (LE:1 11DDE, objects in a 3×3 square), FS:157 (D2D1, claim or crush the square), FS:1D (81D9).</summary>
public interface IPitWorld
{
    /// <summary>11DDE: ecx = the corner cell (x | y&lt;&lt;8), edx = size 0202h; returns the hostile objects found (eax) and those not hostile (ebx).</summary>
    (uint Found, uint Other) Search(uint corner, uint size, byte[] record);
    /// <summary>D2D1: eax = new owner reference, ebx = the reference to replace, cl ≠ 0 crushes the other objects, ch = kind of the crush.</summary>
    uint Area(uint reference, uint replaced, byte crush, byte kind, byte[] record);
    void Service(byte[] record);
}

/// <summary>ARMY__03 «Яма-Ловушка», WRK 7F0 (docs/campaign-items.md): checks every 20 updates for hostile units in the 3×3 square, opens for 19 frames
/// crushing what stands in it, stays open 256 updates, closes and gives the square back. Verified against the original x86 with the services recorded
/// (tools/verify_pit.py, tests/fixtures/pit.json).</summary>
public static class OriginalPit
{
    public static void Initialize(byte[] record)
    {
        record[0x22] = 0xbf; record[0x23] = record[0x24] = record[0x25] = 0xff;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1), 0x289);
        record[0x2a] = 0;
    }

    /// <param name="counter">The low byte of the update counter (FS:10).</param>
    /// <param name="reference">The object's own reference (record offset in the pool).</param>
    public static void Step(IPitWorld world, byte[] record, byte counter, uint reference)
    {
        if ((record[0x14] & 0x80) != 0) { record[0x14] &= 0x7f; world.Service(record); }
        if (record[0x15] == 0)
        {
            if ((counter & 0x1f) != 0) return;
            uint corner = unchecked(((uint)(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4)) >> 5) & 0xffff00ff | ((BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8)) >> 5) & 0xff) << 8) - 0x101);
            var (found, other) = world.Search(corner, 0x202, record);
            if (found == 0 || other != 0) return;
            record[0x15] = 1;
        }
        if (record[0x15] == 1)
        {
            if (record[0x2a] != 0x13 && (counter & 1) == 0)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(1)) + 1));
                record[0x2a]++;
            }
            record[0x0d]++;
            uint crushed = world.Area(reference, 0, 0xfe, 2, record);
            record[0x0d]--;
            if (crushed != 0 || record[0x2a] != 0x13) return;
            record[0x15] = 2;
        }
        if (record[0x15] == 2)
        {
            if (counter != 0) return;
            record[0x15] = 3;
        }
        if ((counter & 1) != 0) return;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(1)) - 1));
        if (--record[0x2a] != 0) return;
        record[0x0d]++;
        world.Area(0, reference, 0, 0, record);
        record[0x0d]--;
        record[0x15] = 0;
    }
}
