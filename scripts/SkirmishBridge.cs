using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>The player's bridge: the build list of the castle orders command 58h with template 7 from the first to the second point
/// (docs/bridges.md); the ported builder engine AFDB pays per segment and lays them along the line, as for the AI.</summary>
public sealed partial class Skirmish
{
    /// <summary>The shape of a new line segment and of its neighbours (fence, bridge): vertical, horizontal, entrance and exit ends (LE:1 BB8B).</summary>
    private void ReshapeLine(SkirmishActor segment) =>
        _lineNeighbors.Reshape((byte)Map.Width, (byte)Map.Height,
            (plane, cell) => Occupancy.Cells[plane * Map.Width * Map.Height + cell] is var id and not 0 ? Actors.FirstOrDefault(a => a.Alive && a.Id == id)?.Record : null,
            index => Convert.FromHexString(_templates.Armies["ARMY__02.DAT"].Templates[index].RawRecordHex), segment.Record);

    /// <summary>Frame offset of the segment's shape (+22) in a line template: shape · (2·DAT[4A] + DAT[4B]).</summary>
    private static int LineShapeFrames(SkirmishActor building) =>
        building.Index02 != -1 && building.Record.Length > 0x22 && (BinaryPrimitives.ReadUInt32LittleEndian(building.Parameters.AsSpan(0x40)) & 0x80) != 0
            ? (sbyte)building.Record[0x22] * (2 * building.Parameters[0x4a] + building.Parameters[0x4b]) : 0;

    /// <summary>DAT footprint height of an ARMY__02 template (for the sprite anchor of buildings and ruins).</summary>
    public int StructureFootprintHeight(int templateIndex) => _templates.Armies["ARMY__02.DAT"].Templates[templateIndex].Construction.FootprintHeight;
    public TemplateEntry BridgeTemplate => _templates.Armies["ARMY__02.DAT"].Templates[7];
    /// <summary>The line template being placed: 7 the bridge, 5 the fence (docs/bridges.md). The UI sets it when a factory list item starts.</summary>
    public int LineIndex { get; set; } = 7;
    public TemplateEntry LineTemplate => _templates.Armies["ARMY__02.DAT"].Templates[LineIndex];

