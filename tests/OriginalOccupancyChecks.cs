using System.Buffers.Binary;
using System.Security.Cryptography;
using Northwind;

public partial class TestRunner
{
    private void OriginalOccupancyChecks()
    {
        var reference = GameData.Read<OccupancyReference>("res://tests/fixtures/occupancy.json");
        Check(reference.FormatVersion == 1, "Occupancy reference schema");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var grid = new OriginalOccupancyMap(reference.Width, reference.Height);
        var initial = new uint[3 * reference.Width * reference.Height];
        var output = new byte[initial.Length * 4 + 4];
        foreach (bool conditional in new[] { false, true })
        {
            int count = 0;
            foreach (var footprint in reference.Footprints)
            for (int top = 0; top <= reference.Height - footprint[1]; top++)
            for (int left = 0; left <= reference.Width - footprint[0]; left++)
            foreach (byte layer in reference.Layers)
            foreach (uint value in reference.Values)
            foreach (var pattern in reference.Patterns)
            foreach (uint expected in conditional ? reference.Values : new uint[] { 0 })
            {
                for (int index = 0; index < initial.Length; index++) initial[index] = pattern[index % pattern.Length];
                grid.LoadCells(initial);
                uint x = (uint)((left + footprint[0] / 2) * 32), y = (uint)((top + footprint[1] / 2) * 32);
                uint conflicts = 0;
                if (conditional) conflicts = grid.ReplaceMatchingFootprint(footprint[0], footprint[1], layer, x, y, expected, value);
                else grid.WriteFootprint(footprint[0], footprint[1], layer, x, y, value);
                var cells = grid.Cells;
                for (int index = 0; index < cells.Length; index++)
                    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(index * 4), cells[index]);
                BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(initial.Length * 4), conflicts);
                digest.AppendData(output.AsSpan(0, initial.Length * 4 + (conditional ? 4 : 0))); count++;
            }
            string mode = conditional ? "conditional" : "overwrite";
            Check(count == reference.CaseCount[mode], $"{mode} occupancy reference coverage");
            Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == reference.Sha256[mode], $"{mode} occupancy matches original x86");
        }
    }

    private void ImportedOccupancyChecks()
    {
        var map = GameData.Read<MapData>("res://assets/imported/map.json");
        var scenario = GameData.Read<ScenarioData>("res://assets/imported/scenario.json");
        var templates = GameData.Read<TemplatesData>("res://assets/imported/templates.json");
        var snapshot = OriginalOccupancySnapshot.Build(map, scenario, templates);
        Check(snapshot.MatchesReference, $"Initial registration matches native x86 reconstruction for {scenario.MapFile}");
        Check(snapshot.References.Count == scenario.Slots.Sum(s => s.Objects.Length), "All source references are indexed without using zero as an object");
    }
}

public sealed class OccupancyReference
{
    public int FormatVersion { get; set; }
    public byte Width { get; set; }
    public byte Height { get; set; }
    public List<byte>[] Footprints { get; set; } = [];
    public List<byte> Layers { get; set; } = [];
    public uint[] Values { get; set; } = [];
    public uint[][] Patterns { get; set; } = [];
    public Dictionary<string, int> CaseCount { get; set; } = [];
    public Dictionary<string, string> Sha256 { get; set; } = [];
}
