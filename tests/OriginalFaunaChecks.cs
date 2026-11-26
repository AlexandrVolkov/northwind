using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private sealed class FaunaReplay(JsonElement c, List<byte> log) : IFaunaWorld
    {
        private readonly uint[] masks = c.GetProperty("masks").EnumerateArray().Select(e => e.GetUInt32()).ToArray();
        public uint[] Masks => masks;
        public uint RandomA() => c.GetProperty("random").GetUInt32();
        public void Redraw() => log.Add((byte)'D');
        public void Wounded(uint eax, uint ebx) { log.Add((byte)'W'); log.AddRange(BitConverter.GetBytes(eax)); log.AddRange(BitConverter.GetBytes(ebx)); }
        public void Vitals() => log.Add((byte)'V');
        public void UnitStep() => log.Add((byte)'U');
        public void Tail(int kind) { log.Add((byte)'J'); log.Add((byte)kind); }
        public byte Tick => (byte)c.GetProperty("tick").GetInt32();
        public uint PoolBase => 0x400000;
        public uint Mask(byte owner) => masks[owner];
        public void SetMask(byte owner, uint value) => masks[owner] = value;
        public (bool Found, uint Target) Scan(byte al, byte owner)
        {
            log.Add((byte)'F'); log.Add(al); log.AddRange(BitConverter.GetBytes(masks[owner]));
            return (c.GetProperty("found").GetInt32() == 0, 0x400000u + 0x40u * (uint)c.GetProperty("target").GetInt32());
        }
        public uint TemplateFlags(uint target) => c.GetProperty("flags40").GetUInt32();
        public void Sound(uint eax, uint target) { log.Add((byte)'B'); log.AddRange(BitConverter.GetBytes(eax)); log.AddRange(BitConverter.GetBytes(target - 0x400000u)); }
        public (bool None, uint Eax, uint Ebx) Spot(byte al, byte ah)
        {
            log.Add((byte)'P'); log.Add(al); log.Add(ah);
            return (c.GetProperty("free").GetInt32() != 0, (uint)c.GetProperty("al").GetInt32(), (uint)c.GetProperty("dir").GetInt32());
        }
        public uint Create(byte al, byte ah, byte bl, byte bh, uint ecx, uint edx)
        {
            log.Add((byte)'C'); log.Add(al); log.Add(ah); log.Add(bl); log.Add(bh); log.AddRange(BitConverter.GetBytes(ecx)); log.AddRange(BitConverter.GetBytes(edx));
            return 0x600;
        }
        public void Occupy(uint reference) { log.Add((byte)'O'); log.AddRange(BitConverter.GetBytes(reference)); }
        public IReadOnlyList<byte> Children(byte owner) => c.GetProperty("children").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
        public (bool Zf, uint Flags) Lookup(byte[] r)
        {
            log.Add((byte)'L'); log.AddRange(BitConverter.GetBytes(0x100u));
            return (c.GetProperty("lookup").GetInt32() != 0, c.GetProperty("flags40").GetUInt32());
        }
    }

    /// <summary>Replays tests/fixtures/fauna.json (tools/verify_fauna.py): the scripts of ARMY__05.WRK.</summary>
    private void OriginalFaunaChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/fauna.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, firstMismatch = -1; var seen = new HashSet<string>();
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            var log = new List<byte>(); var w = new FaunaReplay(c, log);
            var rec = Convert.FromHexString(c.GetProperty("rec").GetString()!);
            rec[0] = (byte)c.GetProperty("army").GetInt32(); rec[3] = (byte)c.GetProperty("owner").GetInt32(); rec[0x0e] = 0; rec[0x19] = (byte)c.GetProperty("health").GetInt32();
            rec[0x14] = (byte)c.GetProperty("flags14").GetInt32(); rec[0x2e] = (byte)c.GetProperty("counter2e").GetInt32();
            string script = c.GetProperty("script").GetString()!;
            if (script == "remains") { BinaryPrimitives.WriteUInt32LittleEndian(rec.AsSpan(0x18), c.GetProperty("c18").GetUInt32()); rec[0x17] = (byte)c.GetProperty("c17").GetInt32(); }
            byte ah = (byte)c.GetProperty("ah").GetInt32(), alIn = (byte)c.GetProperty("al_in").GetInt32(); bool handler = c.GetProperty("handler").GetBoolean();
            if (handler && script is "wolf" or "bear" or "wolf2") OriginalFauna.Hunt(w, rec, script != "wolf2");
            else switch (script)
            {
                case "bird": OriginalFauna.Bird(w, ah, rec); break;
                case "generator": OriginalFauna.Generator(w, ah, rec); break;
                case "wolf": case "wolf2": OriginalFauna.Beast(w, ah, rec, false); break;
                case "bear": OriginalFauna.Beast(w, ah, rec, true); break;
                case "remains": OriginalFauna.Remains(w, alIn, ah, rec, Convert.FromHexString(c.GetProperty("src").GetString()!)); break;
            }
            seen.Add(script + (handler ? "H" : "") + ah);
            byte[] output = [.. rec.AsSpan(0, 0x50), .. new byte[0x40], .. w.Masks.SelectMany(BitConverter.GetBytes), .. log];
            digest.AppendData(output);
            if (Convert.ToHexStringLower(SHA256.HashData(output))[..10] != c.GetProperty("sha256_prefix").GetString() && firstMismatch < 0) { firstMismatch = count; GD.Print($"FAUNA MISMATCH {count} {script} ah {ah} handler {handler}"); if (System.Environment.GetEnvironmentVariable("DEBUG_CASE") == count.ToString()) GD.Print("CSHARP " + Convert.ToHexStringLower(output)); }
            count++;
        }
        GD.Print($"Fauna: {count} cases, {seen.Count} script entries; first mismatch {firstMismatch}");
        var summary = root.GetProperty("results").GetProperty("fauna");
        Check(count == summary.GetProperty("case_count").GetInt32() && seen.Count >= 20, $"Fauna reference coverage ({count} cases, {seen.Count} entries)");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == summary.GetProperty("sha256").GetString(), "Fauna scripts ARMY__05.WRK match original x86");
    }
}
