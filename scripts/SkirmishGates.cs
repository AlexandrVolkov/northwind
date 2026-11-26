using System;
using System.Buffers.Binary;
using System.Linq;
using Godot;
namespace Northwind;

/// <summary>Gates (ARMY__02 #14, docs/gates.md). The state machine is the original one: WRK 0xD85 (ARMY__02.WRK) reads the command state in +14 (2 open, 3 close, 4 automatic)
/// and calls FS:91 (LE:1 D5B1, opening), FS:95 (D61D, closing) or FS:C1 (D689, automatic); the opening runs 32 steps of 3 updates, each raising the gate (+0C, +22) by one pixel;
/// halfway (+22 = 16) the gate must find its cells free in plane 2 (AC63) and moves there (+0D = 2, passable), closing does the same back to plane 1. A unit in the way holds the
/// gate for 30 updates. The automatic mode scans every 32 updates (D895: any unit of the gate's friends near, DAT[72] = 5 cells) and opens or closes accordingly.
/// ADAPTER: the scan D895 is replaced by «an own unit within DAT[72] cells», the damage the blocked gate takes in the original (D5DF) is not applied, the dust effects at completion are not drawn.</summary>
public sealed partial class Skirmish
{
    public const int GateRaiseSteps = 0x20, GateStepUpdates = 3, GateBlockedWait = 0x1e;

    /// <summary>The sprite's lift in pixels (+22: 0 closed … 32 open).</summary>
    public float GateLift(SkirmishActor actor) => actor.Index02 == 14 && actor.Record.Length > 0x23 ? actor.Record[0x22] : 0;

    /// <summary>The player's buttons «Открыть» / «Закрыть» / «Автомат» set the command state of 0xD85 (+14).</summary>
    public void SetGateMode(SkirmishActor gate, int mode)
    {
        if (gate.Index02 != 14 || gate.Team != 0 || !gate.Alive || gate.ConstructionRemaining > 0) return;
        gate.GateMode = mode; gate.Record[0x14] = (byte)(mode == 1 ? 2 : mode == 0 ? 3 : 4); gate.Record[0x15] = 0; gate.Record[0x23] = 0;
        Log(mode == 1 ? "Ворота открываются." : mode == 0 ? "Ворота закрываются." : "Ворота в автоматическом режиме.");
    }

    /// <summary>Finished: the gate stands open (plane 2) or closed (plane 1) and its lift is complete.</summary>
    public bool GateIsOpen(SkirmishActor gate) => gate.Record[0x0d] == 2;

    private bool PlaneFree(SkirmishActor gate, int plane)
    {
        int width = gate.Parameters[3], height = gate.Parameters[4];
        int x0 = (int)(gate.Position.X / 32) - width / 2, y0 = (int)(gate.Position.Y / 32) - height / 2;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int cell = (y0 + y) * Occupancy.Width + x0 + x;
                if (x0 + x < 0 || y0 + y < 0 || x0 + x >= Occupancy.Width || y0 + y >= Occupancy.Height) return false;
                uint id = Occupancy.Cells[plane * Occupancy.Width * Occupancy.Height + cell];
                if (id != 0 && id != gate.Id) return false;
            }
        return true;
    }
    private void MoveGatePlane(SkirmishActor gate, int plane)
    {
        Occupancy.ClearReference(gate.Id); gate.Record[0x0d] = (byte)plane;
        Occupancy.WriteFootprint(gate.Parameters[3], gate.Parameters[4], gate.Record[0x0d], (uint)gate.Position.X, (uint)gate.Position.Y, gate.Id);
    }
    /// <summary>D5B1 (open, true) / D61D (close, false) — one call; returns true when the movement is finished (bl bit 0x40).</summary>
    private bool GateStep(SkirmishActor gate, bool opening)
    {
        var r = gate.Record;
        if (r[0x15] != 0) { r[0x15]--; return false; }
        if (r[0x22] == 0x10)
        {
            if (!PlaneFree(gate, opening ? 2 : 1)) { r[0x15] = GateBlockedWait; return false; }
            MoveGatePlane(gate, opening ? 2 : 1);
        }
        if (opening ? r[0x22] >= GateRaiseSteps : r[0x22] == 0) return true;
        r[0x0c] = (byte)(r[0x0c] + (opening ? 1 : -1)); r[0x22] = (byte)(r[0x22] + (opening ? 1 : -1)); r[0x15] = (byte)(opening ? GateStepUpdates : 1);
        return false;
    }
    private bool GateFriendNear(SkirmishActor gate) =>
        Actors.Any(a => a.Alive && !a.Structure && !a.Scenery && !IsAnimal(a) && (a.Team == gate.Team || !Diplomacy.AtWar(gate.Team, a.Team)) && a.Position.DistanceTo(gate.Position) < gate.Parameters[0x72] * 32);

    private void TickGate(SkirmishActor gate)
    {
        var r = gate.Record;
        // frame: the stage follows the condition downwards (frame 0 whole … 5 nearly destroyed)
        int full = Math.Max(1, (int)gate.Template.InitialCondition);
        int stage = Math.Clamp((full - Math.Min(full, (int)(sbyte)r[0x19])) * 6 / (full + 1), 0, 5);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(1), (ushort)(gate.Template.InitialFrame + stage));
        if (gate.ConstructionRemaining > 0) return;
        if ((r[0x14] & 0x80) != 0) { r[0x14] = 4; r[0x15] = 0; r[0x23] = 0; gate.GateMode = 2; }   // 0xDE5: a finished gate starts automatic
        if ((sbyte)r[0x19] <= 0x19 && r[0x14] != 3 && GateIsOpen(gate)) { r[0x14] = 3; r[0x15] = 0; r[0x23] = 0; }   // 0xDB2: a gate damaged to half closes itself
        switch (r[0x14] & 7)
        {
            case 2: r[0x0f] = 0x18; if (GateStep(gate, true)) { r[0x14] = 0; r[0x15] = 0; r[0x23] = 0; } break;
            case 3: if (r[0x0f] != 0) r[0x0f] = 0x14; if (GateStep(gate, false)) { r[0x14] = 0; r[0x15] = 0; r[0x23] = 0; } break;
            case 4: GateAutomatic(gate); break;
        }
    }
    /// <summary>D689: every 32 updates the scan; then +23 = 1 (opening) or 2 (closing) until the movement is done.</summary>
    private void GateAutomatic(SkirmishActor gate)
    {
        var r = gate.Record; r[0x0f] &= 0xef;
        if (r[0x23] == 0)
        {
            r[0x15]++;
            if ((r[0x15] & 0x1f) != 0) return;
            r[0x23] = (byte)(GateFriendNear(gate) ? 1 : 2); r[0x15] = 0; return;
        }
        bool opening = r[0x23] == 1; r[0x0f] = (byte)(opening ? 8 : 4);
        if (GateStep(gate, opening)) r[0x23] = 0;
    }
}
