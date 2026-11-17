using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private static byte[] ApproachRecord(byte packed, byte state, byte heading, uint[] p)
    {
        var raw = Enumerable.Range(0, 54).Select(i => unchecked((byte)(i * 11 + 9))).ToArray(); raw[0] = 2; raw[0x0e] = 11; raw[0x19] = 5;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), p[0]); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), p[1]);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(0x1c), 0x100); raw[0x30] = packed; raw[0x2d] = state; raw[0x25] = heading;
        raw[0x17] = (p[4] & 8) != 0 ? (byte)70 : (byte)6; return raw;
    }
    private void OriginalWorkerApproachChecks()
    {
        using var doc = GameData.Read<JsonDocument>("res://tests/fixtures/worker-approach.json"); var fixture = doc.RootElement;
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); int count = 0;
        Check(fixture.GetProperty("format_version").GetInt32() == 1, "Worker approach reference schema");
        for (int packed = 0; packed < 256; packed++) foreach (int state in Numbers(fixture.GetProperty("states")))
        foreach (uint flags in fixture.GetProperty("visitor_flags").EnumerateArray().Select(v => v.GetUInt32()))
        foreach (int heading in Numbers(fixture.GetProperty("headings"))) foreach (var profile in fixture.GetProperty("profiles").EnumerateArray())
        {
            var p = profile.EnumerateArray().Select(v => v.GetUInt32()).ToArray(); var raw = ApproachRecord((byte)packed, (byte)state, (byte)heading, p);
            var receiver = Enumerable.Range(0, 37).Select(i => unchecked((byte)(i * 3 + 7))).ToArray(); receiver[0] = 2; receiver[0x0e] = 4; receiver[0x17] = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(receiver.AsSpan(4), p[2]); BinaryPrimitives.WriteUInt32LittleEndian(receiver.AsSpan(8), p[3]);
            var own = Convert.FromHexString(templates[11].RawRecordHex); var target = Convert.FromHexString(templates[4].RawRecordHex);
            own[3] = (byte)p[5]; own[4] = (byte)p[6]; target[3] = (byte)p[7]; target[4] = (byte)p[8];
            BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(0x40), p[4] | 512); new byte[] { 176, 16, 160, 0 }.CopyTo(target, 0x53);
            BinaryPrimitives.WriteUInt32LittleEndian(own.AsSpan(0x40), flags); BinaryPrimitives.WriteUInt16LittleEndian(own.AsSpan(0x48), (ushort)p[9]); own[0x4a] = (byte)p[10];
            var result = OriginalWorkerApproach.Step(raw, receiver, own, target, 4096, 4096);
            digest.AppendData(raw); digest.AppendData(receiver); digest.AppendData(new byte[] { (byte)result.Status, result.MoveRequested ? (byte)1 : (byte)0, result.StopRequested ? (byte)1 : (byte)0 }); count++;
        }
        var expected = fixture.GetProperty("results").GetProperty("approach");
        Check(count == expected.GetProperty("case_count").GetInt32(), "Full approach reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), "Full 8F70 records, result flags and movement boundaries match x86/WRK");
        var missing = ApproachRecord(243, 0, 0, fixture.GetProperty("profiles")[0].EnumerateArray().Select(v => v.GetUInt32()).ToArray());
        BinaryPrimitives.WriteUInt32LittleEndian(missing.AsSpan(0x1c), 0);
        var absent = OriginalWorkerApproach.Step(missing, [], Convert.FromHexString(templates[11].RawRecordHex), [], 4096, 4096);
        Check(absent.Status == WorkerApproachStatus.Invalid && Convert.ToHexStringLower(missing) == fixture.GetProperty("missing_record_hex").GetString(), "Missing target clears the handoff flag without reading a receiver");
        var turn = fixture.GetProperty("turn"); count = 0;
        foreach (var config in turn.GetProperty("configurations").EnumerateArray()) foreach (int state in Numbers(turn.GetProperty("states")))
        foreach (int timer in Numbers(turn.GetProperty("timers"))) foreach (int heading in Numbers(turn.GetProperty("headings"))) foreach (int flags in Numbers(turn.GetProperty("flags")))
        {
            var raw = AnimationRecord((byte)state, (byte)timer, 17, (byte)heading, (ushort)flags);
            var impact = OriginalWorkerAnimation.Step(raw, AnimationTemplate(Numbers(config)));
            if (impact.HasValue) throw new InvalidOperationException("Turn emitted a strike effect");
            digest.AppendData(raw); count++;
        }
        expected = fixture.GetProperty("results").GetProperty("turn");
        Check(count == expected.GetProperty("case_count").GetInt32(), "Turn animation reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), "Turn periods, pending requests and full records match original animation");
    }
}
