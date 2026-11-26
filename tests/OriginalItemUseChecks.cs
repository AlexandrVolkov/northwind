using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/item-use.json (tools/verify_item_use.py): the use of items 124FD with the AH = 3 handlers of the lightnings, the disease and the phantom jug,
    /// and the drop of one / all items 12546 / 12588.</summary>
    private void OriginalItemUseChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/item-use.json");
        var root = reference.RootElement;
        var table = GameData.Read<OriginalItemData>("res://data/original_items.json").Items;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, effects = 0, phantoms = 0, drops = 0, spent = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var classes = c.GetProperty("classes").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
            var rules = new OriginalItemRules(code => table[code].Handler);
            for (int k = 0; k < OriginalItemRules.TableEntries; k++) rules.Entries[k][0x0c] = classes[k];
            rules.Entries[35][0x0e] = (byte)c.GetProperty("phantom").GetInt32();
            var armyClass = c.GetProperty("army_class").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
            var g = c.GetProperty("target");
            var world = new ItemTestWorld { PlacementOk = c.GetProperty("placement_zf").GetInt32() == 0, CreateOk = c.GetProperty("create_zf").GetInt32() != 0,
                TemplateLayers = c.GetProperty("template_flags").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray() };
            world.TargetObject = new ItemTarget((byte)g.GetProperty("army").GetInt32(), (byte)g.GetProperty("template").GetInt32(), (byte)g.GetProperty("layer").GetInt32(), g.GetProperty("x").GetUInt32(), g.GetProperty("y").GetUInt32(), armyClass[g.GetProperty("army").GetInt32()]);
            int owner = c.GetProperty("owner").GetInt32();
            for (int k = 0; k < 3; k++) world.Inventories[k] = k == owner ? c.GetProperty("inventory").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray() : new byte[20];
            var cell = c.GetProperty("cell").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
            string kind = c.GetProperty("kind").GetString()!;
            if (kind == "use") rules.Use(world, (byte)c.GetProperty("item").GetInt32(), owner, cell[0], cell[1], 0x400);
            else if (kind == "drop") { rules.DropOne(world, (byte)c.GetProperty("item").GetInt32(), owner, cell[0], cell[1]); drops++; }
            else { rules.DropAll(world, owner, cell[0], cell[1]); drops++; }
            if (world.Log.Contains((byte)'E')) effects++; if (world.Log.Contains((byte)'I')) phantoms++;
            var output = new List<byte>();
            for (int k = 0; k < 3; k++) output.AddRange(world.Inventories[k]);
            foreach (int k in new[] { 19, 31, 32, 33, 35 }) output.AddRange(rules.Entries[k].AsSpan(0x0e, 5).ToArray());
            var raw = new byte[0x40]; raw[0] = (byte)g.GetProperty("army").GetInt32(); raw[0x0e] = (byte)g.GetProperty("template").GetInt32(); raw[0x0d] = (byte)g.GetProperty("layer").GetInt32();
            BitConverter.GetBytes(g.GetProperty("x").GetUInt32()).CopyTo(raw, 4); BitConverter.GetBytes(g.GetProperty("y").GetUInt32()).CopyTo(raw, 8);
            output.AddRange(raw); output.AddRange(world.Log);
            if (kind == "use" && world.Inventories[owner].Count(i => i != 0) < c.GetProperty("inventory").EnumerateArray().Count(e => e.GetInt32() != 0)) spent++;
            digest.AppendData(output.ToArray());
            if (Convert.ToHexStringLower(SHA256.HashData(output.ToArray()))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print($"ITEMUSE MISMATCH {count} {kind}"); }
            count++;
        }
        GD.Print($"ItemUse: first mismatch {firstMismatch}; effects {effects}, phantoms {phantoms}, drops {drops}, spent {spent}");
        var summary = root.GetProperty("results").GetProperty("use");
        Check(count == summary.GetProperty("case_count").GetInt32() && effects > 100 && phantoms > 15 && drops > 300 && spent > 30, $"Item use reference coverage ({count}: {effects} effects, {phantoms} phantoms, {drops} drops, {spent} spent)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Item use and drop LE:1 124FD/12546/12588/12316/12256/120AD match original x86");
    }
}
