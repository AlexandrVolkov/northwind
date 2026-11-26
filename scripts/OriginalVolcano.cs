using System.Buffers.Binary;
namespace Northwind;

/// <summary>The services of the volcano script: the random streams FS:109 (9E41) and FS:10D (9E55) and the effect FS:19 (9727).</summary>
public interface IVolcanoWorld
{
    uint RandomA();
    uint RandomB();
    void Effect(byte army, byte type, byte layer, uint x, uint y);
}

/// <summary>ARMY__04 «Вулкан», WRK 0..9F: AH = 0 draws the pause (random &amp; 0Fh) · 400h updates, AH = 1 counts it down; then it erupts (the counter at +22, the phase at +15):
/// while the second random draw has no bit of 1Fh it throws the effects 1 (every 20h updates of the counter), 0 and four of 2. Verified against the original x86 with the services
/// recorded (tools/verify_volcano.py, tests/fixtures/volcano.json).</summary>
public static class OriginalVolcano
{
    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static void Put(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);

    public static void Script(IVolcanoWorld world, byte ah, byte[] r)
    {
        if (ah == 0) Initialize(world, r);
        else if (ah == 1) Step(world, r);
    }

    private static void Initialize(IVolcanoWorld world, byte[] r) { Put(r, 0x22, (world.RandomA() & 0xf) << 10); r[0x15] = 0; }

    private static void Step(IVolcanoWorld world, byte[] r)
    {
        if (r[0x15] == 0)
        {
            uint left = U32(r, 0x22) - 1; Put(r, 0x22, left); if (left != 0) return;
            Put(r, 0x22, (world.RandomA() & 0xf) << 10); r[0x15] = 1;
        }
        uint count = U32(r, 0x22) - 1; Put(r, 0x22, count);
        if (count == 0) { Initialize(world, r); return; }
        if ((world.RandomB() & 0x1f) != 0) return;
        uint x = U32(r, 4), y = U32(r, 8);
        if ((U32(r, 0x22) & 0x1f) == 0) world.Effect(r[0], 1, 0, x + 1, y + 0x1b);
        world.Effect(r[0], 0, 1, x + 3, y);
        for (int k = 0; k < 4; k++) world.Effect(r[0], 2, 1, x, y);
    }
}