    /// <summary>The second point snaps to the axis of the longer side (adapter: the line step 69FE is verified for axis lines).</summary>
    public static Vector2I BridgeEnd(Vector2I from, Vector2I to) =>
        Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y) ? new Vector2I(to.X, from.Y) : new Vector2I(from.X, to.Y);

    /// <summary>The line of a bridge from the first to the second clicked cell, both included: the cells the segments cover are [first, last] along
    /// the axis and the segment's own footprint across. Returns the anchor of the first segment (the centre of its footprint: the clicked cell + half the
    /// footprint) and the point where the builder's line (69FE) ends: one footprint past the last segment, so that the last segment covers the second cell.</summary>
    public (Vector2I Start, Vector2I Target, int Segments) BridgeLine(Vector2I from, Vector2I to)
    {
        to = BridgeEnd(from, to);
        var raw = Convert.FromHexString(LineTemplate.RawRecordHex); int fw = raw[3], fh = raw[4];
        var start = from + new Vector2I(fw / 2, fh / 2);
        if (Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y) && to.Y == from.Y)
        {
            int length = Math.Abs(to.X - from.X) + 1, n = (length + fw - 1) / fw, sign = to.X >= from.X ? 1 : -1;
            if (sign < 0) start = new Vector2I(from.X - (fw - 1) + fw / 2, start.Y);
            return (start, start + new Vector2I(sign * n * fw, 0), n);
        }
        int lengthY = Math.Abs(to.Y - from.Y) + 1, m = (lengthY + fh - 1) / fh, signY = to.Y >= from.Y ? 1 : -1;
        if (signY < 0) start = new Vector2I(start.X, from.Y - (fh - 1) + fh / 2);
        return (start, start + new Vector2I(0, signY * m * fh), m);
    }

    /// <summary>The anchors of the segments of the line (their footprints are the cells the bridge occupies).</summary>
    public List<Vector2I> BridgeCells(Vector2I from, Vector2I to)
    {
        var (start, target, _) = BridgeLine(from, to);
        var raw = Convert.FromHexString(LineTemplate.RawRecordHex);
        var engine = new OriginalBuilder(_arctangent);
        var cells = new List<Vector2I>();
        uint x = (uint)(start.X * 32), y = (uint)(start.Y * 32), x1 = (uint)(target.X * 32), y1 = (uint)(target.Y * 32);
        for (int guard = 0; guard < 256; guard++)
        {
            cells.Add(new Vector2I((int)(x >> 5), (int)(y >> 5)));
            var next = engine.Step(raw, x, y, x1, y1);
            if (next.Done) break;
            (x, y) = (next.X, next.Y);
        }
        return cells;
    }

    /// <summary>The terrain and the occupancy allow a segment in the cell (AD5F on the bridge's mask; an occupied cell of another object is refused).</summary>
    public bool BridgeCellFree(Vector2I cell) =>
        new SkirmishBuilderWorld(this, Rules.PlayerSlot).Probe((byte)LineIndex, 2, (uint)(cell.X * 32), (uint)(cell.Y * 32), out _) == BuilderProbe.Clear;

    /// <summary>The wallet covers every segment of the line.</summary>
    public bool CanAffordBridge(int segments)
    {
        var price = Price(LineTemplate); var have = Resources;
        return price.First * (uint)segments <= have.First && price.Second * (uint)segments <= have.Second && price.Third * (uint)segments <= have.Third;
    }

    /// <summary>Order the castle's command 58h: returns the number of segments laid (the line stops at the first segment the wallet or the terrain refuses).</summary>
    public int BuildBridge(Vector2I from, Vector2I to)
    {
        if (Paused || Outcome != "" || !Inside(from) || !Inside(to)) return 0;
        if (!Actors.Any(IsFactory)) { Log("Мосты, заборы и башни строят через завод."); return 0; }
        var (start, target, _) = BridgeLine(from, to);
        if (!BridgeCellFree(start)) { Log("Здесь нельзя строить мост: первая секция не встаёт."); return 0; }
        var raw = Convert.FromHexString(LineTemplate.RawRecordHex);
        var wallet = Resources;
        var participant = new byte[0xf5];
        BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x18), wallet.First); BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x1c), wallet.Second);
        BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x20), wallet.Third); BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x24), wallet.Fourth);
        var builder = new byte[0x31];
        builder[0] = 2; builder[3] = (byte)Rules.PlayerSlot; builder[0x14] = 0x58; builder[0x22] = (byte)LineIndex;
        BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x29), (ushort)(start.X * 32)); BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x2b), (ushort)(start.Y * 32));
        BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x2d), (ushort)(target.X * 32)); BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x2f), (ushort)(target.Y * 32));
        int before = Actors.Count;
        var engine = new OriginalBuilder(_arctangent) { Toggle = _builderToggle };
        engine.Run(new SkirmishBuilderWorld(this, Rules.PlayerSlot), builder, raw, participant);
        _builderToggle = engine.Toggle;
        SetResources(new ResourceQuad(BinaryPrimitives.ReadUInt32LittleEndian(participant.AsSpan(0x18)), BinaryPrimitives.ReadUInt32LittleEndian(participant.AsSpan(0x1c)),
            BinaryPrimitives.ReadUInt32LittleEndian(participant.AsSpan(0x20)), BinaryPrimitives.ReadUInt32LittleEndian(participant.AsSpan(0x24))));
        int laid = Actors.Count - before;
        Log(laid > 0 ? $"{LineTemplate.Name}: заложено секций — {laid}." : $"Здесь нельзя строить: {LineTemplate.Name.ToLowerInvariant()}.");
        return laid;
    }
}
