using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/ai-squad-steps.json (tools/verify_ai_squad_steps.py): the squad step loop 1ED26.</summary>
    private void OriginalAiSquadStepChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/ai-squad-steps.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"AI squad step template {item.Name} matches bundled DAT");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const int S = OriginalAiSquads.Size;
        int count = 0, firstMismatch = -1; var stepCounts = new int[6];
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            byte p = (byte)c.GetProperty("participant").GetInt32(); int salt = c.GetProperty("salt").GetInt32();
            var ai = new byte[0x1a35];
            var squads = c.GetProperty("squads"); ai[OriginalAiSquads.List] = (byte)squads.GetArrayLength();
            int k = 0;
            foreach (var s in squads.EnumerateArray())
            {
                int r = OriginalAiSquads.Record(k);
                for (int i = 0; i < S; i++) ai[r + i] = unchecked((byte)(i * 7 + k * 31 + salt));
                ai[r] = p; ai[r + 1] = (byte)s[0].GetInt32();
                BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(r + 0x1b), s[1].GetUInt32());
                ai[r + 0x4a] = (byte)s[2].GetInt32(); ai[r + 0x4b] = (byte)s[3].GetInt32(); ai[r + 0x4c] = (byte)s[4].GetInt32();
                for (int i = 0; i < 4; i++) ai[r + 0x4d + i] = (byte)s[5][i].GetInt32();
                ai[r + 0x43] = (byte)s[6].GetInt32(); BinaryPrimitives.WriteUInt32LittleEndian(ai.AsSpan(r + 0x22), s[7].GetUInt32());
                ai[r + 0x51] = (byte)s[8].GetInt32(); ai[r + 0x3a] = (byte)s[9].GetInt32();
                ai[r + 0x20] = (byte)s[10][0].GetInt32(); ai[r + 0x21] = (byte)s[10][1].GetInt32();
                k++;
            }
            var world = new SquadProbe(c.GetProperty("four").GetInt32() != 0);
            var limits = c.GetProperty("limits"); world.BoundsLimit = (limits[0].GetUInt32(), limits[1].GetUInt32());
            int q = 0; foreach (var h in c.GetProperty("hostile").EnumerateArray()) world.Hostile[q++] = h.GetUInt32();
            uint at = 0x100; var all = new List<byte[]>();
            byte[] Make(int t, byte owner, byte hp, ushort f17, uint x, uint y)
            {
                var unit = CombatUnit((byte)t, owner, hp, f17, x, y, 1); var rec = new byte[0x40]; unit.CopyTo(rec, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x10), 0x40); return rec;
            }
            foreach (var o in c.GetProperty("own").EnumerateArray())
            {
                int t = o[0].GetInt32(), cell = o[6].GetInt32(), target = o[8].GetInt32();
                var rec = Make(t, p, 10, (ushort)o[5].GetInt32(), (uint)((cell & 31) * 32 + 5), (uint)((cell >> 5) * 32 + 9));
                rec[0x1b] = (byte)o[1].GetInt32(); rec[0x0f] = (byte)o[2].GetInt32(); rec[0x14] = (byte)o[3].GetInt32(); rec[0x15] = (byte)o[4].GetInt32();
                rec[0x20] = (byte)o[7].GetInt32(); BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x1c), target == 0 ? 0 : (uint)(0x100 + 0x40 * target));
                world.Add(p, at, rec, Dat(t), o[9].GetInt32(), cell, own: true); all.Add(rec); at += 0x40;
            }
            foreach (var o in c.GetProperty("enemies").EnumerateArray())
            {
                int t = o[0].GetInt32(), cell = o[4].GetInt32();
                var rec = Make(t, (byte)o[1].GetInt32(), (byte)o[3].GetInt32(), 0, (uint)((cell & 31) * 32 + 17), (uint)((cell >> 5) * 32 + 3));
                rec[0x20] = (byte)o[2].GetInt32();
                world.Add(p, at, rec, Dat(t), o[5].GetInt32(), cell, own: false); all.Add(rec); at += 0x40;
            }
            for (int tick = 0; tick < c.GetProperty("ticks").GetInt32(); tick++)
            {
                for (int i = 0; i < ai[OriginalAiSquads.List]; i++) { int r = OriginalAiSquads.Record(i); if (ai[r + 0x4b] < 6) stepCounts[ai[r + 0x4b]]++; }
                OriginalAiSquadSteps.Tick(ai, world);
            }
            byte[] output = [.. ai.AsSpan(OriginalAiSquads.List, 1 + S * ai[OriginalAiSquads.List]), .. all.SelectMany(r => r), (byte)world.FourCalls];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print("MISMATCH " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"AI squad steps: started per step {string.Join(' ', stepCounts)}; first mismatch {firstMismatch}");
        var result = root.GetProperty("results").GetProperty("steps");
        Check(count == result.GetProperty("case_count").GetInt32(), $"AI squad step reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(), "AI squad steps 1ED26 (F06A 0..5) match original x86");
    }

    private sealed class SquadProbe(bool four) : IAiSquadWorld
    {
        private readonly uint[] _grid = new uint[3 * 1024];
        private readonly Dictionary<uint, (byte[], byte[])> _objects = [];
        private readonly List<(byte[] Record, byte[] Template)> _chain = [];
        private byte _owner;
        public readonly uint[] Hostile = new uint[8];
        public int FourCalls;
        public void Add(byte owner, uint reference, byte[] record, byte[] template, int plane, int cell, bool own)
        {
            _owner = owner; _objects[reference] = (record, template); _grid[plane * 1024 + cell] = reference;
            if (own) _chain.Add((record, template));
        }
        public IReadOnlyList<(byte[] Record, byte[] Template)> Chain(byte participant) => participant == _owner ? _chain : [];
        public (byte[] Record, byte[] Template) Resolve(uint reference) => _objects.TryGetValue(reference, out var o) ? o : (new byte[0x40], new byte[171]);
        public uint Occupant(int plane, int cell) => _grid[plane * 1024 + cell];
        public byte MapWidth => 32;
        public byte MapHeight => 32;
        public uint HostileMask(byte participant) => Hostile[participant];
        public (uint X, uint Y) BoundsLimit { get; set; }
        public bool StepFour(Span<byte> squad, bool started) { FourCalls++; return four; }
    }
}
