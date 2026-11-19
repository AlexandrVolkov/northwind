using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalAiChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/ai-census.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"AI reference template {item.Name} matches bundled DAT");
        using var census = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var threat = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var ai = new byte[0x1a35]; var participant = new byte[0xf5];
            BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(1), c.GetProperty("turn").GetUInt32()); ai[5] = (byte)c.GetProperty("mode").GetInt32();
            BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(0x6b), c.GetProperty("previous_gold").GetUInt32());
            var r = c.GetProperty("resources"); uint g = r[0].GetUInt32(), m = r[1].GetUInt32(), w = r[2].GetUInt32(), people = r[3].GetUInt32();
            BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x18), m); BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x1c), w);
            BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x20), g); BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x24), people);
            BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x28), c.GetProperty("support")[1].GetUInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x2c), c.GetProperty("support")[0].GetUInt32());
            var world = new CensusProbe();
            var chain = new List<(byte[], byte[])>(); uint at = 0x100;
            foreach (var o in c.GetProperty("objects").EnumerateArray())
            {
                int t = o[0].GetInt32();
                var rec = CombatUnit((byte)t, (byte)o[1].GetInt32(), (byte)o[4].GetInt32(), 0, o[2].GetUInt32(), o[3].GetUInt32(), 1);
                rec[0x14] = (byte)o[5].GetInt32(); rec[0x23] = (byte)o[6].GetInt32(); BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x24), o[7].GetUInt32());
                BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x10), 0x40);
                chain.Add((rec, Dat(t))); world.Add(at, rec, Dat(t)); at += 0x40;
            }
            var bounds = OriginalAiRules.Census(ai, participant, chain, 32, 32);
            census.AppendData(ai.AsSpan(0, 0x80)); census.AppendData(ai.AsSpan(0x19e0, 0x50));
            var globals = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(globals, bounds.MaxX); BinaryPrimitives.WriteUInt32LittleEndian(globals.AsSpan(4), bounds.MaxY);
            BinaryPrimitives.WriteUInt32LittleEndian(globals.AsSpan(8), bounds.MinX); BinaryPrimitives.WriteUInt32LittleEndian(globals.AsSpan(12), bounds.MinY);
            census.AppendData(globals);
            var values = OriginalAiRules.Threat(ai, world);
            var encoded = new byte[64]; for (int i = 0; i < 16; i++) BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(i * 4), values[i]);
            threat.AppendData(encoded); count++;
        }
        var results = root.GetProperty("results");
        Check(count == results.GetProperty("census").GetProperty("case_count").GetInt32(), $"AI census reference coverage ({count})");
        Check(Convert.ToHexStringLower(census.GetHashAndReset()) == results.GetProperty("census").GetProperty("sha256").GetString(), "AI census 1E87A matches original x86");
        Check(Convert.ToHexStringLower(threat.GetHashAndReset()) == results.GetProperty("threat").GetProperty("sha256").GetString(), "Territory threat 23B10 matches original x86");
    }
    private sealed class CensusProbe : IEnemySearchWorld
    {
        private readonly uint[] _grid = new uint[3 * 1024];
        private readonly Dictionary<uint, (byte[], byte[])> _objects = [];
        public byte MapWidth => 32;
        public byte MapHeight => 32;
        public uint HostileMask => 0;
        public void Register(uint reference, byte[] record, byte[] template) => _objects[reference] = (record, template);
        public void Add(uint reference, byte[] record, byte[] template)
        {
            _objects[reference] = (record, template);
            _grid[1024 + (int)(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8)) >> 5) * 32 + (int)(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4)) >> 5)] = reference;
        }
        public uint Occupant(int plane, int cell) => _grid[plane * 1024 + cell];
        public (byte[] Record, byte[] Template) Resolve(uint reference) => _objects[reference];
        public bool Contact(byte owner) => true;
        public byte TerrainClass(ReadOnlySpan<byte> record) => 0;
        public void Contacted(FirstContact contact) { }
    }
}

