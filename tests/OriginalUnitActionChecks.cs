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
        public uint G512 { get => BitConverter.ToUInt32(Ds, 0x512); set => BitConverter.GetBytes(value).CopyTo(Ds, 0x512); }
        public uint G51A => BitConverter.ToUInt32(Ds, 0x51a);
        public uint G51E => BitConverter.ToUInt32(Ds, 0x51e);
        public byte G1C9F { get => Ds[0x1c9f]; set => Ds[0x1c9f] = value; }
        public uint G50E { get => BitConverter.ToUInt32(Ds, 0x50e); set => BitConverter.GetBytes(value).CopyTo(Ds, 0x50e); }
        public byte[] Record = [];
        public uint PlanePointer(uint k) => 0x404000 + 0x1000 * k;
        private readonly Dictionary<uint, uint> planeWrites = [];
        public void PlaneWrite(uint a, uint v) => planeWrites[a] = v;
        public uint PlaneRead(uint a) { if (planeWrites.TryGetValue(a, out var wv)) return wv; uint k = (a - 0x404000) / 0x1000, cell = (a - 0x404000) % 0x1000 / 4; if (c.GetProperty("fn").GetString() == "fde0c" && (c.GetProperty("sel").GetInt32() & 1) != 0) return ActionPrng(unchecked(c.GetProperty("map").GetUInt32() + k), 1024)[cell] % 16 == 0 ? 0x900u : 0u; return (ActionPrng(unchecked(c.GetProperty("map").GetUInt32() + k), 1024)[cell] % 3) switch { 0 => 0u, 1 => 0x900u, _ => 0xa00u }; }
        public uint ArmyTable(byte army) => army <= 2 ? 0x400c00u : 0;
        public byte TableByte(uint a) { uint i = a - 0x600900; if (i >= 512) return 0xff; var tv = ActionPrng(c.GetProperty("tab").GetUInt32(), 256); uint seed = c.GetProperty("tab").GetUInt32(); return (i & 1) == 0 ? (byte)(tv[i / 2] & 0x7f) : (byte)((i / 2 * 167 + (seed & 0xff)) & 0xff); }
        public byte[] Ds { get; } = new byte[0x10000];
        public byte Fs13D { get; set; } = (byte)c.GetProperty("fs13d").GetInt32();
        public uint Fs17F { get; set; }
        public byte[] Text { get; } = new byte[0x1000];   // flat 402200..4031FF: the string (+0), the font (+200), the destination (+600)
        public byte Peek(uint a) => a >= 0x402200 && a < 0x403200 ? Text[a - 0x402200] : (byte)0;
        public void Poke(uint a, byte v) { if (a >= 0x402200 && a < 0x403200) Text[a - 0x402200] = v; }
        public byte G525 => Ds[0x525]; public byte G526 => Ds[0x526];
        public byte G1C61 { get => Ds[0x1c61]; set => Ds[0x1c61] = value; }
        public uint G2730 => c.GetProperty("g2730").GetUInt32();
        public byte[] Route { get; } = ActionPrng(c.GetProperty("rt").GetUInt32(), 0x800);
        private byte[]? segment;
        public uint TileProperty(uint cell, int offset)
        {
            if (segment == null)
            {
                segment = ActionPrng(c.GetProperty("map").GetUInt32() + 3, 8192);
                if (c.GetProperty("fn").GetString() is "fde0c" or "fce9d" or "fcf07" or "fcf41" && (c.GetProperty("sel").GetInt32() & 2) != 0) for (int t = 0; t < 1024; t++) segment[t * 8 + 2] = segment[t * 8 + 3] = (c.GetProperty("sel").GetInt32() & 1) == 0 ? (byte)((segment[t * 8 + 2] & 15) * 17) : segment[t * 8 + 2];
            }
            return BitConverter.ToUInt32(segment, (int)(cell * 8) + offset);
        }
        public uint TileDescriptor(uint tile) => (uint)(ActionPrng(c.GetProperty("map").GetUInt32(), 1024)[tile] % 8);
        public byte G1CCB => Ds[0x1ccb + 0x16 * Record[3]]; public byte[] Part = [];
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
        public void SetG1CCB(int index, byte value) => Ds[0x1ccb + index] = value;
        public byte G522 => Ds[0x522];
        private static uint Rel(uint v) => v >= 0x400000 && v < 0x402000 ? v - 0x400000 : v;
        public byte[] ObjectAt(uint p) => p == 0x402000 ? BitConverter.GetBytes(new[] { 0u, 0x900u, 0xa00u }[c.GetProperty("sel").GetInt32()]).Concat(new byte[0x40]).ToArray() : p == 0x400900 ? Obj1 : p == 0x400a00 ? Obj2 : p == 0x400c00 ? Template : (p >= 0x408000 && (p - 0x408000) % 0x60 == 0 && (p - 0x408000) / 0x60 < (uint)Nch) ? Chain((int)((p - 0x408000) / 0x60)) : Scratch;
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
            if (address == 0x9e41) { g.Eax = ax; g.Zf = zf; return; }
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
            var log = new List<byte>(); var w = new UnitActionReplay(c, log); if (fn == "fcb77") { w.Obj1[3] %= 3; w.Obj2[3] %= 3; } { w.Ds[0x273f] = (byte)c.GetProperty("g273").GetInt32(); w.Ds[0x2740] = (byte)c.GetProperty("g274").GetInt32(); uint pt = c.GetProperty("pt").GetUInt32(); var lst = new List<uint>(); for (int k = 0; k < (int)(pt % 7); k++) lst.Add(((pt >> k) & 1) != 0 ? 0x400900u : 0x400a00u); lst.Add(0); for (int k = 0; k < lst.Count; k++) BitConverter.GetBytes(lst[k]).CopyTo(w.Ds, 0x1f45 + 4 * k); } { var dsb = ActionPrng(c.GetProperty("ds").GetUInt32(), 0x2bc); int at = 0; foreach (var (a, n) in new[] { (0x19c0, 0x40), (0x1c30, 0x50), (0x4160, 0x10), (0x9080, 0x10), (0x1978, 0x48), (0x1c80, 0x50), (0x1ec0, 0x10), (0x1960, 0x18), (0x9090, 0x10), (0x1904, 0x10), (0x33c, 0x100), (0x640, 0xc), (0x1940, 0x20) }) { Array.Copy(dsb, at, w.Ds, a, n); at += n; } BitConverter.GetBytes(0x400c00u).CopyTo(w.Ds, 0x1c49); if (c.TryGetProperty("z19", out _)) w.Ds[0x19ea] = 0; if (fn == "fcccb") Array.Copy(ActionPrng(c.GetProperty("ds").GetUInt32() + 5, 0x1090), 0, w.Ds, 0x1f70, 0x1090); BitConverter.GetBytes(0xe00000u).CopyTo(w.Ds, 0x18ee); BitConverter.GetBytes(0xf00000u).CopyTo(w.Ds, 0x18fe); w.Ds[0x18de] = 32; w.Ds[0x18e7] = 4; w.Ds[0x18eb] = 4; BitConverter.GetBytes(0x40a000u).CopyTo(w.Ds, 0x6ba); w.Ds[0x18e2] = 32; BitConverter.GetBytes(c.GetProperty("g512").GetUInt32()).CopyTo(w.Ds, 0x512); BitConverter.GetBytes(c.GetProperty("g51a").GetUInt32()).CopyTo(w.Ds, 0x51a); BitConverter.GetBytes(c.GetProperty("g51e").GetUInt32()).CopyTo(w.Ds, 0x51e); w.Ds[0x522] = (byte)c.GetProperty("g522").GetInt32(); w.Ds[0x525] = (byte)c.GetProperty("g525").GetInt32(); w.Ds[0x526] = (byte)c.GetProperty("g526").GetInt32(); Array.Clear(w.Ds, 0x50e, 4); w.Ds[0x1c9f] = (byte)c.GetProperty("g1c9f").GetInt32(); w.G1C61 = (byte)c.GetProperty("g1c61").GetInt32(); }
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
                case "fda45": g.Eax = (regs[0] & 0xffffff00u) | (regs[0] & 7); OriginalUnitActions.FDA45(w, g); break;
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
                case "f7012": case "f704f": case "f7069":
                    {
                        var txt = Convert.FromHexString(c.GetProperty("txt").GetString()!); var font = Convert.FromHexString(c.GetProperty("font").GetString()!);
                        for (int i = 0; i < 256; i++) { font[2 * i] = (byte)(0x200 + (i % 8) * 4); font[2 * i + 1] = (byte)((0x200 + (i % 8) * 4) >> 8); }
                        Array.Copy(txt, 0, w.Text, 0, txt.Length); Array.Copy(font, 0, w.Text, 0x200, font.Length); BitConverter.GetBytes(0x402400u).CopyTo(w.Ds, 0x19ee);
                        g.Esi = 0x402200; var pr = w.PartOf(0); for (int ow = 0; ow < 8; ow++) w.PartOf((byte)ow)[0xa4 + (ow * 3 + c.GetProperty("nch").GetInt32()) % 30] = 0xff; _ = pr;
                        if (fn == "f7012") OriginalUnitActions.F7012(w, g); else if (fn == "f704f") OriginalUnitActions.F704F(w, g); else OriginalUnitActions.F7069(w, g);
                        break;
                    }
                case "f9bf9": case "f9c57":
                    {
                        void Wd(int a, uint v, int n) { for (int k = 0; k < n; k++) w.Ds[a + k] = (byte)(v >> (8 * k)); }
                        Wd(0x1c35, c.GetProperty("w35").GetUInt32(), 2); Wd(0x1cad, c.GetProperty("d1cad").GetUInt32(), 4); Wd(0x1c4e, c.GetProperty("b1c4e").GetUInt32(), 1); Wd(0x1ca9, c.GetProperty("d1ca9").GetUInt32(), 4);
                        Wd(0x19e9, c.GetProperty("b19e9").GetUInt32(), 1); Wd(0x9090, c.GetProperty("b9090").GetUInt32(), 1); Wd(0x9091, c.GetProperty("b9091").GetUInt32(), 1);
                        var lst = Convert.FromHexString(c.GetProperty("lst").GetString()!); w.Ds[0x1cb4] = 0; Array.Copy(lst, 0, w.Ds, 0x1cb5, lst.Length); Wd(0x1972, c.GetProperty("b1972").GetUInt32(), 1); Wd(0x1cb1, c.GetProperty("d1cb1").GetUInt32(), 4);
                        int ff = c.GetProperty("ff").GetInt32(); byte[] tab = ff % 4 != 0 ? ActionPrng(c.GetProperty("ds").GetUInt32() + 40, 0x16 * 9).Select(b => b != 0xff ? b : (byte)0xfe).ToArray() : [.. Enumerable.Repeat((byte)0xff, 0x16 * 4), .. new byte[0x16 * 5]];
                        Array.Fill(w.Ds, (byte)0, 0x1ccb, 0x16 * 9 + 4); Array.Copy(tab, 0, w.Ds, 0x1ccb, tab.Length); w.Fs17F = 0;
                        if (fn == "f9bf9") OriginalUnitActions.F9BF9(w, g); else OriginalUnitActions.F9C57(w, g);
                        break;
                    }
                case "fa320": OriginalUnitActions.FA320(w, g); break;
                case "fa38a": OriginalUnitActions.FA38A(w, g, r); break;
                case "fa416": OriginalUnitActions.FA416(w, g, r); break;
                case "fa444": OriginalUnitActions.FA444(w, g, r); break;
                case "fa47e": r[1] %= 3; OriginalUnitActions.FA47E(w, g, r); break;
                case "fa4a9": OriginalUnitActions.FA4A9(w, g); break;
                case "fce9d": case "fcf07": case "fcf41":
                    BitConverter.GetBytes(1024u).CopyTo(w.Ds, 0x1902);
                    if (fn == "fce9d") OriginalUnitActions.FCE9D(w, g); else if (fn == "fcf07") OriginalUnitActions.FCF07(w, g); else OriginalUnitActions.FCF41(w, g);
                    break;
                case "f6377": case "f6361": case "f63cb": case "f63f7": case "f640b": case "f6423": case "f643c": case "f645b": case "f66bb": case "f66df": case "f67bd": case "f67c0": case "f67d3":
                    {
                        int sel = c.GetProperty("sel").GetInt32(); var wi = Convert.FromHexString(c.GetProperty("wi").GetString()!); var ob = w.Obj1; if (fn != "f645b") wi[3] %= 8;
                        BitConverter.GetBytes(0x400900u).CopyTo(w.Ds, 0x1969); BitConverter.GetBytes(c.GetProperty("cad").GetUInt32()).CopyTo(w.Ds, 0x1cad);
                        if (fn is "f6377" or "f6361")
                        {
                            wi[2] = 0; wi[0x14] %= 3; ob[3] %= 8; if (fn == "f6361" && (sel & 1) != 0) wi[0x1a] = 1;
                            if ((sel & 2) != 0) w.Template[0x40] |= 8; else w.Template[0x40] &= 0xf7;
                            if ((sel & 1) != 0) for (int k = 0x7c; k < 0x8c; k++) w.Template[k] = k % 4 == 0 ? (byte)(w.Template[k] % 4) : (byte)0;
                            if ((sel & 1) != 0) { var pp = w.PartOf(ob[3]); for (int k = 0x18; k < 0x28; k++) pp[k] = (byte)(k % 4 == 0 ? 100 : 0); }
                        }
                        if (fn == "f63cb" && (sel & 1) != 0) wi[0x14] = ob[0x19];
                        if (fn == "f63f7" && (sel & 1) != 0) wi[0x14] = ob[0x16];
                        if (fn == "f640b") { wi[0x1a] %= 0x40; if ((sel & 1) != 0) wi[0x14] = ob[wi[0x1a]]; }
                        if (fn == "f6423" && (sel & 1) != 0) wi[0x14] = ob[0x27];
                        if (fn == "f643c")
                        {
                            BitConverter.GetBytes(0x400900u).CopyTo(wi, 0x15); if ((sel & 1) != 0) wi[0x14] = 1;
                            if ((sel & 2) != 0) BitConverter.GetBytes(BitConverter.ToUInt32(ob, 0) & (wi[0x14] == 1 ? 0xffu : 0xffffffu)).CopyTo(wi, 0x1a);
                        }
                        if (fn == "f645b") { wi[0x1a] %= 0x40; wi[3] = new byte[] { 0, 1, 3, 3 }[sel]; wi[0x12] = (byte)(0x7f + wi[0x12] % 0x7e); wi[0x15] %= 130; }
                        if (fn is "f67bd" or "f67c0" or "f67d3")
                        {
                            uint r2 = regs[2], r3 = regs[3]; BitConverter.GetBytes(r2 % 12).CopyTo(w.Ds, 0x1906); BitConverter.GetBytes(r3 % 12).CopyTo(w.Ds, 0x190a);
                            BitConverter.GetBytes(0x402e00u).CopyTo(w.Ds, 0x18da); Array.Copy(ActionPrng(c.GetProperty("ds").GetUInt32() + 50, 1024), 0, w.Text, 0xc00, 1024);
                        }
                        Array.Copy(wi, 0, r, 0, 0x30);
                        switch (fn)
                        {
                            case "f6377": OriginalUnitActions.F6377(w, g, r); break;
                            case "f6361": OriginalUnitActions.F6361(w, g, r); break;
                            case "f63cb": OriginalUnitActions.F63CB(w, g, r); break;
                            case "f63f7": OriginalUnitActions.F63F7(w, g, r); break;
                            case "f640b": OriginalUnitActions.F640B(w, g, r); break;
                            case "f6423": OriginalUnitActions.F6423(w, g, r); break;
                            case "f643c": OriginalUnitActions.F643C(w, g, r); break;
                            case "f645b": OriginalUnitActions.F645B(w, g, r); break;
                            case "f66bb": OriginalUnitActions.F66BB(w, g, r); break;
                            case "f66df": OriginalUnitActions.F66DF(w, g, r); break;
                            case "f67bd": OriginalUnitActions.F67BD(w, g); break;
                            case "f67c0": OriginalUnitActions.F67C0(w, g); break;
                            case "f67d3": OriginalUnitActions.F67D3(w, g); break;
                        }
                        break;
                    }
                case "f4409": OriginalUnitActions.F4409(w, g, r); break;
                case "fdb47": OriginalUnitActions.FDB47(w, g, r); break;
                case "fdbae": OriginalUnitActions.FDBAE(w, g, r); break;
                case "fdc91":
                    {
                        w.Template[0x45] = (byte)(1 + w.Template[0x45] % 200); w.Template[0x4a] = (byte)(w.Template[0x4a] % (w.Template[0x45] + 1)); w.Template[0x51] %= 3; w.Template[0x52] %= 3; w.Template[0x4c] %= 0x30; int sel = c.GetProperty("sel").GetInt32();
                        if ((sel & 1) != 0) r[0x1a] = r[0x19];
                        if ((sel & 2) != 0) { r[0x19] = (byte)(1 + r[0x19] % 100); r[0x1a] = 0; }
                        OriginalUnitActions.FDC91(w, g, r); break;
                    }
                case "fde0c": BitConverter.GetBytes(1024u).CopyTo(w.Ds, 0x1902); OriginalUnitActions.FDE0C(w, g); break;
                case "fd2d1":
                    {
                        int sel = c.GetProperty("sel").GetInt32(); uint r1 = regs[1], r2 = regs[2], r3 = regs[3];
                        g.Eax = new[] { 0x900u, 0xa00u, 0u, 0x900u }[sel]; g.Ebx = new[] { 0xa00u, 0x900u, r3, 0u }[(sel + r2) & 3]; g.Ecx = r2; g.Edx = r3;
                        OriginalUnitActions.FD2D1(w, g, r); break;
                    }
                case "fs14b": if (c.GetProperty("sel").GetInt32() != 0) { w.Obj1[0x20] = 0xff; w.Obj1[0x19] = 0x7f; w.Obj1[3] %= 8; w.Obj2[0x20] = 0xff; w.Obj2[0x19] = 0x7f; w.Obj2[3] %= 8; w.Template[0x46] = 5; w.Template[0x74] = 0xf; w.Template[0x40] &= 0xdf; w.Template[0x41] &= 0x7f; } OriginalUnitActions.FS14B(w, g, r); break;
                case "fs187": g.Eax = (regs[0] & 0xffff0000u) | ((((regs[0] >> 8) & 0xffu) % 3) << 8); OriginalUnitActions.FS187(w, g); break;
                case "fs16b": g.Eax = (regs[0] & 0xffff0000u) | ((((regs[0] >> 8) & 0xffu) % 3) << 8); OriginalUnitActions.FS16B(w, g, r); break;
                case "fs18b":
                    {
                        w.Ds[0x3e87] = 0; w.Ds[0x3e88] = 0x50; w.Ds[0x6c9] = (byte)(1 + c.GetProperty("sel").GetInt32() % 4); w.Ds[0x6c6] = (byte)(1 + (c.GetProperty("g273").GetInt32() % 4));
                        var a1 = ActionPrng(c.GetProperty("ds").GetUInt32() + 11, 0x3000); var a2 = ActionPrng(c.GetProperty("ds").GetUInt32() + 12, 0x3000);
                        for (int k = 0; k < 0x3000; k++) w.Ds[0x5000 + k] = (byte)((k % 3 == 0 ? a1[k] | a2[k] : a1[k] & a2[k]) & (k % 0x84 == 0x59 || k % 0x84 == 0x17 ? 0 : 0xff));
                        OriginalUnitActions.FS18B(w, g); break;
                    }
                case "f7a03": { var q = ActionPrng(c.GetProperty("ds").GetUInt32() + 13, 8); w.Ds[0x274e] = q[0]; w.Ds[0x274f] = q[1]; w.Ds[0x2791] = q[2]; w.Ds[0x2792] = q[3]; OriginalUnitActions.F7A03(w, g); break; }
                case "fae3a": OriginalUnitActions.FAE3A(w, g, r); break;
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
            byte[] output = [.. r, .. w.Obj1, .. w.Obj2, w.G1C9F, .. BitConverter.GetBytes(w.G512), .. BitConverter.GetBytes(w.G50E), w.G1CCB, w.G525, w.G526, w.G1C61, .. w.Ds[0x19c0..0x1a00], .. w.Ds[0x1c30..0x1c80], .. w.Ds[0x4160..0x4170], .. w.Ds[0x9080..0x9090], .. w.Ds[0x1978..0x19c0], .. w.Ds[0x1c80..0x1cd0], .. w.Ds[0x1ec0..0x1ed0], .. w.Ds[0x1960..0x1978], .. w.Ds[0x9090..0x90a0], .. w.Ds[0x1904..0x1914], .. w.Ds[0x33c..0x43c], .. w.Ds[0x640..0x64c], .. w.Ds[0x1940..0x1960], w.Fs13D, .. w.Stats[0x170..0x1b0], .. w.Stats[(0x1a35 + 0x170)..(0x1a35 + 0x1b0)], .. w.Stats[(2 * 0x1a35 + 0x170)..(2 * 0x1a35 + 0x1b0)], .. Enumerable.Range(0, 8).SelectMany(k => w.Stats[(k * 0x1a35 + 0x70)..(k * 0x1a35 + 0x80)]), .. w.Ds[0x1ca2..0x1cc2], .. w.Ds[0x1f0a..0x2104], .. w.Ds[0x4c0..0x5d0], .. BitConverter.GetBytes(w.Fs17F), .. w.Text[0x600..0x700], .. BitConverter.GetBytes(Rel(g.Eax)), .. BitConverter.GetBytes(Rel(g.Ebx)), .. BitConverter.GetBytes(Rel(g.Ecx)), .. BitConverter.GetBytes(Rel(g.Edx)), .. BitConverter.GetBytes(Rel(g.Edi)),
                g.Zf ? (byte)1 : (byte)0, g.Cf ? (byte)1 : (byte)0, .. w.PartOf(r[3]), .. (fn == "fd2d1" ? Enumerable.Range(0, 4096).SelectMany(k => BitConverter.GetBytes(w.PlaneRead(0x404000u + (uint)(4 * k)))) : []), .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Unit actions: first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("actions");
        Check(count == summary.GetProperty("case_count").GetInt32() && seen.Count == 112, $"Unit action reference coverage ({count} cases, {seen.Count} functions)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Unit action handlers match original x86");
    }
}
