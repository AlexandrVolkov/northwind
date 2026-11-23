using System;
using System.Buffers.Binary;
using System.Linq;
using Godot;
namespace Northwind;

/// <summary>Gates (ARMY__02 #14, docs/gates.md): a 2×1 plane-1 building from the factory list. The commands «Открыть», «Закрыть», «Автомат» (DAT slots 2,3,4)
/// set WRK 0xD85's state (+14 = 2, 3, 4). ADAPTER: the state machine services (FS:8D/91/95/C1) are not ported; an open gate frees its cells,
/// a closed one keeps them blocked, the automatic gate opens for own units within two cells and closes when none is left.</summary>
public sealed partial class Skirmish
{
    public const int GateOpenFrameOffset = 5, GateClosedFrameOffset = 3, GateAutoRadius = 64;

    public void SetGateMode(SkirmishActor gate, int mode)
    {
        if (gate.Index02 != 14 || gate.Team != 0 || !gate.Alive) return;
        gate.GateMode = mode; gate.Record[0x14] = (byte)(mode == 1 ? 2 : mode == 0 ? 3 : 4);
        Log(mode == 1 ? "Ворота открыты." : mode == 0 ? "Ворота закрыты." : "Ворота в автоматическом режиме.");
        if (mode != 2) ApplyGate(gate, mode == 1);
    }
    private bool GateBlocked(SkirmishActor gate) => Actors.Any(a => a.Alive && a != gate && !a.Structure && a.Team != 99 && Math.Abs(a.Position.X - gate.Position.X) < 40 && Math.Abs(a.Position.Y - gate.Position.Y) < 28);
    private void ApplyGate(SkirmishActor gate, bool open)
    {
        if (gate.GateIsOpen == open) return;
        if (!open && GateBlocked(gate)) return;   // a unit in the gateway keeps it open
        gate.GateIsOpen = open;
        if (open) Occupancy.ClearReference(gate.Id);
        else Occupancy.WriteFootprint(gate.Parameters[3], gate.Parameters[4], gate.Record[0x0d], (uint)gate.Position.X, (uint)gate.Position.Y, gate.Id);
    }
    private void TickGate(SkirmishActor gate)
    {
        ushort baseFrame = (ushort)(gate.Template.InitialFrame);
        if (gate.ConstructionRemaining > 0)
        {
            int stage = Math.Min(GateClosedFrameOffset, (int)gate.Record[0x19] * (GateClosedFrameOffset + 1) / (gate.Template.InitialCondition + 1));
            BinaryPrimitives.WriteUInt16LittleEndian(gate.Record.AsSpan(1), (ushort)(baseFrame + stage)); return;
        }
        if (gate.Team == 0 && gate.GateMode == 2 && (_globalTick & 7) == 0)
            ApplyGate(gate, Actors.Any(a => a.Alive && a.Team == 0 && !a.Structure && a.Position.DistanceTo(gate.Position) < GateAutoRadius));
        else if (gate.Team != 0 && (_globalTick & 7) == 0)
            ApplyGate(gate, Actors.Any(a => a.Alive && a.Team == gate.Team && !a.Structure && a.Position.DistanceTo(gate.Position) < GateAutoRadius));
        BinaryPrimitives.WriteUInt16LittleEndian(gate.Record.AsSpan(1), (ushort)(baseFrame + (gate.GateIsOpen ? GateOpenFrameOffset : GateClosedFrameOffset)));
    }
}
