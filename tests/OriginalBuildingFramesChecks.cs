using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Replays tests/fixtures/building-frames.json (tools/verify_building_frames.py): the frame and condition tail C8E0 of the building update.</summary>
    private void OriginalBuildingFramesChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/building-frames.json");
        var root = reference.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Dat(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        foreach (var item in root.GetProperty("templates_sha256").EnumerateObject())
            Check(Convert.ToHexStringLower(SHA256.HashData(Dat(int.Parse(item.Name)))) == item.Value.GetString(), $"Frame template {item.Name} matches bundled DAT");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1, collapses = 0, animated = 0, faults = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int t = c.GetProperty("t").GetInt32(); var tp = Dat(t); ushort baseFrame = BinaryPrimitives.ReadUInt16LittleEndian(tp.AsSpan(0x48));
            var rec = new byte[0x40]; CombatUnit((byte)t, 3, 10, 0, c.GetProperty("x").GetUInt32(), c.GetProperty("y").GetUInt32(), 1).CopyTo(rec, 0);
            rec[0] = 2; rec[0x0e] = (byte)t; rec[0x19] = (byte)c.GetProperty("cond").GetInt32(); rec[0x1a] = (byte)c.GetProperty("max").GetInt32();
            rec[0x0f] = (byte)c.GetProperty("flags").GetInt32(); rec[0x14] = (byte)c.GetProperty("act").GetInt32(); rec[0x15] = 7; rec[0x22] = (byte)c.GetProperty("rate").GetInt32();
            BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(0x17), (ushort)c.GetProperty("f17").GetInt32());
            BinaryPrimitives.WriteUInt16LittleEndian(rec.AsSpan(1), (ushort)(baseFrame + tp[0x4a] + c.GetProperty("frame_off").GetInt32()));
            var log = new List<byte>(); bool fault = false, zf = false;
            ushort frame0 = BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(1));
            try
            {
                zf = OriginalBuildingFrames.Update(rec, tp, (byte)c.GetProperty("tick").GetInt32(),
                    (al, bx, x, y) => { log.Add((byte)'E'); log.Add(al); log.AddRange(BitConverter.GetBytes(bx)); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y)); },
                    () => log.Add((byte)'R'));
            }
            catch (Exception e) when (e is DivideByZeroException or OverflowException) { fault = true; }
            if (log.Contains((byte)'R')) collapses++; if (BinaryPrimitives.ReadUInt16LittleEndian(rec.AsSpan(1)) != frame0) animated++; if (fault) faults++;
            byte[] output = [.. rec.AsSpan(0, 0x30), (byte)(fault ? 1 : 0), (byte)(zf && !fault || fault && false ? 1 : 0), .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print($"FRAMES MISMATCH {count} t{t} " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Building frames: {animated} frame changes, {collapses} collapses, {faults} faults; first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("frames");
        Check(count == summary.GetProperty("case_count").GetInt32() && animated > 1000 && collapses > 50, $"Building frame reference coverage ({count})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Building frames C8E0 match original x86");
    }
}
