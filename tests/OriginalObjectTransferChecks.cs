using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/object-transfer.json (tools/verify_object_transfer.py): the hand-over of an object to another owner, LE:1 D1BA..D2D1.</summary>
    private void OriginalObjectTransferChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/object-transfer.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var dc) ? dc : -1;
        int count = 0, firstMismatch = -1, failures = 0, notified = 0, carved = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var m = new AllocMemory { PoolBase = 0x400000 };
            const int R = 0x4000, SRC = 0x3c00;
            int owner = c.GetProperty("owner").GetInt32(), sarmy = c.GetProperty("sarmy").GetInt32(), stmpl = c.GetProperty("stmpl").GetInt32();
            for (int a = 0; a < 3; a++)
            {
                m.Size[a] = new byte[4]; m.Flags[a] = new uint[4];
                for (int t = 0; t < 4; t++) { m.Size[a][t] = (byte)c.GetProperty("tsize")[a][t].GetInt32(); m.Flags[a][t] = c.GetProperty("tflags")[a][t].GetUInt32(); }
            }
            int pos = R; var starts = new List<int>(); bool fail = c.GetProperty("fail").GetBoolean();
            var objs = fail ? Enumerable.Range(0, 1100).Select(_ => (0, 0, 0)).ToList() : c.GetProperty("objs").EnumerateArray().Select(e => (e.GetProperty("army").GetInt32(), e.GetProperty("tmpl").GetInt32(), e.GetProperty("slack").GetInt32())).ToList();
            foreach (var (a, t, slack) in objs)
            {
                int sz = m.Size[a][t] + slack;
                starts.Add(pos); Array.Fill(m.Pool, (byte)0xaa, pos, sz); m.Pool[pos] = (byte)a; m.Pool[pos + 0xe] = (byte)t; BitConverter.GetBytes((uint)sz).CopyTo(m.Pool, pos + 0x10); pos += sz;
            }
            starts.Add(pos); int hint = fail ? R : starts[Math.Min(c.GetProperty("hint").GetInt32(), starts.Count - 1)];
            m.First[owner] = m.PoolBase + R; m.End[owner] = (uint)pos; m.Hint[owner] = (uint)hint;
            var src = new byte[0x70]; var seed = c.GetProperty("seed").GetUInt32();
            for (int k = 0; k < src.Length; k++) { seed = unchecked(seed * 1664525 + 1013904223); src[k] = (byte)(seed >> 24); }
            src.CopyTo(m.Pool, SRC); m.Pool[SRC] = (byte)sarmy; m.Pool[SRC + 0xe] = (byte)stmpl; BitConverter.GetBytes(0x70u).CopyTo(m.Pool, SRC + 0x10);
            var log = new List<byte>();
            static uint Rel(uint v) => v >= 0x400000 && v < 0x420000 ? v - 0x400000 : v;
            bool ok = OriginalObjectAllocator.Transfer(m, m.PoolBase + SRC, (byte)owner,
                () => { log.Add((byte)'C'); log.AddRange(BitConverter.GetBytes((ushort)0x9a8e)); log.AddRange(BitConverter.GetBytes((uint)(0xab * stmpl))); log.AddRange(BitConverter.GetBytes((uint)SRC)); notified++; },
                rec => { log.Add((byte)'C'); log.AddRange(BitConverter.GetBytes((ushort)0x43b0)); log.AddRange(BitConverter.GetBytes(rec)); log.AddRange(BitConverter.GetBytes(rec)); });
            if (!ok) failures++; else if (objs.Count > 0) carved++;
            var part = new List<byte>(); part.AddRange(BitConverter.GetBytes(m.First[owner])); part.AddRange(BitConverter.GetBytes(m.End[owner])); part.Add(0); part.AddRange(BitConverter.GetBytes(m.Hint[owner]));
            byte[] output = [.. m.Pool[SRC..(SRC + 0x70)], .. m.Pool[R..(R + 0x700)], .. part, (byte)c.GetProperty("g273f").GetInt32(), (byte)0x55, ok ? (byte)0 : (byte)0xff, ok ? (byte)1 : (byte)0, .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Object transfer: first mismatch {firstMismatch}; {failures} failures, {notified} notifications, {carved} carved");
        var summary = root.GetProperty("results").GetProperty("transfer");
        Check(count == summary.GetProperty("case_count").GetInt32() && failures > 20 && notified > 100 && carved > 300, $"Object transfer reference coverage ({count} cases, {failures} failures)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Object transfer D1BA matches original x86");
    }
}
