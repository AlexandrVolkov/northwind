using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/ai-military.json (tools/verify_ai_military.py): military requests 20DD0.</summary>
    private void OriginalAiMilitaryChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/ai-military.json");
        var root = reference.RootElement;
        var dat = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates
            .Select(t => Convert.FromHexString(t.RawRecordHex)).ToArray();
        var tree = GameData.Read<OriginalTechData>("res://data/original_tech.json").Armies[2].Build();
        Check(Convert.ToHexStringLower(SHA256.HashData(tree)) == root.GetProperty("tree_sha256").GetString(), "AI military tree matches ARMY__02.TRE");
        var random = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json").RandomValues.ToArray();
        var classes = Convert.FromHexString(root.GetProperty("gather_classes_hex").GetString()!);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const int S = OriginalAiSquads.Size, Mil = OriginalAiMilitary.List, Ord = OriginalAiMilitary.Orders;
        int count = 0, firstMismatch = -1; var modes = new int[11];
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            byte p = (byte)c.GetProperty("participant").GetInt32(); int n = c.GetProperty("count").GetInt32();
            var world = new MilitaryProbe(dat, tree, random, (byte)c.GetProperty("random").GetInt32(), (byte)n);
            var hostile = c.GetProperty("hostile");
            for (int q = 0; q < 8; q++)
            {
                var rec = new byte[0xf5]; rec[0] = 2;
                if (q < n)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x12), hostile[q].GetUInt32());
                    int i = 0; foreach (var v in c.GetProperty("relations").GetProperty(q.ToString()).EnumerateArray()) rec[0xb9 + i++] = (byte)v.GetInt32();
                }
                world.Participants[q] = rec;
            }
            for (int q = 0; q < n; q++)
            {
                var b = new byte[0x1a35]; var v = c.GetProperty("blocks").GetProperty(q.ToString()); b[0] = (byte)q;
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x19e9), v.GetProperty("strength").GetUInt32());
                b[0x61] = (byte)v.GetProperty("home")[0].GetInt32(); b[0x62] = (byte)v.GetProperty("home")[1].GetInt32();
                byte lx = (byte)v.GetProperty("land")[0].GetInt32(), ly = (byte)v.GetProperty("land")[1].GetInt32();
                b[0x63] = lx; b[0x64] = ly; b[0x65] = (byte)(lx + 8); b[0x66] = (byte)(ly + 8);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x45), v.GetProperty("45").GetUInt32());
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x13), v.GetProperty("13").GetUInt32());
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x0e), v.GetProperty("0e").GetUInt32());
                b[0x52] = (byte)v.GetProperty("52").GetInt32(); b[0x1a01] = (byte)v.GetProperty("1a01").GetInt32();
                b[0x2a] = (byte)v.GetProperty("2a").GetInt32(); b[0x19e4] = (byte)v.GetProperty("flags").GetInt32();
                for (int k = 0; k < 20; k++) { b[Mil + k * 0x15] = 0xff; b[Ord + k * 0x15] = 0xff; }
                world.Blocks[q] = b;
            }
            var ai = world.Blocks[p];
            int oi = 0; foreach (var m in c.GetProperty("orders").EnumerateArray()) ai[Ord + oi++ * 0x15] = (byte)m.GetInt32();
            var objectList = c.GetProperty("objects").EnumerateArray().ToList();
            uint at = 0x100; var refs = new uint[objectList.Count];
            for (int q = 0; q < n; q++)
                for (int i = 0; i < objectList.Count; i++)
                {
                    var ob = objectList[i];
                    if (ob.GetProperty("owner").GetInt32() != q) continue;
                    int t = ob.GetProperty("t").GetInt32(), x = ob.GetProperty("cell")[0].GetInt32(), y = ob.GetProperty("cell")[1].GetInt32();
                    var unit = CombatUnit((byte)t, (byte)q, (byte)ob.GetProperty("hp").GetInt32(), 0, (uint)(x * 32 + 9), (uint)(y * 32 + 13), 1);
                    var rec = new byte[0x40]; unit.CopyTo(rec, 0);
                    rec[0x1b] = (byte)ob.GetProperty("squad").GetInt32(); rec[0x23] = (byte)ob.GetProperty("kind").GetInt32();
                    BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x24), ob.GetProperty("stock").GetUInt32());
                    BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x10), 0x40);
                    world.Add((byte)q, at, rec, dat[t], y * 32 + x); refs[i] = at; at += 0x40;
                }
            var squads = c.GetProperty("squads"); ai[OriginalAiSquads.List] = (byte)squads.GetArrayLength();
            int sk = 0;
            foreach (var s in squads.EnumerateArray())
            {
                int r = OriginalAiSquads.Record(sk);
                for (int i = 0; i < S; i++) ai[r + i] = unchecked((byte)(i * 3 + sk * 17));
                ai[r] = p; ai[r + 1] = (byte)s[0].GetInt32(); ai[r + 2] = (byte)s[1].GetInt32(); ai[r + 5] = (byte)s[2].GetInt32(); ai[r + 4] = (byte)s[3].GetInt32();
                ai[r + 0x20] = (byte)s[4][0].GetInt32(); ai[r + 0x21] = (byte)s[4][1].GetInt32();
                BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(r + 0x36), s[5].GetUInt32());
                ai[OriginalAiSquads.Ids + ai[r + 1]] = 1; sk++;
            }
            int rk = 0;
            foreach (var r in c.GetProperty("requests").EnumerateArray())
            {
                int a = Mil + rk * 0x15;
                for (int i = 0; i < 0x15; i++) ai[a + i] = unchecked((byte)(i * 11 + rk));
                int mode = r.GetProperty("mode").GetInt32(); if (mode <= 10) modes[mode]++;
                ai[a] = (byte)mode; ai[a + 1] = (byte)r.GetProperty("first").GetInt32(); ai[a + 2] = (byte)r.GetProperty("stage").GetInt32(); ai[a + 3] = (byte)r.GetProperty("prio").GetInt32();
                ai[a + 4] = (byte)r.GetProperty("b4").GetInt32(); ai[a + 5] = (byte)r.GetProperty("b5").GetInt32(); ai[a + 6] = (byte)r.GetProperty("b6").GetInt32(); ai[a + 7] = (byte)r.GetProperty("b7").GetInt32();
                uint param = r.GetProperty("param").GetUInt32();
                if (mode is 4 or 9) param = param == 0 ? 0 : refs[param - 1];
                BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(a + 8), param);
                ai[a + 0x0c] = (byte)r.GetProperty("squad").GetInt32(); BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(a + 0x10), r.GetProperty("age").GetUInt32());
                ai[a + 0x14] = (byte)(mode == 8 ? r.GetProperty("squad").GetInt32() : r.GetProperty("tries").GetInt32());
                rk++;
            }
            var military = new OriginalAiMilitary(world, classes) { ClassMask = c.GetProperty("class")[0].GetUInt32(), ClassValue = c.GetProperty("class")[1].GetUInt32() };
            byte fault = 0;
            try { military.Run(ai); } catch (Exception e) when (e is OverflowException or DivideByZeroException) { fault = 1; }
            byte[] output;
            if (fault != 0) output = [1];
            else
            {
                var final = (byte[])ai.Clone();
                for (int k = 0; k < 20; k++)
                {
                    int a = Mil + k * 0x15; final[a + 7] = 0;
                    if (final[a] != 8) Array.Clear(final, a + 5, 2);
                    if (final[a] == 6) Array.Clear(final, a + 8, 4);
                    a = Ord + k * 0x15; Array.Clear(final, a + 4, 4);
                }
                output = [0, .. final.AsSpan(0x7b, 256), .. final.AsSpan(OriginalAiSquads.List, 1 + S * final[OriginalAiSquads.List]),
                    .. final.AsSpan(Mil, 0x15 * 20), .. final.AsSpan(Ord, 0x15 * 20), .. world.AllRecords(), world.RandomIndex];
            }
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0)
            { firstMismatch = count; GD.Print("MISMATCH " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"AI military: requests per mode {string.Join(' ', modes)}; first mismatch {firstMismatch}");
        var result = root.GetProperty("results").GetProperty("military");
        Check(count == result.GetProperty("case_count").GetInt32(), $"AI military reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(), "AI military requests 20DD0 match original x86");
    }

    private sealed class MilitaryProbe(byte[][] dat, byte[] tree, byte[] random, byte index, byte count) : IAiGarrisonWorld, IAiSquadStateWorld, IAiOrdersWorld, IAiEconomyWorld
    {
        private readonly uint[] _grid = new uint[3 * 1024];
        private readonly Dictionary<uint, (byte[], byte[])> _objects = [];
        private readonly Dictionary<byte[], uint> _references = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<byte, List<(byte[] Record, byte[] Template)>> _chains = [];
        private readonly List<byte[]> _order = [];
        public readonly Dictionary<int, byte[]> Blocks = [];
        public readonly byte[][] Participants = new byte[8][];
        public byte RandomIndex = index;
        public void Add(byte owner, uint reference, byte[] record, byte[] template, int cell)
        {
            _objects[reference] = (record, template); _references[record] = reference; _grid[1024 + cell] = reference; _order.Add(record);
            if (!_chains.TryGetValue(owner, out var chain)) _chains[owner] = chain = [];
            chain.Add((record, template));
        }
        public byte[] AllRecords() => _order.SelectMany(r => r).ToArray();
        public IReadOnlyList<(byte[] Record, byte[] Template)> Chain(byte participant) => _chains.TryGetValue(participant, out var c) ? c : [];
        public (byte[] Record, byte[] Template) Resolve(uint reference) => _objects.TryGetValue(reference, out var o) ? o : (new byte[0x40], new byte[171]); // pool memory before the chain is zero
        public uint Occupant(int plane, int cell) => _grid[plane * 1024 + cell];
        public byte MapWidth => 32;
        public byte MapHeight => 32;
        public uint HostileMask(byte participant) => BinaryPrimitives.ReadUInt32LittleEndian(Participants[participant].AsSpan(0x12));
        public (uint X, uint Y) BoundsLimit => (0x400, 0x400);
        public bool StepFour(Span<byte> squad, bool started) => throw new NotSupportedException();
        public byte Random() => random[RandomIndex++];
        public byte RandomIndex2, RandomIndex3;
        public byte Random2() => random[RandomIndex2++];
        public byte Random3() => random[RandomIndex3++];
        public byte Global19E9 => 0xff;
        public void Message(ReadOnlySpan<byte> squad, uint text) { }
        public void Rescue(Span<byte> squad) { }
        public void WorkerState(Span<byte> ai, int at) { }
        public Span<byte> Block(byte participant) => Blocks[participant];
        public ReadOnlySpan<byte> Participant(byte participant) => Participants[participant];
        public byte ParticipantCount => count;
        public IReadOnlyList<byte> Buildable(byte participant, uint mask, uint value) => OriginalTechRules.Buildable(dat, tree, true, mask, value);
        public byte[] Template(byte participant, byte template) => dat[template];
        public uint Reference(byte[] record) => _references[record];
        public Dictionary<byte, byte[]> Producers = [];
        public Func<int, byte> Terrain = _ => 1;
        public byte[]? Pattern;
        public byte Seed;
        public readonly List<byte> PlacementLog = [];
        public IReadOnlyList<byte> Makes(byte template) => Producers.TryGetValue(template, out var m) ? m : [];
        public byte TemplateCount => (byte)dat.Length;
        public bool PlacementAllowed(byte template, byte army, byte participant, uint x, uint y)
        {
            PlacementLog.Add(template); PlacementLog.AddRange(BitConverter.GetBytes((ushort)(template | army << 8)));
            PlacementLog.AddRange(BitConverter.GetBytes(x)); PlacementLog.AddRange(BitConverter.GetBytes(y));
            return ((x ^ y ^ template ^ Seed) & 3) != 0;
        }
        public byte TileClass(int cell) => Terrain(cell);
        public byte[]? Fortification(uint index) => index == 0 ? Pattern : null;
        public bool TreeAllows(byte participant, byte template) => OriginalTechRules.Available(tree, template);
        public Func<int, byte> FogAt = _ => 0, OreAt = _ => 0;
        public Span<byte> ParticipantRecord(byte participant) => Participants[participant];
        public byte Fog(int cell) => FogAt(cell);   // default 0: everything seen
        public byte TileOre(int cell) => OreAt(cell);
        public bool SiteAllowed(byte template, byte army, byte participant, byte x, byte y)
        {
            PlacementLog.Add(template); PlacementLog.AddRange(BitConverter.GetBytes((ushort)(template | army << 8))); PlacementLog.Add(x); PlacementLog.Add(y);
            return ((x ^ y ^ template ^ Seed) & 3) != 0;
        }
        public byte BuildSeed;
        public readonly byte[] Scratch = new byte[0x40];
        public (uint X, uint Y)? FreeSpot(uint templateArmy, byte variant, uint x, uint y)
        {
            PlacementLog.Add((byte)'F'); PlacementLog.AddRange(BitConverter.GetBytes((ushort)templateArmy)); PlacementLog.Add(variant);
            PlacementLog.AddRange(BitConverter.GetBytes(x)); PlacementLog.AddRange(BitConverter.GetBytes(y));
            return ((x >> 5) ^ (y >> 5) ^ BuildSeed) % 3 != 0 ? (x + 0x20, y) : null;
        }
        public bool BuildingSite(uint templateArmy, byte participant, byte x, byte y)
        {
            PlacementLog.Add((byte)'S'); PlacementLog.AddRange(BitConverter.GetBytes((ushort)templateArmy)); PlacementLog.Add(participant); PlacementLog.Add(x); PlacementLog.Add(y);
            return ((x ^ y ^ BuildSeed) & 1) == 0;
        }
        public byte[]? Create(byte participant, byte army, byte template, byte variant, uint x, uint y)
        {
            PlacementLog.Add((byte)'A'); PlacementLog.AddRange(BitConverter.GetBytes((ushort)(army | participant << 8)));
            PlacementLog.AddRange(BitConverter.GetBytes((ushort)(template | variant << 8))); PlacementLog.AddRange(BitConverter.GetBytes(x)); PlacementLog.AddRange(BitConverter.GetBytes(y));
            Array.Clear(Scratch); return (BuildSeed & 4) == 0 ? Scratch : null;
        }
        public void Register(byte[] record) { PlacementLog.Add((byte)'R'); PlacementLog.AddRange(BitConverter.GetBytes(0x3f000u)); }
        public void Send(byte type, byte other, byte owner, byte bh, uint parameter)
        { PlacementLog.Add((byte)'M'); PlacementLog.Add(type); PlacementLog.Add(other); PlacementLog.Add(owner); PlacementLog.AddRange(BitConverter.GetBytes(parameter)); }
    }
}
