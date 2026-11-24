using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class BuildingScriptReplay(JsonElement c, List<byte> log) : IBuildingScriptWorld
    {
        private readonly List<(bool Zf, uint Eax, uint Ebx)> svc = [.. c.GetProperty("svc").EnumerateArray().Select(e => (e[0].GetInt32() != 0, e[1].GetUInt32(), e[2].GetUInt32()))];
        public byte[] Part = new byte[0xf5], Tmpl = new byte[0x100];

        public void Service(int slot, BuildingRegs g)
        {
            log.Add((byte)'S'); log.AddRange(BitConverter.GetBytes((ushort)slot)); log.AddRange(BitConverter.GetBytes(g.Eax)); log.AddRange(BitConverter.GetBytes(g.Ebx)); log.AddRange(BitConverter.GetBytes(g.Ecx)); log.AddRange(BitConverter.GetBytes(g.Edx));
            if (slot is 0x19 or 0x1d) return;
            var (zf, eax, ebx) = svc.Count > 0 ? svc[0] : (true, 0u, 0u); if (svc.Count > 0) svc.RemoveAt(0);
            g.Eax = eax; g.Ebx = ebx; g.Zf = zf;
        }
        public (byte[] Participant, byte[] Template, uint Pointer) Participant6D() { log.Add((byte)'P'); return (Part, Tmpl, 0xd00); }
        public byte[] DispatchTemplate() => Tmpl;
        public byte[] Big = new byte[0x1a14];
        public byte[] Service1B7(byte al) { log.Add((byte)'Q'); log.Add(al); return Big; }
        public byte[] Found = new byte[0x50];
        public (bool Zf, byte[] Object, uint Flags) Service79() { log.Add((byte)'N'); var z = svc.Count > 0 ? svc[0].Zf : true; if (svc.Count > 0) svc.RemoveAt(0); return (z, Found, c.GetProperty("ftf").GetUInt32()); }
        public uint TemplateFlags71() { log.Add((byte)'T'); return c.GetProperty("tflags").GetUInt32(); }
        public void BeforeHandler(int state, uint ebx, uint handler, uint ecx) { log.Add((byte)'C'); log.Add((byte)state); log.AddRange(BitConverter.GetBytes(ebx)); log.AddRange(BitConverter.GetBytes(handler)); log.AddRange(BitConverter.GetBytes(ecx)); }
        public bool AfterHandler(bool handlerZf) { log.Add((byte)'H'); log.Add(handlerZf ? (byte)1 : (byte)0); return c.GetProperty("zf").GetInt32() != 0; }
    }

    /// <summary>Replays tests/fixtures/building-scripts.json (tools/verify_building_scripts.py): the building scripts of ARMY__01.WRK.</summary>
    private void OriginalBuildingScriptChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/building-scripts.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var dc) ? dc : -1;
        int count = 0, firstMismatch = -1, steps = 0; var handlers = new HashSet<int>();
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var r = new byte[0x50];
            r[0] = (byte)c.GetProperty("f0").GetInt32(); r[3] = (byte)c.GetProperty("f3").GetInt32(); r[0x14] = (byte)c.GetProperty("f14").GetInt32(); r[0x0f] = (byte)c.GetProperty("f0f").GetInt32(); r[0x22] = (byte)c.GetProperty("f22").GetInt32();
            r[0x26] = (byte)c.GetProperty("f26").GetInt32(); r[0x27] = (byte)c.GetProperty("f27").GetInt32(); BitConverter.GetBytes((ushort)c.GetProperty("f17").GetInt32()).CopyTo(r, 0x17); r[0x19] = (byte)c.GetProperty("f19").GetInt32(); r[0x1a] = (byte)c.GetProperty("f1a").GetInt32();
            BitConverter.GetBytes(c.GetProperty("x").GetUInt32()).CopyTo(r, 4); BitConverter.GetBytes(c.GetProperty("y").GetUInt32()).CopyTo(r, 8);
            var log = new List<byte>(); var world = new BuildingScriptReplay(c, log);
            world.Tmpl[0x79] = (byte)c.GetProperty("t79").GetInt32(); world.Tmpl[0x45] = (byte)c.GetProperty("t45").GetInt32(); world.Tmpl[0x46] = (byte)c.GetProperty("t46").GetInt32(); BitConverter.GetBytes(c.GetProperty("t78").GetUInt32()).CopyTo(world.Tmpl, 0x78); BitConverter.GetBytes(c.GetProperty("p2c").GetUInt32()).CopyTo(world.Part, 0x2c);
            r[0x23] = (byte)c.GetProperty("f23").GetInt32(); r[0x24] = (byte)c.GetProperty("f24").GetInt32(); BitConverter.GetBytes(c.GetProperty("p1c").GetUInt32()).CopyTo(world.Part, 0x1c);
            world.Found[0x31] = (byte)c.GetProperty("fo31").GetInt32(); BitConverter.GetBytes((ushort)c.GetProperty("fo17").GetInt32()).CopyTo(world.Found, 0x17);
            BitConverter.GetBytes(c.GetProperty("e1a10").GetUInt32()).CopyTo(world.Big, 0x1a10);
            var other = new byte[0x50]; other[3] = (byte)c.GetProperty("o3").GetInt32();
            int ah = c.GetProperty("ah").GetInt32(), k = c.GetProperty("entry").GetInt32(); if (ah == 1) { steps++; handlers.Add(k * 8 + (r[0x14] & 7)); }
            uint result = OriginalBuildingScripts.Run(world, r, ah, (byte)c.GetProperty("bl").GetInt32(), k, other, out bool rzf);
            byte[] output = [.. r, .. world.Part[0x28..0x30], (byte)c.GetProperty("seed").GetInt32(), .. (ah == 6 ? BitConverter.GetBytes(result) : []), .. (ah == 5 ? new[] { (byte)result, rzf ? (byte)1 : (byte)0 } : []), .. world.Found, .. world.Part[0x18..0x20], .. world.Big[0x1a10..0x1a14], .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Building scripts: first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("unit");
        Check(count == summary.GetProperty("case_count").GetInt32() && steps > 500 && handlers.Count >= 20, $"Building script reference coverage ({count} cases, {steps} steps, {handlers.Count} handlers)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Building scripts of ARMY__01.WRK match original x86");
    }
}
