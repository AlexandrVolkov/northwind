using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class Army01EffectReplay(JsonElement c, List<byte> log) : IArmy01EffectWorld
    {
        private readonly List<(bool Zf, uint Eax, uint Ebx)> svc = [.. c.GetProperty("svc").EnumerateArray().Select(e => (e[0].GetInt32() != 0, e[1].GetUInt32(), e[2].GetUInt32()))];
        public byte[] Found = new byte[0x50], Template = new byte[0x60];
        public byte Wind;

        public void Service(int slot, BuildingRegs g)
        {
            log.Add((byte)'S'); log.AddRange(BitConverter.GetBytes((ushort)slot)); log.AddRange(BitConverter.GetBytes(g.Eax)); log.AddRange(BitConverter.GetBytes(g.Ebx)); log.AddRange(BitConverter.GetBytes(g.Ecx));
            var (zf, eax, ebx) = svc.Count > 0 ? svc[0] : (true, 0u, 0u); if (svc.Count > 0) svc.RemoveAt(0);
            g.Eax = eax; g.Ebx = ebx; g.Zf = zf;
        }
        public byte[] Parent = new byte[0x40];
        public byte[] ObjectAt(uint rel) => rel == 0xb00 ? Found : rel == 0xa00 ? Parent : new byte[0x50];
        public byte[] Template71(uint rel) { log.Add((byte)'T'); log.AddRange(BitConverter.GetBytes(rel)); return Template; }
        public byte StateByte() => Wind;
        public void Sound35(uint eax) { log.Add((byte)'Z'); log.AddRange(BitConverter.GetBytes(eax)); }
    }

    /// <summary>Replays tests/fixtures/army01-effects.json (tools/verify_army01_effects.py): the effect scripts of ARMY__01.WRK 10ED..1357.</summary>
    private void OriginalArmy01EffectChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/army01-effects.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var dc) ? dc : -1;
        int count = 0, firstMismatch = -1, steps = 0; var seen = new HashSet<int>();
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var r = new byte[0x50];
            r[0] = (byte)c.GetProperty("f0").GetInt32(); r[2] = (byte)c.GetProperty("f2").GetInt32(); BitConverter.GetBytes((ushort)c.GetProperty("f3").GetInt32()).CopyTo(r, 3); r[5] = (byte)c.GetProperty("f5").GetInt32(); r[6] = (byte)c.GetProperty("f6").GetInt32();
            BitConverter.GetBytes(c.GetProperty("x").GetUInt32()).CopyTo(r, 7); BitConverter.GetBytes(c.GetProperty("y").GetUInt32()).CopyTo(r, 0xc); r[0x16] = (byte)c.GetProperty("f16").GetInt32(); r[0x17] = (byte)c.GetProperty("f17").GetInt32();
            BitConverter.GetBytes(c.GetProperty("f18").GetUInt32()).CopyTo(r, 0x18); BitConverter.GetBytes(c.GetProperty("f1c").GetUInt32()).CopyTo(r, 0x1c); BitConverter.GetBytes(c.GetProperty("f12").GetUInt32()).CopyTo(r, 0x12);
            var log = new List<byte>(); var world = new Army01EffectReplay(c, log);
            BitConverter.GetBytes((ushort)c.GetProperty("o1").GetInt32()).CopyTo(world.Found, 1); BitConverter.GetBytes((ushort)c.GetProperty("o17").GetInt32()).CopyTo(world.Found, 0x17); world.Found[0x19] = (byte)c.GetProperty("o19").GetInt32();
            BitConverter.GetBytes((ushort)c.GetProperty("t48").GetInt32()).CopyTo(world.Template, 0x48); world.Template[0x4a] = (byte)c.GetProperty("t4a").GetInt32(); world.Template[0x4b] = (byte)c.GetProperty("t4b").GetInt32(); world.Wind = (byte)c.GetProperty("wind").GetInt32();
            var parent = new byte[0x40]; BitConverter.GetBytes((ushort)c.GetProperty("p1").GetInt32()).CopyTo(parent, 1); parent[3] = (byte)c.GetProperty("p3").GetInt32(); BitConverter.GetBytes((ushort)c.GetProperty("p17").GetInt32()).CopyTo(parent, 0x17); parent[0x25] = (byte)c.GetProperty("p25").GetInt32();
            int ah = c.GetProperty("ah").GetInt32(), k = c.GetProperty("entry").GetInt32(); seen.Add(k); if (ah == 1) steps++;
            world.Parent = parent; if (k >= 11) OriginalArmy02Effects.Run(world, r, ah, k - 11, parent, 0xa00); else OriginalArmy01Effects.Run(world, r, ah, k, parent, 0xa00);
            byte[] output = [.. r, .. world.Found, .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"ARMY__01 effects: first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("unit");
        Check(count == summary.GetProperty("case_count").GetInt32() && seen.Count == 28 && steps > 500, $"ARMY__01 effect reference coverage ({count} cases, {steps} steps)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "ARMY__01.WRK effect scripts match original x86");
    }
}