public partial class TestRunner
{
    private void OriginalAiInitChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/ai-init.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        var table = GameData.Read<OriginalAiData>("res://data/original_ai.json").Table();
        Check(table.Length == OriginalAiRules.CharacterSize * OriginalAiRules.CharacterCount, "AI character table has 9 records");
        Check(Convert.ToHexStringLower(SHA256.HashData(table)) == root.GetProperty("character_table_sha256").GetString(), "AI character table matches LE:3 ED43");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            byte player = (byte)c.GetProperty("player").GetInt32();
            uint w = c.GetProperty("width").GetUInt32(), h = c.GetProperty("height").GetUInt32();
            byte index = 0;
            foreach (var part in c.GetProperty("participants").EnumerateArray())
            {
                var participant = new byte[0xf5]; participant[0x16] = (byte)part.GetProperty("character").GetInt32();
                var chain = new List<(byte[], byte[])>();
                foreach (var o in part.GetProperty("objects").EnumerateArray())
                {
                    int t = o[0].GetInt32();
                    var rec = CombatUnit((byte)t, index, (byte)o[3].GetInt32(), 0, o[1].GetUInt32(), o[2].GetUInt32(), 1);
                    rec[0x14] = (byte)o[4].GetInt32(); BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x10), 0x40);
                    chain.Add((rec, Dat(t)));
                }
                var ai = new byte[OriginalAiRules.BlockSize]; Array.Fill(ai, (byte)0x5a);
                OriginalAiRules.Initialize(ai, participant, index, player, table, chain, w, h);
                digest.AppendData(ai); digest.AppendData(participant.AsSpan(0x16, 1));
                index++;
            }
            count++;
        }
        var result = root.GetProperty("results").GetProperty("init");
        Check(count == result.GetProperty("case_count").GetInt32(), $"AI init reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(), "AI block initialisation 1E53A matches original x86");
    }
}

