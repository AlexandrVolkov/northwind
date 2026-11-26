using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>The pit traps of the first map (ARMY__03 12 and 31, slot 6; docs/campaign-items.md): the ported automaton <see cref="OriginalPit"/> with the
/// two services it calls: the area step is the ported D2D1 (<see cref="OriginalUnitActions.FD2D1"/>) over the occupancy planes; the search is the ported FS:14B (<see cref="OriginalUnitActions.FS14B"/>) with its callees 21A00, D7B1, 68BD, C344, D157 over the actors. Off by default (<see cref="SkirmishRules.Pits"/>).</summary>
public sealed partial class Skirmish
{
    private readonly List<byte[]> _pits = [];
    public IReadOnlyList<byte[]> Pits => _pits;
    private SkirmishPitWorld? _pitWorld;
    private readonly Dictionary<byte[], byte[]> _pitTemplates = new(ReferenceEqualityComparer.Instance);

    private void InitializePits(ScenarioData scenario)
    {
        if (!Rules.Pits) return;
        foreach (var slot in scenario.Slots.Where(s => s.SlotIndex is 6 or 7))
            foreach (var obj in slot.Objects.Where(o => o.TemplateFile == "ARMY__03.DAT" && o.TemplateIndex is 12 or 31))
            {
                var record = Convert.FromHexString(obj.RawRecordHex);
                OriginalPit.Initialize(record); record[0x15] = 0; record[0x14] &= 0x7f;
                _pits.Add(record); _pitTemplates[record] = Convert.FromHexString(_templates.Armies[obj.TemplateFile].Templates[obj.TemplateIndex].RawRecordHex);
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
        /// <summary>FS:14B (11DDE) of the original (<see cref="OriginalUnitActions.FS14B"/>, verified against the x86): the objects of the square around the pit, hostile by the trap's mask.</summary>
        public (uint Found, uint Other) Search(uint corner, uint size, byte[] record)
        {
            var world = new PitActionWorld(battle) { Self = record, SelfTemplate = battle._pitTemplates[record] };
            var g = new ActionRegs { Ecx = corner, Edx = size, Esi = PitActionWorld.SelfPointer };
            BinaryPrimitives.WriteUInt32LittleEndian(world.Ds.AsSpan(0x1c49), PitActionWorld.SelfTemplatePointer);
            world.SetMap();
            OriginalUnitActions.FS14B(world, g, record);
            return (g.Eax, g.Ebx);
        }
        /// <summary>D2D1 of the original (<see cref="OriginalUnitActions.FD2D1"/>, verified against the x86) over the skirmish's occupancy planes: the pit claims or frees its 2×2 square and crushes what stands in it.</summary>
        public uint Area(uint reference, uint replaced, byte crush, byte kind, byte[] record)
        {
            var world = new PitActionWorld(battle); world.SetMap();
            var g = new ActionRegs { Eax = reference, Ebx = replaced, Cl = crush, Ch = kind };
            world.Ds[0x273f] = 2; world.Ds[0x2740] = 2;
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
        public const uint SelfPointer = 0x7ffffff0, SelfTemplatePointer = 0x7fffff00, TemplateBase = 0x80000000;
        public byte[]? Self, SelfTemplate;
        public void SetMap()
        {
            BinaryPrimitives.WriteUInt32LittleEndian(Ds.AsSpan(0x18de), (uint)battle.Map.Width); Ds[0x18e2] = (byte)battle.Map.Height;
            BinaryPrimitives.WriteUInt32LittleEndian(Ds.AsSpan(0x1c49), SelfTemplatePointer);
        }
        public byte[] ObjectAt(uint pointer)
        {
            if (pointer == SelfPointer && Self != null) return Self;
            if (pointer == SelfTemplatePointer && SelfTemplate != null) return SelfTemplate;
            if (pointer >= TemplateBase) return new SearchWorld(battle, 0).Resolve(pointer - TemplateBase).Template;
            if (battle.Find(pointer) is { } actor) { Touched.Add(actor); return actor.Record; }
            return new SearchWorld(battle, 0).Resolve(pointer).Record;
        }
        public void Call(uint address, ActionRegs g)
        {
            switch (address)
            {
                case 0x21a00:   // clip the rectangle (CL, CH, DL, DH = left, top, width, height) to the map
                {
                    byte w = Ds[0x18de], h = Ds[0x18e2], cl = g.Cl, ch = g.Ch, dl = g.Dl, dh = g.Dh;
                    if (cl >= w) { dl += cl; cl = 0; }
                    if (ch >= h) { dh += ch; ch = 0; }
                    dl += cl; if (dl > w) dl = w; dl -= cl;
                    dh += ch; if (dh > h) dh = h; dh -= ch;
                    g.Cl = cl; g.Ch = ch; g.Dl = dl; g.Dh = dh; break;
                }
                case 0xd7b1:    // the list of the unique occupants of the rectangle at DS:1F45, ended by 0
                {
                    var list = OriginalTargetRules.Collect(new SearchWorld(battle, 0), g.Cl, g.Ch, g.Dl, g.Dh);
                    for (int i = 0; i < list.Count; i++) BinaryPrimitives.WriteUInt32LittleEndian(Ds.AsSpan(0x1f45 + 4 * i), list[i]);
                    BinaryPrimitives.WriteUInt32LittleEndian(Ds.AsSpan(0x1f45 + 4 * list.Count), 0); break;
                }
                case 0x68bd:    // the template of the object EDI
                    g.Edx = g.Edi == SelfPointer ? SelfTemplatePointer : TemplateBase + g.Edi; break;
                case 0xc344:    // Chebyshev distance of the positions ESI and EDI
                {
                    var a = ObjectAt(g.Esi); var b = ObjectAt(g.Edi);
                    long dx = Math.Abs((long)BitConverter.ToUInt32(a, 4) - BitConverter.ToUInt32(b, 4)), dy = Math.Abs((long)BitConverter.ToUInt32(a, 8) - BitConverter.ToUInt32(b, 8));
                    g.Eax = (uint)Math.Max(dx, dy); break;
                }
                case 0xd157:    // the terrain class under the object EDI
                {
                    var o = ObjectAt(g.Edi); int cell = (int)(BitConverter.ToUInt32(o, 8) >> 5) * battle.Map.Width + (int)(BitConverter.ToUInt32(o, 4) >> 5);
                    g.Eax = cell >= 0 && cell < battle.Map.Cells.Length ? (uint)(battle._properties[(int)(battle.Map.Cells[cell] & 0xffff) * 8] & 7) : 0; break;
                }
                default: throw new InvalidOperationException($"call {address:X}");
            }
        }
        public byte[] Template => throw new NotSupportedException();
        public byte[] Stats => throw new NotSupportedException();
        public uint G512 { get; set; } public uint G51A => 0; public uint G51E => 0; public byte G1C9F { get; set; } public byte G522 => 0; public uint G50E { get; set; }
        public byte[] PartOf(byte owner) => throw new NotSupportedException();
        public uint ArmyTable(byte army) => 0;
        public byte TableByte(uint address) => 0xff;
        public byte Fs13D { get; set; } public byte G525 => 0; public byte G526 => 0; public byte G1C61 { get; set; } public uint G2730 => 0;
        public byte[] Route => throw new NotSupportedException();
        public uint TileDescriptor(uint tile) => 0;
        public uint TileProperty(uint cell, int offset) => 0;
        public uint Fs17F { get; set; }
        public byte[] L3 { get; } = new byte[0x1000];
        public void CallHandler(uint mode, ActionRegs g) { }
        public byte Peek(uint address) => 0;
        public void Poke(uint address, byte value) { }
        public void SetG1CCB(int index, byte value) { }
    }
}
