using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private static byte[] DeliveryRecord()
    {
        var raw = Enumerable.Range(0, 54).Select(i => unchecked((byte)(i * 7 + 5))).ToArray(); raw[0] = 2; raw[0x0e] = 10; raw[0x31] = 0; return raw;
    }
    private void OriginalWorkerDeliveryChecks()
    {
        using var doc = GameData.Read<JsonDocument>("res://tests/fixtures/worker-delivery.json"); var fixture = doc.RootElement;
        var constants = GameData.Read<OriginalTerrainData>("res://data/original_terrain.json");
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates;
        byte[] Template(int index) => Convert.FromHexString(templates[index].RawRecordHex);
        Check(fixture.GetProperty("format_version").GetInt32() == 1, "Worker delivery reference schema");
        foreach (int index in new[] { 4, 10, 11 }) Check(Convert.ToHexStringLower(SHA256.HashData(Template(index))) == fixture.GetProperty("templates_sha256").GetProperty(index.ToString()).GetString(), "Worker delivery uses verified DAT");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); int count = 0;
        void Verify(string name)
        {
            var expected = fixture.GetProperty("results").GetProperty(name);
            Check(count == expected.GetProperty("case_count").GetInt32(), $"Delivery {name} coverage");
            Check(Convert.ToHexStringLower(hash.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), $"Delivery {name} matches original x86"); count = 0;
        }
        void AppendUInt(uint value) { var bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); hash.AppendData(bytes); }
        var spec = fixture.GetProperty("search");
        foreach (var originValue in spec.GetProperty("origins").EnumerateArray())
        foreach (var positions in spec.GetProperty("positions").EnumerateArray())
        for (int condition = 0; condition < 256; condition++)
        foreach (int threshold in Numbers(spec.GetProperty("thresholds")))
        foreach (int flags in Numbers(spec.GetProperty("flags")))
        {
            var origin = originValue.EnumerateArray().Select(v => v.GetUInt32()).ToArray(); var candidates = new List<DeliveryCandidate>(); int i = 0;
            foreach (var pos in positions.EnumerateArray())
            { var p = pos.EnumerateArray().Select(v => v.GetUInt32()).ToArray(); candidates.Add(new((uint)(0x100 + i * 37), p[0], p[1], i == 0 ? (uint)flags : 512, i == 0 ? (byte)condition : (byte)70, i == 0 ? (byte)threshold : (byte)10)); i++; }
            AppendUInt(OriginalWorkerDelivery.FindReceiver(origin[0], origin[1], candidates)); count++;
        }
        Verify("search"); spec = fixture.GetProperty("approach");
        foreach (int width in Numbers(spec.GetProperty("widths")))
        foreach (int flags in Numbers(spec.GetProperty("flags")))
        foreach (var offsetsValue in spec.GetProperty("offsets").EnumerateArray())
        foreach (var positionValue in spec.GetProperty("positions").EnumerateArray())
        {
            var visitor = Template(10); var receiver = Template(4); visitor[3] = (byte)width;
            BinaryPrimitives.WriteUInt32LittleEndian(receiver.AsSpan(0x40), (uint)flags);
            var offsets = Numbers(offsetsValue); for (int i = 0; i < 4; i++) receiver[0x53 + i] = (byte)offsets[i];
            var pos = positionValue.EnumerateArray().Select(v => v.GetUInt32()).ToArray(); var point = OriginalWorkerDelivery.Approach(visitor, receiver, pos[0], pos[1], 4096, 4096);
            AppendUInt(point.X); AppendUInt(point.Y); hash.AppendData(new byte[] { point.Valid ? (byte)1 : (byte)0, point.Valid && point.IsCenter ? (byte)1 : (byte)0 }); count++;
        }
        Verify("approach");
        foreach (byte x in new byte[] { 0, 1, 127, 254 }) foreach (byte y in new byte[] { 0, 1, 127, 254 })
        foreach (byte fw in new byte[] { 0, 1, 127, 254 }) foreach (byte fh in new byte[] { 0, 1, 127, 254 })
        { AppendUInt(OriginalWorkerDelivery.BuildingAnchor(x, fw)); AppendUInt(OriginalWorkerDelivery.BuildingAnchor(y, fh)); count++; }
        Verify("anchor");
        foreach (ushort f1 in new ushort[] { 0, 1, 64, 128, 65535 }) foreach (ushort f2 in new ushort[] { 0, 1, 64, 128, 65535 }) foreach (byte packed in new byte[] { 0, 3, 255 })
        {
            var raw = DeliveryRecord(); var receiver = new byte[37]; raw[0x30] = packed;
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x17), f1); BinaryPrimitives.WriteUInt16LittleEndian(receiver.AsSpan(0x17), f2);
            OriginalWorkerDelivery.RequestHandoff(raw, receiver); hash.AppendData(raw); hash.AppendData(receiver); count++;
        }
        Verify("request");
        for (int packed = 0; packed < 256; packed++) foreach (ushort flags in new ushort[] { 0, 1, 64, 128, 65535 })
        {
            var raw = DeliveryRecord(); raw[0x30] = (byte)packed; BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(0x17), flags);
            var status = OriginalWorkerDelivery.WaitHandoff(raw); hash.AppendData(raw); hash.AppendData(new byte[] { (byte)status }); count++;
        }
        Verify("wait");
        for (int condition = 0; condition < 256; condition++) foreach (uint flags in fixture.GetProperty("grant_flags").EnumerateArray().Select(v => v.GetUInt32()))
        foreach (int heading in Numbers(fixture.GetProperty("headings"))) foreach (int random in Numbers(fixture.GetProperty("random_indices")))
        {
            var raw = DeliveryRecord(); raw[0x19] = (byte)condition; raw[0x25] = (byte)heading; var empty = Template(10);
            BinaryPrimitives.WriteUInt32LittleEndian(empty.AsSpan(0x40), flags);
            bool accepted = OriginalWorkerDelivery.GrantTimber(raw, empty, Template(11), constants.RandomValues[random]);
            hash.AppendData(raw); hash.AppendData(new byte[] { unchecked((byte)(random + (accepted ? 1 : 0))), accepted ? (byte)1 : (byte)0 }); count++;
        }
        Verify("grant");
        for (int heading = 0; heading < 256; heading++) foreach (int random in Numbers(fixture.GetProperty("random_indices")))
        {
            var raw = DeliveryRecord(); raw[0x0e] = 11; raw[0x25] = (byte)heading;
            OriginalWorkerDelivery.ReturnEmpty(raw, Template(10), constants.RandomValues[random]); hash.AppendData(raw); hash.AppendData(new byte[] { unchecked((byte)(random + 1)) }); count++;
        }
        Verify("return");
        foreach (uint x in new uint[] { 0, 16, 128, 240 }) foreach (uint y in new uint[] { 0, 16, 128, 240 })
        foreach (byte layer in new byte[] { 0, 1, 2, 129 }) foreach (uint reference in new uint[] { 0, 0x100 }) foreach (ushort flags in new ushort[] { 0, 64, 65535 })
        {
            var grid = new uint[192]; Array.Fill(grid, reference, (layer & 3) * 64, 64); AppendUInt(OriginalWorkerDelivery.WaitingWorker(8, 8, grid, layer, x, y, Template(4))); count++;
        }
        Verify("waiting");
    }
}
