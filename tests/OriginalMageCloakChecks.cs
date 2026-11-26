using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class MageTestWorld(List<byte> log) : IMageWorld
    { public void DropItems(byte cellX, byte cellY, byte owner) => log.AddRange([(byte)'D', cellX, cellY, owner]); }

    /// <summary>Replays tests/fixtures/mage-cloak.json (tools/verify_mage_cloak.py): the vanishing of the hurt mages.</summary>
    private void OriginalMageCloakChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/mage-cloak.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, requests = 0, hidden = 0, drops = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var r = new byte[0x40]; r[3] = (byte)c.GetProperty("owner").GetInt32(); r[0x19] = (byte)c.GetProperty("condition").GetInt32(); r[0x31] = (byte)c.GetProperty("charge").GetInt32();
            r[0x32] = (byte)c.GetProperty("hidden").GetInt32(); r[0x2e] = (byte)c.GetProperty("tick").GetInt32(); r[0x14] = (byte)c.GetProperty("state").GetInt32(); r[0x20] = (byte)c.GetProperty("mask").GetInt32();
            BitConverter.GetBytes(c.GetProperty("x").GetUInt32()).CopyTo(r, 4); BitConverter.GetBytes(c.GetProperty("y").GetUInt32()).CopyTo(r, 8);
            BitConverter.GetBytes((ushort)c.GetProperty("f17").GetInt32()).CopyTo(r, 0x17);
            string kind = c.GetProperty("kind").GetString()!; var log = new List<byte>(); byte before14 = r[0x14]; var world = new MageTestWorld(log);
            byte al = kind switch
            {
                "mage" => OriginalMageCloak.Step(world, r, OriginalMageCloak.FirstMageLow, OriginalMageCloak.FirstMageHigh, false, 0x55),
                "rufin" or "onis" or "irvil" or "garik" or "kochet" => OriginalMageCloak.Step(world, r, OriginalMageCloak.RufinLow, OriginalMageCloak.RufinHigh, true, 0x55),
                "magus" => OriginalMageCloak.Step(world, r, OriginalMageCloak.RufinLow, OriginalMageCloak.RufinHigh, true, 0x55, 0x1f),
                _ => OriginalMageCloak.FlagStep(world, r, 0x55),
            };
            if (r[0x14] != before14) requests++; if (r[0x20] == 0 && r[0x32] != 0) hidden++; if (log.Count > 0) drops++;
            byte[] output = [.. r, al, .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; GD.Print($"CLOAK MISMATCH {count}"); }
            count++;
        }
        GD.Print($"Cloak: first mismatch {firstMismatch}; requests {requests}, hidden {hidden}, drops {drops}");
        var summary = root.GetProperty("results").GetProperty("cloak");
        Check(count == summary.GetProperty("case_count").GetInt32() && requests > 200 && hidden > 200 && drops > 100, $"Cloak reference coverage ({count}: {requests} requests, {hidden} hidden, {drops} drops)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "The mages' vanishing step ARMY__03.WRK D3D / 79 matches original x86");
    }
}
