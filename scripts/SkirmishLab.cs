using System.Buffers.Binary;
namespace Northwind;

/// <summary>The player's laboratory: the list of researchable items of his tree (A716: id, cost, text of the TRE record) and the ported research executor
/// <see cref="OriginalLabResearch"/> (CCCB), which pays from the third store, gold, every 256th update. The AI's laboratories pick an item at random; the player
/// chooses (docs/tech.md).</summary>
public sealed partial class Skirmish
{
    public sealed record ResearchRow(int Offset, byte Id, string Text, uint Cost);
    private Dictionary<int, string>? _techTexts;
    public static bool IsPlayerLab(SkirmishActor actor) => actor.Alive && actor.Team == 0 && actor.Structure && actor.Index02 == 12 && actor.ConstructionRemaining <= 0;

    private string TechText(int offset)
    {
        _techTexts ??= GameData.Read<OriginalTechData>("res://data/original_tech.json").Armies[2].Records.Where(r => r.Text != null).ToDictionary(r => r.Offset, r => r.Text!);
        return _techTexts.GetValueOrDefault(offset, "Исследование");
    }

    /// <summary>The items the player's laboratory may research now (kind 0 records without unmet conditions).</summary>
    public IReadOnlyList<ResearchRow> ResearchRows()
    {
        var tree = TreeOf(Rules.PlayerSlot);
        return tree.Length == 0 ? [] : OriginalTechRules.Researchable(tree)
            .Select(at => new ResearchRow(at, tree[at + 1], TechText(at), BinaryPrimitives.ReadUInt32LittleEndian(tree.AsSpan(at + 7)))).ToList();
    }

    /// <summary>The running research of a laboratory: its item and the cost still to pay (record +28 and +24, flag +15).</summary>
    public (string Text, uint Remaining)? Researching(SkirmishActor lab)
    {
        if (lab.Record.Length < 0x29 || lab.Record[0x15] == 0) return null;
        var tree = TreeOf(Rules.PlayerSlot); int at = tree.Length == 0 ? -1 : OriginalTechRules.Find(tree, 0, lab.Record[0x28]);
        return (at >= 0 ? TechText(at) : "Исследование", BinaryPrimitives.ReadUInt32LittleEndian(lab.Record.AsSpan(0x24)));
    }

    /// <summary>The player picks an item: the laboratory takes it as CCCB would after its random pick (+28 the item, +24 the cost, +15 the flag).</summary>
    public bool StartResearch(SkirmishActor lab, ResearchRow row)
    {
        if (Paused || Outcome != "" || !IsPlayerLab(lab) || lab.Record[0x15] != 0) return false;
        lab.Record[0x28] = row.Id; BinaryPrimitives.WriteUInt32LittleEndian(lab.Record.AsSpan(0x24), row.Cost); lab.Record[0x15] = 1;
        Log($"Исследование начато: {row.Text}.");
        return true;
    }

    private void TickPlayerLabs()
    {
        foreach (var lab in Actors.Where(IsPlayerLab).ToArray())
        {
            if (lab.Record[0x15] == 0) continue;
            var participant = new byte[0xf5]; BinaryPrimitives.WriteUInt32LittleEndian(participant.AsSpan(0x20), Gold);
            string? text = Researching(lab)?.Text;
            OriginalLabResearch.Tick(new SkirmishLabWorld(this, (byte)Rules.PlayerSlot), lab.Record, participant, _aiBlocks[Rules.PlayerSlot] ?? [], _globalTick);
            SetResources(new ResourceQuad(Metal, Wood, BinaryPrimitives.ReadUInt32LittleEndian(participant.AsSpan(0x20)), FreePeople));
            if (lab.Record[0x15] == 0) Log($"Исследование закончено: {text}.");
        }
    }
}
