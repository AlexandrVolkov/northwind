using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>The pit traps of the first map (ARMY__03 12 and 31, slot 6; docs/campaign-items.md): the ported automaton <see cref="OriginalPit"/> with the
/// two services it calls implemented over the skirmish's actors — adapters, not the original routines 11DDE and D2D1: the search counts living
/// non-building units in the 3×3 square (hostile by the trap's owner mask), the area step crushes (kills) the units in the 2×2 footprint and
/// does not claim occupancy cells. Off by default (<see cref="SkirmishRules.Pits"/>).</summary>
public sealed partial class Skirmish
{
    private readonly List<byte[]> _pits = [];
    public IReadOnlyList<byte[]> Pits => _pits;
    private SkirmishPitWorld? _pitWorld;

    private void InitializePits(ScenarioData scenario)
    {
        if (!Rules.Pits) return;
        foreach (var slot in scenario.Slots.Where(s => s.SlotIndex is 6 or 7))
            foreach (var obj in slot.Objects.Where(o => o.TemplateFile == "ARMY__03.DAT" && o.TemplateIndex is 12 or 31))
            {
                var record = Convert.FromHexString(obj.RawRecordHex);
                OriginalPit.Initialize(record); record[0x15] = 0; record[0x14] &= 0x7f;
                _pits.Add(record);
            }
        _pitWorld = new SkirmishPitWorld(this);
    }

    private void TickPits()
    {
        if (_pitWorld == null) return;
        foreach (var pit in _pits) OriginalPit.Step(_pitWorld, pit, (byte)_globalTick, 0);
    }

    private sealed class SkirmishPitWorld(Skirmish battle) : IPitWorld
    {
        private static IEnumerable<SkirmishActor> Inside(Skirmish battle, int x0, int y0, int w, int h) =>
            battle.Actors.Where(a => a.Alive && !a.Structure && a.Cell.X >= x0 && a.Cell.X < x0 + w && a.Cell.Y >= y0 && a.Cell.Y < y0 + h);
        public (uint Found, uint Other) Search(uint corner, uint size, byte[] record)
        {
            int x0 = (int)(corner & 0xff), y0 = (int)((corner >> 8) & 0xff);
            uint mask = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(0x22)); uint found = 0, other = 0;
            foreach (var a in Inside(battle, x0, y0, 3, 3)) if ((mask >> (a.Record[3] & 31) & 1) != 0) found++; else other++;
            return (found, other);
        }
        public uint Area(uint reference, uint replaced, byte crush, byte kind, byte[] record)
        {
            int x = (int)(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4)) >> 5), y = (int)(BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8)) >> 5);
            uint count = 0;
            foreach (var a in Inside(battle, x - 1, y - 1, 2, 2).ToArray())
            {
                count++;
                if (crush == 0) continue;
                a.Health = 0;
                a.Record[0x17] = (byte)((a.Record[0x17] & 0xe7) | (kind << 3));
                battle.Kill(a);
            }
            return count;
        }
        public void Service(byte[] record) { }
    }
}
