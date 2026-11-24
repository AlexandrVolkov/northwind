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
        public byte[] Obj1 = Constrain(ActionPrng(c.GetProperty("o1").GetUInt32(), 0x50)), Obj2 = Constrain(ActionPrng(c.GetProperty("o2").GetUInt32(), 0x50)), Scratch = new byte[0x50];
        private static byte[] Constrain(byte[] b) { b[0] %= 3; b[0xe] = 0; foreach (int k in new[] { 4, 8 }) { b[k + 1] &= 3; b[k + 2] = 0; b[k + 3] = 0; } return b; }
        public byte[] Stats { get; } = ActionPrng(unchecked(c.GetProperty("map").GetUInt32() + 9), 8 * 0x1a35);
        public byte[] Template { get; } = ActionPrng(c.GetProperty("tm").GetUInt32(), 0x100);
        public uint PoolBase => 0x400000;
        public uint G512 { get; set; } = c.GetProperty("g512").GetUInt32();
        public uint G51A => c.GetProperty("g51a").GetUInt32();
        public uint G51E => c.GetProperty("g51e").GetUInt32();
        public byte G1C9F { get; set; } = (byte)c.GetProperty("g1c9f").GetInt32();
        public uint G50E { get; set; }
        public byte[] Record = [];
        public uint PlanePointer(uint k) => 0x404000 + 0x1000 * k;
        public uint PlaneRead(uint a) { uint k = (a - 0x404000) / 0x1000, cell = (a - 0x404000) % 0x1000 / 4; return (ActionPrng(unchecked(c.GetProperty("map").GetUInt32() + k), 1024)[cell] % 3) switch { 0 => 0u, 1 => 0x900u, _ => 0xa00u }; }
        public uint ArmyTable(byte army) => army <= 2 ? 0x400c00u : 0;
        public byte TableByte(uint a) { uint i = a - 0x600900; if (i >= 512) return 0xff; var tv = ActionPrng(c.GetProperty("tab").GetUInt32(), 256); uint seed = c.GetProperty("tab").GetUInt32(); return (i & 1) == 0 ? (byte)(tv[i / 2] & 0x7f) : (byte)((i / 2 * 167 + (seed & 0xff)) & 0xff); }
        public byte[] Ds { get; } = new byte[0x10000];
        public byte Fs13D { get; set; } = (byte)c.GetProperty("fs13d").GetInt32();
        public byte G525 => (byte)c.GetProperty("g525").GetInt32(); public byte G526 => (byte)c.GetProperty("g526").GetInt32();
        public byte G1C61 { get => Ds[0x1c61]; set => Ds[0x1c61] = value; }
        public uint G2730 => c.GetProperty("g2730").GetUInt32();
        public byte[] Route { get; } = ActionPrng(c.GetProperty("rt").GetUInt32(), 0x800);
        public uint TileDescriptor(uint tile) => (uint)(ActionPrng(c.GetProperty("map").GetUInt32(), 1024)[tile] % 8);
        public byte G1CCB; public byte[] Part = [];
        private int Nch => c.TryGetProperty("nch", out var v) ? (c.GetProperty("fn").GetString() == "fb6f1" ? Math.Max(v.GetInt32(), 1) : v.GetInt32()) : 0;
        private int St => c.TryGetProperty("st", out var v) ? Math.Min(v.GetInt32(), Nch) : 0;
        private readonly Dictionary<byte, byte[]> parts = [];
        public byte[] PartOf(byte owner)
        {
            if (owner >= 8) return new byte[0xf5];
            if (parts.TryGetValue(owner, out var cached)) return cached;
            var b = ActionPrng(unchecked(c.GetProperty("pt").GetUInt32() + owner), 0xf5);
            if (c.TryGetProperty("nch", out _)) { BitConverter.GetBytes((uint)(0x8000 + 0x60 * Nch)).CopyTo(b, 9); BitConverter.GetBytes((uint)(0x8000 + 0x60 * St)).CopyTo(b, 0xe); }
            parts[owner] = b; return b;
        }
        private byte[] Chain(int k) { var b = Constrain(ActionPrng(unchecked(c.GetProperty("o1").GetUInt32() + 7 + (uint)k), 0x50)); BitConverter.GetBytes(0x60u).CopyTo(b, 0x10); if ((b[0x1c] & 1) != 0) b[0x1b] = (byte)c.GetProperty("regs")[1].GetUInt32(); if (c.GetProperty("fn").GetString() == "fb319") { b[0x19] = 0x7f; BitConverter.GetBytes((uint)(900 + b[0x1d] % 120)).CopyTo(b, 4); BitConverter.GetBytes((uint)(850 + b[0x1e] % 100)).CopyTo(b, 8); } if (c.GetProperty("fn").GetString() == "fb5d8" && (b[0x1c] & 2) != 0) b[0x1b] = (byte)(1 + b[0x1d] % (Nch + 1)); return b; }
        public void SetG1CCB(int index, byte value) => G1CCB = value;
        public byte G522 => (byte)c.GetProperty("g522").GetInt32();
        private static uint Rel(uint v) => v >= 0x400000 && v < 0x402000 ? v - 0x400000 : v;
        public byte[] ObjectAt(uint p) => p == 0x400900 ? Obj1 : p == 0x400a00 ? Obj2 : p == 0x400c00 ? Template : (p >= 0x408000 && (p - 0x408000) % 0x60 == 0 && (p - 0x408000) / 0x60 < (uint)Nch) ? Chain((int)((p - 0x408000) / 0x60)) : Scratch;
        public void Call(uint address, ActionRegs g)
        {
            if (address == 0x68bd) { OriginalUnitActions.F68BD(this, g); return; }
            if (address == 0x6885) { OriginalUnitActions.F6885(this, g, Record); return; }
            log.Add((byte)'C'); log.AddRange(BitConverter.GetBytes((ushort)address)); foreach (var v in new[] { g.Eax, g.Ebx, g.Ecx, g.Edx, Rel(g.Edi), Rel(g.Esi) }) log.AddRange(BitConverter.GetBytes(v));
            if (address == 0x68bd) { OriginalUnitActions.F68BD(this, g); return; }
            if (address == 0x6885) { OriginalUnitActions.F6885(this, g, Record); return; }
            bool objEcx = address == 0xbb26;
            bool objEdi = address == 0xa758 || address == 0xac63 || (address == 0xc4cd && (c.GetProperty("fn").GetString() is "fcb77" or "fcc7b"));
            bool tmplEdx = address == 0xc4cd || address == 0x982b;
            var (zf, ax, bx, cx, dx, di) = calls.Count > 0 ? calls[0] : (true, 0u, 0u, 0u, 0u, 0u); if (calls.Count > 0) calls.RemoveAt(0);
            g.Eax = ax; g.Ebx = bx; g.Ecx = cx; g.Edx = tmplEdx ? 0x400c00 : dx; g.Edi = di; g.Zf = zf; if (address == 0xbadf) g.Edx = 0x401900 + 4 * ((ax >> 8) & 0xff); if (address == 0xd1ba && c.GetProperty("fn").GetString() == "fcb77") g.Esi = 0x400a00; if (objEdi) g.Edi = (di & 1) != 0 ? 0x400900u : 0x400a00u; if (objEcx) g.Ecx = (cx & 1) != 0 ? 0x400900u : 0x400a00u;
        }
    }

    /// <summary>Replays tests/fixtures/unit-actions.json (tools/verify_unit_actions.py): the unit action handlers of LE:1 8E3C..902F.</summary>
    private static void Put32(byte[] b, int o, uint v) => BitConverter.GetBytes(v).CopyTo(b, o);
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
            var r = ActionPrng(c.GetProperty("r").GetUInt32(), 0x50); r[0] %= 3; r[0xe] = 0; r[3] = (byte)c.GetProperty("f3").GetInt32(); BitConverter.GetBytes(c.GetProperty("px").GetUInt32()).CopyTo(r, 4); BitConverter.GetBytes(c.GetProperty("py").GetUInt32()).CopyTo(r, 8); BitConverter.GetBytes(c.GetProperty("cur").GetUInt32()).CopyTo(r, 0x28); r[0x14] = (byte)c.GetProperty("f14").GetInt32(); r[0x19] = (byte)c.GetProperty("f19").GetInt32(); r[0x15] = (byte)c.GetProperty("f15").GetInt32(); r[0x31] = (byte)c.GetProperty("f31").GetInt32(); r[0x30] = (byte)c.GetProperty("f30").GetInt32(); Array.Resize(ref r, 0x80); if (c.TryGetProperty("f22", out var f22)) r[0x22] = (byte)f22.GetInt32(); if (c.TryGetProperty("f23", out var f23)) { r[0x23] = (byte)f23.GetInt32(); BitConverter.GetBytes(c.GetProperty("f24").GetUInt32()).CopyTo(r, 0x24); }
            BitConverter.GetBytes((ushort)c.GetProperty("f17").GetInt32()).CopyTo(r, 0x17); BitConverter.GetBytes(c.GetProperty("f1c").GetUInt32()).CopyTo(r, 0x1c);
            var log = new List<byte>(); var w = new UnitActionReplay(c, log); if (fn == "fcb77") { w.Obj1[3] %= 3; w.Obj2[3] %= 3; } { w.Ds[0x273f] = (byte)c.GetProperty("g273").GetInt32(); w.Ds[0x2740] = (byte)c.GetProperty("g274").GetInt32(); uint pt = c.GetProperty("pt").GetUInt32(); var lst = new List<uint>(); for (int k = 0; k < (int)(pt % 7); k++) lst.Add(((pt >> k) & 1) != 0 ? 0x400900u : 0x400a00u); lst.Add(0); for (int k = 0; k < lst.Count; k++) BitConverter.GetBytes(lst[k]).CopyTo(w.Ds, 0x1f45 + 4 * k); } { var dsb = ActionPrng(c.GetProperty("ds").GetUInt32(), 0xe8); int at = 0; foreach (var (a, n) in new[] { (0x19c0, 0x40), (0x1c30, 0x40), (0x4160, 0x10), (0x9080, 0x10), (0x1978, 0x48) }) { Array.Copy(dsb, at, w.Ds, a, n); at += n; } BitConverter.GetBytes(0x400c00u).CopyTo(w.Ds, 0x1c49); if (c.TryGetProperty("z19", out _)) w.Ds[0x19ea] = 0; if (fn == "fcccb") Array.Copy(ActionPrng(c.GetProperty("ds").GetUInt32() + 5, 0x1090), 0, w.Ds, 0x1f70, 0x1090); w.Ds[0x18de] = 32; BitConverter.GetBytes(0x40a000u).CopyTo(w.Ds, 0x6ba); w.Ds[0x18e2] = 32; w.G1C61 = (byte)c.GetProperty("g1c61").GetInt32(); }
            var regs = c.GetProperty("regs").EnumerateArray().Select(e => e.GetUInt32()).ToArray();
            w.Record = r; if (fn is "fd182" or "fd19c") BitConverter.GetBytes(0x10030u).CopyTo(w.Template, 0x3b); var g = new ActionRegs { Eax = regs[0], Ebx = regs[1], Ecx = regs[2], Edx = regs[3], Edi = regs[4] };
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
                case "fa10b": OriginalUnitActions.FA10B(w, g); break;
                case "fda45": OriginalUnitActions.FDA45(w, g); break;
                case "fd740": OriginalUnitActions.FD740(w, g); break;
                case "fd77a": g.Edi = 0x400900; OriginalUnitActions.FD77A(w, g); break;
                case "f8659": OriginalUnitActions.F8659(w, g, r); break;
                case "fbb8b": g.Edx = 0x100; OriginalUnitActions.FBB8B(w, g, r, 0x600800); break;
                case "f6877": OriginalUnitActions.F6877(w, g, r); break;
                case "f6885": OriginalUnitActions.F6885(w, g, r); break;
                case "f68a0": g.Eax = (regs[0] & 0xffff00ffu) | ((((regs[0] >> 8) & 0xffu) % 3) << 8); OriginalUnitActions.F68A0(w, g); break;
                case "f68bd": g.Edi = 0x400a00; OriginalUnitActions.F68BD(w, g); break;
                case "fb6f1":
                    {
                        g.Esi = 0x408000; g.Ebp = (uint)(0x408000 + 0x60 * Math.Max(c.GetProperty("nch").GetInt32(), 1)); int sel = c.GetProperty("sel").GetInt32(); var tb = ActionPrng(c.GetProperty("tm").GetUInt32(), 0x100);
                        for (int k = 0; k < 6; k++) if (((sel >> (k % 2)) & 1) != 0) w.Ds[0x1978 + 0xc * k + 7] = tb[0x12 + 8 * k];
                        w.Ds[0x1c87] = (byte)c.GetProperty("regs")[1].GetUInt32(); OriginalUnitActions.FB6F1(w, g); break;
                    }
                case "f6eb2": OriginalUnitActions.F6EB2(w, g); break;
                case "f6ec1":
                    {
                        BitConverter.GetBytes(0x400900u).CopyTo(w.Ds, 0x1969);
                        for (int k = 0; k < 6; k++) Put32(w.Template, 0x0d + 8 * k, BitConverter.ToUInt32(w.Template, 0x0d + 8 * k) % 0x30);
                        OriginalUnitActions.F6EC1(w, g); break;
                    }
                case "fb319": if (c.GetProperty("sel").GetInt32() != 0) { w.Template[0x46] = 5; w.Template[0x4e] = 0x10; w.Template[0x40] |= 8; } OriginalUnitActions.FB319(w, g, r); break;
                case "fcccb": OriginalUnitActions.FCCCB(w, g, r); break;
                case "fcb21": OriginalUnitActions.FCB21(w, g, r); break;
                case "fcb77": OriginalUnitActions.FCB77(w, g, r); break;
                case "fcc7b": OriginalUnitActions.FCC7B(w, g, r); break;
                case "fccc4": OriginalUnitActions.FCCC4(g, r); break;
                case "fbe6f": OriginalUnitActions.FBE6F(w, g, r); break;
                case "fbe2e":
                case "fc0f6":
                    {
                        if (fn == "fc0f6") g.Edi = 0x400c00; g.Eax = regs[0] & 0xffffff00u; var tb = w.Template; var pb = w.PartOf(r[3]); int sel = c.GetProperty("sel").GetInt32();
                        foreach (int k in new[] { 0x7c, 0x80, 0x84, 0x88 }) if ((sel & 1) != 0) Put32(tb, k, BitConverter.ToUInt32(tb, k) & 0xff);
                        foreach (int k in new[] { 0x18, 0x1c, 0x20, 0x24 }) if ((sel & 2) != 0) Put32(pb, k, BitConverter.ToUInt32(pb, k) | 0x10000);
                        if (fn == "fbe2e") OriginalUnitActions.FBE2E(w, g, r); else OriginalUnitActions.FC0F6(w, g, r); break;
                    }
                case "fb5ab": OriginalUnitActions.FB5AB(w, g, r); break;
                case "fb616":
                    {
                        uint r2 = regs[2], r3 = regs[3]; int sel = c.GetProperty("sel").GetInt32();
                        g.Ecx = (r2 & 0xffff0000u) | (r2 & 31) | (((r2 >> 8) & 31) << 8); g.Edx = (r3 & 0xffff0000u) | (r3 & 31) | (((r3 >> 8) & 31) << 8);
                        g.Eax = (regs[0] & 0xffffff00u) | ((sel & 1) != 0 ? w.Obj1[3] : (sel & 2) != 0 ? w.Obj2[3] : regs[0] & 0xffu);
                        OriginalUnitActions.FB616(w, g); break;
                    }
                case "fb5d8": g.Eax = (regs[0] & 0xffffff00u) | (regs[0] & 7); OriginalUnitActions.FB5D8(w, g); break;
                case "fb9eb": g.Eax = (regs[0] & 0xffffff00u) | (regs[0] & 7); OriginalUnitActions.FB9EB(w, g); break;
                case "fba2e": g.Eax = (regs[0] & 0xffffff00u) | (regs[0] & 7); OriginalUnitActions.FBA2E(w, g); break;
                case "fbc15": OriginalUnitActions.FBC15(w, g, 0x600800); break;
                case "fc439": g.Ebx = (uint)c.GetProperty("bm").GetInt64(); g.Eax = c.GetProperty("sel").GetInt32() != 0 ? (BitConverter.ToUInt32(w.Template, 0x40) & g.Ebx) : regs[0]; OriginalUnitActions.FC439(w, g, r); break;
                case "fc4cd": g.Edi = 0x400900; OriginalUnitActions.FC4CD(w, g, r); break;
                case "fcaee": OriginalUnitActions.FCAEE(w, g, r); break;
                case "fd12c": OriginalUnitActions.FD12C(w, g, r); break;
                case "fd157": g.Edi = 0x400a00; OriginalUnitActions.FD157(w, g); break;
                case "fd182": OriginalUnitActions.FD182(w, g, r); break;
                case "fd19c": g.Eax = regs[0]; g.Ebx = (((regs[1] >> 8) & 0xffu) % 3) << 8; OriginalUnitActions.FD19C(w, g); break;
                case "fc331": OriginalUnitActions.FC331(g); break;
                case "fc344": g.Edi = 0x400900; OriginalUnitActions.FC344(w, g, r); break;
                case "fc35f": g.Edx = regs[3] & 3; OriginalUnitActions.FC35F(w, g, r); break;
                case "f9a8e": OriginalUnitActions.F9A8E(w, g, r); break;
                case "f7fa7": OriginalUnitActions.F7FA7(w, g, r); break;
                case "f902f": OriginalUnitActions.F902F(w, g, r); break;
                case "f81d9": OriginalUnitActions.F81D9(w, g, r); break;
            }
            uint Rel(uint v) => v >= 0x400000 && v < 0x402000 ? v - 0x400000 : v;
            byte[] output = [.. r, .. w.Obj1, .. w.Obj2, w.G1C9F, .. BitConverter.GetBytes(w.G512), .. BitConverter.GetBytes(w.G50E), w.G1CCB, w.G525, w.G526, w.G1C61, .. w.Ds[0x19c0..0x1a00], .. w.Ds[0x1c30..0x1c70], .. w.Ds[0x4160..0x4170], .. w.Ds[0x9080..0x9090], .. w.Ds[0x1978..0x19c0], w.Fs13D, .. w.Stats[0x170..0x1b0], .. w.Stats[(0x1a35 + 0x170)..(0x1a35 + 0x1b0)], .. w.Stats[(2 * 0x1a35 + 0x170)..(2 * 0x1a35 + 0x1b0)], .. Enumerable.Range(0, 8).SelectMany(k => w.Stats[(k * 0x1a35 + 0x70)..(k * 0x1a35 + 0x80)]), .. w.Ds[0x1ca2..0x1cc2], .. w.Ds[0x1f0a..0x1f45], .. BitConverter.GetBytes(Rel(g.Eax)), .. BitConverter.GetBytes(Rel(g.Ebx)), .. BitConverter.GetBytes(Rel(g.Ecx)), .. BitConverter.GetBytes(Rel(g.Edx)), .. BitConverter.GetBytes(Rel(g.Edi)),
                g.Zf ? (byte)1 : (byte)0, g.Cf ? (byte)1 : (byte)0, .. w.PartOf(r[3]), .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Unit actions: first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("actions");
        Check(count == summary.GetProperty("case_count").GetInt32() && seen.Count == 73, $"Unit action reference coverage ({count} cases, {seen.Count} functions)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Unit action handlers match original x86");
    }
}