public partial class TestRunner
{
    private void OriginalAiTurnChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/ai-turn.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, stopped = 0, plans = 0; var sent = new Dictionary<byte, int>();
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            byte me = (byte)c.GetProperty("me").GetInt32();
            var parts = c.GetProperty("participants").EnumerateArray().ToArray();
            var world = new AiTurnProbe(parts.Length);
            for (int p = 0; p < parts.Length; p++) FillAiParticipant(world, p, parts[p], p == me ? c.GetProperty("turn").GetUInt32() : 0);
            uint at = 0x100;
            foreach (var o in c.GetProperty("objects").EnumerateArray())
            {
                int t = o[0].GetInt32();
                var rec = CombatUnit((byte)t, (byte)o[1].GetInt32(), (byte)o[4].GetInt32(), 0, o[2].GetUInt32(), o[3].GetUInt32(), 1);
                world.Add(at, rec, Dat(t)); at += 0x40;
            }
            int before = CountPlans(world.Block(me));
            var threat = Enumerable.Repeat(0x55555555u, 16).ToArray();
            bool reached = OriginalAiTurn.Decide(world, me, c.GetProperty("ebp").GetUInt32(), 32, threat);
            plans += CountPlans(world.Block(me)) - before;
            for (int p = 0; p < parts.Length; p++)
            {
                var block = world.Block(p).ToArray();
                for (int k = 0; k < OriginalAiTurn.PlanCount; k++) block.AsSpan(OriginalAiTurn.Plans + k * OriginalAiTurn.PlanSize + 5, 3).Clear();
                digest.AppendData(block); digest.AppendData(world.Participant(p));
            }
            var encoded = new byte[64]; for (int i = 0; i < 16; i++) BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(i * 4), threat[i]);
            digest.AppendData(encoded);
            var calls = string.Join(", ", world.Calls.Select(x => $"[{x.Type}, {x.Other}, {x.Owner}, {x.Bh}, {x.Parameter}]"));
            digest.AppendData(System.Text.Encoding.UTF8.GetBytes($"[{(reached ? "true" : "false")}, [{calls}]]"));
            foreach (var x in world.Calls) sent[x.Type] = sent.GetValueOrDefault(x.Type) + 1;
            if (reached) stopped++;
            count++;
        }
        var result = root.GetProperty("results").GetProperty("turn");
        Check(count == result.GetProperty("case_count").GetInt32(), $"AI turn reference coverage ({count}, construction stage {stopped}, plans {plans}, messages {string.Join(" ", sent.Select(kv => $"{kv.Key:X}:{kv.Value}"))})");
        Godot.GD.Print($"AI turn: construction stage {stopped}, plans {plans}, messages {string.Join(" ", sent.Select(kv => $"{kv.Key:X}:{kv.Value}"))}");
        Check(stopped > 0 && stopped < count && plans > 100 && sent.ContainsKey(5) && sent.ContainsKey(0xd) && sent.ContainsKey(0x13), "AI turn cases reach every outcome");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(), "AI turn 2323A..23550 matches original x86");
    }

    private static int CountPlans(ReadOnlySpan<byte> ai)
    {
        int n = 0;
        for (int k = 0; k < OriginalAiTurn.PlanCount; k++) if (ai[OriginalAiTurn.Plans + k * OriginalAiTurn.PlanSize] != 0xff) n++;
        return n;
    }

    private static void FillAiParticipant(AiTurnProbe world, int p, JsonElement part, uint turn)
    {
        var b = world.Block(p); b[0] = (byte)p; BinaryPrimitives.WriteUInt32LittleEndian(b[1..], turn);
        var t = part.GetProperty("territory");
        b[0x63] = (byte)t[0].GetInt32(); b[0x64] = (byte)t[2].GetInt32(); b[0x65] = (byte)t[1].GetInt32(); b[0x66] = (byte)t[3].GetInt32();
        BinaryPrimitives.WriteUInt32LittleEndian(b[0x35..], part.GetProperty("limits")[0].GetUInt32());
        BinaryPrimitives.WriteUInt32LittleEndian(b[0x3d..], part.GetProperty("limits")[1].GetUInt32());
        BinaryPrimitives.WriteUInt32LittleEndian(b[0x6f..], part.GetProperty("loss").GetUInt32());
        BinaryPrimitives.WriteUInt32LittleEndian(b[0x19f1..], part.GetProperty("strength").GetUInt32());
        var previous = part.GetProperty("previous");
        for (int i = 0; i < 3; i++) BinaryPrimitives.WriteUInt32LittleEndian(b[(0x1a28 + 4 * i)..], previous[i].GetUInt32());
        b[0x1a27] = (byte)part.GetProperty("flags").GetInt32();
        int[] at = [0x19e4, 0x19fd, 0x19fe, 0x19ff, 0x1a15, 0x1a34]; var bytes = part.GetProperty("bytes");
        for (int i = 0; i < 6; i++) b[at[i]] = (byte)bytes[i].GetInt32();
        for (int k = 0; k < 20; k++) b[OriginalAiTurn.Plans + k * 0x15] = 0xff;
        int slot = 0;
        foreach (var plan in part.GetProperty("plans").EnumerateArray())
        {
            b[OriginalAiTurn.Plans + slot * 0x15] = (byte)plan[0].GetInt32(); b[OriginalAiTurn.Plans + slot * 0x15 + 1] = (byte)plan[1].GetInt32();
            b[OriginalAiTurn.Plans + slot * 0x15 + 4] = (byte)plan[2].GetInt32(); slot++;
        }
        var r = world.Participant(p); var res = part.GetProperty("resources");
        BinaryPrimitives.WriteUInt32LittleEndian(r[0x18..], res[1].GetUInt32()); BinaryPrimitives.WriteUInt32LittleEndian(r[0x1c..], res[2].GetUInt32());
        BinaryPrimitives.WriteUInt32LittleEndian(r[0x20..], res[0].GetUInt32());
        int q = 0; foreach (var rel in part.GetProperty("relations").EnumerateArray()) r[0xb9 + q++] = (byte)rel.GetInt32();
        for (int s = 0; s < 8; s++) { r[0x34 + s * 14] = 0xff; }
        int m = 0;
        foreach (var msg in part.GetProperty("queue").EnumerateArray()) { r[0x34 + m * 14] = (byte)msg[0].GetInt32(); r[0x34 + m * 14 + 9] = (byte)msg[1].GetInt32(); m++; }
    }

    private sealed class AiTurnProbe(int count) : IAiTurnWorld
    {
        private readonly byte[][] _blocks = Enumerable.Range(0, count).Select(_ => new byte[OriginalAiRules.BlockSize]).ToArray();
        private readonly byte[][] _participants = Enumerable.Range(0, count).Select(_ => new byte[0xf5]).ToArray();
        private readonly uint[] _grid = new uint[3 * 1024];
        private readonly Dictionary<uint, (byte[], byte[])> _objects = [];
        public List<(byte Type, byte Other, byte Owner, byte Bh, uint Parameter)> Calls { get; } = [];
        public int Count => count;
        public Span<byte> Block(int participant) => _blocks[participant];
        public Span<byte> Participant(int participant) => _participants[participant];
        public void Send(byte type, byte other, byte owner, byte bh, uint parameter) => Calls.Add((type, other, owner, bh, parameter));
        public void Register(uint reference, byte[] record, byte[] template) => _objects[reference] = (record, template);
        public void Add(uint reference, byte[] record, byte[] template)
        {
            _objects[reference] = (record, template);
            _grid[1024 + (int)(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8)) >> 5) * 32 + (int)(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4)) >> 5)] = reference;
        }
        public byte MapWidth => 32;
        public byte MapHeight => 32;
        public uint HostileMask => 0;
        public uint Occupant(int plane, int cell) => _grid[plane * 1024 + cell];
        public (byte[] Record, byte[] Template) Resolve(uint reference) => _objects[reference];
        public bool Contact(byte owner) => true;
        public byte TerrainClass(ReadOnlySpan<byte> record) => 0;
        public void Contacted(FirstContact contact) { }
    }
}

