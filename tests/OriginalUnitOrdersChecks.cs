using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class OrdersReplay(JsonElement c, List<byte> log) : IOrderWorld
    {
        public void ReleaseRoute() => log.Add((byte)'R');
        public (bool Found, uint Eax, uint Ebx, uint Edi) SearchEnemy(byte mode)
        {
            log.Add((byte)'S'); log.Add(mode); var f = c.GetProperty("found");
            return (c.GetProperty("zf").GetInt32() == 0, (uint)f[0].GetInt64(), (uint)f[1].GetInt64(), (uint)f[2].GetInt64());
        }
        public uint Nearest(byte owner, uint x, uint y) { log.Add((byte)'N'); log.Add(owner); log.AddRange(BitConverter.GetBytes(x)); log.AddRange(BitConverter.GetBytes(y)); return (uint)c.GetProperty("edi").GetInt64(); }
        public byte[]? Resolve(uint reference)
        {
            var t = new byte[0x40]; BitConverter.GetBytes((uint)c.GetProperty("tx").GetInt32()).CopyTo(t, 4); BitConverter.GetBytes((uint)c.GetProperty("ty").GetInt32()).CopyTo(t, 8); return t;
        }
    }

    /// <summary>Replays tests/fixtures/unit-orders.json (tools/verify_unit_orders.py): LE:1 8CA1, 8CBB, 8D0B, 8D1A, 8D3E, 8D89.</summary>
    private void OriginalUnitOrdersChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/unit-orders.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1; var functions = new HashSet<string>();
        int only = int.TryParse(System.Environment.GetEnvironmentVariable("DEBUG_CASE"), out var d) ? d : -1;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var r = new byte[0x40]; r[0x2d] = (byte)c.GetProperty("f2d").GetInt32(); r[0x2e] = (byte)c.GetProperty("f2e").GetInt32(); r[0x30] = (byte)c.GetProperty("f30").GetInt32(); r[0x15] = (byte)c.GetProperty("f15").GetInt32();
            r[0x22] = (byte)c.GetProperty("f22").GetInt32(); r[0x23] = (byte)c.GetProperty("f23").GetInt32(); r[3] = (byte)c.GetProperty("f03").GetInt32();
            BitConverter.GetBytes((uint)c.GetProperty("target").GetInt32()).CopyTo(r, 0x1c); BitConverter.GetBytes((uint)c.GetProperty("x").GetInt32()).CopyTo(r, 4); BitConverter.GetBytes((uint)c.GetProperty("y").GetInt32()).CopyTo(r, 8);
            var old = c.GetProperty("old"); var g = new OriginalUnitOrders.Globals { G512 = (uint)old[0].GetInt64(), G516 = (uint)old[1].GetInt64(), G51A = (uint)old[2].GetInt64(), G51E = (uint)old[3].GetInt64() };
            var oldBytes = new byte[17]; BitConverter.GetBytes(g.G512).CopyTo(oldBytes, 0); BitConverter.GetBytes(g.G516).CopyTo(oldBytes, 4); BitConverter.GetBytes(g.G51A).CopyTo(oldBytes, 8); BitConverter.GetBytes(g.G51E).CopyTo(oldBytes, 12);
            var regs = new OrderRegs { Eax = (uint)(c.GetProperty("al").GetInt32() | (c.GetProperty("ah").GetInt32() << 8)), Ebx = (uint)c.GetProperty("bl").GetInt32(), Ecx = 0x55aa, Edx = 0x66bb };
            var log = new List<byte>(); var world = new OrdersReplay(c, log); string fn = c.GetProperty("fn").GetString()!; functions.Add(fn);
            switch (fn)
            {
                case "move": OriginalUnitOrders.Move(r, regs, world); break;
                case "follow": OriginalUnitOrders.Follow(r, regs, world); break;
                case "state5": OriginalUnitOrders.RequestRange(r, regs); break;
                case "scan": OriginalUnitOrders.Scan(r, regs, g, world); break;
                case "scan2": OriginalUnitOrders.Scan2(r, regs, g, world); break;
                default: OriginalUnitOrders.Decode(r, regs, g); break;
            }
            // the harness reads DS:512..522 after the call: the globals the routine did not touch keep their old values (G522 is a byte of the old words)
            var mem = (byte[])oldBytes.Clone();
            if (fn is "scan" or "scan2") { BitConverter.GetBytes(g.G512).CopyTo(mem, 0); BitConverter.GetBytes(g.G516).CopyTo(mem, 4); BitConverter.GetBytes(g.G51A).CopyTo(mem, 8); BitConverter.GetBytes(g.G51E).CopyTo(mem, 12); }
            if (fn == "decode") mem[16] = g.G522;
            byte[] output = [.. r, .. mem, .. BitConverter.GetBytes(regs.Eax), .. BitConverter.GetBytes(regs.Ebx), .. BitConverter.GetBytes(regs.Ecx), .. BitConverter.GetBytes(regs.Edx), (byte)(regs.Zf ? 1 : 0), .. log];
            digest.AppendData(output);
            string hex = Convert.ToHexStringLower(SHA256.HashData(output))[..10];
            if (hex != c.GetProperty("sha256_prefix").GetString()) { if (firstMismatch < 0) firstMismatch = count; if (only == count) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Orders: first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("orders");
        Check(count == summary.GetProperty("case_count").GetInt32() && functions.Count == 6, $"Unit order helpers reference coverage ({count} cases)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Unit order helpers LE:1 8CA1..8D89 match original x86");
    }
}
