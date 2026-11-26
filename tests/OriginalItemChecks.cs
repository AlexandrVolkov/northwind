using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class ItemTestWorld : IItemWorld
    {
        public byte[][] Participants = [new byte[0xf5], new byte[0xf5], new byte[0xf5]]; public byte[][] Inventories = [new byte[20], new byte[20], new byte[20]];
        public uint[] Stats = new uint[6]; public byte[] Random = []; public byte[] Index = new byte[3];
        public byte[] Objects_ = []; public List<ItemObject> Items = []; public List<byte> Log = []; public byte MapWidth, MapHeight;
        public byte Tick19E9 { get; set; } public byte Tick19EA { get; set; } public byte Global1EC5 { get; set; }
        public int PlayerOwner { get; set; } public int ParticipantCount { get; set; } public bool ItemsActive { get; set; }
        public byte[] Inventory(int owner) => Inventories[owner];
        public uint Gold(int owner) => BitConverter.ToUInt32(Participants[owner], 0x20); public void SetGold(int owner, uint v) => BitConverter.GetBytes(v).CopyTo(Participants[owner], 0x20);
        public uint Wood(int owner) => BitConverter.ToUInt32(Participants[owner], 0x18); public void SetWood(int owner, uint v) => BitConverter.GetBytes(v).CopyTo(Participants[owner], 0x18);
        public void AddGoldStatistics(int owner, uint v) => Stats[owner * 2] += v; public void AddWoodStatistics(int owner, uint v) => Stats[owner * 2 + 1] += v;
        public byte Random1() => Random[Index[0]++]; public byte Random2() => Random[Index[1]++]; public byte Random3() => Random[Index[2]++];
        public IReadOnlyList<ItemObject> Objects(int owner) { foreach (var o in Items) Log.AddRange([(byte)'T', .. BitConverter.GetBytes((uint)(0x100 + 0x40 * Items.IndexOf(o)))]); return Items; }
        public void FleeRequest(byte[] record) => OriginalCombatRules.Flee(record, MapWidth, MapHeight, Random1, Random2);
        public void Message() => Log.Add((byte)'M'); public void Sound(byte id) => Log.AddRange([(byte)'S', id]);
        // the world of the use handlers (tests/OriginalItemUseChecks.cs)
        public ItemTarget TargetObject; public bool PlacementOk, CreateOk; public byte[] TemplateLayers = new byte[8];
        public ItemTarget Target(uint reference) => TargetObject;
        public bool PlacementFree(byte al, byte ah, byte bl, uint x, uint y)
        { Log.AddRange([(byte)'K', al, ah, bl]); Log.AddRange(BitConverter.GetBytes(x)); Log.AddRange(BitConverter.GetBytes(y)); return PlacementOk; }
        public (bool Created, uint Reference) Create(byte army, byte owner, byte template, byte layer, uint x, uint y)
        { Log.AddRange([(byte)'C', army, owner, template, layer]); Log.AddRange(BitConverter.GetBytes(x)); Log.AddRange(BitConverter.GetBytes(y)); return (CreateOk, 0x800); }
        public void Setup(uint reference) { Log.Add((byte)'I'); Log.AddRange(BitConverter.GetBytes(reference)); }
        public void Effect(byte al, byte type, byte layer, uint x, uint y, uint argument) { Log.AddRange([(byte)'E', al, type, layer]); Log.AddRange(BitConverter.GetBytes(x)); Log.AddRange(BitConverter.GetBytes(y)); Log.AddRange(BitConverter.GetBytes(argument > 0xff ? argument + 0x400000 : argument)); }
        public byte TemplateLayer(byte army, byte template) => template < 8 ? TemplateLayers[template] : (byte)0;   // the oracle writes only eight templates
    }

    /// <summary>Replays tests/fixtures/items.json (tools/verify_items.py): the item loop 12383, the pickup 1243A and the handlers of the item table.</summary>
    private void OriginalItemChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/items.json");
        var root = reference.RootElement;
        var random = root.GetProperty("random_values").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
        var table = GameData.Read<OriginalItemData>("res://data/original_items.json").Items;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, consumed = 0, kept = 0, curses = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var rules = new OriginalItemRules(code => table[code].Handler);
            var world = new ItemTestWorld { Random = random, ItemsActive = c.GetProperty("flag").GetInt32() != 0, ParticipantCount = c.GetProperty("count").GetInt32(), PlayerOwner = c.GetProperty("player").GetInt32(),
                Tick19E9 = (byte)c.GetProperty("tick").GetInt32(), Global1EC5 = (byte)c.GetProperty("global_1ec5").GetInt32() };
            world.Tick19EA = (byte)(world.Tick19E9 ^ 0x5a);
            int owner = c.GetProperty("owner").GetInt32();
            var start = c.GetProperty("random_start").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray(); world.Index = start;
            var map = c.GetProperty("map").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray(); world.MapWidth = map[0]; world.MapHeight = map[1];
            var inventory = c.GetProperty("inventory").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
            for (int k = 0; k < 3; k++)
            {
                world.Inventories[k] = k == owner ? [.. inventory] : Enumerable.Range(0, 20).Select(i => (i * 7 + k) % 3 != 0 ? inventory[i] : (byte)0).ToArray();
                world.SetGold(k, c.GetProperty("gold").GetUInt32()); world.SetWood(k, c.GetProperty("wood").GetUInt32());
            }
            for (int k = 0; k < 6; k++) world.Stats[k] = c.GetProperty("stats").GetUInt32();
            foreach (var entry in rules.Entries) BitConverter.GetBytes(c.GetProperty("timer").GetUInt32()).CopyTo(entry, 0x15);
            foreach (var o in c.GetProperty("objects").EnumerateArray())
            {
                var raw = new byte[0x40]; raw[0] = (byte)o.GetProperty("army").GetInt32(); raw[3] = (byte)owner; raw[0x16] = (byte)o.GetProperty("level").GetInt32();
                raw[0x2d] = (byte)o.GetProperty("state").GetInt32(); raw[0x17] = (byte)o.GetProperty("extra").GetInt32();
                BitConverter.GetBytes(0x40).CopyTo(raw, 0x10); BitConverter.GetBytes(o.GetProperty("flags").GetUInt32()).CopyTo(raw, 0x1c);
                world.Items.Add(new ItemObject(raw, (o.GetProperty("flags").GetUInt32() & 8) != 0));
            }
            for (int k = 0; k < 3; k++) { BitConverter.GetBytes(0x100).CopyTo(world.Participants[k], 0x0e); BitConverter.GetBytes(0x100 + 0x40 * world.Items.Count).CopyTo(world.Participants[k], 0x09); }
            string regs = "{}";
            for (int u = 0; u < c.GetProperty("updates").GetInt32(); u++)
            {
                if (c.GetProperty("kind").GetString() == "loop") rules.Update(world);
                else
                {
                    var (al, zf) = rules.Pickup(world, (byte)c.GetProperty("item").GetInt32(), owner);
                    regs = $"{{\"eax\": {al}, \"zf\": {(zf ? 1 : 0)}}}";
                    if (zf && al == 0) { if (world.Inventories[owner].SequenceEqual(inventory)) consumed++; else kept++; }
                }
                world.Tick19E9 = (byte)(world.Tick19E9 + 1); // the loop of the harness: the counter moves between updates
            }
            // the serialisation of the Python harness: participants, table states, objects, statistics, random indices, registers, log
            var output = new List<byte>();
            for (int k = 0; k < 3; k++) { var p = (byte[])world.Participants[k].Clone(); world.Inventories[k].CopyTo(p, 0xcd); output.AddRange(p); }
            foreach (var entry in rules.Entries) output.AddRange(entry.AsSpan(0x15, 4).ToArray());
            foreach (var o in world.Items) output.AddRange(o.Record);
            foreach (var s in world.Stats) output.AddRange(BitConverter.GetBytes(s));
            output.AddRange(world.Index); output.AddRange(System.Text.Encoding.UTF8.GetBytes(regs)); output.AddRange(world.Log);
            digest.AppendData(output.ToArray());
            if (Convert.ToHexStringLower(SHA256.HashData(output.ToArray()))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print($"ITEMS MISMATCH {count}: {c.GetProperty("kind")} {(c.TryGetProperty("item", out var it) ? it : "-")}"); }
            if (c.GetProperty("kind").GetString() == "loop" && c.GetProperty("inventory").EnumerateArray().Any(e => e.GetInt32() is 15 or 17 or 18)) curses++;
            count++;
        }
        GD.Print($"Items: first mismatch {firstMismatch}; consumed {consumed}, kept {kept}, curse loops {curses}");
        var summary = root.GetProperty("results").GetProperty("items");
        Check(count == summary.GetProperty("case_count").GetInt32() && consumed > 10 && kept > 10 && curses > 20, $"Item reference coverage ({count}, {consumed} consumed, {kept} kept, {curses} curses)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Item loop, pickup and handlers LE:1 12383/1243A/11EB0.. match original x86");
    }
}
