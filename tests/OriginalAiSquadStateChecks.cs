using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/ai-squad-states.json (tools/verify_ai_squad_states.py): squad states 1F078.</summary>
    private void OriginalAiSquadStateChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/ai-squad-states.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"AI squad state template {item.Name} matches bundled DAT");
        var random = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json").RandomValues.ToArray();
        var arctangent = Convert.FromHexString(root.GetProperty("arctangent_hex").GetString()!);
        var messages = Convert.FromHexString(root.GetProperty("messages_hex").GetString()!);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const int S = OriginalAiSquads.Size;
        int count = 0, firstMismatch = -1; var states = new int[11];
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            byte p = (byte)c.GetProperty("participant").GetInt32(); int salt = c.GetProperty("salt").GetInt32();
            var ai = new byte[0x1a35]; ai[0] = p;
            var b = c.GetProperty("block");
            BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(0x0a), b.GetProperty("0a").GetUInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(0x0e), b.GetProperty("0e").GetUInt32());
            ai[0x77] = (byte)b.GetProperty("77").GetInt32(); ai[0x2a] = (byte)b.GetProperty("2a").GetInt32(); ai[0x29] = (byte)b.GetProperty("29").GetInt32();
            int li = 0;
            foreach (var l in c.GetProperty("lists").EnumerateArray())
            {
                int at = (li < 20 ? 0x1ab : 0x34f) + li % 20 * 0x15;
                for (int i = 0; i < 0x15; i++) ai[at + i] = unchecked((byte)(i * 5 + li));
                ai[at] = (byte)l[0].GetInt32(); ai[at + 1] = (byte)l[1].GetInt32(); li++;
            }
            var squads = c.GetProperty("squads"); ai[OriginalAiSquads.List] = (byte)squads.GetArrayLength();
            int k = 0;
            foreach (var s in squads.EnumerateArray())
            {
                int r = OriginalAiSquads.Record(k);
                for (int i = 0; i < S; i++) ai[r + i] = unchecked((byte)(i * 7 + k * 31 + salt));
                byte G(string name) => (byte)s.GetProperty(name).GetInt32();
                void Bytes(int at, string name) { int i = 0; foreach (var v in s.GetProperty(name).EnumerateArray()) ai[r + at + i++] = (byte)v.GetInt32(); }
                void Word(int at, string name) => BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(r + at), s.GetProperty(name).GetUInt32());
                ai[r] = p; ai[r + 1] = G("id"); ai[r + 2] = G("state"); ai[r + 4] = G("priority"); ai[r + 5] = G("stage"); ai[r + 6] = G("sub"); ai[r + 7] = G("mode");
                Bytes(0x0d, "point"); int obj = G("object");
                BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(r + 0x0f), obj == 0 ? 0 : (uint)(0x100 + 0x40 * (obj - 1)));
                if (ai[r + 2] == 7) ai[r + 0x13] = G("join"); else Bytes(0x13, "target");
                Bytes(0x17, "home"); Word(0x1b, "timer"); ai[r + 0x1f] = G("saved"); Bytes(0x20, "centre"); Word(0x22, "count"); Word(0x36, "strength");
                ai[r + 0x3a] = G("planes"); ai[r + 0x41] = G("flags41"); ai[r + 0x4a] = G("step"); ai[r + 0x4b] = G("next"); Bytes(0x4d, "step_area");
                states[ai[r + 2]]++; k++;
            }
            int seed = c.GetProperty("explored_seed").GetInt32();
            var world = new StateProbe(random, (byte)c.GetProperty("global").GetInt32(), (byte)c.GetProperty("random")[0].GetInt32(), (byte)c.GetProperty("random")[1].GetInt32(),
                Enumerable.Range(0, 1024).Select(x => (byte)((x * 167 + seed) ^ (x >> 3) ^ (seed >> 8))).ToArray());
            int q = 0; foreach (var h in c.GetProperty("hostile").EnumerateArray()) world.Hostile[q++] = h.GetUInt32();
            uint at2 = 0x100;
            byte[] Make(int t, byte owner, byte hp, ushort f17, uint x, uint y)
            {
                var unit = CombatUnit((byte)t, owner, hp, f17, x, y, 1); var rec = new byte[0x40]; unit.CopyTo(rec, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x10), 0x40); return rec;
            }
            foreach (var o in c.GetProperty("enemies").EnumerateArray())
            {
                int t = o[0].GetInt32(), cell = o[4].GetInt32();
                var rec = Make(t, (byte)o[1].GetInt32(), (byte)o[3].GetInt32(), 0, (uint)((cell & 31) * 32 + 17), (uint)((cell >> 5) * 32 + 3));
                rec[0x20] = (byte)o[2].GetInt32();
                world.Add(p, at2, rec, Dat(t), o[5].GetInt32(), cell, own: false); at2 += 0x40;
            }
            while (at2 < 0x100 + 0x40 * 3) { world.Add(p, at2, Make(0, 7, 0, 0, 40, 40), Dat(0), -1, 0, own: false); at2 += 0x40; }
            var own = new List<byte[]>();
            foreach (var o in c.GetProperty("own").EnumerateArray())
            {
                int t = o[0].GetInt32(), cell = o[6].GetInt32();
                var rec = Make(t, p, 10, (ushort)o[5].GetInt32(), (uint)((cell & 31) * 32 + 5), (uint)((cell >> 5) * 32 + 9));
                rec[0x1b] = (byte)o[1].GetInt32(); rec[0x0f] = (byte)o[2].GetInt32(); rec[0x14] = (byte)o[3].GetInt32(); rec[0x20] = (byte)o[7].GetInt32();
                world.Add(p, at2, rec, Dat(t), -1, cell, own: true); own.Add(rec); at2 += 0x40;
            }
            var machine = new OriginalAiSquadStates(world, arctangent, messages) { EnemyY = (byte)c.GetProperty("stale_y").GetInt32() };
            for (int tick = 0; tick < c.GetProperty("ticks").GetInt32(); tick++) machine.Run(ai);
            var masked = new List<byte>();
            foreach (int list in new[] { 0x1ab, 0x34f })
                for (int i = 0; i < 20; i++) { var r = ai.AsSpan(list + i * 0x15, 0x15).ToArray(); r[3] = 0; Array.Clear(r, 5, 7); masked.AddRange(r); }
            byte[] output = [.. ai.AsSpan(OriginalAiSquads.List, 1 + S * ai[OriginalAiSquads.List]), .. ai.AsSpan(0x17c, 0x2f), .. masked, ai[0x19e8],
                .. own.SelectMany(r => r), .. world.Log, world.RandomIndex, world.RandomIndex2];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0)
            { firstMismatch = count; GD.Print("MISMATCH " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"AI squad states: squads per state {string.Join(' ', states)}; first mismatch {firstMismatch}");
        var result = root.GetProperty("results").GetProperty("states");
        Check(count == result.GetProperty("case_count").GetInt32(), $"AI squad state reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(), "AI squad states 1F078 (F083 0..8) match original x86");
    }

    private sealed class StateProbe(byte[] random, byte global, byte index, byte index2, byte[] explored) : IAiSquadStateWorld
    {
        private readonly uint[] _grid = new uint[3 * 1024];
        private readonly Dictionary<uint, (byte[], byte[])> _objects = [];
        private readonly List<(byte[] Record, byte[] Template)> _chain = [];
        private byte _owner;
        public readonly uint[] Hostile = new uint[8];
        public readonly List<byte> Log = [];
        public byte RandomIndex = index, RandomIndex2 = index2;
        public void Add(byte owner, uint reference, byte[] record, byte[] template, int plane, int cell, bool own)
        {
            _owner = owner; _objects[reference] = (record, template);
            if (plane >= 0) _grid[plane * 1024 + cell] = reference;
            if (own) _chain.Add((record, template));
        }
        public IReadOnlyList<(byte[] Record, byte[] Template)> Chain(byte participant) => participant == _owner ? _chain : [];
        public (byte[] Record, byte[] Template) Resolve(uint reference) => _objects.TryGetValue(reference, out var o) ? o : throw new InvalidOperationException($"Unknown object {reference:x}");
        public uint Occupant(int plane, int cell) => _grid[plane * 1024 + cell];
        public byte MapWidth => 32;
        public byte MapHeight => 32;
        public uint HostileMask(byte participant) => participant < 8 ? Hostile[participant] : 0;
        public (uint X, uint Y) BoundsLimit => (0x400, 0x400);
        public bool StepFour(Span<byte> squad, bool started) => throw new NotSupportedException();
        public byte Global19E9 => global;
        public byte Random() => random[RandomIndex++];
        public byte Random2() => random[RandomIndex2++];
        public byte Fog(int cell) => explored[cell];
        public void Message(ReadOnlySpan<byte> squad, uint text) { Log.Add(1); Log.AddRange(BitConverter.GetBytes(text)); }
        public void Rescue(Span<byte> squad) { Log.Add(2); Log.Add(squad[1]); }
        public void WorkerState(Span<byte> squad) { Log.Add(3); Log.Add(squad[1]); }
    }
}
