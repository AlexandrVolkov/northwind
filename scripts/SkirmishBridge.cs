using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>The player's bridge: the build list of the castle orders command 58h with template 7 from the first to the second point
/// (docs/bridges.md); the ported builder engine AFDB pays per segment and lays them along the line, as for the AI.</summary>
public sealed partial class Skirmish
{
    public TemplateEntry BridgeTemplate => _templates.Armies["ARMY__02.DAT"].Templates[7];

    /// <summary>The second point snaps to the axis of the longer side (adapter: the line step 69FE is verified for axis lines).</summary>
    public static Vector2I BridgeEnd(Vector2I from, Vector2I to) =>
        Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y) ? new Vector2I(to.X, from.Y) : new Vector2I(from.X, to.Y);

    /// <summary>The cells of the segments from the first to the second point (69FE steps).</summary>
    public List<Vector2I> BridgeCells(Vector2I from, Vector2I to)
    {
        to = BridgeEnd(from, to);
        var raw = Convert.FromHexString(BridgeTemplate.RawRecordHex);
        var engine = new OriginalBuilder(_arctangent);
        var cells = new List<Vector2I>();
        uint x = (uint)(from.X * 32), y = (uint)(from.Y * 32), x1 = (uint)(to.X * 32), y1 = (uint)(to.Y * 32);
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
        new SkirmishBuilderWorld(this, Rules.PlayerSlot).Probe(7, 2, (uint)(cell.X * 32), (uint)(cell.Y * 32), out _) == BuilderProbe.Clear;

    /// <summary>The wallet covers every segment of the line.</summary>
    public bool CanAffordBridge(int segments)
    {
        var price = Price(BridgeTemplate); var have = Resources;
        return price.First * (uint)segments <= have.First && price.Second * (uint)segments <= have.Second && price.Third * (uint)segments <= have.Third;
    }

    /// <summary>Order the castle's command 58h: returns the number of segments laid (the line stops at the first segment the wallet or the terrain refuses).</summary>
    public int BuildBridge(Vector2I from, Vector2I to)
    {
        if (Paused || Outcome != "" || !PlayerCastle.Alive || !Inside(from) || !Inside(to)) return 0;
        to = BridgeEnd(from, to);
        if (!BridgeCellFree(from)) { Log("Здесь нельзя строить мост: первая секция не встаёт."); return 0; }
        var raw = Convert.FromHexString(BridgeTemplate.RawRecordHex);
        var wallet = Resources;
        var participant = new byte[0xf5];
        BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x18), wallet.First); BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x1c), wallet.Second);
        BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x20), wallet.Third); BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x24), wallet.Fourth);
        var builder = new byte[0x31];
        builder[0] = 2; builder[3] = (byte)Rules.PlayerSlot; builder[0x14] = 0x58; builder[0x22] = 7;
        BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x29), (ushort)(from.X * 32)); BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x2b), (ushort)(from.Y * 32));
        BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x2d), (ushort)(to.X * 32)); BinaryPrimitives.WriteUInt16LittleEndian(builder.AsSpan(0x2f), (ushort)(to.Y * 32));
        int before = Actors.Count;
        var engine = new OriginalBuilder(_arctangent) { Toggle = _builderToggle };
        engine.Run(new SkirmishBuilderWorld(this, Rules.PlayerSlot), builder, raw, participant);
        _builderToggle = engine.Toggle;
        SetResources(new ResourceQuad(BinaryPrimitives.ReadUInt32LittleEndian(participant.AsSpan(0x18)), BinaryPrimitives.ReadUInt32LittleEndian(participant.AsSpan(0x1c)),
            BinaryPrimitives.ReadUInt32LittleEndian(participant.AsSpan(0x20)), BinaryPrimitives.ReadUInt32LittleEndian(participant.AsSpan(0x24))));
        int laid = Actors.Count - before;
        Log(laid > 0 ? $"Мост: заложено секций — {laid}." : "Здесь нельзя строить мост.");
        return laid;
    }
}
