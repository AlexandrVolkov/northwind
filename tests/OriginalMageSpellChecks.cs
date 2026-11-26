using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class SpellTestWorld : IMageSpellWorld
    {
        public List<byte> Log = []; public bool Spot; public byte SpotDirection, SpotLayer; public uint SpotX, SpotY; public byte[] Random_ = []; public byte RandomIndex; public byte[] Items = new byte[20]; public byte[] Created = new byte[0x22];
        public void Effect(byte army, byte type, byte layer, uint x, uint y) { Log.AddRange([(byte)'E', army, type, layer]); Log.AddRange(BitConverter.GetBytes(x)); Log.AddRange(BitConverter.GetBytes(y)); }
        public void Sound(uint id) { Log.Add((byte)'S'); Log.AddRange(BitConverter.GetBytes(id)); }
        public void Blast(byte attack, byte kind, byte planes, byte cellX, byte cellY, byte width, byte height)
        { Log.Add((byte)'H'); foreach (byte b in new[] { attack, kind, planes, cellX, cellY, width, height }) Log.AddRange([b, 0, 0, 0]); }
        public (bool Found, byte Layer, uint X, uint Y) FindSpot(byte template, byte army) { Log.AddRange([(byte)'F', template, army]); return (Spot, SpotLayer, SpotX, SpotY); }
        public byte[]? CreateObject(byte army, byte owner, byte template, byte layer, uint x, uint y) { Log.AddRange([(byte)'C', army, owner, template, layer]); Log.AddRange(BitConverter.GetBytes(x)); Log.AddRange(BitConverter.GetBytes(y)); return Created; }
        public void Setup(byte[] created) { Log.Add((byte)'I'); Log.AddRange(BitConverter.GetBytes(0x600u)); }
        public byte Random() => Random_[RandomIndex++];
        public byte[] Inventory(int owner) => Items;
    }

    /// <summary>Replays tests/fixtures/mage-spell.json (tools/verify_mage_spell.py): the discharge of the mages, ARMY__03 effects 0 and 8.</summary>
    private void OriginalMageSpellChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/mage-spell.json");
        var root = reference.RootElement;
        var random = root.GetProperty("random_values").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, fires = 0, blasts = 0, summons = 0, areas = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var world = new SpellTestWorld { Random_ = random, RandomIndex = (byte)c.GetProperty("seed").GetInt32(), Spot = c.GetProperty("spot").GetInt32() != 0, SpotDirection = (byte)c.GetProperty("spot_direction").GetInt32(), SpotLayer = (byte)c.GetProperty("layer").GetInt32(), SpotX = c.GetProperty("x").GetUInt32() ^ 0x20, SpotY = c.GetProperty("y").GetUInt32() ^ 0x40,
                Items = c.GetProperty("inventory").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray() };
            var attacker = new byte[0x40]; attacker[0] = (byte)c.GetProperty("army").GetInt32(); attacker[3] = (byte)c.GetProperty("owner").GetInt32(); attacker[0x0e] = (byte)c.GetProperty("template").GetInt32();
            attacker[0x25] = (byte)c.GetProperty("heading").GetInt32(); attacker[0x31] = (byte)c.GetProperty("charge").GetInt32();
            BitConverter.GetBytes(c.GetProperty("x").GetUInt32()).CopyTo(attacker, 4); BitConverter.GetBytes(c.GetProperty("y").GetUInt32()).CopyTo(attacker, 8);
            var effect = new byte[0x22]; effect[1] = (byte)c.GetProperty("army").GetInt32(); effect[5] = (byte)c.GetProperty("layer").GetInt32();
            string kind = c.GetProperty("kind").GetString()!;
            if (kind == "blast") { BitConverter.GetBytes(c.GetProperty("ex").GetUInt32()).CopyTo(effect, 7); BitConverter.GetBytes(c.GetProperty("ty").GetUInt32()).CopyTo(effect, 0xc); }
            else { BitConverter.GetBytes(c.GetProperty("tx").GetUInt32()).CopyTo(effect, 7); BitConverter.GetBytes(c.GetProperty("ty").GetUInt32()).CopyTo(effect, 0xc); }
            if (kind == "step") { effect[0x16] = (byte)c.GetProperty("timer").GetInt32(); effect[0x17] = (byte)c.GetProperty("frames").GetInt32(); BitConverter.GetBytes((ushort)c.GetProperty("frame").GetInt32()).CopyTo(effect, 3); }
            if (kind == "create") { BitConverter.GetBytes(c.GetProperty("tx").GetUInt32()).CopyTo(effect, 7); BitConverter.GetBytes(c.GetProperty("ty").GetUInt32()).CopyTo(effect, 0xc); OriginalMageSpell.Create(world, effect, attacker, c.GetProperty("tx").GetUInt32(), c.GetProperty("ty").GetUInt32()); }
            else if (kind == "step") OriginalMageSpell.Step(effect);
            else OriginalMageSpell.BlastStep(world, effect, attacker);
            if (world.Log.Contains((byte)'E')) { if (world.Log.Count > 2 && world.Log[2] == 8) blasts++; else fires++; } if (world.Log.Contains((byte)'I')) summons++; if (world.Log.Contains((byte)'H')) areas++;
            var created = (byte[])world.Created.Clone();
            // the Python harness reads back the 0x22 bytes of the created object at +600 (zero unless created) and the effect at +200
            byte[] output = [.. effect, .. (world.Log.Contains((byte)'I') ? created : new byte[0x22]), .. world.Log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; GD.Print($"SPELL MISMATCH {count} {kind}"); }
            count++;
        }
        GD.Print($"Spell: first mismatch {firstMismatch}; fires {fires}, blasts {blasts}, summons {summons}, areas {areas}");
        var summary = root.GetProperty("results").GetProperty("spell");
        Check(count == summary.GetProperty("case_count").GetInt32() && fires > 100 && blasts > 100 && summons > 5 && areas > 300, $"Spell reference coverage ({count}: {fires} fires, {blasts} blasts, {summons} summons, {areas} areas)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Mage discharge ARMY__03.WRK 2E4C / 30D1 matches original x86");
    }
}
