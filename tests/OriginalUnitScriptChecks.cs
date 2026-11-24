using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class UnitScriptReplay(byte seed, byte[] random, List<byte> log) : IUnitScriptWorld
    {
        public byte Index { get; private set; } = seed;
        public byte Random() => random[Index++];
        public void Appearance(byte army) { log.Add((byte)'N'); log.AddRange(BitConverter.GetBytes((uint)army)); }
        public void Voice() => log.Add((byte)'Y');
        public void Redraw() => log.Add((byte)'D');
        public void Vitals() => log.Add((byte)'V');
        public void Actions() => log.Add((byte)'U');
        public void Effect(byte al, byte bl, byte bh, uint ecx, uint edx) { log.Add((byte)'E'); log.AddRange([al, bl, bh]); log.AddRange(BitConverter.GetBytes(ecx)); log.AddRange(BitConverter.GetBytes(edx)); }
    }

    /// <summary>Replays tests/fixtures/unit-scripts.json (tools/verify_unit_scripts.py): the standard unit scripts of ARMY__01/02.WRK.</summary>
    private void OriginalUnitScriptChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/unit-scripts.json");
        var root = reference.RootElement;
        var random = root.GetProperty("random_values").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, inits = 0; var seen = new HashSet<int>();
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var r = new byte[0x50]; r[0x14] = (byte)c.GetProperty("f14").GetInt32(); r[0x2d] = (byte)c.GetProperty("f2d").GetInt32(); r[0x0f] = (byte)c.GetProperty("f0f").GetInt32(); r[0x2e] = (byte)c.GetProperty("f2e").GetInt32();
            r[0x31] = (byte)c.GetProperty("f31").GetInt32(); r[0x19] = (byte)c.GetProperty("f19").GetInt32(); r[0x25] = (byte)c.GetProperty("f25").GetInt32(); r[0] = (byte)c.GetProperty("f00").GetInt32(); r[0x0e] = (byte)c.GetProperty("f0e").GetInt32();
            BitConverter.GetBytes(c.GetProperty("x").GetUInt32()).CopyTo(r, 4); BitConverter.GetBytes(c.GetProperty("y").GetUInt32()).CopyTo(r, 8);
            int entry = c.GetProperty("entry").GetInt32(); seen.Add(entry); var log = new List<byte>(); var world = new UnitScriptReplay((byte)c.GetProperty("seed").GetInt32(), random, log);
            int ah = c.GetProperty("ah").GetInt32(); if (ah == 0) inits++;
            if (entry >= 6) OriginalUnitScript.Worker(world, r, ah, (byte)c.GetProperty("bl").GetInt32(), entry - 6, (byte)c.GetProperty("al").GetInt32());
            else OriginalUnitScript.Run(world, r, ah, (byte)c.GetProperty("bl").GetInt32(), entry is 2 or 5);
            byte[] output = [.. r, world.Index, .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; }
            count++;
        }
        GD.Print($"Unit scripts: first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("unit");
        Check(count == summary.GetProperty("case_count").GetInt32() && seen.Count == 9 && inits > 100, $"Unit script reference coverage ({count} cases, {seen.Count} entries)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Standard unit scripts ARMY__01/02.WRK match original x86");
    }
}