public partial class TestRunner
{
    private void OriginalAiPlanChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/ai-plans.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0; var sent = new Dictionary<byte, int>(); var finalModes = new HashSet<byte>();
        const int Plans = OriginalAiPlans.Plans, Military = OriginalAiPlans.Military;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            byte me = (byte)c.GetProperty("me").GetInt32();
            var parts = c.GetProperty("participants").EnumerateArray().ToArray();
            var world = new AiTurnProbe(parts.Length);
            for (int p = 0; p < parts.Length; p++)
            {
                var part = parts[p]; var b = world.Block(p); b[0] = (byte)p;
                var t = part.GetProperty("territory");
                b[0x63] = (byte)t[0].GetInt32(); b[0x64] = (byte)t[2].GetInt32(); b[0x65] = (byte)t[1].GetInt32(); b[0x66] = (byte)t[3].GetInt32();
                BinaryPrimitives.WriteUInt32LittleEndian(b[0x19f1..], part.GetProperty("strength").GetUInt32());
                for (int k = 0; k < 20; k++) { b[Plans + k * 0x15] = 0xff; b[Military + k * 0x15] = 0xff; }
                var r = world.Participant(p);
                int q = 0; foreach (var rel in part.GetProperty("relations").EnumerateArray()) r[0xb9 + q++] = (byte)rel.GetInt32();
                for (int s = 0; s < 8; s++) r[0x34 + s * 14] = 0xff;
                int m = 0;
                foreach (var msg in part.GetProperty("queue").EnumerateArray()) { r[0x34 + m * 14] = (byte)msg[0].GetInt32(); r[0x34 + m * 14 + 9] = (byte)msg[1].GetInt32(); m++; }
            }
            var ai = world.Block(me);
            ai[0x1a27] = (byte)c.GetProperty("flags").GetInt32();
            BinaryPrimitives.WriteUInt32LittleEndian(ai[0x31..], c.GetProperty("limits")[0].GetUInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(ai[0x41..], c.GetProperty("limits")[1].GetUInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(ai[0x49..], c.GetProperty("factor")[0].GetUInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(ai[0x5d..], c.GetProperty("factor")[1].GetUInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(ai[0x19f9..], c.GetProperty("base").GetUInt32());
            int slot = 0;
            foreach (var plan in c.GetProperty("plans").EnumerateArray())
            {
                int at = Plans + slot++ * 0x15;
                ai[at] = (byte)plan[0].GetInt32(); ai[at + 1] = (byte)plan[1].GetInt32(); ai[at + 2] = (byte)plan[2].GetInt32(); ai[at + 4] = (byte)plan[1].GetInt32();
                BinaryPrimitives.WriteUInt32LittleEndian(ai[(at + 8)..], plan[3].GetUInt32()); BinaryPrimitives.WriteUInt32LittleEndian(ai[(at + 0x10)..], plan[4].GetUInt32());
            }
            slot = 0;
            foreach (var record in c.GetProperty("military").EnumerateArray())
            {
                int at = Military + slot++ * 0x15;
                ai[at] = (byte)record[0].GetInt32(); ai[at + 1] = (byte)record[1].GetInt32(); ai[at + 4] = (byte)record[2].GetInt32();
                BinaryPrimitives.WriteUInt32LittleEndian(ai[(at + 8)..], 0x100 + 0x40 * record[3].GetUInt32());
            }
            uint reference0 = 0x100; var objects = c.GetProperty("objects").EnumerateArray().ToArray();
            foreach (var o in objects)
            {
                int t = o[0].GetInt32();
                world.Add(reference0, CombatUnit((byte)t, (byte)o[1].GetInt32(), (byte)o[4].GetInt32(), 0, o[2].GetUInt32(), o[3].GetUInt32(), 1), Dat(t));
                reference0 += 0x40;
            }
            for (int k = objects.Length; k < 8; k++) world.Register(0x100 + 0x40 * (uint)k, CombatUnit(0, (byte)(k % parts.Length), 0, 0, 16, 16, 1), Dat(0));
            var threat = Enumerable.Repeat(0x55555555u, 16).ToArray();
            OriginalAiPlans.Execute(world, me, threat);
            for (int p = 0; p < parts.Length; p++)
            {
                var block = world.Block(p).ToArray();
                if (p == me)
                    for (int k = 0; k < 20; k++)
                    {
                        int at = Military + k * 0x15; block.AsSpan(at + 5, 7).Clear(); block[at + 3] = 0;
                        finalModes.Add(block[Plans + k * 0x15]);
                    }
                digest.AppendData(block); digest.AppendData(world.Participant(p));
            }
            var encoded = new byte[64]; for (int i = 0; i < 16; i++) BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(i * 4), threat[i]);
            digest.AppendData(encoded);
            digest.AppendData(System.Text.Encoding.UTF8.GetBytes("[" + string.Join(", ", world.Calls.Select(x => $"[{x.Type}, {x.Other}, {x.Owner}]")) + "]"));
            foreach (var x in world.Calls) sent[x.Type] = sent.GetValueOrDefault(x.Type) + 1;
            count++;
        }
        var result = root.GetProperty("results").GetProperty("plans");
        Godot.GD.Print($"AI plans: messages {string.Join(" ", sent.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key:X}:{kv.Value}"))}, final modes {string.Join(" ", finalModes.Order().Select(m => $"{m:X}"))}");
        Check(count == result.GetProperty("case_count").GetInt32(), $"AI plan reference coverage ({count})");
        Check(new byte[] { 0, 1, 2, 4, 5, 6, 7, 8 }.All(sent.ContainsKey), "AI plan cases send every executor message");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(), "AI plan executor 23667 matches original x86");
    }
}
