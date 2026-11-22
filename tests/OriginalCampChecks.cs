using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/camp.json (tools/verify_camp.py): the camp of the horde (LE:1 11943..11B4D) with the real 1E740.</summary>
    private void OriginalCampChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/camp.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies;
        byte[] Dat(int army, int index) => Convert.FromHexString(templates[$"ARMY__0{army}.DAT"].Templates[index].RawRecordHex);
        var characters = GameData.Read<OriginalAiData>("res://data/original_ai.json").Table();
        var randomTable = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json").RandomValues.ToArray();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, founded = 0, fed = 0, reverted = 0, beacons = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int owner = c.GetProperty("owner").GetInt32();
            var blocks = Enumerable.Range(0, 8).Select(p => new byte[OriginalAiRules.BlockSize]).ToArray();
            for (int p = 0; p < 8; p++) blocks[p][0] = (byte)p;
            blocks[owner][0x5c] = (byte)c.GetProperty("castles").GetInt32();
            var strength = c.GetProperty("strength").EnumerateArray().Select(v => (uint)v.GetInt32()).ToArray();
            for (int p = 0; p < 3; p++) BinaryPrimitives.WriteUInt32LittleEndian(blocks[p].AsSpan(0x19e9), strength[p]);
            var participants = Enumerable.Range(0, 8).Select(p => new byte[0xf5]).ToArray();
            var resources = c.GetProperty("resources").EnumerateArray().Select(v => (uint)v.GetInt32()).ToArray();
            for (int r = 0; r < 4; r++) BinaryPrimitives.WriteUInt32LittleEndian(participants[owner].AsSpan(0x18 + 4 * r), resources[r]);
            var camp = new byte[0x40]; CombatUnit(0, (byte)owner, 100, 0, c.GetProperty("x").GetUInt32(), c.GetProperty("y").GetUInt32(), 1).CopyTo(camp, 0);
            camp[0] = 0; camp[3] = (byte)owner; camp[0x0e] = 0; camp[0x15] = (byte)c.GetProperty("state").GetInt32(); camp[0x27] = (byte)c.GetProperty("alert").GetInt32();
            BinaryPrimitives.WriteUInt32LittleEndian(camp.AsSpan(0x23), c.GetProperty("timer").GetUInt32());
            var world = new CampWorld(blocks, participants, Dat, characters, randomTable, (byte)c.GetProperty("index").GetInt32(), c.GetProperty("pick").GetBoolean(),
                (byte)c.GetProperty("template").GetInt32(), (byte)c.GetProperty("army").GetInt32(), c.GetProperty("spot").EnumerateArray().ToArray(), c.GetProperty("create").GetBoolean(),
                ((byte)c.GetProperty("cursor")[0].GetInt32(), (byte)c.GetProperty("cursor")[1].GetInt32()));
            byte before = camp[0x15];
            OriginalCamp.Step(world, camp);
            if (before == 0 && camp[0x15] == 1) founded++; if (before == 1 && camp[0x15] == 0) reverted++; if (world.Log.Contains((byte)'B')) beacons++;
            if (before == 1 && participants[owner][0x24] == 0x32) fed++;
            byte[] output = [.. camp.AsSpan(0, 0x30), .. participants[owner].AsSpan(0x16, 0x10), .. blocks[owner], .. world.Log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print($"CAMP MISMATCH {count}"); }
            count++;
        }
        GD.Print($"Camp: {founded} founded, {fed} feedings, {reverted} returns, {beacons} alerts; first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("camp");
        Check(count == summary.GetProperty("case_count").GetInt32() && founded > 20 && fed > 50 && beacons > 50, $"Camp reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Camp of the horde LE:1 11943 matches original x86");
    }

    private sealed class CampWorld(byte[][] blocks, byte[][] participants, Func<int, int, byte[]> dat, byte[] characters, byte[] random, byte index, bool pick,
        byte template, byte army, JsonElement[] spot, bool create, (byte X, byte Y) cursor) : ICampWorld
    {
        public readonly List<byte> Log = [];
        private int _made;
        private byte _index = index;
        public Span<byte> Block(byte participant) => blocks[participant];
        public Span<byte> Participant(byte participant) => participants[participant];
        public byte Random() => random[_index++];
        public (byte Template, byte Army)? Pick(byte participant, uint mask, uint value)
        {
            Log.Add((byte)'P'); Log.Add(participant); Log.AddRange(BitConverter.GetBytes(mask)); Log.AddRange(BitConverter.GetBytes(value));
            return pick ? (template, army) : null;
        }
        public ReadOnlySpan<byte> Template(byte armyId, byte t) => dat(armyId, t);
        public (uint X, uint Y)? Spot(byte t, byte a, byte variant, uint x, uint y)
        {
            Log.Add((byte)'S'); Log.AddRange(BitConverter.GetBytes((ushort)(t | a << 8))); Log.Add(variant); Log.AddRange(BitConverter.GetBytes(x)); Log.AddRange(BitConverter.GetBytes(y));
            return spot[2].GetBoolean() ? (spot[0].GetUInt32(), spot[1].GetUInt32()) : null;
        }
        public byte[]? Create(byte armyId, byte owner, byte t, byte variant, uint x, uint y)
        {
            Log.Add((byte)'A'); Log.AddRange([armyId, owner, t, variant]); Log.AddRange(BitConverter.GetBytes(x)); Log.AddRange(BitConverter.GetBytes(y));
            var record = new byte[0x40]; _lastOffset = 0x4000 + 0x40 * _made++;
            return create ? record : null;
        }
        private int _lastOffset;
        public void Register(byte[] record) { Log.Add((byte)'R'); Log.AddRange(BitConverter.GetBytes((uint)_lastOffset)); }
        public void SetCharacter(byte participant, byte character) => OriginalAiRules.SetCharacter(blocks[participant], participants[participant], character, characters);
        public void Beacon(byte[] record, byte cx, byte cy) { Log.Add((byte)'B'); Log.Add(cx); Log.Add(cy); Log.Add(record[0x15]); Log.Add(record[0x22]); Log.Add(record[0x27]); }
        public (byte X, byte Y) Cursor => cursor;
        public IEnumerable<byte[]> Objects(byte owner) => [];
        public void Retire(byte[] record) => Log.Add((byte)'D');
    }
}
