using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class ObjectReplay(JsonElement c, byte[] random, List<byte> log) : IObjectScriptWorld
    {
        private byte _i = (byte)c.GetProperty("seed").GetInt32(), _j = (byte)(c.GetProperty("seed").GetInt32() ^ 0x5a);
        public byte I => _i; public byte J => _j;
        public byte Random() => random[_i++];
        public byte Random2() => random[_j++];
        private readonly Queue<(uint, uint, int)> _area2 = new(c.GetProperty("area").EnumerateArray().Select(a => ((uint)a[0].GetInt32(), (uint)a[1].GetInt32(), a[2].GetInt32())));
        private readonly Queue<uint> _claim = new(c.GetProperty("claim").EnumerateArray().Select(a => (uint)a.GetInt32()));
        private readonly Queue<byte> _dist = new(c.GetProperty("dist").EnumerateArray().Select(a => (byte)a.GetInt32()));
        public byte Tick => (byte)c.GetProperty("tick").GetInt32();
        public uint Reference(byte[] record) => 0x800;
        public (uint Count, uint Hostile, byte[]? Object) Area(uint center, uint size)
        {
            log.Add((byte)'A'); log.AddRange(BitConverter.GetBytes(center)); log.AddRange(BitConverter.GetBytes(size));
            if (_area2.Count == 0) return (0, 0, null);
            var (count, hostile, k) = _area2.Dequeue(); byte[]? obj = null;
            if (k != 0) { obj = new byte[0x40]; BitConverter.GetBytes((uint)(c.GetProperty("area")[0][3].GetInt64() >> k)).CopyTo(obj, 4); BitConverter.GetBytes((uint)(c.GetProperty("area")[0][4].GetInt64() >> k)).CopyTo(obj, 8); }
            return (count, hostile, obj);
        }
        public uint Claim(uint eax, uint ebx, ushort cx) { log.Add((byte)'C'); log.AddRange(BitConverter.GetBytes(eax)); log.AddRange(BitConverter.GetBytes(ebx)); log.AddRange(BitConverter.GetBytes(cx)); return _claim.Count > 0 ? _claim.Dequeue() : 0; }
        public byte Distance(uint x, uint y) { log.Add((byte)'L'); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y)); return _dist.Count > 0 ? _dist.Dequeue() : (byte)0; }
        public void Spawn(ushort eax, byte bl, ushort cx, uint edx) { log.Add((byte)'N'); log.AddRange(BitConverter.GetBytes(eax)); log.Add(bl); log.AddRange(BitConverter.GetBytes(cx)); log.AddRange(BitConverter.GetBytes(edx)); }
        public void Redraw() => log.Add((byte)'D');
        public void Remove(byte[] record) => log.Add((byte)'R');
        public void Vitals() => log.Add((byte)'V');
        public void Actions() => log.Add((byte)'U');
        private readonly Queue<bool> _zf = new(c.GetProperty("zf").EnumerateArray().Select(a => a.GetInt32() != 0));
        private readonly Queue<(bool, uint, uint)> _spot = new(c.GetProperty("spot").EnumerateArray().Select(a => (a[0].GetInt32() != 0, (uint)a[1].GetInt32(), (uint)a[2].GetInt32())));
        private readonly Queue<(bool, byte, byte, byte)> _beside = new(c.GetProperty("beside").EnumerateArray().Select(a => (a[0].GetInt32() != 0, (byte)a[1].GetInt32(), (byte)a[2].GetInt32(), (byte)a[3].GetInt32())));
        private readonly Queue<bool> _create = new(c.GetProperty("create").EnumerateArray().Select(a => a.GetInt32() != 0));
        private readonly Queue<(bool, byte)> _s11 = new(c.GetProperty("svc11").EnumerateArray().Select(a => (a[0].GetInt32() != 0, (byte)a[1].GetInt32())));
        private readonly Queue<bool> _a5 = new(c.GetProperty("svca5").EnumerateArray().Select(a => a.GetInt32() != 0));
        private readonly Queue<(bool, byte, int)> _a1 = new(c.GetProperty("a1").EnumerateArray().Select(a => (a[0].GetInt32() != 0, (byte)a[1].GetInt32(), a[2].GetInt32())));
        private readonly Queue<byte> _a9 = new(c.GetProperty("a9").EnumerateArray().Select(a => (byte)a.GetInt32()));
        public (bool Zf, byte Al, byte[] Object, uint Reference) ServiceA1()
        {
            log.Add((byte)'V'); var (zf, al, k) = _a1.Count > 0 ? _a1.Dequeue() : (true, (byte)0, 0);
            return (zf, al, k == 1 ? Obj1 : (k == 2 ? Obj2 : new byte[0x40]), (uint)(0x100 + 0x40 * k));
        }
        public byte ServiceA9(ushort ax) { log.Add((byte)'Y'); log.AddRange(BitConverter.GetBytes(ax)); return _a9.Count > 0 ? _a9.Dequeue() : (byte)0; }
        public byte[]? ObjectAt(uint reference) => reference == 0x140 ? Obj1 : (reference == 0x180 ? Obj2 : new byte[0x40]);
        public byte Parent25 => (byte)c.GetProperty("parent25").GetInt32();
        public byte Bh => (byte)c.GetProperty("ef1").GetInt32();
        public byte Bl => (byte)c.GetProperty("bl").GetInt32();
        public (bool Zf, uint Flags) Service119(ushort ax) { log.Add((byte)'H'); log.AddRange(BitConverter.GetBytes(ax)); return (c.GetProperty("a1").GetArrayLength() == 0 || c.GetProperty("a1")[0][0].GetInt32() != 0, (uint)(c.GetProperty("strength").GetInt64() & 0x1f)); }
        public int K => c.GetProperty("k").GetInt32();
        public uint MapWidthPx => (uint)c.GetProperty("dim4").GetInt32(); public uint MapHeightPx => (uint)c.GetProperty("dim0").GetInt32(); public uint FoundFlags => (uint)(c.GetProperty("strength").GetInt64() & 0x1f);
        public (bool Found, byte[]? Object) Scan17B(uint eax, uint ebx, uint ecx, uint edx, uint esi)
        {
            log.Add((byte)'F'); foreach (var v in new[] { eax, ebx, ecx, edx, esi }) log.AddRange(BitConverter.GetBytes(v));
            int k = _finds.Count > 0 ? _finds.Dequeue() : 0; return (k != 0, k == 1 ? Obj1 : (k == 2 ? Obj2 : null));
        }
        public void Say143(byte message, byte participant, byte speakerOwner) { log.Add((byte)'M'); log.AddRange([message, participant, speakerOwner]); }
        public byte[] ParentRecord()
        {
            var p = new byte[0x40]; p[0x25] = Parent25; p[3] = (byte)c.GetProperty("vowner").GetInt32();
            BitConverter.GetBytes((uint)c.GetProperty("ef7").GetInt64()).CopyTo(p, 4); BitConverter.GetBytes((uint)c.GetProperty("efc").GetInt64()).CopyTo(p, 8); return p;
        }
        public void Service25(uint parent, byte bl) { log.Add((byte)'a'); log.AddRange(BitConverter.GetBytes(parent)); log.Add(bl); }
        public void Service29(byte bl) { log.Add((byte)'b'); log.Add(bl); }
        public void Sound35(uint id) { log.Add((byte)'S'); log.AddRange(BitConverter.GetBytes(id)); }
        public (bool Zf, byte Al) Service11(uint eax) { log.Add((byte)'Q'); log.AddRange(BitConverter.GetBytes(eax)); return _s11.Count > 0 ? _s11.Dequeue() : (true, (byte)0); }
        public bool ServiceA5() { log.Add((byte)'W'); return _a5.Count > 0 ? _a5.Dequeue() : true; }
        public void ServiceAD(byte al, byte bl) { log.Add((byte)'M'); log.Add(al); log.Add(bl); }
        public void LevelExit(byte mode) { log.Add((byte)'X'); log.Add(mode); }
        public void Appearance(byte army) { log.Add((byte)'N'); log.Add(army); }
        public (bool Found, uint Reference) ScanMasked(byte mode, uint mask)
        {
            log.Add((byte)'F'); log.Add(mode); log.AddRange(BitConverter.GetBytes(Part12 != 0 || true ? QuestMask(mask) : mask));
            int k = _finds.Count > 0 ? _finds.Dequeue() : 0; return (k != 0, (uint)(0x100 + 0x40 * k));
        }
        private static uint QuestMask(uint mask) => mask;
        public uint QuestField(byte[] record) => Part12;
        public uint AiStrength(byte owner) { log.Add((byte)'T'); log.Add(owner); return (uint)c.GetProperty("strength").GetInt64(); }
        public byte ParticipantCount => (byte)c.GetProperty("count").GetInt32();
        public uint TemplateSound => (uint)c.GetProperty("datsound").GetInt64();
        public void OccupyRef(uint reference) { log.Add((byte)'O'); log.AddRange(BitConverter.GetBytes(reference)); }
        public void Handler(byte slot) => log.Add((byte)'H');
        public uint Part12 = (uint)c.GetProperty("part12").GetInt64();
        private readonly Queue<int> _finds = new(c.GetProperty("finds").EnumerateArray().Select(a => a.GetInt32()));
        public byte[] ObjectBytes = Convert.FromHexString(c.GetProperty("objbytes").GetString()!);
        public void EffectAt(byte army, byte type, byte layer, uint x, uint y) { log.Add((byte)'E'); log.AddRange([army, type, layer]); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y)); }
        public byte[]? FindObject(byte owner, byte army, byte template, byte skip)
        {
            log.Add((byte)'F'); log.AddRange([template, army, owner, skip]); int k = _finds.Count > 0 ? _finds.Dequeue() : 0;
            return k == 0 ? null : (k == 1 ? Obj1 : Obj2);
        }
        public byte[] Obj1 = new byte[0x40], Obj2 = new byte[0x40];
        public void ClearQuestField(byte[] record) => Part12 = 0;
        public byte TemplateByte3 => (byte)c.GetProperty("dat3").GetInt32();
        public ushort TemplateBase => (ushort)c.GetProperty("dat48").GetInt32();
        public bool Service1BF(uint edx) { log.Add((byte)'Z'); log.AddRange(BitConverter.GetBytes(edx)); return _zf.Count > 0 ? _zf.Dequeue() : true; }
        public (bool Found, uint X, uint Y) FreeSpot(byte al, byte ah, byte bl, uint x, uint y)
        {
            log.Add((byte)'P'); log.AddRange([al, ah, bl]); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y));
            var (ok, sx, sy) = _spot.Count > 0 ? _spot.Dequeue() : (true, 0u, 0u); return (ok, sx, sy);   // the stub's ZF = success of the service
        }
        public (bool Found, byte Al, byte Ah, byte Bl) Beside(byte al, byte ah)
        {
            log.Add((byte)'B'); log.AddRange([al, ah]);
            var (zf, a, h, b) = _beside.Count > 0 ? _beside.Dequeue() : (true, (byte)0, (byte)0, (byte)0); return (!zf, a, h, b);
        }
        public (bool Created, byte[]? Object) Create(byte army, byte owner, byte template, byte layer, uint x, uint y)
        {
            log.Add((byte)'K'); log.AddRange([army, owner, template, layer]); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y));
            bool zf = _create.Count > 0 ? _create.Dequeue() : true; return (zf, new byte[0x40]);   // ZF = created
        }
        public void Occupy(byte[] record) { log.Add((byte)'O'); log.AddRange(BitConverter.GetBytes(0x600u)); }
        public IEnumerable<byte> OwnTemplates(byte[] record) => c.GetProperty("units").EnumerateArray().Select(a => (byte)a.GetInt32());
        public void Effect(byte[] record, byte type, byte layer, uint x, uint y) { log.Add((byte)'E'); log.Add(type); log.Add(layer); log.AddRange(BitConverter.GetBytes(0x800u)); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y)); }
        public void Sound(uint id) { log.Add((byte)'S'); log.AddRange(BitConverter.GetBytes(id)); }
    }

    private void ObjectScriptReplay(string file, string key, string label, Action<IObjectScriptWorld, byte[], int> script, int minEffects = 5, bool extra = false, bool part = false, bool idle = false, bool manuscript = false, bool itemobj = false)
    {
        using var reference = GameData.Read<JsonDocument>($"res://tests/fixtures/{file}.json");
        var root = reference.RootElement;
        var random = root.GetProperty("random_values").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, effects = 0;
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var d) ? d : -1;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var r = new byte[0x50]; BitConverter.GetBytes((uint)c.GetProperty("x").GetInt32()).CopyTo(r, 4); BitConverter.GetBytes((uint)c.GetProperty("y").GetInt32()).CopyTo(r, 8);
            r[0x15] = (byte)c.GetProperty("f15").GetInt32(); r[0x14] = (byte)c.GetProperty("f14").GetInt32(); r[0x0d] = (byte)c.GetProperty("f0d").GetInt32(); r[0x19] = (byte)c.GetProperty("f19").GetInt32(); r[0x2d] = (byte)c.GetProperty("f2d").GetInt32(); BitConverter.GetBytes((uint)c.GetProperty("f26").GetInt64()).CopyTo(r, 0x26); r[0x2a] = (byte)c.GetProperty("f2a").GetInt32();
            BitConverter.GetBytes((ushort)c.GetProperty("w1").GetInt32()).CopyTo(r, 1); BitConverter.GetBytes((uint)c.GetProperty("f22").GetInt64()).CopyTo(r, 0x22);
            r[0x27] = (byte)c.GetProperty("f27").GetInt32(); r[0x28] = (byte)c.GetProperty("mask28").GetInt32(); r[0x31] = (byte)c.GetProperty("f31").GetInt32();
            r[0] = (byte)c.GetProperty("e0").GetInt32(); BitConverter.GetBytes((uint)c.GetProperty("e12").GetInt64()).CopyTo(r, 0x12);
            r[1] = (byte)c.GetProperty("ef1").GetInt32(); r[5] = (byte)c.GetProperty("ef5").GetInt32(); BitConverter.GetBytes((uint)c.GetProperty("ef7").GetInt64()).CopyTo(r, 7); BitConverter.GetBytes((uint)c.GetProperty("efc").GetInt64()).CopyTo(r, 0xc);
            r[0x16] = (byte)c.GetProperty("ef16").GetInt32(); r[0x17] = (byte)c.GetProperty("ef17").GetInt32(); BitConverter.GetBytes((uint)c.GetProperty("ef18").GetInt64()).CopyTo(r, 0x18); BitConverter.GetBytes((uint)c.GetProperty("ef1c").GetInt64()).CopyTo(r, 0x1c);
            var log = new List<byte>(); var world = new ObjectReplay(c, random, log); Array.Copy(world.ObjectBytes, 0, world.Obj1, 0, 0x40); Array.Copy(world.ObjectBytes, 0x40, world.Obj2, 0, 0x40);
            ushort ax = 0;
            if (itemobj) { ax = OriginalObjectScripts.Pickup(world, r, c.GetProperty("ah").GetInt32(), c.GetProperty("k").GetInt32(), (byte)c.GetProperty("bl").GetInt32()); }
            else if (manuscript) { r[0x28] = (byte)c.GetProperty("mask28").GetInt32(); ax = OriginalObjectScripts.Manuscript(world, r, c.GetProperty("ah").GetInt32(), new byte[] { 0x18, 0x19, 0x1b }[c.GetProperty("k").GetInt32()], (byte)c.GetProperty("vowner").GetInt32()); }
            else script(world, r, c.GetProperty("ah").GetInt32());
            if (c.GetProperty("ah").GetInt32() != 8) ax = 0;   // AX is meaningful for the visitor's call only
            byte[] output = itemobj || manuscript ? [.. BitConverter.GetBytes(ax), .. r, world.I, world.J, .. log] : part ? [.. r, world.I, world.J, .. BitConverter.GetBytes(world.Part12), .. log] : extra ? [.. r, world.I, world.J, .. world.Obj1, .. world.Obj2, .. BitConverter.GetBytes(world.Part12), .. log] : [.. r, world.I, world.J, .. log];
            digest.AppendData(output); if (log.Contains((byte)'E')) effects++;
            string hex = Convert.ToHexStringLower(SHA256.HashData(output))[..10];
            if (hex != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"{key}: first mismatch {firstMismatch}; {effects} effects");
        var summary = root.GetProperty("results").GetProperty(key);
        Check(count == summary.GetProperty("case_count").GetInt32() && effects >= minEffects, $"{key} reference coverage ({count} cases, {effects} effects)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), $"{label} matches original x86");
    }

    private void OriginalObjectScriptChecks()
    {
        ObjectScriptReplay("obj-rockfall", "rockfall", "The rockfall script ARMY__03.WRK 189", OriginalObjectScripts.Rockfall);
        ObjectScriptReplay("obj-wall", "wall", "The stone wall script ARMY__03.WRK 3F8", OriginalObjectScripts.Wall, 0);
        ObjectScriptReplay("obj-head", "head", "The knight's head script ARMY__03.WRK 522", OriginalObjectScripts.Head, 0);
        ObjectScriptReplay("obj-tree", "tree", "The living fir script ARMY__03.WRK 624", OriginalObjectScripts.Tree, 0);
        ObjectScriptReplay("obj-raven", "raven", "The raven script ARMY__03.WRK 6BA", OriginalObjectScripts.Raven, 0);
        ObjectScriptReplay("obj-chest", "chest", "The chest with the birds ARMY__03.WRK 2A6B", OriginalObjectScripts.Chest, 0);
        ObjectScriptReplay("obj-generator", "generator", "The generator script ARMY__03.WRK 29F1", OriginalObjectScripts.Generator, 0);
        ObjectScriptReplay("obj-dragon", "dragon", "The dragon script ARMY__03.WRK 279A", OriginalObjectScripts.Dragon, 0, true);
        ObjectScriptReplay("obj-phantom32", "phantom32", "The phantom 32 script ARMY__03.WRK 2BA7", OriginalObjectScripts.Phantom32, 0, false, true);
        ObjectScriptReplay("obj-phantom32idle", "phantom32idle", "The phantom 32 idle handler ARMY__03.WRK 2C1D", (w, r, ah) => OriginalObjectScripts.Phantom32Idle(w, r), 0, false, true, true);
        ObjectScriptReplay("obj-jugphantom", "jugphantom", "The jug with the phantom ARMY__03.WRK 2C6D", OriginalObjectScripts.JugPhantom, 0, false, true);
        ObjectScriptReplay("obj-talking", "talking", "The talking object ARMY__03.WRK 2D08", OriginalObjectScripts.Talking, 0, false, true);
        ObjectScriptReplay("obj-faceb", "faceb", "The second face ARMY__03.WRK 2D52", OriginalObjectScripts.FaceB, 0, false, true);
        ObjectScriptReplay("obj-rockfallq", "rockfallq", "The quest rockfall ARMY__03.WRK 2E19", OriginalObjectScripts.RockfallQuest, 0, false, true);
        ObjectScriptReplay("obj-manuscript", "manuscript", "The manuscripts ARMY__03.WRK 2710", (w, r, ah) => { }, 0, false, false, false, true);
        ObjectScriptReplay("obj-switch", "switch", "The switch ARMY__00.WRK 52", OriginalObjectScripts.Switch, 0, true);
        ObjectScriptReplay("obj-switch2", "switch2", "The second switch ARMY__00.WRK 371", OriginalObjectScripts.Switch2, 0, true);
        ObjectScriptReplay("obj-exit", "exit", "The exit ARMY__00.WRK 3A9", OriginalObjectScripts.Exit, 0, false, true);
        ObjectScriptReplay("obj-itemobj", "itemobj", "The item objects ARMY__00.WRK 86..4F3", (w, r, ah) => { }, 0, false, false, false, false, true);
        ObjectScriptReplay("obj-fx54a", "fx54a", "The effect ARMY__00.WRK 54A", (w, r, ah) => { var parent = new byte[0x40]; parent[0x25] = ((ObjectReplay)w).Parent25; OriginalObjectScripts.Fx54a(w, r, ah, parent); }, 0);
        ObjectScriptReplay("obj-fx6fe", "fx6fe", "The spark ARMY__00.WRK 6FE", (w, r, ah) => OriginalObjectScripts.Spark(w, r, ah, false), 0);
        ObjectScriptReplay("obj-fx735", "fx735", "The hard spark ARMY__00.WRK 735", (w, r, ah) => OriginalObjectScripts.Spark(w, r, ah, true), 0);
        ObjectScriptReplay("obj-fx7fe", "fx7fe", "The smoke ARMY__00.WRK 7FE", (w, r, ah) => OriginalObjectScripts.Fx7fe(w, r, ah, ((ObjectReplay)w).Bl), 0);
        ObjectScriptReplay("obj-fx8da", "fx8da", "The flame ARMY__00.WRK 8DA", OriginalObjectScripts.Fx8da, 0);
        ObjectScriptReplay("obj-fxa6c", "fxa6c", "The explosion ARMY__00.WRK A6C", (w, r, ah) => OriginalObjectScripts.Explosion(w, r, ah, ((ObjectReplay)w).Bl, false), 0);
        ObjectScriptReplay("obj-fxab4", "fxab4", "The large explosion ARMY__00.WRK AB4", (w, r, ah) => OriginalObjectScripts.Explosion(w, r, ah, ((ObjectReplay)w).Bl, true), 0);
        ObjectScriptReplay("obj-fxaeb", "fxaeb", "The following mark ARMY__00.WRK AEB", (w, r, ah) => OriginalObjectScripts.Fxaeb(w, r, ah, ((ObjectReplay)w).Bl), 0, true);
        ObjectScriptReplay("obj-fxb62", "fxb62", "The effect ARMY__00.WRK B62", (w, r, ah) => OriginalObjectScripts.Fx54aKind16(w, r, ah, Enumerable.Range(0, 0x40).Select(i => i == 0x25 ? ((ObjectReplay)w).Parent25 : (byte)0).ToArray()), 0);
        ObjectScriptReplay("obj-fxfd1", "fxfd1", "The effect ARMY__00.WRK FD1", (w, r, ah) => OriginalObjectScripts.FxKind17(w, r, ah, Enumerable.Range(0, 0x40).Select(i => i == 0x25 ? ((ObjectReplay)w).Parent25 : (byte)0).ToArray()), 0);
        ObjectScriptReplay("obj-fx1012", "fx1012", "The burst ARMY__00.WRK 1012", (w, r, ah) => OriginalObjectScripts.Fx1012(w, r, ah, 0xa00), 0);
        ObjectScriptReplay("obj-fx10b3", "fx10b3", "The hit spark ARMY__00.WRK 10B3", (w, r, ah) => { var p = new byte[0x40]; BitConverter.GetBytes((uint)new[] { 0, 0x100, 0x140 }[((ObjectReplay)w).K % 3]).CopyTo(p, 0x1c); OriginalObjectScripts.FxHit(w, r, p); }, 0);
        ObjectScriptReplay("obj-fx1105", "fx1105", "The wandering charm ARMY__00.WRK 1105", (w, r, ah) => OriginalObjectScripts.Fx1105(w, r, ah, (uint)((ObjectReplay)w).Bl, 0x400000), 0, true);
        byte[] Par(IObjectScriptWorld w) => Enumerable.Range(0, 0x40).Select(i => i == 0x25 ? ((ObjectReplay)w).Parent25 : (byte)0).ToArray();
        ObjectScriptReplay("obj-m2f4e", "m2f4e", "Mage effect ARMY__03.WRK 2F4E", (w, r, ah) => OriginalObjectScripts.MageFx2f4e(w, r, ah, Par(w)), 0);
        ObjectScriptReplay("obj-m30a0", "m30a0", "Mage effect ARMY__03.WRK 30A0", OriginalObjectScripts.MageFx30a0, 0);
        ObjectScriptReplay("obj-m31e7", "m31e7", "Mage effect ARMY__03.WRK 31E7", (w, r, ah) => OriginalObjectScripts.MageFx31e7(w, r, ah, Par(w)), 0);
        ObjectScriptReplay("obj-m333a", "m333a", "Mage effect ARMY__03.WRK 333A", OriginalObjectScripts.MageFx333a, 0);
        ObjectScriptReplay("obj-m336e", "m336e", "Mage effect ARMY__03.WRK 336E", OriginalObjectScripts.MageFx336e, 0);
        ObjectScriptReplay("obj-m33e0", "m33e0", "Mage effect ARMY__03.WRK 33E0", (w, r, ah) => OriginalObjectScripts.MageFx33e0(w, r, ah, ((ObjectReplay)w).Bl, ((ObjectReplay)w).Bh), 0);
        ObjectScriptReplay("obj-m340f", "m340f", "Mage effect ARMY__03.WRK 340F", (w, r, ah) => OriginalObjectScripts.MageFx340f(w, r, ah, ((ObjectReplay)w).Bl), 0);
        ObjectScriptReplay("obj-mblast", "mblast", "Mage blasts ARMY__03.WRK 2F75..2FD9", (w, r, ah) => OriginalObjectScripts.MageBlast(w, r, ((ObjectReplay)w).ParentRecord(), 0x400a00, ((ObjectReplay)w).K % 3), 0, true);
        ObjectScriptReplay("obj-mspray", "mspray", "Mage spray ARMY__03.WRK 300B..31D0", (w, r, ah) => OriginalObjectScripts.MageSpray(w, r, ah, ((ObjectReplay)w).ParentRecord(), ((ObjectReplay)w).K), 0);
        ObjectScriptReplay("obj-face", "face", "The stone face script ARMY__03.WRK 2B9", OriginalObjectScripts.Face);
    }
}
