using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private static byte[] ActionPrng(uint seed, int n)
    {
        var o = new byte[n]; uint x = seed;
        for (int i = 0; i < n; i++) { x = unchecked(x * 1664525 + 1013904223); o[i] = (byte)(x >> 24); }
        return o;
    }

    private sealed class UnitActionReplay(JsonElement c, List<byte> log) : IUnitActionWorld
    {
        private readonly List<(bool Zf, uint Ax, uint Bx, uint Cx, uint Dx, uint Di)> calls = [.. c.GetProperty("calls").EnumerateArray().Select(e => (e[0].GetInt32() != 0, e[1].GetUInt32(), e[2].GetUInt32(), e[3].GetUInt32(), e[4].GetUInt32(), e[5].GetUInt32()))];
        public byte[] Obj1 = ActionPrng(c.GetProperty("o1").GetUInt32(), 0x50), Obj2 = ActionPrng(c.GetProperty("o2").GetUInt32(), 0x50), Scratch = new byte[0x50];
        public byte[] Template { get; } = ActionPrng(c.GetProperty("tm").GetUInt32(), 0x100);
        public uint PoolBase => 0x400000;
        public uint G512 { get; set; } = c.GetProperty("g512").GetUInt32();
        public uint G51A => c.GetProperty("g51a").GetUInt32();
        public uint G51E => c.GetProperty("g51e").GetUInt32();
        public byte G1C9F { get; set; } = (byte)c.GetProperty("g1c9f").GetInt32();
        public uint G50E { get; set; }
        public byte G1CCB; public byte[] Part = [];
        public byte[] PartOf(byte owner) => Part;
        public void SetG1CCB(int index, byte value) => G1CCB = value;
        public byte G522 => (byte)c.GetProperty("g522").GetInt32();
        private static uint Rel(uint v) => v >= 0x400000 && v < 0x402000 ? v - 0x400000 : v;
        public byte[] ObjectAt(uint p) => p == 0x400900 ? Obj1 : p == 0x400a00 ? Obj2 : Scratch;
        public void Call(uint address, ActionRegs g)
        {
            log.Add((byte)'C'); log.AddRange(BitConverter.GetBytes((ushort)address)); foreach (var v in new[] { g.Eax, g.Ebx, g.Ecx, g.Edx, Rel(g.Edi), Rel(g.Esi) }) log.AddRange(BitConverter.GetBytes(v));
            if (address == 0x68bd) { g.Edx = 0x400c00; return; }
            bool tmplEdx = address == 0xc4cd;
            var (zf, ax, bx, cx, dx, di) = calls.Count > 0 ? calls[0] : (true, 0u, 0u, 0u, 0u, 0u); if (calls.Count > 0) calls.RemoveAt(0);
            g.Eax = ax; g.Ebx = bx; g.Ecx = cx; g.Edx = tmplEdx ? 0x400c00 : dx; g.Edi = di; g.Zf = zf;
        }
    }

    /// <summary>Replays tests/fixtures/unit-actions.json (tools/verify_unit_actions.py): the unit action handlers of LE:1 8E3C..902F.</summary>
    private void OriginalUnitActionChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/unit-actions.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var dc) ? dc : -1;
        int count = 0, firstMismatch = -1; var seen = new HashSet<string>();
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            string fn = c.GetProperty("fn").GetString()!; seen.Add(fn);
            var r = ActionPrng(c.GetProperty("r").GetUInt32(), 0x50); r[3] = (byte)c.GetProperty("f3").GetInt32(); r[0x14] = (byte)c.GetProperty("f14").GetInt32(); r[0x19] = (byte)c.GetProperty("f19").GetInt32(); r[0x15] = (byte)c.GetProperty("f15").GetInt32(); r[0x31] = (byte)c.GetProperty("f31").GetInt32(); r[0x30] = (byte)c.GetProperty("f30").GetInt32();
            BitConverter.GetBytes((ushort)c.GetProperty("f17").GetInt32()).CopyTo(r, 0x17); BitConverter.GetBytes(c.GetProperty("f1c").GetUInt32()).CopyTo(r, 0x1c);
            var log = new List<byte>(); var w = new UnitActionReplay(c, log); w.Part = ActionPrng(c.GetProperty("pt").GetUInt32(), 0xf5);
            var regs = c.GetProperty("regs").EnumerateArray().Select(e => e.GetUInt32()).ToArray();
            var g = new ActionRegs { Eax = regs[0], Ebx = regs[1], Ecx = regs[2], Edx = regs[3], Edi = regs[4] };
            switch (fn)
            {
                case "f8e3c": OriginalUnitActions.F8E3C(w, g, r); break;
                case "f8e56": OriginalUnitActions.F8E56(w, g, r); break;
                case "f8e81": OriginalUnitActions.F8E81(w, g, r); break;
                case "f8ec7": OriginalUnitActions.F8EC7(w, g, r); break;
                case "f8f70": OriginalUnitActions.F8F70(w, g, r); break;
                case "f8227": OriginalUnitActions.F8227(w, g, r, 0x6007e8, [0x10030, 0x10040, 0x10050, 0x10060, 0x10070, 0x10080]); break;
                case "f8273": OriginalUnitActions.F8273(w, g, r); break;
                case "f82c3": OriginalUnitActions.F82C3(w, g, r); break;
                case "f833f": OriginalUnitActions.F833F(w, g, r); break;
                case "f839f": OriginalUnitActions.F839F(w, g, r); break;
                case "f83d9": OriginalUnitActions.F83D9(w, g, r); break;
                case "f843e": OriginalUnitActions.F843E(w, g, r); break;
                case "f877f": OriginalUnitActions.F877F(w, g, r); break;
                case "f878d": OriginalUnitActions.F878D(w, g, r); break;
                case "f881b": OriginalUnitActions.F881B(w, g, r); break;
                case "f8917": OriginalUnitActions.F8917(w, g, r); break;
                case "f89f1": OriginalUnitActions.F89F1(w, g, r); break;
                case "f8a54": OriginalUnitActions.F8A54(w, g, r); break;
                case "f8a7f": OriginalUnitActions.F8A7F(w, g, r); break;
                case "f8a9f": OriginalUnitActions.F8A9F(w, g, r); break;
                case "f8abf": OriginalUnitActions.F8ABF(w, g, r); break;
                case "f8af4": OriginalUnitActions.F8AF4(w, g, r); break;
                case "f8b0f": OriginalUnitActions.F8B0F(w, g, r); break;
                case "f8b37": OriginalUnitActions.F8B37(w, g, r); break;
                case "f8b4c": OriginalUnitActions.F8B4C(w, g, r); break;
                case "f8bc7": OriginalUnitActions.F8BC7(w, g, r); break;
                case "f8bea": OriginalUnitActions.F8BEA(w, g, r); break;
                case "f8c47": OriginalUnitActions.F8C47(w, g, r); break;
                case "f8c89": OriginalUnitActions.F8C89(w, g, r); break;
                case "f8cf3": OriginalUnitActions.F8CF3(w, g, r); break;
                case "f84aa": OriginalUnitActions.F84AA(w, g, r); break;
            }
            uint Rel(uint v) => v >= 0x400000 && v < 0x402000 ? v - 0x400000 : v;
            byte[] output = [.. r, .. w.Obj1, .. w.Obj2, w.G1C9F, .. BitConverter.GetBytes(w.G512), .. BitConverter.GetBytes(w.G50E), w.G1CCB, .. BitConverter.GetBytes(Rel(g.Eax)), .. BitConverter.GetBytes(Rel(g.Ebx)), .. BitConverter.GetBytes(Rel(g.Ecx)), .. BitConverter.GetBytes(Rel(g.Edx)), .. BitConverter.GetBytes(Rel(g.Edi)),
                g.Zf ? (byte)1 : (byte)0, g.Cf ? (byte)1 : (byte)0, .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Unit actions: first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("actions");
        Check(count == summary.GetProperty("case_count").GetInt32() && seen.Count == 31, $"Unit action reference coverage ({count} cases, {seen.Count} functions)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Unit action handlers match original x86");
    }
}
