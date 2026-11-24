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
            log.Add((byte)'S'); log.AddRange(BitConverter.GetBytes((ushort)slot)); log.AddRange(BitConverter.GetBytes(Rel(g.Eax))); log.AddRange(BitConverter.GetBytes(Rel(g.Ebx))); log.AddRange(BitConverter.GetBytes(Rel(g.Ecx))); log.AddRange(BitConverter.GetBytes(Rel(g.Esi)));
            var (zf, eax, ebx) = svc.Count > 0 ? svc[0] : (true, 0u, 0u); if (svc.Count > 0) svc.RemoveAt(0);
            g.Eax = eax; g.Ebx = ebx; g.Zf = zf;
        }
        public byte[] Parent = new byte[0x40];
        public byte[] ObjectAt(uint rel) => rel == 0xb00 ? Found : rel == 0xa00 ? Parent : new byte[0x50];
        public byte[] Template71(uint rel) { log.Add((byte)'T'); log.AddRange(BitConverter.GetBytes(rel)); return Template; }
        private static uint Rel(uint v) => v >= 0x400000 && v < 0x402000 ? v - 0x400000 : v;
        public byte[] TemplateRaw => Template;
        byte[] IArmy01EffectWorld.Found => Found;
        public uint Limit0 => c.GetProperty("dim0").GetUInt32();
        public uint Limit4 => c.GetProperty("dim4").GetUInt32();
        private readonly List<int> r10d = [.. c.GetProperty("r10d").EnumerateArray().Select(e => e.GetInt32())];
        public byte Index = 0x31;
        public static byte[] RandomTable = [];
        public byte Random() => RandomTable[Index++];
        public byte Random10D() { int v = r10d.Count > 0 ? r10d[0] : 0; if (r10d.Count > 0) r10d.RemoveAt(0); log.Add((byte)'R'); log.Add((byte)v); return (byte)v; }
        public bool Effect19(uint ebx, uint ecx, uint edx, uint a, uint b, uint cc)
        {
            log.Add((byte)'W'); foreach (var v in new[] { Rel(ebx), Rel(ecx), Rel(edx), a, b, cc }) log.AddRange(BitConverter.GetBytes(v));
            bool zf = svc.Count > 0 ? svc[0].Zf : true; if (svc.Count > 0) svc.RemoveAt(0); return zf;
        }
        public void Jump(uint target, uint eax, uint ebx) { log.Add((byte)'J'); log.AddRange(BitConverter.GetBytes(target)); log.AddRange(BitConverter.GetBytes(eax)); log.AddRange(BitConverter.GetBytes(Rel(ebx))); }
        public byte StateByte() => Wind;
        public void Sound35(uint eax) { log.Add((byte)'Z'); log.AddRange(BitConverter.GetBytes(eax)); }
    }

    /// <summary>Replays tests/fixtures/army01-effects.json (tools/verify_army01_effects.py): the effect scripts of ARMY__01.WRK 10ED..1357.</summary>
    private void OriginalArmy01EffectChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/army01-effects.json");
        var root = reference.RootElement;
        Army01EffectReplay.RandomTable = root.GetProperty("random_values").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
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
            BitConverter.GetBytes(c.GetProperty("t40").GetUInt32()).CopyTo(world.Template, 0x40); world.Template[0x45] = (byte)c.GetProperty("t45").GetInt32(); BitConverter.GetBytes((ushort)c.GetProperty("t48").GetInt32()).CopyTo(world.Template, 0x48); world.Template[0x4a] = (byte)c.GetProperty("t4a").GetInt32(); world.Template[0x4b] = (byte)c.GetProperty("t4b").GetInt32(); world.Wind = (byte)c.GetProperty("wind").GetInt32();
            var parent = new byte[0x40]; BitConverter.GetBytes((ushort)c.GetProperty("p1").GetInt32()).CopyTo(parent, 1); parent[3] = (byte)c.GetProperty("p3").GetInt32(); BitConverter.GetBytes((ushort)c.GetProperty("p17").GetInt32()).CopyTo(parent, 0x17); parent[0x25] = (byte)c.GetProperty("p25").GetInt32();
            int ah = c.GetProperty("ah").GetInt32(), k = c.GetProperty("entry").GetInt32(); seen.Add(k); if (ah == 1) steps++;
            world.Parent = parent; if (k >= 28) OriginalArmy00Effects.Run(world, r, ah, k - 28, parent, 0xa00, (uint)c.GetProperty("bl7").GetInt32());
            else if (k >= 11) OriginalArmy02Effects.Run(world, r, ah, k - 11, parent, 0xa00); else OriginalArmy01Effects.Run(world, r, ah, k, parent, 0xa00);
            byte[] output = [.. r, .. world.Found, .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"ARMY__01 effects: first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("unit");
        Check(count == summary.GetProperty("case_count").GetInt32() && seen.Count == 36 && steps > 500, $"ARMY__01 effect reference coverage ({count} cases, {steps} steps)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "ARMY__01.WRK effect scripts match original x86");
    }
}
