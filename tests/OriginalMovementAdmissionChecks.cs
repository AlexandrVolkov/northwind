using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Northwind;

public partial class TestRunner
{
    private static MovementAdmission AdmissionCase(int fw, int fh, int x, int y, byte layer, byte direction, int[] terrain,
        byte[] values, int[] pattern, bool ground, int[] obstacle, uint mask = 0, byte field = 9, int[]? support = null)
    {
        var raw = new byte[54]; raw[0] = 2; raw[3] = 2; raw[0x0d] = layer; raw[0x19] = 5;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4), (uint)(x * 32 + 16)); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8), (uint)(y * 32 + 16));
        var own = new byte[171]; own[3] = (byte)fw; own[4] = (byte)fh; own[0x3f] = field; values.CopyTo(own, 0x58);
        var objects = new Dictionary<uint, MovementObject>();
        void Other(uint reference, int index, int[] p)
        {
            var r = new byte[54]; r[0] = 2; r[3] = (byte)p[0]; r[0x0e] = (byte)index; r[0x19] = (byte)p[3]; r[0x2d] = (byte)p[5]; r[0x24] = (byte)p[6]; r[0x25] = (byte)p[7]; r[0x14] = (byte)p[8];
            var t = new byte[171]; t[0x3f] = (byte)p[2]; t[0x46] = (byte)p[4]; BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(0x40), (uint)p[1]);
            objects[reference] = new(r, t);
        }
        Other(100, 1, obstacle); Other(200, 2, [1,0,9,70,10,0x22,4,4,0]);
        Other(300, 3, support == null ? [0,10,60,20,15,0,0,0,0] : [0,support[0],support[1],support[2],support[3],0,0,0,0]);
        var cells = Enumerable.Range(0, 64).Select(i => (uint)(i % terrain.Length)).ToArray(); var properties = new byte[terrain.Length * 8];
        for (int i = 0; i < terrain.Length; i++) properties[i * 8] = (byte)terrain[i];
        var grid = new uint[192]; if (ground) Array.Fill(grid, 300u, 0, 64);
        for (int i = 0; i < 64; i++) grid[(layer & 3) * 64 + i] = (uint)pattern[i % pattern.Length];
        for (int yy = y - fh / 2; yy < y - fh / 2 + fh; yy++) for (int xx = x - fw / 2; xx < x - fw / 2 + fw; xx++) grid[(layer & 3) * 64 + yy * 8 + xx] = 1;
        return OriginalMovementAdmission.ProbeStep(8, 8, cells, properties, grid, raw, own, direction, mask, reference => objects[reference], 1);
    }
    private void OriginalMovementAdmissionChecks()
    {
        using var doc = GameData.Read<JsonDocument>("res://tests/fixtures/movement-admission.json"); var fixture = doc.RootElement;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); int count = 0;
        void Append(MovementAdmission result) { hash.AppendData(new byte[] { result.Code, result.HardObject ? (byte)1 : (byte)0 }); count++; }
        void Verify(string name)
        {
            var expected = fixture.GetProperty("results").GetProperty(name);
            Check(count == expected.GetProperty("case_count").GetInt32(), $"Movement admission {name} coverage");
            Check(Convert.ToHexStringLower(hash.GetHashAndReset()) == expected.GetProperty("sha256").GetString(), $"Movement admission {name} matches original x86"); count = 0;
        }
        Check(fixture.GetProperty("format_version").GetInt32() == 1, "Movement admission reference schema");
        int[] ordinary = [0,0,9,70,10,0x22,4,0,0];
        for (int terrain = 0; terrain < 256; terrain++) for (int value = 0; value < 256; value++)
        {
            var values = Enumerable.Repeat((byte)8, 16).ToArray(); values[terrain & 15] = (byte)value;
            Append(AdmissionCase(1,1,3,3,1,2,[terrain],values,[0],false,ordinary));
        }
        Verify("terrain"); var spec = fixture.GetProperty("support");
        foreach (int terrain in Numbers(spec.GetProperty("terrain"))) foreach (int value in Numbers(spec.GetProperty("values")))
        foreach (int layer in Numbers(spec.GetProperty("layers"))) foreach (int flags in Numbers(spec.GetProperty("flags")))
        foreach (int condition in Numbers(spec.GetProperty("conditions"))) foreach (int field in Numbers(spec.GetProperty("fields"))) foreach (int threshold in Numbers(spec.GetProperty("thresholds")))
        {
            var values = Enumerable.Repeat((byte)8, 16).ToArray(); values[terrain & 15] = (byte)value;
            Append(AdmissionCase(1,1,3,3,(byte)layer,2,[terrain],values,[0],true,ordinary, support: [flags,field,condition,threshold]));
        }
        Verify("support"); spec = fixture.GetProperty("occupants");
        foreach (int owner in Numbers(spec.GetProperty("owners"))) foreach (uint mask in spec.GetProperty("masks").EnumerateArray().Select(v => v.GetUInt32()))
        foreach (int field in Numbers(spec.GetProperty("mover_fields"))) foreach (int other in Numbers(spec.GetProperty("other_fields")))
        foreach (int flags in Numbers(spec.GetProperty("flags"))) foreach (int work in Numbers(spec.GetProperty("work"))) foreach (int state in Numbers(spec.GetProperty("states")))
        foreach (int phase in Numbers(spec.GetProperty("phases"))) foreach (int heading in Numbers(spec.GetProperty("headings"))) foreach (int direction in Numbers(spec.GetProperty("directions")))
            Append(AdmissionCase(1,1,3,3,1,(byte)direction,[0],Enumerable.Repeat((byte)8,16).ToArray(),[100],false,[owner,flags,other,70,10,state,phase,heading,work],mask,(byte)field));
        Verify("occupants"); spec = fixture.GetProperty("footprints");
        foreach (var size in spec.GetProperty("sizes").EnumerateArray())
        {
            var dimensions = Numbers(size); int fw = dimensions[0], fh = dimensions[1];
            for (int y = fh / 2; y <= 8 - fh + fh / 2; y++) for (int x = fw / 2; x <= 8 - fw + fw / 2; x++) for (byte direction = 0; direction < 8; direction++)
            foreach (var pattern in spec.GetProperty("patterns").EnumerateArray()) foreach (var terrain in spec.GetProperty("terrain").EnumerateArray())
            foreach (int layer in Numbers(spec.GetProperty("layers"))) foreach (bool ground in new[] { false, true })
                Append(AdmissionCase(fw,fh,x,y,(byte)layer,direction,Numbers(terrain),Enumerable.Repeat((byte)8,16).ToArray(),Numbers(pattern),ground,[0,0,9,70,10,0x22,4,0,0]));
        }
        Verify("footprints");
    }
    private void MovementBridgeChecks()
    {
        using var battle = CreateFundedSkirmish();
        var first = Recruit(battle, true)!; var second = Recruit(battle, true)!;
        var props = Convert.FromHexString(battle.Map.SegmentPropertiesHex);
        int water = Enumerable.Range(0, props.Length / 8).First(i => props[i * 8] == 2 && battle.Map.Terrain[i] == "water");
        int ground = Enumerable.Range(0, props.Length / 8).First(i => props[i * 8] == 0 && battle.Map.Terrain[i] == "ground");
        Array.Fill(battle.Map.Cells, (uint)water); battle.Occupancy.LoadCells(new uint[battle.Occupancy.Cells.Length]);
        for (int y = 90; y < 95; y++) for (int x = 40; x < 50; x++) if (x < 44 || x > 45) battle.Map.Cells[y * battle.Map.Width + x] = (uint)ground;
        // 90E9 drowns idle units on unsupported water; keep ground under the rest of the army so the mission continues.
        foreach (var other in battle.Actors.Where(a => !a.Structure && a != first && a != second)) battle.Map.Cells[other.Cell.Y * battle.Map.Width + other.Cell.X] = (uint)ground;
        void Place(SkirmishActor actor, int x, int y)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(actor.Record.AsSpan(4), (uint)(x * 32 + 16)); BinaryPrimitives.WriteUInt32LittleEndian(actor.Record.AsSpan(8), (uint)(y * 32 + 16));
            battle.Occupancy.WriteFootprint(actor.Parameters[3],actor.Parameters[4],1,(uint)actor.Position.X,(uint)actor.Position.Y,actor.Id);
        }
        Place(first,41,91); Place(second,41,93);
        var bridgeCell = new Vector2I(44,92);
        Check(!battle.CanStand(first,bridgeCell), "Water blocks the worker without a ground-plane support object");
        var template = GameData.Read<TemplatesData>("res://assets/imported/templates.json").Armies["ARMY__02.DAT"].Templates[7];
        var raw = new byte[template.RuntimeRecordBytes]; raw[0] = 2; raw[0x0e] = 7; raw[0x19] = template.InitialCondition;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(4),45*32); BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(8),92*32);
        var bridge = new SkirmishActor { Source = new ObjectRecord { RuntimeReference = 0x200000, TemplateName = "Мост", TemplateFile = "ARMY__02.DAT", TemplateIndex = 7 },
            Template = template, Record = raw, Parameters = Convert.FromHexString(template.RawRecordHex), Rules = new() { Health = 100 }, Health = 100 };
        battle.Actors.Add(bridge); battle.Occupancy.WriteFootprint(2,2,0,45*32,92*32,bridge.Id);
        Check(battle.CanStand(first,bridgeCell), "Real bridge DAT permits movement over a tile still classified as water by the palette");
        bridge.Record[0x19] = bridge.Parameters[0x46]; Check(!battle.CanStand(first,bridgeCell), "Support must exceed its original signed condition threshold"); bridge.Record[0x19] = template.InitialCondition;
        byte supportField = bridge.Parameters[0x3f]; bridge.Parameters[0x3f] = 8; Check(!battle.CanStand(first,bridgeCell), "Support DAT[3F] must reach the visitor's field value"); bridge.Parameters[0x3f] = supportField;
        first.Record[0x0d] = 2; Check(!battle.CanStand(first,bridgeCell), "Ground-plane support fallback applies only to movement layer one"); first.Record[0x0d] = 1;
        first.Parameters[3] = first.Parameters[4] = 2;
        Check(!battle.CanStand(first,new(40,92)), "A larger unit cannot stand with part of its footprint over unsupported water"); first.Parameters[3] = first.Parameters[4] = 1;
        battle.Occupancy.WriteFootprint(1,1,1,(uint)first.Position.X,(uint)first.Position.Y,0);
        first.Parameters[3] = first.Parameters[4] = 2; Place(first,41,92);
        var ids = new[] { first.Id, second.Id }; Check(battle.Move(ids,new(48,92)) == 2 && first.Goal != second.Goal, "Group order assigns two destinations across the bridge");
        var goals = new[] { first.Goal!.Value, second.Goal!.Value };
        Check(!new Rect2I(goals[0] - Vector2I.One,new(2,2)).HasPoint(goals[1]), "Group destinations preserve both footprints, not just different centers");
        battle.Advance(1.0 / battle.Rules.TicksPerSecond);
        var reservedCorner = first.StepGoal + Vector2I.Up;
        Check(first.Stepping && battle.Occupancy.Cells[battle.Map.Width * battle.Map.Height + reservedCorner.Y * battle.Map.Width + reservedCorner.X] != first.Id &&
            !battle.CanStand(second,reservedCorner), "Next-step reservation protects the large unit's entire footprint before occupancy transfer");
        for (int i = 0; i < 1800 && (first.Cell != goals[0] || second.Cell != goals[1] || first.Stepping || second.Stepping); i++) battle.Advance(1.0 / battle.Rules.TicksPerSecond);
        Check(first.Cell == goals[0] && second.Cell == goals[1] && !first.Stepping && !second.Stepping,
            $"Two workers cross the bridge through normal group orders: {first.Position}/{second.Position}");
        var occupancy = battle.Occupancy.Cells.ToArray();
        Check(occupancy.Count(v => v == bridge.Id) == 4 && occupancy.Count(v => v == first.Id) == 4 && occupancy.Count(v => v == second.Id) == 1,
            "Bridge crossing preserves support occupancy and transfers both worker footprints without stale cells");
    }
}
