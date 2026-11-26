using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>The pit traps of the first map (ARMY__03 12 and 31, slot 6; docs/campaign-items.md): the ported automaton <see cref="OriginalPit"/> with the
/// two services it calls: the area step is the ported D2D1 (<see cref="OriginalUnitActions.FD2D1"/>) over the occupancy planes; the search is still an adapter instead of 11DDE: it counts living
/// non-building units in the 3×3 square (hostile by the trap's owner mask). Off by default (<see cref="SkirmishRules.Pits"/>).</summary>
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
        /// <summary>D2D1 of the original (<see cref="OriginalUnitActions.FD2D1"/>, verified against the x86) over the skirmish's occupancy planes: the pit claims or frees its 2×2 square and crushes what stands in it.</summary>
        public uint Area(uint reference, uint replaced, byte crush, byte kind, byte[] record)
        {
            var world = new PitActionWorld(battle);
            var g = new ActionRegs { Eax = reference, Ebx = replaced, Cl = crush, Ch = kind };
            world.Ds[0x273f] = 2; world.Ds[0x2740] = 2; BinaryPrimitives.WriteUInt32LittleEndian(world.Ds.AsSpan(0x18de), (uint)battle.Map.Width);
            OriginalUnitActions.FD2D1(world, g, record);
            foreach (var a in world.Touched.ToArray()) if (!a.Alive) battle.Kill(a);
            return g.Eax;
        }
        public void Service(byte[] record) { }
    }

    /// <summary>The memory the pit's area step reads: the occupancy planes ([18CE + 4k]), the references (actor ids) and the data segment windows.</summary>
    private sealed class PitActionWorld(Skirmish battle) : IUnitActionWorld
    {
        public List<SkirmishActor> Touched { get; } = [];
        public byte[] Ds { get; } = new byte[0x10000];
        private const uint Base = 0x1000;
        public uint PlanePointer(uint k) => Base + k * (uint)(battle.Occupancy.Width * battle.Occupancy.Height) * 4;
        public uint PlaneRead(uint address) => battle.Occupancy.Cells[(int)((address - Base) / 4)];
        public void PlaneWrite(uint address, uint value) => battle.Occupancy.SetCell((int)((address - Base) / 4), value);
        public uint PoolBase => 0;
        public byte[] ObjectAt(uint pointer)
        {
            if (battle.Find(pointer) is { } actor) { Touched.Add(actor); return actor.Record; }
            return new byte[0x40];
        }
        public void Call(uint address, ActionRegs g) => throw new InvalidOperationException($"call {address:X}");
        public byte[] Template => throw new NotSupportedException();
        public byte[] Stats => throw new NotSupportedException();
        public uint G512 { get; set; } public uint G51A => 0; public uint G51E => 0; public byte G1C9F { get; set; } public byte G522 => 0; public uint G50E { get; set; }
        public byte[] PartOf(byte owner) => throw new NotSupportedException();
        public uint ArmyTable(byte army) => 0;
        public byte TableByte(uint address) => 0xff;
        public byte Fs13D { get; set; } public byte G525 => 0; public byte G526 => 0; public byte G1C61 { get; set; } public uint G2730 => 0;
        public byte[] Route => throw new NotSupportedException();
        public uint TileDescriptor(uint tile) => 0;
        public void SetG1CCB(int index, byte value) { }
    }
}
