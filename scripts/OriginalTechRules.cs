using System.Buffers.Binary;
namespace Northwind;

public sealed class OriginalTechRecord
{
    public int Offset { get; set; }
    public byte Kind { get; set; }
    public byte Id { get; set; }
    public byte Unmet { get; set; }
    public uint TextOffset { get; set; }
    public uint Cost { get; set; }
    public string Unknown0b { get; set; } = "00000000";
    public string? Text { get; set; }
    public List<List<byte>> Children { get; set; } = [];
}
public sealed class OriginalTechArmy
{
    public int Army { get; set; }
    public string File { get; set; } = "";
    public int End { get; set; }
    public string Terminator { get; set; } = "ffff";
    public List<OriginalTechRecord> Records { get; set; } = [];

    /// <summary>The record area of the TRE file (+ terminator), as each settlement's working copy starts.</summary>
    public byte[] Build()
    {
        var raw = new List<byte>();
        foreach (var r in Records)
        {
            var head = new byte[0x10]; head[0] = r.Kind; head[1] = r.Id; head[2] = r.Unmet;
            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(3), r.TextOffset); BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(7), r.Cost);
            Convert.FromHexString(r.Unknown0b).CopyTo(head, 0xb); head[0xf] = (byte)r.Children.Count;
            raw.AddRange(head);
            foreach (var c in r.Children) { raw.Add(c[0]); raw.Add(c[1]); }
        }
        raw.AddRange(Convert.FromHexString(Terminator));
        return [.. raw];
    }
}
/// <summary>data/original_tech.json from tools/tech_source.py.</summary>
public sealed class OriginalTechData
{
    public int FormatVersion { get; set; }
    public List<OriginalTechArmy> Armies { get; set; } = [];
}

/// <summary>Development tree routines of LE:1 over one settlement's TRE copy (docs/tech.md): A673, A6AE/A6F9, A716, 223B4.
/// Record: +0 kind (bit 7 — open/done), +1 id, +2 unmet conditions, +3 text, +7 cost, +0F child count, +10 children (kind, id).</summary>
public static class OriginalTechRules
{
    private static int Next(ReadOnlySpan<byte> tree, int at) => at + 0x10 + 2 * tree[at + 0xf];

    /// <summary>A673: template <paramref name="template"/> has an open record (80h, id) with no unmet conditions.
    /// The walk stops at a record whose +1 is FFh.</summary>
    public static bool Available(ReadOnlySpan<byte> tree, byte template)
    {
        for (int at = 0; tree[at + 1] != 0xff; at = Next(tree, at))
            if (tree[at + 1] == template && tree[at] == 0x80) return tree[at + 2] == 0;
        return false;
    }

    /// <summary>A6F9: offset of the record with kind and id, or −1. The walk stops at a record whose +0 is FFh.</summary>
    public static int Find(ReadOnlySpan<byte> tree, byte kind, byte id)
    {
        for (int at = 0; tree[at] != 0xff; at = Next(tree, at))
            if (tree[at] == kind && tree[at + 1] == id) return at;
        return -1;
    }

    /// <summary>A6AE: mark the item done (+0 |= 80h) and lower the unmet count of every child (a result with bit 7 set becomes 0).</summary>
    public static void Complete(Span<byte> tree, byte kind, byte id)
    {
        int at = Find(tree, kind, id);
        if (at < 0) return;
        tree[at] |= 0x80;
        for (int k = 0; k < tree[at + 0xf]; k++)
        {
            int child = Find(tree, tree[at + 0x10 + 2 * k], tree[at + 0x11 + 2 * k]);
            if (child < 0) continue;
            byte unmet = unchecked((byte)(tree[child + 2] - 1));
            tree[child + 2] = (sbyte)unmet < 0 ? (byte)0 : unmet;   // DEC, JNS: a negative result becomes 0
        }
    }

    /// <summary>A716: offsets of research items (+0 = 0) with no unmet conditions; the first record is always examined.</summary>
    public static List<int> Researchable(ReadOnlySpan<byte> tree)
    {
        var list = new List<int>();
        int at = 0;
        do
        {
            if (tree[at + 2] == 0 && tree[at] == 0) list.Add(at);
            at = Next(tree, at);
        } while (tree[at + 1] != 0xff);
        return list;
    }

    /// <summary>223B4: template indices whose DAT[40] &amp; mask = value and DAT[9C] ≠ FFh; with <paramref name="openOnly"/> also A673.</summary>
    public static List<byte> Buildable(IReadOnlyList<byte[]> templates, ReadOnlySpan<byte> tree, bool openOnly, uint mask, uint value)
    {
        var list = new List<byte>();
        for (int i = 0; i < templates.Count; i++)
        {
            var t = templates[i];
            if ((BinaryPrimitives.ReadUInt32LittleEndian(t.AsSpan(0x40)) & mask) != value || t[0x9c] == 0xff) continue;
            if (openOnly && !Available(tree, (byte)i)) continue;
            list.Add((byte)i);
        }
        return list;
    }
}
