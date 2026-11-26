using Godot;
using Northwind;

public partial class TestRunner
{
    /// <summary>Shadow comparison of the skirmish walk adapter (MoveTick: TurnAndAdvance / AdvanceMotion / Stride / WriteFrame) with the ported walk step 70B0 (<see cref="OriginalUnitWalk"/>) over <see cref="SkirmishWalkWorld"/>.</summary>
    private void WalkShadowChecks()
    {
        var route = new Vector2I[] { new(5, 5), new(6, 5), new(7, 6), new(7, 7), new(6, 8), new(5, 8), new(4, 7), new(4, 6) };
        var template = MotionTemplate(100, 4, 0x10); template[0x57] = 2; template[0xa2] = 0xff;
        var props = new byte[16]; var cells = Enumerable.Repeat(1u, 256).ToArray();
        byte[] NewRecord() { var r = SpatialRecord(0x40, 0, 0, 5 * 32 + 16, 5 * 32 + 16); r[0x25] = 0; r[0x24] = 0; r[0x26] = 0; r[0x27] = 0; r[0x2c] = 0; r[0x2d] = 0x22; BitConverter.GetBytes(0xffffffffu).CopyTo(r, 0x28); return r; }
        var gridA = new OriginalOccupancyMap(16, 16); var gridB = new OriginalOccupancyMap(16, 16);
        gridA.WriteFootprint(1, 1, 0, 5 * 32 + 16, 5 * 32 + 16, 77); gridB.WriteFootprint(1, 1, 0, 5 * 32 + 16, 5 * 32 + 16, 77);
        var a = NewRecord(); var b = NewRecord();
        var world = new SkirmishWalkWorld(gridB, cells, props) { Record = b, Template = template, Reference = 77 };
        var queue = new Queue<Vector2I>(route.Skip(1)); var bQueue = new Queue<Vector2I>(route.Skip(1));
        world.RouteCount = () => bQueue.Count; world.RoutePeek = () => bQueue.Peek(); world.RouteDequeue = () => bQueue.Dequeue();
        var walk = new OriginalUnitWalk(world);
        bool stepping = false, turning = false; int firstDiff = -1; string detail = "";
        for (int tick = 0; tick < 200 && firstDiff < 0; tick++)
        {
            // A: the adapter of MoveTick
            if (!stepping && queue.Count > 0)
            {
                var cur = new Vector2I((int)(BitConverter.ToUInt32(a, 4) >> 5), (int)(BitConverter.ToUInt32(a, 8) >> 5)); var next = queue.Dequeue();
                byte heading = OriginalMotionPreparation.DirectionToward((uint)cur.X, (uint)cur.Y, (uint)next.X, (uint)next.Y);
                a[0x25] = (byte)((a[0x25] & 7) | (heading << 3)); a[0x24] = 3; a[0x2c] = 32; stepping = turning = true;
            }
            if (stepping)
            {
                if (turning) { var prepared = OriginalMotionPreparation.TurnAndAdvance(gridA, cells, props, a, template, 77, 0); turning = !prepared.EnteredMotion; if (prepared.EnteredMotion) OriginalUnitWalk.Stride(a, template, null); }
                else { OriginalSpatialRules.AdvanceMotion(gridA, a, 77, template[3], template[4], 0); OriginalUnitWalk.Stride(a, template, null); }
                OriginalUnitWalk.WriteFrame(a, template);
                if (a[0x2c] == 0) { a[0x24] = (byte)((a[0x24] & 0xf8) | 1); stepping = false; if (queue.Count == 0) { a[0x27] = 0; OriginalUnitWalk.WriteFrame(a, template); } }
            }
            // B: the ported step
            if (b[0x24] != 0 || bQueue.Count > 0 || tick == 0) walk.Step(b, template, 2, 2, 0);
            bool lagTick = queue.Count == 0 && !stepping && a[0x27] == 0 && b[0x27] != 0 && b[0x24] != 0;   // the original ends the walk one update after the last step (7FA7 answers «none»)
            if (lagTick) { walk.Step(b, template, 2, 2, 0); }
            if (!a.SequenceEqual(b) || !gridA.Cells.ToArray().SequenceEqual(gridB.Cells.ToArray()))
            {
                firstDiff = tick; var diffs = Enumerable.Range(0, a.Length).Where(k => a[k] != b[k]).Select(k => $"+{k:X2}:{a[k]:X2}/{b[k]:X2}"); detail = string.Join(" ", diffs);
            }
        }
        Check(firstDiff < 0, $"The ported walk step 70B0 equals the skirmish adapter tick by tick (first difference at tick {firstDiff}: {detail})");
    }
}
