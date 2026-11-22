using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class TowerWorld(bool found, byte[] victim, List<byte> log) : ITowerWorld
    {
        public byte[]? FindEnemy(byte[] tower) { log.Add((byte)'D'); log.Add(1); return found ? victim : null; }
        public void Effect(byte type, byte layer, byte army, uint x, uint y, byte[] tower, uint targetX, uint targetY)
        {
            log.Add((byte)'E'); log.Add(type); log.Add(layer); log.Add(army); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y));
            log.AddRange(BitConverter.GetBytes(0x100u)); log.AddRange(BitConverter.GetBytes(targetX)); log.AddRange(BitConverter.GetBytes(targetY));
        }
    }

    /// <summary>Replays tests/fixtures/tower.json (tools/verify_tower.py): the tower's attack LE:1 CA7C with the direction 9688.</summary>
    private void OriginalTowerChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/tower.json");
        var root = reference.RootElement;
        var dat = Convert.FromHexString(GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates[13].RawRecordHex);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, shots = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var log = new List<byte>();
            var tower = new byte[80]; tower[0] = (byte)c.GetProperty("army").GetInt32();
            uint x = c.GetProperty("x").GetUInt32(), y = c.GetProperty("y").GetUInt32();
            BinaryPrimitives.WriteUInt32LittleEndian(tower.AsSpan(4), x); BinaryPrimitives.WriteUInt32LittleEndian(tower.AsSpan(8), y);
            tower[0x19] = (byte)c.GetProperty("condition").GetInt32(); tower[0x23] = (byte)c.GetProperty("tick").GetInt32(); tower[0x24] = (byte)c.GetProperty("reload").GetInt32(); tower[0x16] = 0x55;
            var victim = new byte[80];
            BinaryPrimitives.WriteUInt32LittleEndian(victim.AsSpan(4), unchecked((uint)((int)x + c.GetProperty("dx").GetInt32()))); BinaryPrimitives.WriteUInt32LittleEndian(victim.AsSpan(8), unchecked((uint)((int)y + c.GetProperty("dy").GetInt32())));
            victim[0x0d] = (byte)c.GetProperty("layer").GetInt32();
            OriginalTowerFire.Step(new TowerWorld(c.GetProperty("found").GetBoolean(), victim, log), tower, dat);
            if (log.Contains((byte)'E')) shots++;
            byte[] output = [.. tower.AsSpan(0, 0x30), .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print($"TOWER MISMATCH {count}"); }
            count++;
        }
        GD.Print($"Tower: {shots} shots; first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("tower");
        Check(count == summary.GetProperty("case_count").GetInt32() && shots > 30, $"Tower reference coverage ({count}, {shots} shots)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Tower attack LE:1 CA7C matches original x86");
    }
}
