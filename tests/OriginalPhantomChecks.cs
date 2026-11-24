using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class PhantomTestWorld(List<byte> log, byte[] random, byte index) : IPhantomWorld
    {
        public byte Index = index;
        public void Appearance(byte army) { log.Add((byte)'N'); log.AddRange(BitConverter.GetBytes((uint)army)); }
        public void Redraw() => log.Add((byte)'D'); public void Vitals() => log.Add((byte)'V'); public void UnitStep() => log.Add((byte)'U');
        public byte Random() => random[Index++];
    }

    /// <summary>Replays tests/fixtures/phantom.json (tools/verify_phantom.py): the script of the phantom ARMY__03.WRK 212.</summary>
    private void OriginalPhantomChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/phantom.json");
        var root = reference.RootElement;
        var random = root.GetProperty("random_values").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, deaths = 0, inits = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var r = new byte[0x40]; r[0x19] = (byte)c.GetProperty("condition").GetInt32(); r[0x31] = (byte)c.GetProperty("life").GetInt32(); r[0x2e] = (byte)c.GetProperty("tick").GetInt32(); r[0x14] = (byte)c.GetProperty("state").GetInt32();
            BitConverter.GetBytes((ushort)c.GetProperty("flags17").GetInt32()).CopyTo(r, 0x17);
            var log = new List<byte>(); var world = new PhantomTestWorld(log, random, (byte)c.GetProperty("seed").GetInt32()); byte al = 0x55; int ah = c.GetProperty("ah").GetInt32();
            if (c.GetProperty("kind").GetString() == "tail") al = OriginalPhantom.Continuation(r);
            else if (ah == 0) { al = OriginalPhantom.Initialize(world, r, (byte)c.GetProperty("bl").GetInt32()); inits++; }
            else if (ah == 1) OriginalPhantom.Update(world, r);
            if (c.GetProperty("kind").GetString() == "tail" && r[0x19] == 0) deaths++;
            byte[] output = [.. r, al, .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; GD.Print($"PHANTOM MISMATCH {count}"); }
            count++;
        }
        GD.Print($"Phantom: first mismatch {firstMismatch}; inits {inits}, deaths {deaths}");
        var summary = root.GetProperty("results").GetProperty("phantom");
        Check(count == summary.GetProperty("case_count").GetInt32() && inits > 100 && deaths > 100, $"Phantom reference coverage ({count}: {inits} inits, {deaths} deaths)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Phantom script ARMY__03.WRK 212/27F matches original x86");
    }
}
