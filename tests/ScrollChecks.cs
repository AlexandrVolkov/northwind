using System.Security.Cryptography;
using System.Text.Json;
using Northwind;

public partial class TestRunner
{
    private void OriginalScrollChecks()
    {
        using var reference = GameData.Read<JsonDocument>("res://tests/fixtures/scroll.json");
        var root = reference.RootElement;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0, moved = 0;
        foreach (var c in root.GetProperty("cases").EnumerateArray())
        {
            int key = c.GetProperty("key").GetInt32();
            var input = new ScrollInput(c.GetProperty("x").GetInt32(), c.GetProperty("y").GetInt32(), 640, 480,
                key == 0xe04b, key == 0xe04d, key == 0xe048, key == 0xe050);
            var step = OriginalScrollRules.Step(input, (byte)c.GetProperty("timer").GetInt32(), (byte)c.GetProperty("last").GetInt32(), (byte)c.GetProperty("pan").GetInt32());
            if (step.Dx != 0 || step.Dy != 0) moved++;
            digest.AppendData(System.Text.Encoding.UTF8.GetBytes($"[{step.CursorFirst}, {step.CursorLast}, {step.Dx}, {step.Dy}, {step.LastTimer}]"));
            count++;
        }
        var result = root.GetProperty("results").GetProperty("scroll");
        Check(count == result.GetProperty("case_count").GetInt32() && moved > 200, $"Scroll reference coverage ({count}, moved {moved})");
        Check(Convert.ToHexStringLower(digest.GetHashAndReset()) == result.GetProperty("sha256").GetString(), "Map scroll step 3FA1 matches original x86");
        Check(OriginalOrderCursor.ObjectClass(0x1800, 0, 0, 0xff, false) == 1 && OriginalOrderCursor.ObjectClass(0x1800, 1, 0, 0xff, false) == 2 &&
              OriginalOrderCursor.ObjectClass(0x1800, 1, 0, 0xff, true) == 3 && OriginalOrderCursor.ObjectClass(0x20020, 1, 0, 0xff, true) == 1,
              "Object class [0D]: own 1, other owner 2, at war 3, 20020h objects stay neutral (4607..4652)");
        Check(OriginalOrderCursor.For(1, null) == OriginalOrderCursor.Move && OriginalOrderCursor.For(2, null) == OriginalOrderCursor.Target &&
              OriginalOrderCursor.For(2, 1) == OriginalOrderCursor.TargetObject && OriginalOrderCursor.For(2, 3) == OriginalOrderCursor.TargetEnemy,
              "Pending order cursors: cross, grey skull, gold skull over an object, red skull over an enemy at war (4704..4818)");
        Check(OriginalScrollRules.ChangePan(16, 1) == 16 && OriginalScrollRules.ChangePan(1, -1) == 1 && OriginalScrollRules.ChangePan(13, 1) == 14, "«Панорама» stays within 1..16 (1132F)");
    }
}
