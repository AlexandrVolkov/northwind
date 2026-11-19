using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalVisibilityChecks()
    {
        using var fixture = GameData.Read<JsonDocument>("res://tests/fixtures/visibility.json");
        var root = fixture.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string Finish() => Convert.ToHexStringLower(digest.GetHashAndReset());
        int count = 0;
        foreach (var row in root.GetProperty("reveal").GetProperty("cases").EnumerateArray())
        {
            int[] a = row.EnumerateArray().Select(v => v.GetInt32()).ToArray();
            int width = a[0], height = a[1]; byte owner = (byte)a[6];
            byte[] map = Enumerable.Range(0, width * height).Select(i => unchecked((byte)(i * 37 + owner * 19))).ToArray();
            byte[] record = new byte[64], template = new byte[171];
            record[3] = owner; record[0x16] = (byte)a[5]; template[0x47] = (byte)a[4];
            template[3] = 4; template[4] = 7; template[0x40] = 8;
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)(a[2] * 32 + 16));
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), (uint)(a[3] * 32 + 31));
            OriginalVisibilityRules.Reveal(map, width, height, record, template);
            digest.AppendData(map); count++;
        }
        Check(count == root.GetProperty("reveal").GetProperty("case_count").GetInt32(), "All native reveal boundary, radius, experience and owner cases replayed");
        Check(Finish() == root.GetProperty("reveal").GetProperty("sha256").GetString(), "Exploration maps match 813E/6885/8198 byte for byte");
        for (int owner = 0; owner < 256; owner++)
        {
            byte[] map = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            OriginalVisibilityRules.RevealAllFromScenario(map, (byte)owner); digest.AppendData(map);
        }
        Check(Finish() == root.GetProperty("scenario_all").GetProperty("sha256").GetString(), "Scenario reveal preserves native FE shift, including its extra cleared bits");
        for (int participant = 0; participant < 16; participant++)
        foreach (int noise in new[] { 0, 255 })
        for (int pattern = 0; pattern < 512; pattern++)
        {
            int bit = OriginalVisibilityRules.ParticipantBit((byte)participant);
            byte[] values = Enumerable.Range(0, 9).Select(i => (byte)((noise & (255 ^ bit)) | ((pattern & (1 << i)) != 0 ? bit : 0))).ToArray();
            digest.AppendData([OriginalVisibilityRules.FogCorners(values[8], values.AsSpan(0, 8), (byte)participant)]);
        }
        Check(Finish() == root.GetProperty("corners").GetProperty("sha256").GetString(), "All 16384 native fog corner masks agree, including unrelated participant bits");
        for (byte mask = 1; mask < 15; mask++)
        for (int parity = 0; parity < 2; parity++)
        {
            byte[] value = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(value, OriginalVisibilityRules.FogSprite(mask, 0, parity)); digest.AppendData(value);
        }
        Check(Finish() == root.GetProperty("sprites").GetProperty("sha256").GetString(), "Fog sprite selector matches A063 for both world parities");
        Check(OriginalVisibilityRules.FogSprite(0, 0, 0) == -1 && OriginalVisibilityRules.FogSprite(15, 0, 0) == -1,
            "Clear and solid black tiles do not request a mask sprite");
        foreach (var row in root.GetProperty("minimap").GetProperty("cases").EnumerateArray())
        {
            int[] a = row.EnumerateArray().Select(v => v.GetInt32()).ToArray();
            int width = a[0], height = a[1], salt = a[3], n = width * height;
            byte[] map = Enumerable.Range(0, n).Select(i => unchecked((byte)(i * 37 + salt * 19))).ToArray();
            ushort[] tiles = Enumerable.Range(0, n).Select(i => (ushort)((i * 17 + salt * 3) % 256)).ToArray();
            byte[] terrain = Enumerable.Range(0, 256).Select(i => unchecked((byte)(i * 3 + 29))).ToArray();
            uint[] plane1 = Enumerable.Range(0, n).Select(i => i % 5 == salt % 5 ? (uint)(1 + (i * 3) % 23) : 0).ToArray();
            uint[] plane2 = Enumerable.Range(0, n).Select(i => i % 7 == salt % 7 ? (uint)(1 + i % 23) : 0).ToArray();
            byte[] colors = Enumerable.Range(0, 16).Select(i => unchecked((byte)(i * 11 + salt * 7))).ToArray();
            Array.Clear(colors, 8, 8); // Native colours 8..15 alias the zero minimap X/Y globals.
            byte[] pixels = OriginalVisibilityRules.Minimap(map, tiles, terrain, plane1, plane2,
                reference => unchecked((byte)(reference * 5 + salt)), (byte)a[2], (byte)a[4], colors);
            OriginalVisibilityRules.DrawCameraOutline(pixels, width, height, 1, 1);
            byte[] framebuffer = new byte[640 * 480]; Array.Fill(framebuffer, (byte)0xa5);
            for (int y = 0; y < height; y++) pixels.AsSpan(y * width, width).CopyTo(framebuffer.AsSpan(y * 640));
            digest.AppendData(framebuffer);
        }
        Check(Finish() == root.GetProperty("minimap").GetProperty("sha256").GetString(), "60 complete minimap framebuffers match A1D1, including black, owners, plane priority and camera outline");
        byte[] unexplored = OriginalVisibilityRules.CreateUnexplored(16, 16);
        Check(unexplored.All(v => v == 255), "Original initial map is entirely unexplored for every participant");
        Check(!OriginalVisibilityRules.IsExplored(255, 0) && OriginalVisibilityRules.IsExplored(254, 0) && !OriginalVisibilityRules.IsExplored(254, 1),
            "Exploration persists independently for each participant bit");
        byte[] example = new byte[64], tpl = new byte[171];
        BinaryPrimitives.WriteUInt32LittleEndian(example.AsSpan(4), 8 * 32 + 16);
        BinaryPrimitives.WriteUInt32LittleEndian(example.AsSpan(8), 8 * 32 + 16);
        tpl[0x47] = 3; example[0x16] = 40;
        OriginalVisibilityRules.Reveal(unexplored, 16, 16, example, tpl);
        byte[] before = (byte[])unexplored.Clone();
        OriginalVisibilityRules.Reveal(unexplored, 16, 16, example, tpl);
        Check(unexplored.SequenceEqual(before), "Repeated reveal never closes already explored cells");
        Check(OriginalVisibilityRules.Area(16, 16, example, tpl) == new VisibilityArea(4, 4, 9, 9), "Experience 40 adds one to radius 3");
    }
}
