using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/burn.json (tools/verify_burn.py): fire CE3 (types 13h..15h), embers FA4, column 9D5.</summary>
    private void OriginalBurnChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/burn.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Fire reference template {item.Name} matches bundled DAT");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        const byte W = 16, H = 16;
        int count = 0, firstMismatch = -1, burns = 0, scorches = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            string mode = c.GetProperty("mode").GetString()!;
            uint x = c.GetProperty("x").GetUInt32(), y = c.GetProperty("y").GetUInt32(); byte layer = (byte)c.GetProperty("layer").GetInt32();
            byte G(string name) => (byte)c.GetProperty(name).GetInt32();
            var e = Enumerable.Range(0, 0x22).Select(i => unchecked((byte)(i * 5 + 9))).ToArray();
            e[0] = mode switch { "init" => 0x13, "step" => G("type"), "column" => 0x0b, _ => 0x16 }; e[1] = G("army"); e[5] = layer;
            BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(7), x); BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(0xc), y);
            e[6] = G("f06"); e[0xb] = G("f0b"); e[0x16] = G("f16"); e[0x17] = G("f17"); e[0x1e] = G("f1e");
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x1c), (ushort)c.GetProperty("f1c").GetInt32()); e[0x11] = G("f11");
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x10), (ushort)c.GetProperty("f10").GetInt32());
            BinaryPrimitives.WriteUInt16LittleEndian(e.AsSpan(0x20), (ushort)c.GetProperty("f20").GetInt32());
            BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(0x18), 0);
            int cell = (int)((y >> 5) * W + (x >> 5));
            var world = new BurnProbe(W, H, (byte)c.GetProperty("forest").GetInt32(), c.GetProperty("drift")[0].GetInt32(), c.GetProperty("drift")[1].GetInt32());
            var refs = new List<byte[]>();
            if (c.GetProperty("object") is { ValueKind: JsonValueKind.Object } ob)
            {
                int t = ob.GetProperty("t").GetInt32();
                var rec = CombatUnit((byte)t, (byte)ob.GetProperty("owner").GetInt32(), (byte)ob.GetProperty("hp").GetInt32(), 0x0f,
                    (uint)Math.Max(0, (int)x + ob.GetProperty("dx").GetInt32()), (uint)Math.Max(0, (int)y + ob.GetProperty("dy").GetInt32()), layer);
                BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(1), (ushort)ob.GetProperty("frame").GetInt32());
                world.Add(0x100, rec, Dat(t), (layer & 3) * W * H + cell); refs.Add(rec);
                if (mode == "step" && e[0x17] != 0xff) BinaryPrimitives.WriteUInt32LittleEndian(e.AsSpan(0x18), 0x100);
            }
            else if (c.GetProperty("cell_object") is { ValueKind: JsonValueKind.Object } co)
            {
                int t = co.GetProperty("t").GetInt32();
                var rec = CombatUnit((byte)t, 1, (byte)co.GetProperty("hp").GetInt32(), 0, x, y, layer);
                world.Add(0x140, rec, Dat(t), (layer & 3) * W * H + cell); refs.Add(rec);
            }
            var before = refs.Select(r => r[0x19]).ToArray();
            switch (mode)
            {
                case "init": OriginalBurnRules.Ignite(e, world); break;
                case "step": OriginalBurnRules.Step(e, world); break;
                case "column": OriginalBurnRules.StepColumn(e, c.GetProperty("drift")[0].GetInt32(), c.GetProperty("drift")[1].GetInt32(), W * 32, H * 32); break;
                default: OriginalBurnRules.StepEmbers(e); break;
            }
            burns += refs.Where((r, i) => r[0x19] != before[i]).Count();
            scorches += world.Log.Count(l => l.StartsWith("[\"C\""));
            byte[] output = [.. e, .. refs.SelectMany(r => r.AsSpan(0, 0x36).ToArray()), world.Scratch11, .. Encoding.UTF8.GetBytes("[" + string.Join(", ", world.Log) + "]")];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0)
            { firstMismatch = count; GD.Print($"BURN MISMATCH {count} {mode} " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Fire CE3: {burns} objects burned, {scorches} scorched cells; first mismatch {firstMismatch}");
        var result = root.GetProperty("results").GetProperty("burn");
        Check(count == result.GetProperty("case_count").GetInt32() && burns >= 200 && scorches > 10, $"Fire reference coverage ({count}, {burns}, {scorches})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(), "Fire CE3, embers FA4 and column 9D5 match original x86");
    }

    private sealed class BurnProbe(byte width, byte height, byte forest, int driftX, int driftY) : IFireWorld
    {
        private readonly uint[] _grid = new uint[3 * width * height];
        private readonly Dictionary<uint, (byte[], byte[])> _objects = [];
        private readonly uint[] _cells = Enumerable.Range(0, width * height).Select(i => (uint)i).ToArray();
        private readonly byte[] _properties = Enumerable.Range(0, width * height * 8).Select(i => i % 8 == 0 ? forest : (byte)0).ToArray();
        public readonly List<string> Log = [];
        public byte Scratch11;
        public void Add(uint reference, byte[] record, byte[] template, int slot) { _objects[reference] = (record, template); _grid[slot] = reference; }
        public (byte[] Record, byte[] Template) Resolve(uint reference) => _objects[reference];
        public (uint Reference, byte Terrain) Struck(uint x, uint y, byte layer)
        {
            var found = OriginalCombatRules.FindStruck(x, y, layer, width, _cells, _properties, (p, c) => _grid[p * width * height + c],
                r => _objects.TryGetValue(r, out var o) ? o : null);
            return (found.Reference, found.TerrainClass);
        }
        public (uint Reference, byte Terrain) InCell(uint x, uint y, byte layer)
        {
            int cell = (byte)(y >> 5) * width + (int)(x >> 5);
            return (_grid[(layer & 3) * width * height + cell], (byte)(_properties[(int)(_cells[cell] & 0xffff) * 8] & 7));
        }
        public void Spawn(byte army, byte type, byte layer, uint x, uint y, byte? h)
        {
            Log.Add($"[\"S\", {army}, {layer << 8 | type}, {x}, {y}]"); Scratch11 = h ?? 0;
        }
        public void Sound(int id) => Log.Add($"[\"A\", {id}]");
        public void Scorch(byte x, byte y) => Log.Add($"[\"C\", {y << 8 | x}, 257, 257]");
        public (int X, int Y) Drift => (driftX, driftY);
    }
}

public partial class TestRunner
{
    /// <summary>The skirmish bridge of the fire: a fire on a building burns it down over time (CE3 through Skirmish.Stones).</summary>
    private void SkirmishFireChecks()
    {
        using var battle = CreateSkirmish();
        battle.AiEnabled = false;
        var castle = battle.Actors.First(a => a.Team == 0 && a.Source.TemplateName == "Замок");
        byte before = castle.Record[0x19];
        var shot = new byte[0x22]; shot[0] = OriginalFireRules.Shot; shot[2] = 1; shot[5] = castle.Record[0x0d];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(shot.AsSpan(7), (uint)castle.Position.X);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(shot.AsSpan(0xc), (uint)castle.Position.Y);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(shot.AsSpan(0x12), 0);   // lands at once
        battle.Stones.Add(shot);
        bool burning = false;
        for (int t = 0; t < 18 * 60; t++)
        {
            battle.Advance(1 / 18.2);
            burning |= battle.Stones.Any(e => e[0] is OriginalBurnRules.Small or OriginalBurnRules.Medium or OriginalBurnRules.Large);
        }
        Check(burning, "A fire shot landing on a building sets it alight (BAB → CE3)");
        Check(battle.Sounds.Any(e => e.Bank == "ARMY__00.FX" && e.Id == 5) && battle.Sounds.Any(e => e.Bank == "ARMY__00.FX" && e.Id == 8),
            "The fire raises its sounds (A47E: ignition 5, crackle 8 of ARMY__00.FX)");
        var bank = OriginalSoundBank.Load();
        Check(battle.Sounds.All(e => bank.Choose(e.Bank, e.Id, e.Random) != null), "Every sound event names a sample of the original banks");
        Check(castle.Record[0x19] < before, $"The fire burns the building down over time ({before} → {castle.Record[0x19]})");
        battle.Stones.Clear();
        var bolt = new byte[0x22]; bolt[0] = OriginalCatapultRules.Bolt; bolt[2] = 1; bolt[5] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bolt.AsSpan(7), (uint)castle.Position.X + 64);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bolt.AsSpan(0xc), (uint)castle.Position.Y + 64);
        battle.Stones.Add(bolt);
        battle.Advance(1 / 18.2);
        Check(battle.Stones.Any(e => e[0] == OriginalCatapultRules.BigExplosion) && battle.Stones.Count(e => e[0] == OriginalFireRules.Shot) > 0,
            "A landing ballista bolt explodes and throws fire shots (12EC → A44, BAB)");
    }
}
