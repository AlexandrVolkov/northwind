using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/effect-pool.json (tools/verify_effect_pool.py): the effect pool 9727 (create) and 96CD (update).</summary>
    private void OriginalEffectPoolChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/effect-pool.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var dc) ? dc : -1;
        int count = 0, firstMismatch = -1, creates = 0, failures = 0, updates = 0;
        const int Records = 0x7fde / 0x22 + 2;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            string fn = c.GetProperty("fn").GetString()!;
            var m = new EffectMemory { Base = 0x404000, LimitX = c.GetProperty("l1").GetUInt32(), LimitY = c.GetProperty("l2").GetUInt32() };
            Array.Fill(m.Pool, (byte)0xff, 0, Records * 0x22 + 0x40);
            var filler = ActionPrng(c.GetProperty("bytes").GetUInt32(), 0x22);
            if (fn == "create")
            {
                int used = c.GetProperty("used").GetInt32(); var rec = (byte[])filler.Clone(); if (rec[0] == 0xff) rec[0] = 0;
                for (int i = 0; i < used; i++) rec.CopyTo(m.Pool, i * 0x22);
            }
            else
            {
                int i = 0; var armies = c.GetProperty("armies").EnumerateArray().Select(e => e.GetInt32()).ToArray();
                foreach (var t in c.GetProperty("live").EnumerateArray()) { var rec = (byte[])filler.Clone(); rec[0] = (byte)t.GetInt32(); rec[1] = (byte)armies[i % 30]; rec.CopyTo(m.Pool, i * 0x22); i++; }
            }
            for (int a = 0; a < 3; a++) { m.TablePointer[a] = (uint)(0x400000 + 0x1000 + 0x100 * a); m.WrkBase[a] = 0; m.ScriptOffset[a] = 0x10030; }
            m.HighWater = fn == "loop" ? m.Base + (uint)(c.GetProperty("live").GetArrayLength() * 0x22) : m.Base + (uint)(c.GetProperty("hw").GetInt32() * 0x22);
            var log = new List<byte>(); uint esiSeed = (uint)(0x1234 + c.GetProperty("esi").GetInt32());
            var g = new ActionRegs { Eax = c.GetProperty("ax").GetUInt32(), Ebx = c.GetProperty("bx").GetUInt32(), Ecx = c.GetProperty("cx").GetUInt32(), Edx = c.GetProperty("dx").GetUInt32(), Esi = esiSeed, Edi = esiSeed };
            uint Rel(uint v) => v >= 0x400000 && v < 0x420000 ? v - 0x400000 : v;
            Action<uint, uint, uint, uint, uint, uint> script = (a, b, cc, d, edi, esi) => { log.Add((byte)'S'); foreach (var v in new[] { a, b, cc, d, Rel(edi), Rel(esi) }) log.AddRange(BitConverter.GetBytes(v)); };
            if (fn == "create")
            {
                var args = c.GetProperty("args").EnumerateArray().Select(e => e.GetUInt32()).ToArray();
                OriginalEffectPool.Create(m, g, args, script); creates++; if (g.Ah == 0xff) failures++;
            }
            else { OriginalEffectPool.Update(m, g, (c.GetProperty("esi").GetInt32() & 7) != 0, script); updates++; }
            byte[] output = [.. m.Pool[..(66 * 0x22)], .. BitConverter.GetBytes(m.HighWater), .. BitConverter.GetBytes(m.ScriptAddress), .. BitConverter.GetBytes(Rel(g.Eax)), .. BitConverter.GetBytes(Rel(g.Ebx)), .. BitConverter.GetBytes(Rel(g.Ecx)), .. BitConverter.GetBytes(Rel(g.Edx)), .. BitConverter.GetBytes(Rel(g.Edi)), g.Zf ? (byte)1 : (byte)0, g.Cf ? (byte)1 : (byte)0, .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Effect pool: first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("pool");
        Check(count == summary.GetProperty("case_count").GetInt32() && failures > 5 && updates > 100, $"Effect pool reference coverage ({count} cases, {failures} failures, {updates} updates)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Effect pool 9727 / 96CD matches original x86");
    }
}
