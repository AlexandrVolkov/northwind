using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class RufinTestWorld : IRufinWorld
    {
        public DiplomacyState Diplomacy { get; set; } = new(4);
        public int ParticipantCount { get; set; } public byte Tick { get; set; }
        public byte[][] Inventories = []; public List<byte[]> Pool = []; public Dictionary<(byte, byte), uint> Flags = new(); public List<byte> Log = [];
        public bool ScanFound, CreateOk; public int ScanIndex; public List<int> TerritoryIndices = []; public byte[] RandomTable = []; public byte RandomIndex; public byte[] Created = new byte[0x40];
        public bool HasItem(int participant, byte item) => Array.IndexOf(Inventories[participant], item) >= 0;
        public byte[]? FindObject(int owner, byte army, byte template, int skip)
        { foreach (var o in Pool) if (o[0x0e] == template && o[0] == army && skip-- == 0) return o; return null; }
        public void Say(byte message, byte participant, byte speakerOwner) => Log.AddRange([(byte)'M', message, participant, speakerOwner]);
        public void Reveal(uint x, uint y, uint size) { Log.Add((byte)'A'); Log.AddRange(BitConverter.GetBytes(x)); Log.AddRange(BitConverter.GetBytes(y)); Log.AddRange(BitConverter.GetBytes(size)); }
        public void Sweep(byte participant) { Log.Add((byte)'S'); Log.AddRange(BitConverter.GetBytes((uint)participant)); }
        public (bool Found, byte[]? Object) ScanAttacker(byte mode) { Log.AddRange([(byte)'F', mode]); return (ScanFound, ScanFound ? (ScanIndex < Pool.Count ? Pool[ScanIndex] : new byte[0x40]) : null); }
        public (bool Created, byte[]? Object) Create(byte army, byte owner, byte template, byte layer, uint x, uint y)
        { Log.AddRange([(byte)'C', army, owner, template, layer]); Log.AddRange(BitConverter.GetBytes(x)); Log.AddRange(BitConverter.GetBytes(y)); return (CreateOk, Created); }
        public void Initialize(byte[] record, uint eax, uint ecx) { Log.Add((byte)'I'); Log.AddRange(BitConverter.GetBytes(0x600u)); Log.AddRange(BitConverter.GetBytes(eax)); Log.AddRange(BitConverter.GetBytes(ecx)); }
        public void Remove(byte[] record) { Log.Add((byte)'R'); Log.AddRange(BitConverter.GetBytes(0x600u)); }
        public void Redraw() => Log.Add((byte)'D');
        public IReadOnlyList<byte[]> Territory() { Log.Add((byte)'T'); return TerritoryIndices.Select(i => Pool[i]).ToList(); }
        public bool IsBuilding(byte[] record, out byte threshold) { threshold = 10; uint f = Flags[(record[0], record[0x0e])]; return (f & 8) != 0 && (f & 0x20) == 0; }
        public void StandardVitals() => Log.Add((byte)'V'); public void StandardActions() => Log.Add((byte)'U');
        public void Voice(ushort id) { Log.Add((byte)'Y'); Log.AddRange(BitConverter.GetBytes((uint)id)); }
        public void Appearance(byte army) { Log.Add((byte)'N'); Log.AddRange(BitConverter.GetBytes((uint)army)); }
        public byte Random() => RandomTable[RandomIndex++];
    }

    /// <summary>Replays tests/fixtures/rufin.json (tools/verify_rufin.py): the quest script of the mage Rufin (ARMY__03.WRK 8EC).</summary>
    private void OriginalRufinChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/rufin.json");
        var root = reference.RootElement;
        var random = root.GetProperty("random_values").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, speeches = 0, created = 0, reveals = 0, sweeps = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int participants = c.GetProperty("count").GetInt32(); int owner = c.GetProperty("owner").GetInt32();
            var world = new RufinTestWorld { Diplomacy = new DiplomacyState(participants), ParticipantCount = participants, Tick = (byte)c.GetProperty("tick").GetInt32(), RandomTable = random, RandomIndex = (byte)c.GetProperty("seed").GetInt32(),
                ScanFound = c.GetProperty("scan").GetProperty("zf").GetInt32() == 0, ScanIndex = c.GetProperty("scan").GetProperty("object").GetInt32(), CreateOk = c.GetProperty("create_zf").GetInt32() != 0 };
            world.Inventories = Enumerable.Range(0, participants).Select(p => c.GetProperty("inventory")[p].EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray()).ToArray();
            for (int p = 0; p < participants; p++)
            {
                var relations = c.GetProperty("relations")[p].EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray(); for (int q = 0; q < 8; q++) world.Diplomacy.Relations[p, q] = relations[q];
                for (int s = 0; s < 8; s++) { var entry = c.GetProperty("queue")[p][s]; world.Diplomacy.Queue[p, s].Type = (byte)entry[0].GetInt32(); world.Diplomacy.Queue[p, s].Other = (byte)entry[1].GetInt32(); }
            }
            var objects = c.GetProperty("objects").EnumerateArray().ToArray();
            foreach (var g in objects)
            {
                var raw = new byte[0x40]; raw[0] = (byte)g.GetProperty("army").GetInt32(); raw[3] = (byte)g.GetProperty("owner").GetInt32(); raw[0x0d] = (byte)g.GetProperty("layer").GetInt32(); raw[0x0e] = (byte)g.GetProperty("template").GetInt32();
                raw[0x19] = (byte)g.GetProperty("condition").GetInt32(); BitConverter.GetBytes(0x40).CopyTo(raw, 0x10); raw[0x22] = (byte)g.GetProperty("mask22").GetInt32(); raw[0x28] = (byte)g.GetProperty("mask28").GetInt32();
                world.Pool.Add(raw); world.Flags[(raw[0], raw[0x0e])] = g.GetProperty("flags").GetUInt32();
            }
            world.TerritoryIndices = c.GetProperty("territory").EnumerateArray().Select(e => e.GetInt32()).Where(i => i < objects.Length).ToList();
            var r = new byte[0x50]; r[0] = 3; r[3] = (byte)owner; r[0x14] = (byte)c.GetProperty("flags14").GetInt32(); BitConverter.GetBytes((ushort)c.GetProperty("flags17").GetInt32()).CopyTo(r, 0x17); r[0x19] = (byte)c.GetProperty("condition").GetInt32();
            foreach (var m in c.GetProperty("masks").EnumerateObject()) BitConverter.GetBytes((uint)m.Value.GetInt32()).CopyTo(r, Convert.ToInt32(m.Name, 16));
            BitConverter.GetBytes((ushort)c.GetProperty("word47").GetInt32()).CopyTo(r, 0x47);
            int ah = c.GetProperty("ah").GetInt32();
            if (ah == 0) OriginalRufin.Initialize(world, r, 7); else if (ah == 1) OriginalRufin.Step(world, r); else OriginalRufin.Speak(world);
            speeches += world.Log.Count(b => b == (byte)'M'); if (world.Log.Contains((byte)'C')) created++; if (world.Log.Contains((byte)'A')) reveals++; if (world.Log.Contains((byte)'S')) sweeps++;
            var output = new List<byte>(r); foreach (var o in world.Pool) output.AddRange(o); output.AddRange(world.Created);
            for (int p = 0; p < participants; p++) for (int s = 0; s < 8; s++) { var q = new byte[14]; q[0] = world.Diplomacy.Queue[p, s].Type; q[9] = world.Diplomacy.Queue[p, s].Other; output.AddRange(q); }
            output.AddRange(world.Log);
            digest.AppendData(output.ToArray());
            if (Convert.ToHexStringLower(SHA256.HashData(output.ToArray()))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; GD.Print($"RUFIN MISMATCH {count} ah={ah}"); }
            count++;
        }
        GD.Print($"Rufin: first mismatch {firstMismatch}; speeches {speeches}, creations {created}, reveals {reveals}, sweeps {sweeps}");
        var summary = root.GetProperty("results").GetProperty("rufin");
        Check(count == summary.GetProperty("case_count").GetInt32() && speeches > 300 && created > 5 && reveals > 10 && sweeps > 3, $"Rufin reference coverage ({count}: {speeches} speeches, {created} creations, {reveals} reveals, {sweeps} sweeps)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Rufin's quest script ARMY__03.WRK 8EC matches original x86");
    }
}
