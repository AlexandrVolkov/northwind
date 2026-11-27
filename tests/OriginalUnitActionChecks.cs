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
        private byte[]? planeData;
        public uint PlaneRead(uint a) { if (planeWrites.TryGetValue(a, out var wv)) return wv; if (c.GetProperty("fn").GetString() is "fcd72" or "fcf77" or "fcfac" or "fd0d5") { planeData ??= Convert.FromHexString(c.GetProperty("plane").GetString()!); return BitConverter.ToUInt32(planeData, (int)(a - 0x404000)); } uint k = (a - 0x404000) / 0x1000, cell = (a - 0x404000) % 0x1000 / 4; if (c.GetProperty("fn").GetString() == "fde0c" && (c.GetProperty("sel").GetInt32() & 1) != 0) return ActionPrng(unchecked(c.GetProperty("map").GetUInt32() + k), 1024)[cell] % 16 == 0 ? 0x900u : 0u; return (ActionPrng(unchecked(c.GetProperty("map").GetUInt32() + k), 1024)[cell] % 3) switch { 0 => 0u, 1 => 0x900u, _ => 0xa00u }; }
        public uint ArmyTable(byte army) => army <= 2 ? 0x400c00u : 0;
        public byte TableByte(uint a) { uint i = a - 0x600900; if (i >= 512) return 0xff; var tv = ActionPrng(c.GetProperty("tab").GetUInt32(), 256); uint seed = c.GetProperty("tab").GetUInt32(); return (i & 1) == 0 ? (byte)(tv[i / 2] & 0x7f) : (byte)((i / 2 * 167 + (seed & 0xff)) & 0xff); }
        public byte[] Ds { get; } = new byte[0x10000];
        public byte Fs13D { get; set; } = (byte)c.GetProperty("fs13d").GetInt32();
        public byte Fs10 { get; set; }
        public uint Fs17F { get; set; }
        public byte[] L3 { get; } = new byte[0x4000];
        public void CallHandler(uint mode, ActionRegs g) => Call(0x20232, g);
        public byte[] Text { get; } = new byte[0x1000];   // flat 402200..4031FF: the string (+0), the font (+200), the destination (+600)
        public byte[] Video { get; } = InitVideo();
        private static byte[] InitVideo() { var v = new byte[0x80000]; Array.Fill(v, (byte)0x5a, 0, 0x10000); return v; }
        public byte Peek(uint a) => a >= 0xc00000 && a < 0xc80000 ? Video[a - 0xc00000] : a >= 0x402200 && a < 0x403200 ? Text[a - 0x402200] : a < (uint)Ds.Length ? Ds[a] : (byte)0;
        public void Poke(uint a, byte v) { if (a >= 0xc00000 && a < 0xc80000) Video[a - 0xc00000] = v; else if (a >= 0x402200 && a < 0x403200) Text[a - 0x402200] = v; else if (a < (uint)Ds.Length) Ds[a] = v; }
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
                if (c.GetProperty("fn").GetString() is "fde0c" or "fcd72" or "fcf77" or "fcfac" or "fd0d5" or "fce9d" or "fcf07" or "fcf41" && (c.GetProperty("sel").GetInt32() & 2) != 0) for (int t = 0; t < 1024; t++) segment[t * 8 + 2] = segment[t * 8 + 3] = (c.GetProperty("sel").GetInt32() & 1) == 0 ? (byte)((segment[t * 8 + 2] & 15) * 17) : segment[t * 8 + 2];
                if (c.TryGetProperty("sel4", out var s4) && s4.GetInt32() != 0 && (c.GetProperty("sel").GetInt32() & 2) != 0) for (int t = 0; t < 1024; t++) segment[t * 8 + 3] = (byte)((segment[t * 8 + 2] & 15) | (segment[t * 8 + 3] & 0xf0));
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
        private readonly Dictionary<int, byte[]> chainCache = [];
        private byte[] Chain(int k) { if (chainCache.TryGetValue(k, out var hit)) return hit; var b = Constrain(ActionPrng(unchecked(c.GetProperty("o1").GetUInt32() + 7 + (uint)k), 0x50)); BitConverter.GetBytes(0x60u).CopyTo(b, 0x10); if ((b[0x1c] & 1) != 0) b[0x1b] = (byte)c.GetProperty("regs")[1].GetUInt32(); if (c.GetProperty("fn").GetString() == "fb319") { b[0x19] = 0x7f; BitConverter.GetBytes((uint)(900 + b[0x1d] % 120)).CopyTo(b, 4); BitConverter.GetBytes((uint)(850 + b[0x1e] % 100)).CopyTo(b, 8); } if (c.GetProperty("fn").GetString() == "fb5d8" && (b[0x1c] & 2) != 0) b[0x1b] = (byte)(1 + b[0x1d] % (Nch + 1)); if (c.GetProperty("fn").GetString() == "f59de") b[0xe] = 0; if (c.GetProperty("fn").GetString() == "f42bb") { b[0xe] = 0; BitConverter.GetBytes((b[0x1e] & 1) != 0 ? (uint)(0x8000 + 0x60 * (b[0x1d] % Math.Max(Nch, 1))) : 0u).CopyTo(b, 0x1c); } chainCache[k] = b; return b; }
        public void SetG1CCB(int index, byte value) => Ds[0x1ccb + index] = value;
        public byte G522 => Ds[0x522];
        private static uint Rel(uint v) => v >= 0x400000 && v < 0x402000 ? v - 0x400000 : v;
        public byte[] ObjectAt(uint p) => p == 0x400800 ? Record : p == 0x402000 ? BitConverter.GetBytes(new[] { 0u, 0x900u, 0xa00u }[c.GetProperty("sel").GetInt32()]).Concat(new byte[0x40]).ToArray() : p == 0x400900 ? Obj1 : p == 0x400a00 ? Obj2 : p == 0x400c00 ? Template : (p >= 0x408000 && (p - 0x408000) % 0x60 == 0 && (p - 0x408000) / 0x60 < (uint)Nch) ? Chain((int)((p - 0x408000) / 0x60)) : Scratch;
        public void Out(ushort port, byte value) { log.Add((byte)'O'); log.AddRange(BitConverter.GetBytes(port)); log.Add(value); }
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
            uint ownerIn = g.Eax & 255;
            var (zf, ax, bx, cx, dx, di) = calls.Count > 0 ? calls[0] : (true, 0u, 0u, 0u, 0u, 0u); if (calls.Count > 0) calls.RemoveAt(0);
            if (address == 0x9e41 || address == 0x9e55 || address == 0x9e69) { g.Eax = ax; g.Zf = zf; return; }
            g.Eax = ax; g.Ebx = bx; g.Ecx = cx; g.Edx = tmplEdx ? 0x400c00 : dx; if (address == 0x1ead8) g.Edx = 0x40a000u + 0x1a35u * ownerIn; if (address != 0x43b0) g.Edi = di; g.Zf = zf; if (address == 0xbadf) g.Edx = 0x401900 + 4 * ((ax >> 8) & 0xff); if (address == 0xd1ba && c.GetProperty("fn").GetString() == "fcb77") g.Esi = 0x400a00; if (objEdi) g.Edi = (di & 1) != 0 ? 0x400900u : 0x400a00u; if (objEcx) g.Ecx = (cx & 1) != 0 ? 0x400900u : 0x400a00u;
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
                case "fcd72": case "fcf77": case "fcfac": case "fd0d5":
                    BitConverter.GetBytes(1024u).CopyTo(w.Ds, 0x1902); Array.Copy(Convert.FromHexString(c.GetProperty("mask").GetString()!), 0, w.Ds, 0x7c0, 36);
                    BitConverter.GetBytes((uint)c.GetProperty("v06").GetInt32()).CopyTo(w.Ds, 0x1906); BitConverter.GetBytes((uint)c.GetProperty("v0a").GetInt32()).CopyTo(w.Ds, 0x190a); BitConverter.GetBytes(0x404000u).CopyTo(w.Ds, 0x18ee);
                    if (fn == "fcd72") OriginalUnitActions.FCD72(w, g); else if (fn == "fcf77") OriginalUnitActions.FCF77(w, g); else if (fn == "fd0d5") { w.Template[3] = (byte)c.GetProperty("tw").GetInt32(); w.Template[4] = (byte)c.GetProperty("th").GetInt32(); BitConverter.GetBytes((uint)c.GetProperty("px").GetInt32() * 32 + 5).CopyTo(w.Record, 4); BitConverter.GetBytes((uint)c.GetProperty("py").GetInt32() * 32 + 7).CopyTo(w.Record, 8); OriginalUnitActions.FD0D5(w, g); } else OriginalUnitActions.FCFAC(w, g); break;
                case "fce9d": case "fcf07": case "fcf41":
                    BitConverter.GetBytes(1024u).CopyTo(w.Ds, 0x1902);
                    if (fn == "fce9d") OriginalUnitActions.FCE9D(w, g); else if (fn == "fcf07") OriginalUnitActions.FCF07(w, g); else OriginalUnitActions.FCF41(w, g);
                    break;
                case "f6377": case "f6361": case "f63cb": case "f63f7": case "f640b": case "f6423": case "f643c": case "f645b": case "f66bb": case "f66df": case "f67bd": case "f67c0": case "f67d3": case "f616f": case "f6267": case "f6294": case "f64ad": case "f6562": case "f658a": case "f65df":
                    {
                        int sel = c.GetProperty("sel").GetInt32(); var wi = Convert.FromHexString(c.GetProperty("wi").GetString()!); var ob = w.Obj1; if (fn != "f645b") wi[3] %= 8;
                        BitConverter.GetBytes(0x400900u).CopyTo(w.Ds, 0x1969); BitConverter.GetBytes(c.GetProperty("cad").GetUInt32()).CopyTo(w.Ds, 0x1cad);
                        if (fn is "f6377" or "f6361" or "f6294")
                        {
                            wi[2] = 0; wi[0x14] %= 3; ob[3] %= 8; if (fn == "f6361" && (sel & 1) != 0) wi[0x1a] = 1; if (fn == "f6294") { wi[0x1a] = (byte)(wi[6] & 1); ob[0x40] = 0xff; if ((wi[5] & 1) != 0) ob[0x26] = 0xff; }
                            if ((sel & 2) != 0) w.Template[0x40] |= 8; else w.Template[0x40] &= 0xf7;
                            if ((sel & 1) != 0) for (int k = 0x7c; k < 0x8c; k++) w.Template[k] = k % 4 == 0 ? (byte)(w.Template[k] % 4) : (byte)0;
                            if ((sel & 1) != 0) { var pp = w.PartOf(ob[3]); for (int k = 0x18; k < 0x28; k++) pp[k] = (byte)(k % 4 == 0 ? 100 : 0); }
                        }
                        if (fn == "f616f")
                        {
                            wi[2] %= 6; if (sel != 3) wi[0x1a] = 0; int sl = 0x1978 + 12 * wi[2];
                            w.Ds[sl + 6] = new byte[] { 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0xb, 0xa }[wi[5] % 14];
                            if ((sel & 1) != 0) { w.Ds[sl + 9] = w.Ds[0x1c35]; w.Ds[sl + 10] = w.Ds[0x1c36]; } w.Ds[0x1c89] = (byte)(wi[9] & 1);
                        }
                        if (fn == "f64ad") wi[3] = (byte)(5 + wi[3] % 3);
                        if (fn is "f64ad" or "f6562" or "f658a")
                        {
                            for (int k = 0x22; k < 0x27; k++) ob[k] = (byte)((wi[k] & 1) != 0 ? 0xff : 0);
                            ob[0] = (byte)(sel % 3); if ((wi[5] & 1) != 0) ob[0xf] = 1;
                            if (fn == "f658a" && (wi[6] & 1) != 0) { int sm = 0xd; for (int k = 0x22; k < 0x27; k++) sm += ob[k]; wi[0x1a] = (byte)sm; }
                            if (fn == "f64ad")
                            {
                                g.Edi = 0x400800; Array.Clear(w.L3, 0x7d4, 0x604); for (int k = 0; k < c.GetProperty("qn").GetInt32(); k++) w.L3[0x7f2 + 0x1e * k + 1] = (byte)(k == 0 ? c.GetProperty("qid").GetInt32() : c.GetProperty("qid").GetInt32() + 1); BitConverter.GetBytes((uint)(0x7f2 + 0x1e * c.GetProperty("qn").GetInt32())).CopyTo(w.L3, 0xdce); w.L3[0] = (byte)c.GetProperty("l0q").GetInt32(); w.L3[1] = 0;
                            }
                        }
                        if (fn == "f65df")
                        {
                            int nch = c.GetProperty("nch").GetInt32(); int k = wi[0x18] % Math.Max(nch, 1); BitConverter.GetBytes((uint)(0x8000 + 0x60 * k)).CopyTo(wi, 0x18);
                            if ((wi[5] & 1) != 0) wi[2] = w.ObjectAt((uint)(0x408000 + 0x60 * k))[0x1b];
                            if ((wi[6] & 1) != 0) wi[0x1d] = w.Ds[0x1972];
                            w.L3[2] = (byte)(wi[7] % 3 / 2);
                            w.Ds[0x1c96] = wi[9];
                        }
                        if (fn == "f6267") { byte l2 = new byte[] { 0, 2, 7, 0xa, 0x10 }[wi[7] % 5]; if ((wi[8] & 2) != 0 && (wi[8] & 1) == 0) l2 = 0xa; w.L3[2] = l2; if ((wi[8] & 1) != 0) wi[0x1b] = l2; else if ((wi[8] & 2) != 0) wi[0x1b] = 7; }
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
                            case "f616f": OriginalUnitActions.F616F(w, g, r); break;
                            case "f6267": OriginalUnitActions.F6267(w, g, r); break;
                            case "f6294": OriginalUnitActions.F6294(w, g, r); break;
                            case "f64ad": OriginalUnitActions.F64AD(w, g); break;
                            case "f6562": OriginalUnitActions.F6562(w, g); break;
                            case "f658a": OriginalUnitActions.F658A(w, g, r); break;
                            case "f65df": OriginalUnitActions.F65DF(w, g, r); break;
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
                case "f12ae1": case "f12b29": case "f12ba3": case "f12bd8": case "f12bf2": case "f12bfb": case "f12c10": case "f12c48": case "f12c65": case "f12cb1": case "f12d08":
                    {
                        int sel = c.GetProperty("sel").GetInt32();
                        if ((sel & 1) != 0) { w.Template[0x72] = 0xff; w.Template[0x73] = (byte)(w.Template[0x73] % 4); w.Template[0x74] = 0xf; }
                        w.Template[0x4f] = (byte)(1 + w.Template[0x4f] % 6); w.Ds[0x522] = new byte[] { 3, 6, 1, (byte)c.GetProperty("g522").GetInt32() }[(sel >> 1) & 3];
                        BitConverter.GetBytes((sel & 2) != 0 ? c.GetProperty("g512").GetUInt32() : 0u).CopyTo(w.Ds, 0x512);
                        switch (fn)
                        {
                            case "f12ae1": OriginalUnitActions.F12AE1(w, g, r); break;
                            case "f12b29": OriginalUnitActions.F12B29(w, g, r); break;
                            case "f12ba3": OriginalUnitActions.F12BA3(w, g, r); break;
                            case "f12bd8": OriginalUnitActions.F12BD8(w, g, r); break;
                            case "f12bf2": OriginalUnitActions.F12BF2(w, g, r); break;
                            case "f12bfb": OriginalUnitActions.F12BFB(w, g, r); break;
                            case "f12c10": OriginalUnitActions.F12C10(w, g, r); break;
                            case "f12c48": OriginalUnitActions.F12C48(w, g, r); break;
                            case "f12c65": OriginalUnitActions.F12C65(w, g, r); break;
                            case "f12cb1": OriginalUnitActions.F12CB1(w, g, r); break;
                            case "f12d08": OriginalUnitActions.F12D08(w, g, r); break;
                        }
                        break;
                    }
                case "faab3": case "fac23":
                    {
                        var dd = c.GetProperty("deltas").EnumerateArray().Select(e => e.GetInt64()).ToArray();
                        if (fn == "faab3")
                        {
                            byte owner = (byte)(c.GetProperty("sel").GetInt32() % 8); w.Ds[0x1972] = owner; var pp = w.PartOf(owner);
                            int[] at = [0x18, 0x1c, 0x20], shown = [0x1c62, 0x1c66, 0x1c6a];
                            for (int k = 0; k < 3; k++) BitConverter.GetBytes(unchecked((uint)(BitConverter.ToUInt32(pp, at[k]) + dd[k]))).CopyTo(w.Ds, shown[k]);
                        }
                        for (int k = 0; k < 9; k++) BitConverter.GetBytes(new uint[] { 100000000, 10000000, 1000000, 100000, 10000, 1000, 100, 10, 1 }[k]).CopyTo(w.Ds, 0x19fe + 4 * k);
                        if (fn == "faab3") OriginalUnitActions.FAAB3(w, g); else OriginalUnitActions.FAC23(w, g);
                        break;
                    }
                case "f4580": case "f4672": case "f46f7": case "f478d": case "f486d": case "f4938": case "f4965": case "f4b03": case "f4b12": case "f4c46": case "f4d41": case "f4d50": case "f4e27": case "f4e40": case "f4fce": case "f500d": case "f50e5": case "f50f4": case "f51ac": case "fae57": case "faea8": case "f520b": case "f5297": case "f52a6": case "f538d": case "f53a8": case "f53c4": case "f53d3": case "f5679": case "f5573": case "f5580": case "f5593": case "f55b5": case "f55d1": case "f55e0": case "f566a":
                    {
                        var cu = c.GetProperty("cu"); int sel = c.GetProperty("sel").GetInt32(); byte owner = (byte)cu.GetProperty("own").GetInt32();
                        void Wd(int a, long v, int n) { for (int k = 0; k < n; k++) w.Ds[a + k] = (byte)(v >> (8 * k)); }
                        int I(string k) => cu.GetProperty(k).GetInt32();
                        Wd(0x1c60, I("c60"), 1); Wd(0x193a, I("b193a"), 1); Wd(0x194b, I("b194b"), 1); Wd(0x1c58, I("vx"), 4); Wd(0x1c5c, I("vy"), 4); Wd(0x194d, I("vx") + I("mx") % 20, 4); Wd(0x1951, I("vy") + I("my") % 20, 4);
                        Wd(0x1972, owner, 1); Wd(0x1971, I("b1971"), 1); Wd(0x1c89, I("g89"), 1); Wd(0x1c87, I("b87"), 1); Wd(0x1986, I("b86"), 1); Wd(0x198a, I("b8a"), 1); Wd(0x19c0, I("o19c0"), 1); Wd(0xed20, cu.GetProperty("ed20").GetInt64(), 4);
                        BitConverter.GetBytes((sel & 1) != 0 ? 0x400900u : 0x400a00u).CopyTo(w.Ds, 0x1969);
                        Array.Clear(w.L3, 0, 0x40); w.L3[0] = (byte)I("l0"); w.L3[0x12] = (byte)I("l12"); w.L3[0x13] = (byte)I("l13");
                        BitConverter.GetBytes(I("l5") == 0 ? 0u : I("l5") == 1 ? 0x400900u : 0x400a00u).CopyTo(w.L3, 5); BitConverter.GetBytes(0x400c00u).CopyTo(w.L3, 9); w.L3[0xd] = (byte)I("ld");
                        BitConverter.GetBytes(0u).CopyTo(w.Ds, 0x1906); BitConverter.GetBytes(0u).CopyTo(w.Ds, 0x190a);
                        BitConverter.GetBytes(0x402e00u).CopyTo(w.Ds, 0x18da); Array.Copy(ActionPrng(c.GetProperty("ds").GetUInt32() + 50, 1024), 0, w.Text, 0xc00, 1024);
                        if (fn is "f4b03" or "f4b12" or "f4c46" or "f4d41" or "f4d50" or "f4e27" or "f4e40" or "f4fce" or "f500d" or "f50e5" or "f50f4" or "f51ac" or "fae57" or "faea8" or "f520b" or "f5297" or "f52a6" or "f538d" or "f53a8" or "f53c4" or "f53d3" or "f5679" or "f5573" or "f5580" or "f5593" or "f55b5" or "f55d1" or "f55e0" or "f566a")
                        {
                            Wd(0x199e, I("v99e"), 1); Wd(0x19a2, I("v9a2"), 1); Wd(0x19aa, I("v9aa"), 1); Wd(0x19ae, I("v9ae"), 1); Wd(0x1992, I("v92"), 1); Wd(0x1996, I("v96"), 1); Wd(0x4c, I("c4c"), 1);
                            Wd(0x27f4, I("v27f4"), 1); Wd(0x4190, I("b87"), 1); Wd(0x4f0, I("a4f0"), 4); Wd(0x4f4, I("a4f4"), 4); Wd(0x4f8, I("a4f8"), 4); Wd(0x4fc, I("a4fc"), 4); Wd(0x2849, 0x400c00, 4); w.Obj1[0x22] = 0; w.Obj2[0x22] = 0;
                            Wd(0x1c35, I("k35"), 2); BitConverter.GetBytes(I("e0") != 0 ? 0u : 0x400900u).CopyTo(w.L3, 0xe); Wd(0x1955, I("d55") * 32, 4); Wd(0x1959, I("e0") * 64, 4); Wd(0x1c7e, 96, 4); Wd(0x1c82, 32 * I("d55"), 4); Wd(0xf0b7, 0, 4); w.L3[4] = (byte)I("bx");
                            BitConverter.GetBytes(I("l5b") == 0 ? 0u : I("l5b") == 1 ? 0x400900u : 0x400a00u).CopyTo(w.L3, 5); BitConverter.GetBytes(0x400c00u).CopyTo(w.L3, 9); w.L3[0xd] = (byte)(I("ld") | (I("e0") != 0 ? 0x10 : 0)); w.L3[2] = 0; w.L3[0x12] = 0;
                            if ((sel & 1) != 0) w.Template[0x41] = (byte)((w.Template[0x41] & 0xf0) | 4);
                        }
                        if (fn == "f4965") { Wd(0x1c86, I("c86"), 1); Wd(0x1c35, I("k35"), 2); Wd(0x1974, I("p74") == 0 ? 0 : I("p74") == 1 ? 0x400900 : 0x400a00, 4); w.L3[3] = (byte)I("l3"); w.L3[4] = (byte)I("l13"); w.L3[0] = (byte)I("l0b"); Wd(0x1955, I("d55"), 4); Wd(0x1959, I("d55") ^ I("e0"), 4); Wd(0x1c7e, I("e0") != 0 ? I("d55") : 7, 4); Wd(0x1c82, I("d55") ^ I("e0"), 4); Wd(0xf0b7, 0, 4); }
                        if (fn is "f486d" or "f4938") { w.Obj1[0x19] = (byte)I("hp"); w.Obj2[0x19] = (byte)I("hp"); Wd(0x194d, 0x10 + I("px"), 4); Wd(0x1951, 0x10 + I("py"), 4); w.L3[2] = (byte)I("l2"); w.L3[0] = (byte)I("l0b"); w.L3[4] = (byte)I("l13"); }
                        if (fn is "f5593" or "f64ad") { Array.Clear(w.L3, 0x7d4, 0x604); for (int k = 0; k < c.GetProperty("qn").GetInt32(); k++) w.L3[0x7f2 + 0x1e * k + 1] = (byte)(k == 0 ? c.GetProperty("qid").GetInt32() : c.GetProperty("qid").GetInt32() + 1); BitConverter.GetBytes((uint)(0x7f2 + 0x1e * c.GetProperty("qn").GetInt32())).CopyTo(w.L3, 0xdce); w.L3[0] = (byte)c.GetProperty("l0q").GetInt32(); w.L3[1] = 0; }
                        if (fn == "f5580") w.L3[2] = (byte)c.GetProperty("cu").GetProperty("l2").GetInt32();
                        switch (fn) { case "f5679": OriginalUnitActions.F5679(w, g); break; case "f5573": OriginalUnitActions.F5573(w, g); break; case "f5580": OriginalUnitActions.F5580(w, g); break; case "f5593": OriginalUnitActions.F5593(w, g); break; case "f55b5": OriginalUnitActions.F55B5(w, g); break; case "f55d1": OriginalUnitActions.F55D1(w, g); break; case "f55e0": OriginalUnitActions.F55E0(w, g); break; case "f566a": OriginalUnitActions.F566A(w, g); break; case "f52a6": OriginalUnitActions.F52A6(w, g); break; case "f538d": OriginalUnitActions.F538D(w, g); break; case "f53a8": OriginalUnitActions.F53A8(w, g); break; case "f53c4": OriginalUnitActions.F53C4(w, g); break; case "f53d3": OriginalUnitActions.F53D3(w, g); break; case "f4fce": OriginalUnitActions.F4FCE(w, g); break; case "f500d": OriginalUnitActions.F500D(w, g); break; case "f50e5": OriginalUnitActions.F50E5(w, g); break; case "f50f4": OriginalUnitActions.F50F4(w, g); break; case "f51ac": OriginalUnitActions.F51AC(w, g); break; case "fae57": OriginalUnitActions.FAE57(w, g); break; case "faea8": OriginalUnitActions.FAEA8(w, g); break; case "f520b": OriginalUnitActions.F520B(w, g); break; case "f5297": OriginalUnitActions.F5297(w, g); break; case "f4d50": OriginalUnitActions.F4D50(w, g); break; case "f4e27": OriginalUnitActions.F4E27(w, g); break; case "f4e40": OriginalUnitActions.F4E40(w, g); break; case "f4b03": OriginalUnitActions.F4B03(w, g); break; case "f4b12": OriginalUnitActions.F4B12(w, g); break; case "f4c46": OriginalUnitActions.F4C46(w, g); break; case "f4d41": OriginalUnitActions.F4D41(w, g); break; case "f4965": OriginalUnitActions.F4965(w, g); break; case "f486d": OriginalUnitActions.F486D(w, g); break; case "f4938": OriginalUnitActions.F4938(w, g); break; case "f4580": OriginalUnitActions.F4580(w, g); break; case "f4672": OriginalUnitActions.F4672(w, g); break; case "f46f7": OriginalUnitActions.F46F7(w, g); break; default: OriginalUnitActions.F478D(w, g); break; }
                        break;
                    }
                case "fdec8": case "fdf2a": case "fdf7e":
                    {
                        var qb = ActionPrng(c.GetProperty("ds").GetUInt32() + 30, 0x604); for (int e = 0x7f2; e < 0x7f2 + 0x1e * 40; e += 0x1e) qb[e - 0x7d4 + 1] %= 4; Array.Copy(qb, 0, w.L3, 0x7d4, 0x604);
                        int nch = c.GetProperty("nch").GetInt32(), st = c.GetProperty("st").GetInt32();
                        BitConverter.GetBytes((uint)(0x7f2 + 0x1e * (fn == "fdf7e" ? (nch * 3 + st) % 45 : nch % 5))).CopyTo(w.L3, 0xdce);
                        w.L3[0] = (byte)(ActionPrng(c.GetProperty("ds").GetUInt32() + 32, 1)[0] % 4); w.L3[1] = 0;
                        Array.Copy(ActionPrng(c.GetProperty("ds").GetUInt32() + 31, 0x1e), 0, w.Text, 0, 0x1e); g.Esi = 0x402200; g.Eax = (regs[0] & 0xffffff00u) | (regs[0] % 4);
                        if (fn == "fdec8") OriginalUnitActions.FDEC8(w, g); else if (fn == "fdf2a") OriginalUnitActions.FDF2A(w, g); else OriginalUnitActions.FDF7E(w, g);
                        break;
                    }
                case "f5960": case "f5af6": case "f5b55":
                    {
                        int sel = c.GetProperty("sel").GetInt32(); var wi = Convert.FromHexString(c.GetProperty("wi").GetString()!); var ob = w.Obj1;
                        wi[3] = (byte)c.GetProperty("own").GetInt32(); ob[0x19] = (byte)c.GetProperty("hp").GetInt32();
                        wi[0x1a] = (sel & 1) != 0 ? ob[0xe] : (byte)(ob[0xe] + 1);
                        wi[0x1d] = (byte)c.GetProperty("k1d").GetInt32(); wi[0x1c] = (byte)c.GetProperty("k1c").GetInt32(); wi[0x12] = (byte)c.GetProperty("t12").GetInt32(); wi[0x1b] = sel == 2 ? (byte)0x3f : (byte)((wi[0x1b] & 0xc0) | (sel * 5 & 255));
                        BitConverter.GetBytes(0x400900u).CopyTo(w.Ds, 0x1969); w.Ds[0x1972] = (byte)(sel * 2 % 8); w.Ds[0x1c89] = (byte)((sel >> 1) & 1); w.Ds[0x19e7] = (byte)c.GetProperty("b19e7").GetInt32();
                        BitConverter.GetBytes(0x402e00u).CopyTo(w.Ds, 0x18da); Array.Copy(ActionPrng(c.GetProperty("ds").GetUInt32() + 50, 1024), 0, w.Text, 0xc00, 1024);
                        var txt = Convert.FromHexString(c.GetProperty("txt").GetString()!); var font = Convert.FromHexString(c.GetProperty("font").GetString()!);
                        for (int i = 0; i < 256; i++) { font[2 * i] = (byte)(0x200 + (i % 8) * 4); font[2 * i + 1] = (byte)((0x200 + (i % 8) * 4) >> 8); }
                        Array.Copy(txt, 0, w.Text, 0, txt.Length); Array.Copy(font, 0, w.Text, 0x200, font.Length); BitConverter.GetBytes(0x402400u).CopyTo(w.Ds, 0x19ee);
                        for (int k = 0; k < 6; k++) BitConverter.GetBytes(0x402200u).CopyTo(w.Ds, 0x1978 + 0xc * k + 2);
                        Array.Clear(w.L3, 0x7d4, 0x604); for (int k = 0; k < c.GetProperty("qn").GetInt32(); k++) w.L3[0x7f2 + 0x1e * k + 1] = (byte)(k == 0 ? c.GetProperty("qid").GetInt32() : c.GetProperty("qid").GetInt32() + 1); BitConverter.GetBytes((uint)(0x7f2 + 0x1e * c.GetProperty("qn").GetInt32())).CopyTo(w.L3, 0xdce); w.L3[0] = (byte)c.GetProperty("l0q").GetInt32(); w.L3[1] = 0;
                        Array.Copy(wi, 0, r, 0, 0x30);
                        if (fn == "f5960") OriginalUnitActions.F5960(w, g, r); else if (fn == "f5af6") OriginalUnitActions.F5AF6(w, g, r); else OriginalUnitActions.F5B55(w, g, r);
                        break;
                    }
                case "f5e1c": case "f5f0a": case "f5fc5": case "f6057": case "f608e": case "f612b": case "f6142": case "f7a1": case "f7d0": case "f862": case "f8c9": case "fe5e6": case "fe621": case "fe636": case "fbf07": case "fbf55":
                    {
                        uint? d1951Override = null; var wi = ActionPrng(c.GetProperty("ds").GetUInt32() + 51, 0x80); wi[3] = (byte)c.GetProperty("wf").GetInt32(); wi[0x1a] = (byte)c.GetProperty("w1a").GetInt32(); wi[0x1b] = (byte)(c.GetProperty("w1a").GetInt32() >> 8);
                        wi[0x1c] = (byte)c.GetProperty("k1c").GetInt32(); wi[0x1d] = (byte)c.GetProperty("k1d").GetInt32(); uint t22 = c.GetProperty("t22").GetUInt32();
                        BitConverter.GetBytes(c.GetProperty("m16").GetInt32() != 0 ? t22 : c.GetProperty("r16").GetUInt32()).CopyTo(wi, 0x16); if (fn == "f7d0") wi[0x1d] %= 8; if (fn == "f7d0" && wi[3] < 8) wi[3] += 8; if (fn == "fbf07") wi[0x31] = (byte)c.GetProperty("k31").GetInt32(); if (fn == "fbf55") { uint ea = (uint)(((wi[0x14] | wi[0x15] << 8) >> 1) + BitConverter.ToUInt32(wi, 8)); d1951Override = unchecked(ea + (uint)c.GetProperty("dd").GetInt32()); } BitConverter.GetBytes(t22).CopyTo(w.Obj1, 0x22);
                        BitConverter.GetBytes((ushort)c.GetProperty("k35").GetInt32()).CopyTo(w.Ds, 0x1c35); w.Ds[0x19e7] = (byte)c.GetProperty("b19e7").GetInt32(); BitConverter.GetBytes(d1951Override ?? (uint)c.GetProperty("d1951").GetInt32()).CopyTo(w.Ds, 0x1951);
                        BitConverter.GetBytes(0x400900u - 0x9dfu).CopyTo(w.Ds, 0xed20); w.Ds[0x1c87] = (byte)c.GetProperty("b87").GetInt32(); Array.Copy(wi, 0, r, 0, 0x80);
                        Array.Clear(w.L3, 0x7d4, 0x604); for (int k = 0; k < c.GetProperty("qn").GetInt32(); k++) w.L3[0x7f2 + 0x1e * k + 1] = (byte)(k == 0 ? c.GetProperty("qid").GetInt32() : c.GetProperty("qid").GetInt32() + 1); BitConverter.GetBytes((uint)(0x7f2 + 0x1e * c.GetProperty("qn").GetInt32())).CopyTo(w.L3, 0xdce); w.L3[0] = (byte)c.GetProperty("l0q").GetInt32(); w.L3[1] = 0;
                        if (fn == "f6057") BitConverter.GetBytes(c.GetProperty("m16").GetInt32() != 0 ? t22 : BitConverter.ToUInt32(r, 0x1a)).CopyTo(r, 0x1a);
                        if (fn == "f608e") { r[0x1b] = (byte)c.GetProperty("k1d").GetInt32(); BitConverter.GetBytes(c.GetProperty("pos").GetUInt32()).CopyTo(r, 4); }
                        if (fn is "f7a1" or "f7d0" or "f862" or "f8c9")
                        {
                            w.Ds[0x19ea] = (byte)c.GetProperty("k19ea").GetInt32();
                            for (int k = 0; k < 8; k++) BitConverter.GetBytes((uint)(0x7cf + c.GetProperty("v7cf").GetInt32())).CopyTo(w.Stats, k * 0x1a35 + 0x19f1);
                            bool seq = c.GetProperty("seq").GetInt32() != 0; if (fn == "f7d0") { w.L3[0xefb + 0x13 * 3 + 0x11] = 5; w.L3[0xefb + 0x13 * 3 + 0x12] = (byte)(seq ? 5 : 6);
                            w.L3[0xefb + 0x13 * 1 + 0x11] = 7; w.L3[0xefb + 0x13 * 1 + 0x12] = (byte)(seq ? 7 : 8); w.L3[0xefb + 0x13 * 2 + 0x11] = 9; w.L3[0xefb + 0x13 * 2 + 0x12] = (byte)(seq ? 9 : 4); }
                        }
                        w.Ds[0x19c2] = (byte)c.GetProperty("b19e7").GetInt32(); BitConverter.GetBytes(0x400900u).CopyTo(w.Ds, 0x1969);
                        w.Ds[0x1972] = (byte)c.GetProperty("own").GetInt32(); if (fn == "f5f0a") w.Template[0x45] = (byte)(64 + c.GetProperty("k1d").GetInt32() % 190);
                        if (fn == "f5e1c") OriginalUnitActions.F5E1C(w, g, r); else if (fn == "f5f0a") OriginalUnitActions.F5F0A(w, g, r); else if (fn == "f5fc5") OriginalUnitActions.F5FC5(w, g, r); else if (fn == "fbf07") OriginalUnitActions.FBF07(w, g, r); else if (fn == "fbf55") OriginalUnitActions.FBF55(w, g, r); else if (fn == "fe5e6") OriginalUnitActions.FE5E6(w, g, r); else if (fn == "fe621") OriginalUnitActions.FE621(w, g, r); else if (fn == "fe636") OriginalUnitActions.FE636(w, g, r); else if (fn == "f7a1") OriginalUnitActions.FF7A1(w, g, r); else if (fn == "f7d0") OriginalUnitActions.FF7D0(w, g, r); else if (fn == "f862") OriginalUnitActions.FF862(w, g, r); else if (fn == "f8c9") OriginalUnitActions.FF8C9(w, g, r); else if (fn == "f6057") OriginalUnitActions.F6057(w, g, r); else if (fn == "f608e") OriginalUnitActions.F608E(w, g, r); else if (fn == "f612b") OriginalUnitActions.F612B(w, g); else OriginalUnitActions.F6142(w, g);
                        break;
                    }
                case "f66e9": case "f670f": case "f672f": case "f674f":
                    {
                        BitConverter.GetBytes(c.GetProperty("u22").GetUInt32()).CopyTo(w.Ds, 0x1922); BitConverter.GetBytes(c.GetProperty("u2a").GetUInt32()).CopyTo(w.Ds, 0x192a);
                        if (fn == "f66e9") OriginalUnitActions.F66E9(w, g); else if (fn == "f670f") OriginalUnitActions.F670F(w, g); else if (fn == "f672f") OriginalUnitActions.F672F(w, g); else OriginalUnitActions.F674F(w, g);
                        break;
                    }
                case "f11b4e": w.Ds[0x1c4e] = (byte)c.GetProperty("k4e").GetInt32(); w.Ds[0x1906] = (byte)c.GetProperty("r1").GetInt32(); w.Ds[0x190a] = (byte)c.GetProperty("r2").GetInt32(); OriginalUnitActions.F11B4E(w, g); break;
                case "fa1d1":
                    {
                        BitConverter.GetBytes(0xc00000u).CopyTo(w.Ds, 0x68e); w.Ds[0x1c4e] = (byte)c.GetProperty("k4e").GetInt32(); w.Ds[0x1c60] = (byte)c.GetProperty("k60").GetInt32(); w.Ds[0x1972] = (byte)c.GetProperty("own").GetInt32(); w.Ds[0x1971] = (byte)c.GetProperty("k71").GetInt32();
                        BitConverter.GetBytes((uint)c.GetProperty("m1906").GetInt32()).CopyTo(w.Ds, 0x1906); BitConverter.GetBytes((uint)c.GetProperty("m190a").GetInt32()).CopyTo(w.Ds, 0x190a);
                        Array.Copy(Convert.FromHexString(c.GetProperty("fog").GetString()!), 0, w.Text, 0xc00, 1024); BitConverter.GetBytes(0x402e00u).CopyTo(w.Ds, 0x18da);
                        Array.Copy(Convert.FromHexString(c.GetProperty("pal").GetString()!), 0, w.Text, 0, 1024); BitConverter.GetBytes(0x402200u).CopyTo(w.Ds, 0x18fa);
                        if (c.GetProperty("k71").GetInt32() is 1 or 2) { w.Obj1[3] = (byte)c.GetProperty("k71").GetInt32(); w.Obj2[3] = (byte)(c.GetProperty("k71").GetInt32() ^ 4); }
                        OriginalUnitActions.FA1D1(w, g); break;
                    }
                case "f3d40": case "f3d67": w.Ds[0x194c] = (byte)c.GetProperty("k194c").GetInt32(); if (fn == "f3d40") OriginalUnitActions.F3D40(w, g); else OriginalUnitActions.F3D67(w, g); break;
                case "f5c2e":
                    Array.Copy(Convert.FromHexString(c.GetProperty("rec").GetString()!), 0, w.Text, 0x100, 0x200); g.Esi = 0x402300; w.L3[2] = (byte)c.GetProperty("l2").GetInt32(); w.Ds[0x19e7] = (byte)c.GetProperty("k19e7").GetInt32(); BitConverter.GetBytes((uint)c.GetProperty("k1951").GetInt32()).CopyTo(w.Ds, 0x1951);
                    OriginalUnitActions.F5C2E(w, g); break;
                case "f5cd9":
                    Array.Copy(Convert.FromHexString(c.GetProperty("txt").GetString()!), 0, w.Text, 0, 0x300); w.Text[0x400 + 0x1a] = (byte)c.GetProperty("b1a").GetInt32(); w.Text[0x400 + 0x1b] = (byte)c.GetProperty("b1b").GetInt32(); w.Text[0x400 + 0x1c] = (byte)c.GetProperty("b1c").GetInt32(); w.Text[0x400 + 0x1d] = (byte)c.GetProperty("b1d").GetInt32();
                    g.Esi = 0x402600; BitConverter.GetBytes(0x402400u).CopyTo(w.Ds, 0x191a); OriginalUnitActions.F5CD9(w, g); break;
                case "fc010": case "fc06e": case "fc0a4":
                    {
                        w.Ds[0x19e4] = (byte)c.GetProperty("t19e4").GetInt32(); var rec = Convert.FromHexString(c.GetProperty("rec").GetString()!);
                        if (fn == "fc06e") { Array.Copy(rec, 0, w.Ds, 0x52d, rec.Length); OriginalUnitActions.FC06E(w, g); }
                        else { Array.Copy(rec, 0, w.Text, 0x100, rec.Length); g.Esi = 0x402300; if (fn == "fc010") OriginalUnitActions.FC010(w, g); else OriginalUnitActions.FC0A4(w, g); }
                        break;
                    }
                case "fa5f4":
                    Array.Copy(Convert.FromHexString(c.GetProperty("tbl5").GetString()!), 0, w.Text, 0x100, 0x100); var l5 = Convert.FromHexString(c.GetProperty("lst5").GetString()!); Array.Copy(l5, 0, w.Text, 0x300, l5.Length);
                    w.Obj1[3] %= 8; BitConverter.GetBytes(0x402300u).CopyTo(w.Ds, 0x6ca + 0x56 * w.Obj1[0] + 0x15 + 4 * w.Obj1[3]); g.Esi = 0x400900; OriginalUnitActions.FA5F4(w, g); break;
                case "f6f18":
                    Array.Fill(w.Video, (byte)0x5a, 0, 0x49000); Array.Copy(ActionPrng(c.GetProperty("seed").GetUInt32(), 0x2000), 0, w.Video, 0x50000, 0x2000); Array.Copy(ActionPrng(c.GetProperty("seed").GetUInt32() + 1, 0x3000), 0, w.Video, 0x60000, 0x3000);
                    BitConverter.GetBytes(0xc00000u).CopyTo(w.Ds, 0x68e); BitConverter.GetBytes(0xc50000u - 0x700).CopyTo(w.Ds, 0x19f6); g.Esi = 0xc60000; OriginalUnitActions.F6F18(w, g); break;
                case "fca49":
                    {
                        var t = c.GetProperty("t"); var ob = c.GetProperty("o"); w.Template[0x45] = (byte)t[0].GetInt32(); w.Template[0x46] = (byte)t[1].GetInt32(); w.Template[0x4a] = (byte)t[2].GetInt32(); BitConverter.GetBytes((ushort)t[3].GetInt32()).CopyTo(w.Template, 0x48);
                        w.Record[0x19] = (byte)ob[0].GetInt32(); w.Record[0x1a] = (byte)ob[1].GetInt32(); w.Record[0xf] = (byte)ob[2].GetInt32(); OriginalUnitActions.FCA49(w, g); break;
                    }
                case "fb3b6": BitConverter.GetBytes((uint)c.GetProperty("v1922").GetInt64()).CopyTo(w.Ds, 0x1922); OriginalUnitActions.FB3B6(w, g); break;
                case "f9f38":
                    {
                        Array.Fill(w.Video, (byte)0x5a, 0, 0x49000); Array.Copy(ActionPrng(c.GetProperty("seed").GetUInt32(), 0x10000), 0, w.Video, 0x50000, 0x10000); Array.Copy(Convert.FromHexString(c.GetProperty("tile").GetString()!), 0, w.Video, 0x60000, 0x1800);
                        BitConverter.GetBytes(0xc00000u).CopyTo(w.Ds, 0x68e); BitConverter.GetBytes(0xc50000u - 0x700).CopyTo(w.Ds, 0x19f6); BitConverter.GetBytes(0xc60000u).CopyTo(w.Ds, 0x191a);
                        BitConverter.GetBytes((uint)c.GetProperty("v06").GetInt32()).CopyTo(w.Ds, 0x1906); BitConverter.GetBytes((uint)c.GetProperty("v0a").GetInt32()).CopyTo(w.Ds, 0x190a); w.Ds[0x1972] = (byte)c.GetProperty("own").GetInt32();
                        Array.Copy(Convert.FromHexString(c.GetProperty("fog").GetString()!), 0, w.Text, 0x440, 0x480); BitConverter.GetBytes(0x402640u).CopyTo(w.Ds, 0x18da);
                        OriginalUnitActions.F9F38(w, g); break;
                    }
                case "f3f79": case "f3eea": case "f3df2": { var dst = Convert.FromHexString(c.GetProperty("dst").GetString()!); Array.Copy(dst, 0, w.Text, 0x800, dst.Length); } BitConverter.GetBytes(0x402a00u).CopyTo(w.Ds, 0x64c); if (fn == "f3f79") OriginalUnitActions.F3F79(w, g); else if (fn == "f3df2") OriginalUnitActions.F3DF2(w, g); else OriginalUnitActions.F3EEA(w, g); break;
                case "f3f32": OriginalUnitActions.F3F32(w, g); break;
                case "f3db5":
                    BitConverter.GetBytes(c.GetProperty("d193e").GetUInt32()).CopyTo(w.Ds, 0x193e); w.Ds[0x19e4] = (byte)c.GetProperty("k19e4").GetInt32(); w.Ds[0x194a] = (byte)c.GetProperty("k194a").GetInt32();
                    BitConverter.GetBytes((ushort)c.GetProperty("w1946").GetInt32()).CopyTo(w.Ds, 0x1946); BitConverter.GetBytes((ushort)c.GetProperty("w1948").GetInt32()).CopyTo(w.Ds, 0x1948); OriginalUnitActions.F3DB5(w, g); break;
                case "f3fa1":
                    w.Ds[0x19c2] = (byte)c.GetProperty("k19c2").GetInt32(); w.Ds[0x908f] = (byte)c.GetProperty("k908f").GetInt32(); w.Ds[0x19e4] = (byte)c.GetProperty("k19e4").GetInt32(); w.Ds[0x19c1] = (byte)c.GetProperty("k19c1").GetInt32();
                    BitConverter.GetBytes((uint)c.GetProperty("mx").GetInt32()).CopyTo(w.Ds, 0x1932); BitConverter.GetBytes((uint)c.GetProperty("my").GetInt32()).CopyTo(w.Ds, 0x1936); BitConverter.GetBytes((uint)c.GetProperty("v06").GetInt32()).CopyTo(w.Ds, 0x1906); BitConverter.GetBytes((uint)c.GetProperty("v0a").GetInt32()).CopyTo(w.Ds, 0x190a); BitConverter.GetBytes((ushort)c.GetProperty("key").GetInt32()).CopyTo(w.Ds, 0x1c35);
                    OriginalUnitActions.F3FA1(w, g); break;
                case "f677f": case "f6d4a": BitConverter.GetBytes(0xc00000u).CopyTo(w.Ds, 0x68e); if (fn == "f677f") OriginalUnitActions.F677F(w, g); else OriginalUnitActions.F6D4A(w, g); break;
                case "f6ba3": case "f6c04": case "f6c66": case "f6cd6":
                    {
                        var rr = ActionPrng(c.GetProperty("bk").GetUInt32(), 0x400); var bank = new byte[0x400];
                        for (int ix = 0; ix < 0x100; ix++) BitConverter.GetBytes((ushort)(0x200 + 0x40 * (ix % 8))).CopyTo(bank, 2 * ix);
                        for (int sp = 0; sp < 8; sp++)
                        {
                            int bs = 0x200 + 0x40 * sp; int ww = 1 + rr[sp * 3] % 8, hh = 1 + rr[sp * 3 + 1] % 5; bank[bs] = (byte)(rr[sp * 3 + 2] % 4); BitConverter.GetBytes((uint)(ww | hh << 8)).CopyTo(bank, bs + 1);
                            for (int q = 0; q < ww * hh; q++) { byte v = rr[0x40 + sp * 0x30 + q]; bank[bs + 3 + q] = (byte)(v % 10 < 3 ? 0 : (v | 1)); }
                        }
                        Array.Copy(bank, 0, w.Text, 0x800, 0x400); BitConverter.GetBytes(0x402a00u).CopyTo(w.Ds, 0x19ee); BitConverter.GetBytes(0xc00000u).CopyTo(w.Ds, 0x68e); Array.Copy(Convert.FromHexString(c.GetProperty("tbl").GetString()!), 0, w.Text, 0xc00, 256); g.Ebp = 0x402e00;
                        if (fn is "f6c66" or "f6cd6")
                        {
                            var tx = new List<byte>(); int nn = 1 + rr[0x3f0] % 6;
                            for (int q = 0; q < nn; q++) { byte v = rr[0x3e0 + q]; if (v % 16 == 0) tx.Add(0x0d); else if (v % 23 == 0) { tx.Add(0x7e); tx.Add((byte)(v % 8)); } else tx.Add((byte)(0x20 + v % 0x5e)); }
                            tx.Add(0xff); Array.Copy(tx.ToArray(), 0, w.Text, 0, tx.Count); g.Esi = 0x402200;
                            if (fn == "f6c66") OriginalUnitActions.F6C66(w, g); else OriginalUnitActions.F6CD6(w, g); break;
                        }
                        if (fn == "f6ba3") OriginalUnitActions.F6BA3(w, g); else OriginalUnitActions.F6C04(w, g); break;
                    }
                case "fdee4":
                    {
                        Array.Clear(w.L3, 0x7d4, 0x604); int room = c.GetProperty("room").GetInt32(); int qn = c.GetProperty("qn").GetInt32(); int end = 0xdce - room; end = room > 0x80 ? 0x7f2 + 0x1e * qn : end;
                        BitConverter.GetBytes((uint)end).CopyTo(w.L3, 0xdce); w.L3[0] = 0; w.L3[1] = 0;
                        var rec = new byte[0x50]; BitConverter.GetBytes((uint)c.GetProperty("len").GetInt32()).CopyTo(rec, 0); BitConverter.GetBytes(0x10030u).CopyTo(rec, 4); rec[8] = (byte)c.GetProperty("rb8").GetInt32();
                        var dat = Convert.FromHexString(c.GetProperty("dat").GetString()!); Array.Copy(dat, 0, rec, 9, 0x40 - 9); Array.Copy(rec, 0, w.Text, 0, 0x50);
                        g.Esi = 0x402200; OriginalUnitActions.FDEE4(w, g); break;
                    }
                case "fe571": case "fe5ab": case "fe5f6":
                    {
                        var wi = ActionPrng(c.GetProperty("ds").GetUInt32() + 51, 0x80); wi[3] = (byte)c.GetProperty("wf").GetInt32(); wi[0x10] = (byte)c.GetProperty("w10").GetInt32(); wi[0x11] = (byte)c.GetProperty("w11").GetInt32();
                        Array.Copy(wi, 0, r, 0, 0x80); w.Ds[0x19e7] = (byte)c.GetProperty("k19e7").GetInt32();
                        int lw = c.GetProperty("l0w").GetInt32(); if (lw == 1) { w.L3[0] = wi[1]; w.L3[1] = wi[2]; } else if (lw == 2) { w.L3[0] = 7; w.L3[1] = 0; }
                        if (fn == "fe571") OriginalUnitActions.FE571(w, g, r); else if (fn == "fe5ab") OriginalUnitActions.FE5AB(w, g, r); else OriginalUnitActions.FE5F6(w, g, r);
                        break;
                    }
                case "fe65b":
                    {
                        Array.Clear(w.L3, 0x7d4, 0x604); int qn = c.GetProperty("qn").GetInt32(); for (int k = 0; k < qn; k++) w.L3[0x7f2 + 0x1e * k + 1] = (byte)c.GetProperty("ids")[k].GetInt32();
                        BitConverter.GetBytes((uint)(0x7f2 + 0x1e * qn)).CopyTo(w.L3, 0xdce); w.L3[0] = 0; w.L3[1] = 0;
                        Array.Clear(w.Ds, 0x1f45, 0x20); BitConverter.GetBytes(0x40a000u).CopyTo(w.Ds, 0x6ba); int own = c.GetProperty("f3").GetInt32(); w.Ds[0x1972] = (byte)own; w.Ds[0x6c6] = (byte)c.GetProperty("cnt").GetInt32();
                        var pr = w.PartOf((byte)own); for (int k = 0; k < 8; k++) pr[0xb9 + k] = (byte)c.GetProperty("rel")[k].GetInt32();
                        for (int k = 0; k < 8; k++) BitConverter.GetBytes((uint)(0x7cf + c.GetProperty("dv")[k].GetInt32())).CopyTo(w.Stats, k * 0x1a35 + 0x19f1);
                        OriginalUnitActions.FE65B(w, g); break;
                    }
                case "fbe9d": OriginalUnitActions.FBE9D(w, g); break;
                case "fbea7": case "fbebb": case "fbed7": case "fbee6":
                    {
                        Array.Clear(w.L3, 0x7d4, 0x604); int qn = c.GetProperty("qn").GetInt32(); for (int k = 0; k < qn; k++) w.L3[0x7f2 + 0x1e * k + 1] = (byte)c.GetProperty("ids")[k].GetInt32();
                        BitConverter.GetBytes((uint)(0x7f2 + 0x1e * qn)).CopyTo(w.L3, 0xdce); w.L3[0] = 0; w.L3[1] = 0; BitConverter.GetBytes(0x400900u).CopyTo(w.Ds, 0x1969);
                        if (fn == "fbea7") OriginalUnitActions.FBEA7(w, g); else if (fn == "fbebb") OriginalUnitActions.FBEBB(w, g); else if (fn == "fbed7") OriginalUnitActions.FBED7(w, g); else OriginalUnitActions.FBEE6(w, g);
                        break;
                    }
                case "fdfe5": case "fe02a": case "fe03c": case "fe055": case "fe070":
                    {
                        Array.Clear(w.L3, 0x7d4, 0x604); int qn = c.GetProperty("qn").GetInt32();
                        for (int k = 0; k < qn; k++) { var en = c.GetProperty("qe")[k]; for (int q = 0; q < 3; q++) w.L3[0x7f2 + 0x1e * k + q] = (byte)en[q].GetInt32(); }
                        BitConverter.GetBytes((uint)(0x7f2 + 0x1e * qn)).CopyTo(w.L3, 0xdce); w.L3[0] = (byte)c.GetProperty("l0q").GetInt32(); w.L3[1] = 0;
                        if (fn == "fdfe5") OriginalUnitActions.FDFE5(w, g); else if (fn == "fe02a") OriginalUnitActions.FE02A(w, g); else if (fn == "fe03c") OriginalUnitActions.FE03C(w, g); else if (fn == "fe055") OriginalUnitActions.FE055(w, g); else OriginalUnitActions.FE070(w, g);
                        break;
                    }
                case "ff629":
                    {
                        Array.Clear(w.L3, 0x7d4, 0x604); for (int k = 0; k < c.GetProperty("qn").GetInt32(); k++) w.L3[0x7f2 + 0x1e * k + 1] = (byte)c.GetProperty("ids")[k].GetInt32(); BitConverter.GetBytes((uint)(0x7f2 + 0x1e * c.GetProperty("qn").GetInt32())).CopyTo(w.L3, 0xdce); w.L3[0] = 0; w.L3[1] = 0;
                        int own = c.GetProperty("own").GetInt32(); uint d3 = c.GetProperty("d3cda").GetUInt32(); w.Ds[0x6c6] = 1; w.Ds[0x1972] = (byte)own; w.Ds[0x19e9] = (byte)c.GetProperty("k19e9").GetInt32();
                        BitConverter.GetBytes(d3).CopyTo(w.L3, 0x3cda); BitConverter.GetBytes(c.GetProperty("v1ca9").GetUInt32()).CopyTo(w.Ds, 0x1ca9);
                        var p0 = w.PartOf(0); int jj = 0;
                        foreach (var en in c.GetProperty("ent").EnumerateArray())
                        {
                            int bs = 0x34 + 0xe * jj++; p0[bs] = (byte)en[0].GetInt32(); BitConverter.GetBytes(unchecked(d3 + (uint)en[1].GetInt32())).CopyTo(p0, bs + 1); p0[bs + 9] = (byte)(en[2].GetInt32() != 0 ? own : (own + 1) & 255);
                        }
                        var txt = Convert.FromHexString(c.GetProperty("txt").GetString()!);
                        for (int k = 0; k < 4; k++)
                        {
                            int nv = c.GetProperty("nv")[k].GetInt32(); var blk = new List<byte> { (byte)nv };
                            for (int v = 0; v < nv; v++) { for (int q = 0; q < 1 + v % 4; q++) blk.Add(txt[(k * 3 + v) * 5 + q]); blk.Add(0xff); }
                            Array.Copy(blk.ToArray(), 0, w.Text, 0x40 * k, blk.Count); BitConverter.GetBytes(0x402200u + 0x40u * (uint)k).CopyTo(w.L3, 0xefb + 0x13 * k); BitConverter.GetBytes((uint)c.GetProperty("eff")[k].GetInt32()).CopyTo(w.L3, 0xefb + 0x13 * k + 4);
                        }
                        var font = ActionPrng(c.GetProperty("ds").GetUInt32() + 70, 0x200 + 32); for (int i = 0; i < 256; i++) { font[2 * i] = (byte)(0x200 + (i % 8) * 4); font[2 * i + 1] = (byte)((0x200 + (i % 8) * 4) >> 8); }
                        Array.Copy(font, 0, w.Text, 0x200, font.Length); BitConverter.GetBytes(0x402400u).CopyTo(w.Ds, 0x19ee);
                        OriginalUnitActions.FF629(w, g); break;
                    }
                case "f5ce": case "f5f2": case "f781": case "f791":
                    {
                        if (fn == "f5ce") { w.Ds[0x1c4e] = (byte)c.GetProperty("k4e").GetInt32(); w.L3[0] = (byte)c.GetProperty("l0").GetInt32(); OriginalUnitActions.FF5CE(w, g); }
                        else if (fn == "f5f2")
                        {
                            Array.Clear(w.L3, 0x7d4, 0x604); for (int k = 0; k < c.GetProperty("qn").GetInt32(); k++) w.L3[0x7f2 + 0x1e * k + 1] = (byte)(k == 0 ? c.GetProperty("qid").GetInt32() : c.GetProperty("qid").GetInt32() + 1); BitConverter.GetBytes((uint)(0x7f2 + 0x1e * c.GetProperty("qn").GetInt32())).CopyTo(w.L3, 0xdce); w.L3[0] = (byte)c.GetProperty("l0q").GetInt32(); w.L3[1] = 0;
                            for (int k = 0; k < c.GetProperty("qn").GetInt32(); k++) w.L3[0x7f2 + 0x1e * k + 1] = (byte)c.GetProperty("ids")[k].GetInt32();
                            OriginalUnitActions.FF5F2(w, g);
                        }
                        else if (fn == "f781") OriginalUnitActions.FF781(w, g); else OriginalUnitActions.FF791(w, g);
                        break;
                    }
                case "f4473": case "f44c0":
                    {
                        w.Ds[0x1972] = (byte)c.GetProperty("own").GetInt32(); BitConverter.GetBytes(c.GetProperty("cad").GetUInt32()).CopyTo(w.Ds, 0x1cad);
                        if (fn == "f4473")
                        {
                            w.Ds[0x6c6] = (byte)c.GetProperty("k6c6").GetInt32(); int k = 0; foreach (var v in c.GetProperty("lst").EnumerateArray()) w.Ds[0x1cb5 + k++] = (byte)v.GetInt32();
                            k = 0; foreach (var v in c.GetProperty("t").EnumerateArray()) w.Ds[0x1ccb + 0x16 * k++] = (byte)v.GetInt32();
                            OriginalUnitActions.F4473(w, g);
                        }
                        else
                        {
                            w.Obj1[0x19] = (byte)c.GetProperty("hp").GetInt32(); w.Ds[0x1ec6] = (byte)c.GetProperty("k1ec6").GetInt32(); BitConverter.GetBytes(c.GetProperty("p1974").GetInt32() != 0 ? 0x400900u : 0u).CopyTo(w.Ds, 0x1974);
                            w.Ds[0x19ea] = (byte)c.GetProperty("k9ea").GetInt32(); BitConverter.GetBytes(c.GetProperty("b6ba").GetUInt32()).CopyTo(w.Ds, 0x6ba);
                            OriginalUnitActions.F44C0(w, g);
                        }
                        break;
                    }
                case "f42bb":
                    {
                        w.Ds[0x6c6] = (byte)c.GetProperty("cnt").GetInt32(); w.Ds[0x1c4e] = (byte)c.GetProperty("k4e").GetInt32(); w.Ds[0x1c4f] = (byte)c.GetProperty("k4f").GetInt32(); w.Ds[0x19ea] = (byte)c.GetProperty("k9ea").GetInt32();
                        BitConverter.GetBytes(0x10030u).CopyTo(w.Template, 0x3b);
                        OriginalUnitActions.F42BB(w, g); break;
                    }
                case "f5969": case "f59de":
                    {
                        Array.Clear(w.L3, 0x7d4, 0x604); for (int k = 0; k < c.GetProperty("qn").GetInt32(); k++) w.L3[0x7f2 + 0x1e * k + 1] = (byte)(k == 0 ? c.GetProperty("qid").GetInt32() : c.GetProperty("qid").GetInt32() + 1); BitConverter.GetBytes((uint)(0x7f2 + 0x1e * c.GetProperty("qn").GetInt32())).CopyTo(w.L3, 0xdce); w.L3[0] = (byte)c.GetProperty("l0q").GetInt32(); w.L3[1] = 0;
                        if (fn == "f5969")
                        {
                            var buf = ActionPrng(c.GetProperty("rs").GetUInt32(), 0x200); int pos = 0; uint rs = c.GetProperty("rs").GetUInt32();
                            for (int k = 0; k < c.GetProperty("nrec").GetInt32(); k++)
                            {
                                buf[pos] = (byte)(((rs >> k) & 1) != 0 ? 0x80 : 0x20); buf[pos + 1] = 1; BitConverter.GetBytes((((rs >> (k + 8)) & 1) != 0 ? 0u : (uint)(k + 2))).CopyTo(buf, pos + 3); int cnt = buf[pos + 0xf] % 3; buf[pos + 0xf] = (byte)cnt; pos += 0x10 + 2 * cnt;
                            }
                            buf[pos + 1] = 0xff; Array.Copy(buf, 0, w.Text, 0, 0x200);
                            w.Obj1[0] = (byte)c.GetProperty("sel").GetInt32(); w.Obj1[3] = (byte)c.GetProperty("own").GetInt32();
                            BitConverter.GetBytes(0x400900u).CopyTo(w.Ds, 0x1969); BitConverter.GetBytes(0x402200u).CopyTo(w.Ds, 0x6df + 0x56 * c.GetProperty("sel").GetInt32() + 4 * c.GetProperty("own").GetInt32());
                            OriginalUnitActions.F5969(w, g);
                        }
                        else { w.Ds[0x1972] = (byte)c.GetProperty("own").GetInt32(); w.Ds[0x1c87] = (byte)c.GetProperty("b87").GetInt32(); g.Edi = 0x400800; OriginalUnitActions.F59DE(w, g); }
                        break;
                    }
                case "f6996": case "f69fe": case "f6a73": case "f6acd":
                    {
                        if (fn is "f6996" or "f69fe")
                        {
                            Array.Copy(ActionPrng(c.GetProperty("tab").GetUInt32(), 256), 0, w.Ds, 0x287e, 256); w.Obj1[3] = (byte)c.GetProperty("s3").GetInt32(); w.Obj1[4] = (byte)c.GetProperty("s4").GetInt32();
                            if (fn == "f6996") OriginalUnitActions.F6996(w, g); else OriginalUnitActions.F69FE(w, g);
                            break;
                        }
                        BitConverter.GetBytes(0x400900u).CopyTo(w.Ds, 0x1969); w.Ds[0x1972] = c.GetProperty("m3").GetInt32() != 0 ? w.Obj1[3] : (byte)(w.Obj1[3] + 1);
                        Array.Clear(w.L3, 0x7d4, 0x604); for (int k = 0; k < c.GetProperty("qn").GetInt32(); k++) w.L3[0x7f2 + 0x1e * k + 1] = (byte)(k == 0 ? c.GetProperty("qid").GetInt32() : c.GetProperty("qid").GetInt32() + 1); BitConverter.GetBytes((uint)(0x7f2 + 0x1e * c.GetProperty("qn").GetInt32())).CopyTo(w.L3, 0xdce); w.L3[0] = (byte)c.GetProperty("l0q").GetInt32(); w.L3[1] = 0;
                        if (fn == "f6a73") OriginalUnitActions.F6A73(w, g); else OriginalUnitActions.F6ACD(w, g);
                        break;
                    }
                case "f5cc2": case "f5da5": case "f5df6":
                    {
                        BitConverter.GetBytes((ushort)c.GetProperty("k35").GetInt32()).CopyTo(w.Ds, 0x1c35); BitConverter.GetBytes((uint)c.GetProperty("d193a").GetInt32()).CopyTo(w.Ds, 0x193a);
                        BitConverter.GetBytes(0x400900u).CopyTo(w.Ds, 0x1969); BitConverter.GetBytes((uint)c.GetProperty("ed20").GetInt32()).CopyTo(w.Ds, 0xed20); w.Ds[0x1c87] = (byte)c.GetProperty("b87").GetInt32();
                        w.Obj1[0x19] = (byte)c.GetProperty("hp").GetInt32(); w.Obj1[0xe] = 0; w.Obj1[0] = (byte)(c.GetProperty("sel").GetInt32() % 3);
                        Array.Clear(w.L3, 0x7d4, 0x604); for (int k = 0; k < c.GetProperty("qn").GetInt32(); k++) w.L3[0x7f2 + 0x1e * k + 1] = (byte)(k == 0 ? c.GetProperty("qid").GetInt32() : c.GetProperty("qid").GetInt32() + 1); BitConverter.GetBytes((uint)(0x7f2 + 0x1e * c.GetProperty("qn").GetInt32())).CopyTo(w.L3, 0xdce); w.L3[0] = (byte)c.GetProperty("l0q").GetInt32(); w.L3[1] = 0;
                        if (fn == "f5cc2") OriginalUnitActions.F5CC2(w, g); else if (fn == "f5da5") OriginalUnitActions.F5DA5(w, g); else OriginalUnitActions.F5DF6(w, g);
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
                case "fs14b": case "f11d34": if (c.GetProperty("sel").GetInt32() != 0) { w.Obj1[0x20] = 0xff; w.Obj1[0x19] = 0x7f; w.Obj1[3] %= 8; w.Obj2[0x20] = 0xff; w.Obj2[0x19] = 0x7f; w.Obj2[3] %= 8; w.Template[0x46] = 5; w.Template[0x74] = 0xf; w.Template[0x40] &= 0xdf; w.Template[0x41] &= 0x7f; } if (fn == "f11d34") OriginalUnitActions.F11D34(w, g, r); else OriginalUnitActions.FS14B(w, g, r); break;
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
            byte[] extra5ce = fn is "f5f2" or "f7d0" ? [.. w.L3[0x3cd0..0x3ce0], w.L3[0xdd4]] : fn == "ff629" ? [.. w.L3[0x3cd0..0x3ce0], .. w.L3[0x7d4..0x9a0]] : [];
            byte[] extra6ba = fn is "f6ba3" or "f6c04" or "f6c66" or "f6cd6" or "f677f" or "f6d4a" or "fa1d1" ? w.Video[0..0x10000] : [];
            byte[] extraC = fn is "f3f79" or "f3eea" or "f3df2" ? w.Text[0x800..0xcb0] : fn == "f6f18" ? w.Video[0..0x4000] : fn is "fcd72" or "fcf77" or "fcfac" or "fd0d5" ? [.. w.Ds[0x2045..0x20a5], .. w.Ds[0x33c..0x64c], .. w.Ds[0x7b8..0x7c0], w.Ds[0x7e8], .. w.Ds[0x7ea..0x7ee], .. Enumerable.Range(0, 1024).SelectMany(k => BitConverter.GetBytes(w.PlaneRead(0x404000u + 4u * (uint)k)))] : fn is "fae57" or "faea8" ? [.. w.Ds[0x27e0..0x27f0], .. w.Ds[0x2840..0x2850]] : fn is "fc010" or "fc0a4" ? w.Text[0x100..0x160] : fn == "fc06e" ? w.Ds[0x52d..0x54d] : fn == "f5cd9" ? w.Text[0..0x440] : fn == "f5c2e" ? w.Text[0x100..0x300] : fn == "f9f38" ? [.. w.Video[0..0x49000], .. w.Ds[0x43c..0x640], .. w.Ds[0x1f31..0x2045]] : [];
            byte[] extraB = fn == "f3db5" ? w.Ds[0x193e..0x1942] : [];
            byte[] extra44 = fn == "f44c0" ? [.. w.Ds[0xed20..0xed24], w.Ds[0x1ccb + 0x16 * c.GetProperty("own").GetInt32()]] : [];
            byte[] extra42 = fn == "f42bb" ? [.. w.Ds[0x2730..0x2760], w.Fs10, .. Enumerable.Range(0, c.GetProperty("nch").GetInt32()).SelectMany(k => w.ObjectAt((uint)(0x408000 + 0x60 * k))[0..0x20])] : [];
            byte[] output = [.. r, .. w.Obj1, .. w.Obj2, w.G1C9F, .. BitConverter.GetBytes(w.G512), .. BitConverter.GetBytes(w.G50E), w.G1CCB, w.G525, w.G526, w.G1C61, .. w.Ds[0x19c0..0x1a00], .. w.Ds[0x1c30..0x1c80], .. w.Ds[0x4160..0x4170], .. w.Ds[0x9080..0x9090], .. w.Ds[0x1978..0x19c0], .. w.Ds[0x1c80..0x1cd0], .. w.Ds[0x1ec0..0x1ed0], .. w.Ds[0x1960..0x1978], .. w.Ds[0x9090..0x90a0], .. w.Ds[0x1904..0x1914], .. w.Ds[0x33c..0x43c], .. w.Ds[0x640..0x64c], .. w.Ds[0x1940..0x1960], .. w.Ds[0x1a10..0x1a20], w.Fs13D, .. w.Stats[0x170..0x1b0], .. w.Stats[(0x1a35 + 0x170)..(0x1a35 + 0x1b0)], .. w.Stats[(2 * 0x1a35 + 0x170)..(2 * 0x1a35 + 0x1b0)], .. Enumerable.Range(0, 8).SelectMany(k => w.Stats[(k * 0x1a35 + 0x70)..(k * 0x1a35 + 0x80)]), .. w.Ds[0x1ca2..0x1cc2], .. w.Ds[0x1f0a..0x2104], .. w.Ds[0x4c0..0x5d0], .. w.L3[0..0x14], .. w.Ds[0xf0b0..0xf0c0], .. w.Ds[0x40..0x60], .. w.Ds[0x27f0..0x2850], .. (fn is "fdec8" or "fdf2a" or "fdf7e" or "f5af6" or "f5b55" or "f5cc2" or "f5df6" or "f5da5" or "f5e1c" or "f5593" or "f64ad" or "f6a73" or "f6acd" or "f5969" or "f59de" or "f5f2" or "f7a1" or "f7d0" or "f862" or "f8c9" or "fdfe5" or "fe02a" or "fe03c" or "fe055" or "fe070" or "fdee4" or "fe5e6" or "fe621" or "fe636" or "fe65b" or "fbea7" or "fbebb" or "fbed7" or "fbee6" ? w.L3[0x7d4..0xdd8] : new byte[0x604]), .. BitConverter.GetBytes(w.Fs17F), .. w.Text[0x600..0x700], .. BitConverter.GetBytes(Rel(g.Eax)), .. BitConverter.GetBytes(Rel(g.Ebx)), .. BitConverter.GetBytes(Rel(g.Ecx)), .. BitConverter.GetBytes(Rel(g.Edx)), .. BitConverter.GetBytes(Rel(g.Edi)),
                g.Zf ? (byte)1 : (byte)0, g.Cf ? (byte)1 : (byte)0, .. w.PartOf(r[3]), .. (fn == "fd2d1" ? Enumerable.Range(0, 4096).SelectMany(k => BitConverter.GetBytes(w.PlaneRead(0x404000u + (uint)(4 * k)))) : []), .. log, .. extra42, .. extra44, .. extra5ce, .. extra6ba, .. extraB, .. extraC];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Unit actions: first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("actions");
        Check(count == summary.GetProperty("case_count").GetInt32() && seen.Count == 257, $"Unit action reference coverage ({count} cases, {seen.Count} functions)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Unit action handlers match original x86");
    }
}
