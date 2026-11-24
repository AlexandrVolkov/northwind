using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/object-alloc.json (tools/verify_object_alloc.py): the object allocator FS:45 = A758.</summary>
    private void OriginalObjectAllocChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/object-alloc.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var dc) ? dc : -1;
        int count = 0, firstMismatch = -1, failures = 0, carved = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var m = new AllocMemory { PoolBase = 0x400000 };
            const int R = 0x4000, TT = 0x1000;
            int army = c.GetProperty("army").GetInt32(), owner = c.GetProperty("owner").GetInt32(), tmpl = c.GetProperty("tmpl").GetInt32();
            for (int a = 0; a < 3; a++)
            {
                m.Size[a] = new byte[4]; m.Hp[a] = new byte[4]; m.Script[a] = new uint[4]; m.Frame[a] = new ushort[4];
                for (int t = 0; t < 4; t++)
                {
                    m.Size[a][t] = (byte)c.GetProperty("tsize")[a][t].GetInt32(); m.Script[a][t] = c.GetProperty("tscript")[a][t].GetUInt32();
                    m.Hp[a][t] = (byte)c.GetProperty("thp")[a][t].GetInt32(); m.Frame[a][t] = (ushort)c.GetProperty("tframe")[a][t].GetInt32();
                }
                m.WrkBase[a] = a == army ? 0x10000u + 0x30 - m.Script[a][tmpl] : 0x10000u + 0x30 - 0x40;
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
            var args = c.GetProperty("args").EnumerateArray().Select(e => e.GetUInt32()).ToArray();
            var log = new List<byte>();
            uint eax = (uint)army | ((uint)owner << 8) | (c.GetProperty("ax").GetUInt32() & 0xffff0000);
            var (ok, rec) = OriginalObjectAllocator.Allocate(m, (byte)army, (byte)owner, (byte)tmpl, (byte)c.GetProperty("layer").GetInt32(), c.GetProperty("cx").GetUInt32(), c.GetProperty("dx").GetUInt32(), eax, args,
                (a, b, cc, d, edi) => { log.Add((byte)'I'); foreach (var v in new[] { a, b, cc, d, edi }) log.AddRange(BitConverter.GetBytes(v)); });
            if (!ok) failures++; else if (objs.Count > 0) carved++;
            var part = new List<byte>(); part.AddRange(BitConverter.GetBytes(m.First[owner])); part.AddRange(BitConverter.GetBytes(m.End[owner])); part.Add(0); part.AddRange(BitConverter.GetBytes(m.Hint[owner]));
            byte[] output = [.. m.Pool[R..(R + 0x700)], .. part, .. BitConverter.GetBytes(m.ScriptAddress), m.HpGlobal, .. BitConverter.GetBytes(m.FrameGlobal), .. BitConverter.GetBytes(rec), ok ? (byte)0 : (byte)1, ok ? (byte)1 : (byte)0, .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Object allocator: first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("alloc");
        Check(count == summary.GetProperty("case_count").GetInt32() && failures > 20 && carved > 300, $"Object allocator reference coverage ({count} cases, {failures} failures)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Object allocator FS:45 matches original x86");
    }
}
