using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/spot.json (tools/verify_spot.py): the search of a free place next to an object, LE:1 9064.</summary>
    private void OriginalSpotChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/spot.json");
        var root = reference.RootElement;
        var random = root.GetProperty("random_values").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, found = 0, none = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            byte index = (byte)c.GetProperty("seed").GetInt32(); var free = c.GetProperty("free").EnumerateArray().Select(e => e.GetInt32() != 0).ToArray();
            var newTemplate = new byte[0xab]; newTemplate[3] = (byte)c.GetProperty("new").GetProperty("w").GetInt32(); newTemplate[4] = (byte)c.GetProperty("new").GetProperty("h").GetInt32(); newTemplate[0x77] = (byte)c.GetProperty("new").GetProperty("layer").GetInt32();
            var oldTemplate = new byte[0xab]; oldTemplate[3] = (byte)c.GetProperty("old").GetProperty("w").GetInt32(); oldTemplate[4] = (byte)c.GetProperty("old").GetProperty("h").GetInt32();
            // the harness makes the target's template differ from the new one when they coincide; both are filled with the 'old' sizes except the new template
            var target = new byte[0x40]; BitConverter.GetBytes(c.GetProperty("x").GetUInt32()).CopyTo(target, 4); BitConverter.GetBytes(c.GetProperty("y").GetUInt32()).CopyTo(target, 8);
            var log = new List<byte>(); int calls = 0; byte template = (byte)c.GetProperty("template").GetInt32();
            var (ok, direction, layer, x, y) = OriginalSpotFinder.Find(target, newTemplate, oldTemplate, () => random[index++], (l, px, py) =>
            {
                log.Add((byte)'P'); log.Add(l); log.AddRange(BitConverter.GetBytes(px)); log.AddRange(BitConverter.GetBytes(py)); log.Add(template);
                return free[calls++ % 8];
            });
            // result registers as the original leaves them: EAX the template and army, BX = BH·256 + layer on success (BH = the direction) or the entry BH 0 · 256 + layer, ECX/EDX the last point
            byte army = (byte)c.GetProperty("army").GetInt32();
            ushort bx = (ushort)((ok ? direction : (0x6ca + 0x56 * army) >> 8 & 255) << 8 | layer);   // none found: BH still holds the high byte of the army's table pointer left by 68A0
            byte[] output = [.. BitConverter.GetBytes((uint)(template | army << 8)), .. BitConverter.GetBytes((uint)bx), .. BitConverter.GetBytes(x), .. BitConverter.GetBytes(y), (byte)(ok ? 0 : 1), .. log];
            if (ok) found++; else none++;
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; GD.Print($"SPOT MISMATCH {count}"); }
            count++;
        }
        GD.Print($"Spot: first mismatch {firstMismatch}; found {found}, none {none}");
        var summary = root.GetProperty("results").GetProperty("spot");
        Check(count == summary.GetProperty("case_count").GetInt32() && found > 500 && none > 100, $"Spot reference coverage ({count}: {found} found, {none} none)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Free place next to an object LE:1 9064 matches original x86");
    }
}
