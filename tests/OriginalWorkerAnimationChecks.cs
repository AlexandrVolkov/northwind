using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private static byte[] AnimationRecord(byte state, byte timer, byte progress, byte heading, ushort flags)
    {
        var raw = Enumerable.Range(0, 54).Select(i => unchecked((byte)(i * 17 + 3))).ToArray();
        raw[0] = 2; raw[3] = 12; raw[0x22] = 5; raw[0x23] = 254;
        raw[0x24] = progress; raw[0x25] = heading; raw[0x26] = timer; raw[0x2d] = state;
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(1), 65534);
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x17), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), 0xfffffff0);
        return raw;
    }
    private static byte[] AnimationTemplate(int[] config)
    {
        var raw = new byte[171]; BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x48), (ushort)config[0]);
        for (int i = 1; i < config.Length; i++) raw[0x49 + i] = (byte)config[i];
        new byte[] { 128, 127, 255, 1, 0, 0, 64, 192, 127, 128, 1, 255, 192, 64, 0, 127 }.CopyTo(raw, 0x60);
        raw[0x70] = 134; return raw;
    }
    private static byte[] EncodeImpact(WorkerImpact? impact)
    {
        var data = new byte[16];
        if (impact is not { } hit) return data;
        data[0] = 1; BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(1), hit.X);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(5), hit.Y);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(9), hit.TargetX);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(11), hit.TargetY);
        data[13] = hit.Army; data[14] = hit.Code; data[15] = hit.Layer; return data;
    }
    private void OriginalWorkerAnimationChecks()
    {
        using var document = GameData.Read<JsonDocument>("res://tests/fixtures/worker-animation.json");
        var fixture = document.RootElement; var step = fixture.GetProperty("step");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); int count = 0;
        Check(fixture.GetProperty("format_version").GetInt32() == 1, "Worker animation reference schema");
        foreach (var config in step.GetProperty("configurations").EnumerateArray())
        foreach (int state in Numbers(step.GetProperty("states")))
        for (int timer = 0; timer < 256; timer++)
        foreach (int progress in Numbers(step.GetProperty("progress")))
        foreach (int heading in Numbers(step.GetProperty("headings")))
        foreach (int flags in Numbers(step.GetProperty("flags")))
        {
            var raw = AnimationRecord((byte)state, (byte)timer, (byte)progress, (byte)heading, (ushort)flags);
            var impact = OriginalWorkerAnimation.Step(raw, AnimationTemplate(Numbers(config)));
            digest.AppendData(raw); digest.AppendData(EncodeImpact(impact)); count++;
        }
        Check(count == step.GetProperty("case_count").GetInt32(), "Worker animation reference coverage");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == step.GetProperty("sha256").GetString(),
            "Stationary worker full record and effect request match native x86");
        var source = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates[10];
        var template = Convert.FromHexString(source.RawRecordHex); var sequence = fixture.GetProperty("sequence");
        Check(Convert.ToHexStringLower(SHA256.HashData(template)) == sequence.GetProperty("template_sha256").GetString(), "Worker sequence uses original DAT");
        var worker = AnimationRecord(0, 0, 0, 2, 0);
        worker[0x23] = 3; worker[0x32] = 5; worker[0x33] = 3;
        worker[0x15] = 2; worker[0x16] = 0; worker[0x2f] = 0; worker[0x34] = template[0x78];
        BinaryPrimitives.WriteUInt32LittleEndian(worker.AsSpan(4), 4 * 32 + 16);
        BinaryPrimitives.WriteUInt32LittleEndian(worker.AsSpan(8), 3 * 32 + 16);
        var impacts = new List<int>(); bool completed = false; int ticks = 0;
        for (; ticks < 2000; ticks++)
        {
            var impact = OriginalWorkerAnimation.Step(worker, template);
            if (impact.HasValue) impacts.Add(ticks);
            if ((worker[0x2d] & 15) != 3)
            {
                if ((worker[0x34] & 31) == 0) { OriginalForestryRules.FinishLoad(worker); completed = true; }
                else OriginalForestryRules.AdvanceWork(worker, template);
            }
            digest.AppendData(worker); digest.AppendData(EncodeImpact(impact)); digest.AppendData(new byte[] { completed ? (byte)1 : (byte)0 });
            if (completed) { ticks++; break; }
        }
        Check(completed && ticks == sequence.GetProperty("ticks").GetInt32() && impacts.SequenceEqual(Numbers(sequence.GetProperty("impact_ticks"))),
            "Twenty-strike load finishes on native ticks and all effect boundaries match");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == sequence.GetProperty("sha256").GetString(),
            "Every record in the animation/work sequence matches original instructions");
    }
}
