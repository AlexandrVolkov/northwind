using Godot;
namespace Northwind;

/// <summary>The services of the walk step (<see cref="OriginalUnitWalk"/>, LE:1 70B0) over the skirmish's occupancy plane for ONE unit: the route is the list of cells of the skirmish (an adapter for 7FA7: the original reads
/// packed nibbles from the route pool), the turn is 9653, the occupancy transfer at the middle of a step is 9B0D (as <see cref="OriginalSpatialRules.AdvanceMotion"/> does it); the effects, the fog reveal and the enemy search are not driven here.</summary>
public sealed class SkirmishWalkWorld(OriginalOccupancyMap grid, uint[] mapCells, byte[] properties) : IWalkWorld
{
    private static readonly (int X, int Y)[] Directions = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];
    public byte[] Record = []; public byte[] Template = []; public uint Reference; public Queue<Vector2I> Route = new();
    /// <summary>True when the route step was refused (a blocked cell): the step ends with BL = 80h.</summary>
    public Func<Vector2I, bool>? CanEnter;
    public byte MapWidth => grid.Width;

    private static uint U32(byte[] b, int o) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static void Put(byte[] b, int o, uint v) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);

    public (byte Heading, byte Code) NextStep()
    {
        if (Route.Count == 0) return (0, 0);
        var next = Route.Peek(); int cx = (int)(U32(Record, 4) >> 5), cy = (int)(U32(Record, 8) >> 5);
        if (CanEnter != null && !CanEnter(next)) return (0, 1);
        if (Math.Max(Math.Abs(next.X - cx), Math.Abs(next.Y - cy)) != 1) return (0, 1);
        Route.Dequeue();
        return (OriginalMotionPreparation.DirectionToward((uint)cx, (uint)cy, (uint)next.X, (uint)next.Y), 3);
    }
    public bool Turn(byte target) => OriginalMotionPreparation.TurnToward(Record, target, Template);
    public void Occupancy(uint step)
    {
        // 9B0D: the footprint moves from the old to the new rectangle when the step crosses the middle (called before the coordinates change).
        byte remaining = Record[0x2c];
        var direction = Directions[(Record[0x25] >> 3) & 7]; uint x = U32(Record, 4), y = U32(Record, 8);
        uint oldX = unchecked(x + (uint)(-direction.X * (int)(32 - remaining))), oldY = unchecked(y + (uint)(-direction.Y * (int)(32 - remaining)));
        uint newX = unchecked(x + (uint)(direction.X * (int)remaining)), newY = unchecked(y + (uint)(direction.Y * (int)remaining));
        grid.ValidateFootprint(Template[3], Template[4], Record[0x0d], oldX, oldY); grid.ValidateFootprint(Template[3], Template[4], Record[0x0d], newX, newY);
        grid.WriteFootprint(Template[3], Template[4], Record[0x0d], oldX, oldY, 0); grid.WriteFootprint(Template[3], Template[4], Record[0x0d], newX, newY, Reference);
    }
    public void Effect(byte army, byte type, uint x, uint y) { }
    public void RevealFog() { }
    public void ReleaseRoute() { }
    public void SearchEnemy(byte mode) { }
    public uint OccupancyWord(uint cell) => cell < grid.Cells.Length ? grid.Cells[(int)cell] : 0;
    public byte TerrainType(uint cell) { int offset = (int)(mapCells[(int)cell] & 0xffff) * 8; return offset < properties.Length ? properties[offset] : (byte)0; }
    public sbyte Delta(int index) { var d = Directions[(index >> 1) & 7]; return (sbyte)((index & 1) == 0 ? d.X : d.Y); }
}
