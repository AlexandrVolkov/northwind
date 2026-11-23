using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>The rockfall trap of the first map (ARMY__03 2, WRK 189; docs/campaign-items.md): every fourth update (random byte &amp; 3 = 0) it looks for a living unit in
/// the 5×5 cells below it (FS:14B, the owner of the trap and the birds and decorations excluded) and lets a rock fall at a random point of the strip
/// (x ± 48, y + 50 .. 145 px, as WRK 1CF..1F5 computes it). Adapter, not the original effect chain (type 18h, WRK ARMY__00 1012, services A5/A9/AD): the rock falls for
/// <see cref="FallTicks"/> updates, strikes the cell with the strength of a catapult hit and leaves a puff of dust (effect 16h, frames 170h..179h).</summary>
public sealed partial class Skirmish
{
    public const int FallTicks = 12, DustTicks = 30;
    private readonly List<byte[]> _rockfalls = [];
    public List<(Vector2 Point, int Left, int Variant)> Rocks { get; } = [];
    public List<(Vector2 Point, int Left)> Dust { get; } = [];
    public IReadOnlyList<byte[]> Rockfalls => _rockfalls;

    private void InitializeRockfalls(ScenarioData scenario)
    {
        if (!Rules.Pits) return;
        foreach (var obj in scenario.Slots.Where(s => s.SlotIndex is 6 or 7).SelectMany(s => s.Objects).Where(o => o.TemplateFile == "ARMY__03.DAT" && o.TemplateIndex is 2 or 36))
            _rockfalls.Add(Convert.FromHexString(obj.RawRecordHex));
    }

    private void TickRockfalls()
    {
        for (int i = Rocks.Count - 1; i >= 0; i--)
        {
            var rock = Rocks[i];
            if (rock.Left > 1) { Rocks[i] = rock with { Left = rock.Left - 1 }; continue; }
            Rocks.RemoveAt(i); RockLands(rock.Point);
        }
        for (int i = Dust.Count - 1; i >= 0; i--) { var d = Dust[i]; if (d.Left <= 1) Dust.RemoveAt(i); else Dust[i] = d with { Left = d.Left - 1 }; }
        foreach (var fall in _rockfalls)
        {
            if ((_terrainCatalog.NextSecondaryByte() & 3) != 0) continue;
            int cx = (int)(BinaryPrimitives.ReadUInt32LittleEndian(fall.AsSpan(4)) >> 5), cy = (int)(BinaryPrimitives.ReadUInt32LittleEndian(fall.AsSpan(8)) >> 5);
            bool prey = Actors.Any(a => a.Alive && !a.Structure && !a.Scenery && !IsBird(a) && a.Record[3] != fall[3] && a.Cell.X >= cx - 2 && a.Cell.X <= cx + 2 && a.Cell.Y >= cy && a.Cell.Y <= cy + 4);
            if (!prey) continue;
            float x = BinaryPrimitives.ReadUInt32LittleEndian(fall.AsSpan(4)) + (_terrainCatalog.NextRandomByte() * 0x60 >> 8) - 0x30;
            float y = BinaryPrimitives.ReadUInt32LittleEndian(fall.AsSpan(8)) + (_terrainCatalog.NextRandomByte() * 0x60 >> 8) + 0x32;
            Rocks.Add((new Vector2(x, y), FallTicks, _terrainCatalog.NextRandomByte() % 5));
            if ((_terrainCatalog.NextRandomByte() & 0x1f) == 0 && Sounds.Count < 64) Sounds.Add(new("ARMY__03.FX", 6, SoundRandom(), new Vector2(x, y)));
        }
    }

    private void RockLands(Vector2 point)
    {
        if (point.X < 0 || point.Y < 0 || point.X >= Map.Width * 32 || point.Y >= Map.Height * 32) return;
        byte cx = (byte)((int)point.X >> 5), cy = (byte)((int)point.Y >> 5); int plane = Occupancy.Width * Occupancy.Height;
        var struck = new List<SkirmishActor>();
        OriginalCatapultRules.Hit(OriginalCatapultRules.Attack, 2, 3, cx, cy, 1, 1, (byte)Map.Width, (byte)Map.Height,
            (p, cell) => p * plane + cell < Occupancy.Cells.Length ? Occupancy.Cells[p * plane + cell] : 0,
            r => { if (Find(r) is not { } a || a.Scenery || IsBird(a)) return (new byte[0x40], new byte[171]); struck.Add(a); return (a.Record, a.Parameters); });
        foreach (var victim in struck) if (!victim.Alive) Kill(victim);
        Dust.Add((point, DustTicks));
    }
}
