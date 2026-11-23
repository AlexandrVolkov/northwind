using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class QuestTestWorld : IQuestWorld
    {
        public DiplomacyState Diplomacy { get; } = new(3);
        public OriginalItemRules Items { get; } = new(code => "");
        public byte[][] Participants = [new byte[0xf5], new byte[0xf5], new byte[0xf5]]; public byte[][] Inventories = [new byte[20], new byte[20], new byte[20]];
        public bool[] Stat5Flags = new bool[3]; public List<(byte[] Record, uint Flags)> Pool = []; public List<byte> Log = [];
        public byte[] Inventory(int owner) => Inventories[owner];
        public uint Gold(int owner) => BitConverter.ToUInt32(Participants[owner], 0x20); public void SetGold(int owner, uint v) => BitConverter.GetBytes(v).CopyTo(Participants[owner], 0x20);
        public uint Wood(int owner) => BitConverter.ToUInt32(Participants[owner], 0x18); public void SetWood(int owner, uint v) => BitConverter.GetBytes(v).CopyTo(Participants[owner], 0x18);
        public bool Stat5(int owner) => Stat5Flags[owner];
        public IReadOnlyList<QuestObject> QuestObjects(int owner) => Pool.Select(o => new QuestObject(o.Record, o.Flags, 10)).ToList();
        public byte PoolOwnerAt(uint reference) { int index = (int)(reference / 0x40); return index < Pool.Count && reference % 0x40 <= 0x3c ? Pool[index].Record[(int)(reference % 0x40) + 3] : (byte)0; }
        public void Send(byte type, byte other, byte receiver, byte bh, uint parameter) { Log.AddRange([(byte)'S', type, other, receiver, bh]); Log.AddRange(BitConverter.GetBytes(parameter)); }
        public void React(byte bl, byte bh, uint parameter) { Log.AddRange([(byte)'R', bl, bh]); Log.AddRange(BitConverter.GetBytes(parameter)); }
        public void Give(byte item, byte owner) => Log.AddRange([(byte)'G', item, owner]);
        // the item world is not needed by the handlers
        public uint Gold_(int o) => 0;
        public void AddGoldStatistics(int owner, uint value) { } public void AddWoodStatistics(int owner, uint value) { }
        public byte Random1() => 0; public byte Random2() => 0; public byte Random3() => 0; public byte Tick19E9 => 0; public byte Tick19EA => 0; public byte Global1EC5 => 0;
        public int PlayerOwner => 0; public int ParticipantCount => 3; public bool ItemsActive => false;
        public IReadOnlyList<ItemObject> Objects(int owner) => []; public void FleeRequest(byte[] record) { } public void Message() { } public void Sound(byte id) { }
        public ItemTarget Target(uint reference) => default; public bool PlacementFree(byte al, byte ah, byte bl, uint x, uint y) => false;
        public (bool Created, uint Reference) Create(byte army, byte owner, byte template, byte layer, uint x, uint y) => (false, 0);
        public void Setup(uint reference) { } public void Effect(byte al, byte type, byte layer, uint x, uint y, uint argument) { } public byte TemplateLayer(byte army, byte template) => 0;
    }

    /// <summary>Replays tests/fixtures/quest.json (tools/verify_quest.py): the handlers of the dialog messages F072..F5CE.</summary>
    private bool _dumped;
    private void OriginalQuestChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/quest.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, gifts = 0, peaces = 0, patched = 0, messages = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var world = new QuestTestWorld();
            int owner = c.GetProperty("owner").GetInt32(), other = c.GetProperty("other").GetInt32();
            for (int p = 0; p < 3; p++)
            {
                world.Inventories[p] = c.GetProperty("inventory")[p].EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
                var wallet = c.GetProperty("wallet")[p].EnumerateArray().Select(e => e.GetUInt32()).ToArray();
                int[] offsets = [0x18, 0x1c, 0x20, 0x24, 0x28, 0x2c]; for (int k = 0; k < 6; k++) BitConverter.GetBytes(wallet[k]).CopyTo(world.Participants[p], offsets[k]);
                world.Diplomacy.Hostile[p] = (uint)c.GetProperty("mask")[p].GetInt32();
                var relations = c.GetProperty("relations")[p].EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray(); for (int q = 0; q < 8; q++) world.Diplomacy.Relations[p, q] = relations[q];
                for (int s = 0; s < 8; s++) { var entry = c.GetProperty("queue")[p][s]; world.Diplomacy.Queue[p, s].Type = (byte)entry[0].GetInt32(); world.Diplomacy.Queue[p, s].Other = (byte)entry[1].GetInt32(); }
                world.Stat5Flags[p] = c.GetProperty("stat5")[p].GetInt32() != 0; world.Participants[p][0xcb] = (byte)c.GetProperty("dead")[p].GetInt32();
            }
            foreach (var w in c.GetProperty("words").EnumerateObject())
            {
                int addr = int.Parse(w.Name); int item = (addr - 0x4191 - 0x15) / 0x19; ushort v = (ushort)w.Value.GetInt32();
                world.Items.Entries[item][0x15] = (byte)v; world.Items.Entries[item][0x16] = (byte)(v >> 8);
            }
            foreach (var g in c.GetProperty("objects").EnumerateArray())
            {
                var raw = new byte[0x40]; raw[0] = (byte)g.GetProperty("army").GetInt32(); raw[3] = (byte)g.GetProperty("owner").GetInt32(); raw[0x0e] = (byte)g.GetProperty("template").GetInt32(); raw[0x19] = (byte)g.GetProperty("condition").GetInt32();
                BitConverter.GetBytes(0x40).CopyTo(raw, 0x10); BitConverter.GetBytes(g.GetProperty("target").GetUInt32()).CopyTo(raw, 0x1c);
                world.Pool.Add((raw, g.GetProperty("flags").GetUInt32()));
            }
            uint handler = c.GetProperty("handler").GetUInt32(); byte type = (byte)c.GetProperty("type").GetInt32();
            byte result = OriginalQuestRules.Run(world, handler, (byte)owner, (byte)other, (byte)other, c.GetProperty("parameter").GetUInt32(), type);
            if (world.Log.Contains((byte)'G')) gifts++; if (world.Log.Contains((byte)'S')) messages++; if (result != type) patched++;
            if (world.Diplomacy.Relations[owner, other] is 1 or 3 && handler is 0xf12b or 0xf072) peaces++;
            var output = new List<byte>();
            for (int p = 0; p < 3; p++)
            {
                var part = (byte[])world.Participants[p].Clone(); world.Inventories[p].CopyTo(part, 0xcd);
                BitConverter.GetBytes(world.Diplomacy.Hostile[p]).CopyTo(part, 0x12); for (int q = 0; q < 8; q++) part[0xb9 + q] = world.Diplomacy.Relations[p, q];
                for (int s = 0; s < 8; s++) { part[0x34 + 14 * s] = world.Diplomacy.Queue[p, s].Type; part[0x34 + 14 * s + 9] = world.Diplomacy.Queue[p, s].Other; }
                BitConverter.GetBytes(0).CopyTo(part, 0x0e); BitConverter.GetBytes(0x40 * world.Pool.Count).CopyTo(part, 0x09);
                output.AddRange(part);
            }
            foreach (var o in world.Pool) output.AddRange(o.Record);
            output.Add(result); output.AddRange(world.Log);
            digest.AppendData(output.ToArray());
            if (Convert.ToHexStringLower(SHA256.HashData(output.ToArray()))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; GD.Print($"QUEST MISMATCH {count} {handler:X}"); }
            count++;
        }
        GD.Print($"Quest: first mismatch {firstMismatch}; gifts {gifts}, messages {messages}, patched {patched}, peaces {peaces}");
        var summary = root.GetProperty("results").GetProperty("quest");
        Check(count == summary.GetProperty("case_count").GetInt32() && gifts > 100 && messages > 30 && peaces > 30, $"Quest reference coverage ({count}: {gifts} gifts, {messages} messages, {patched} patched, {peaces} peaces)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Dialog handlers LE:1 F072..F5CE match original x86");
    }
}
