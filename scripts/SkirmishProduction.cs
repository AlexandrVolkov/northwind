using System.Buffers.Binary;
using Godot;
namespace Northwind;

public sealed class ProductionData
{
    public Dictionary<int, byte> RecruitmentIntervals { get; set; } = [];
    public Dictionary<string, ProductionTable> Tables { get; set; } = [];
    /// <summary>Interval of a template in the producer's WRK table (castle or barracks), else the supported recruits.</summary>
    public byte Interval(string producer, byte template)
    {
        string key = producer == "Казарма" ? "barracks" : "castle";
        if (Tables.TryGetValue(key, out var table) && table.Entries.FirstOrDefault(e => e.Template == template) is { } entry) return entry.Interval;
        return RecruitmentIntervals[template];
    }
}
public sealed class ProductionTable { public List<ProductionEntry> Entries { get; set; } = []; }
public sealed class ProductionEntry { public byte Template { get; set; } public byte Interval { get; set; } }

public sealed partial class Skirmish
{
    private ProductionData _production = null!;
    public SkirmishActor? PlayerBarracks => Actors.FirstOrDefault(a => a.Alive && a.Team == 0 && a.Source.TemplateName == "Казарма");
    public static bool IsBarracks(SkirmishActor actor) => actor.Alive && actor.Team == 0 && actor.Source.TemplateName == "Казарма";
    public bool BarracksReady => PlayerBarracks is { ConstructionRemaining: <= 0 };
    public int RecruitmentCount => CountQueue(PlayerCastle);
    public int BarracksRecruitmentCount => PlayerBarracks is { } b ? CountQueue(b) : 0;
    public int BarracksRecruitmentCountFor(SkirmishActor barracks) => IsBarracks(barracks) ? CountQueue(barracks) : 0;
    public bool RecruitmentPaid => RecruitmentCount > 0 && PlayerCastle.Record[0x15] != 0;
    public bool RecruitmentBlocked { get; private set; }
    public uint LastRecruitId { get; private set; }
    public byte ProductionSpeedShift => 0;
    public TemplateEntry? RecruitmentTemplate => QueuedTemplate(PlayerCastle);
    public string RecruitmentText => ProductionText(PlayerCastle);
    public string BarracksRecruitmentText => PlayerBarracks is { } b ? ProductionText(b) : "Постройте казарму";
    public string BarracksRecruitmentTextFor(SkirmishActor barracks) => IsBarracks(barracks) ? ProductionText(barracks) : "Постройте казарму";
    private readonly HashSet<uint> _blockedBarracks = [];
    public bool BarracksRecruitmentBlocked(SkirmishActor barracks) => _blockedBarracks.Contains(barracks.Id);
    private static int CountQueue(SkirmishActor producer)
    { int count = 0; foreach (byte slot in producer.Record.AsSpan(0x22, 5)) if (slot != 0xff) count++; return count; }
    private TemplateEntry? QueuedTemplate(SkirmishActor producer) => CountQueue(producer) == 0 ? null : _templates.Armies[producer.Source.TemplateFile].Templates[producer.Record[0x22]];
    private string ProductionText(SkirmishActor producer)
    {
        if (QueuedTemplate(producer) is not { } template) return "Очередь найма пуста";
        string names = string.Join(" → ", producer.Record.AsSpan(0x22, 5).ToArray().Where(t => t != 0xff)
            .Select(t => _templates.Armies["ARMY__02.DAT"].Templates[t].Name));
        string state = producer.Record[0x15] == 0 ? (CanAfford(template) ? "Ожидает начала подготовки" : MissingResources(template))
            : (producer == PlayerCastle ? RecruitmentBlocked : BarracksRecruitmentBlocked(producer)) ? "Готов. Освободите выход здания."
            : $"Подготовка: {producer.Record[0x27]}/{template.InitialCondition}";
        return $"Найм {CountQueue(producer)}/5: {names}\n{state}";
    }
    public ProductionExit RecruitExit(TemplateEntry template) => RecruitExit(PlayerCastle, template);
    public ProductionExit RecruitExit(SkirmishActor producer, TemplateEntry template) => OriginalProductionRules.Exit(producer.Record, producer.Parameters, Convert.FromHexString(template.RawRecordHex));
    private void InitializeProduction() => _production = GameData.Read<ProductionData>("res://data/original_production.json");
    public bool QueueWorker() => QueueRecruit(PlayerCastle, 10);
    public bool QueueMilitia() => QueueRecruit(PlayerCastle, 0); // legacy laboratory API; gameplay uses barracks
    public bool QueueBarracksMilitia() => PlayerBarracks is { ConstructionRemaining: <= 0 } b && QueueRecruit(b, 0);
    public bool QueueBarracksMilitia(SkirmishActor barracks) => IsBarracks(barracks) && QueueRecruit(barracks, 0);
    private bool QueueRecruit(SkirmishActor producer, byte template)
    {
        if (Paused || Outcome != "" || !producer.Alive || producer.ConstructionRemaining > 0) return false;
        if (!OriginalProductionRules.Enqueue(producer.Record, template)) { Log("Очередь здания заполнена: пять заказов."); return false; }
        Log(_templates.Armies["ARMY__02.DAT"].Templates[template].Name + " добавлен в очередь. Оплата — при начале подготовки."); return true;
    }
    public bool CancelRecruitment() => CancelRecruitment(PlayerCastle);
    public bool CancelBarracksRecruitment() => PlayerBarracks is { } b && CancelRecruitment(b);
    public bool CancelBarracksRecruitment(SkirmishActor barracks) => IsBarracks(barracks) && CancelRecruitment(barracks);
    private bool CancelRecruitment(SkirmishActor producer)
    {
        if (Paused || Outcome != "" || !producer.Alive || QueuedTemplate(producer) is not { } template) return false;
        if (producer.Record[0x15] != 0)
            SetResources(OriginalProductionRules.Refund(Resources, Price(template), producer.Record[0x27], template.InitialCondition));
        producer.Record.AsSpan(0x22, 5).Fill(0xff); producer.Record[0x15] = producer.Record[0x27] = 0;
        producer.Record[0x0f] = 0x24; producer.Record[0x14] &= 0xf8;
        SetBlocked(producer, false); Log("Очередь найма очищена. Люди текущего заказа возвращены; материалы — по исходной формуле прогресса."); return true;
    }
    private void SetBlocked(SkirmishActor producer, bool blocked)
    { if (producer.Team != 0) return; if (producer == PlayerCastle) RecruitmentBlocked = blocked; else if (blocked) _blockedBarracks.Add(producer.Id); else _blockedBarracks.Remove(producer.Id); }
    private void TickProduction()
    {
        TickProduction(PlayerCastle);
        foreach (var barracks in Actors.Where(IsBarracks).ToArray()) TickProduction(barracks);
        foreach (var producer in Actors.Where(a => a.Alive && a.Team != 0 && IsEnemyProducer(a)).ToArray()) TickProduction(producer);
    }
    private ResourceQuad Wallet(int team) => team == 0 ? Resources : AiResources(SlotOfTeam(team));
    private void SetWallet(int team, ResourceQuad value) { if (team == 0) SetResources(value); else SetAiResources(SlotOfTeam(team), value); }
    private void TickProduction(SkirmishActor producer)
    {
        if (!producer.Alive || producer.ConstructionRemaining > 0 || QueuedTemplate(producer) is not { } template) return;
        var record = producer.Record;
        string file = producer.Source.TemplateFile;
        var unitRules = file == "ARMY__02.DAT" ? (Rules.Units.TryGetValue(template.Name, out var known) ? known : null) : SpecOf(file, template, Convert.FromHexString(template.RawRecordHex));
        if (producer.Team != 0 && !(file == "ARMY__02.DAT" ? template.TemplateIndex == 10 : unitRules is { Worker: true }) && (unitRules == null || unitRules.Damage == 0
            || (file == "ARMY__02.DAT" && !_soldierSeeds.ContainsKey(template.TemplateIndex) && unitRules.Range > 48 && template.TemplateIndex is not (16 or 26))))
        { OriginalProductionRules.Complete(record); return; }   // no rules, or a ranged unit without a seed record: dropped
        if (record[0x15] == 0)
        {
            var payment = OriginalConstructionRules.TryDebitUpfront(Wallet(producer.Team), Price(template));
            if (!payment.Accepted) return;
            SetWallet(producer.Team, payment.Balances); OriginalProductionRules.StartPaid(record); SetBlocked(producer, false); return;
        }
        byte interval = producer.Team == 0 ? _production.Interval(producer.Source.TemplateName, record[0x22]) : EnemyInterval(producer, record[0x22]);
        if (!OriginalProductionRules.Tick(record, interval, ProductionSpeedShift, template.InitialCondition)) return;
        if (!ReleaseRecruit(producer, template)) { SetBlocked(producer, true); if (producer.Team != 0) EnemyClearExit(producer, template); return; }
        OriginalProductionRules.Complete(record); SetBlocked(producer, false);
    }
    private Vector2I? EnemyExitCell(SkirmishActor recruit, Vector2I exit, byte layer)
    {
        for (int radius = 1; radius <= 3; radius++)
            for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    var cell = exit + new Vector2I(x, y);
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) == radius && Inside(cell) && CanStand(recruit, cell) && !_reservations.ContainsKey((cell, layer))) return cell;
                }
        return null;
    }
    private bool ReleaseRecruit(SkirmishActor producer, TemplateEntry template)
    {
        var exit = RecruitExit(producer, template); var cell = new Vector2I((int)(exit.X >> 5), (int)(exit.Y >> 5));
        if (!Inside(cell)) return false;
        string file = producer.Source.TemplateFile;
        bool worker = file == "ARMY__02.DAT" ? template.TemplateIndex == 10 : SpecOf(file, template, Convert.FromHexString(template.RawRecordHex)).Worker;
        var recruit = NewEconomyActor(template.TemplateIndex, cell, worker, !worker, producer.Team, file);
        recruit.Record[0x0d] = exit.Layer;
        BinaryPrimitives.WriteUInt32LittleEndian(recruit.Record.AsSpan(4), exit.X);
        BinaryPrimitives.WriteUInt32LittleEndian(recruit.Record.AsSpan(8), exit.Y);
        if (!CanStand(recruit, cell) || _reservations.ContainsKey((cell, exit.Layer)))
        {
            // The enemy has nobody to clear its exit: its recruit takes the nearest free cell within 3 of it (adapter).
            if (producer.Team == 0 || EnemyExitCell(recruit, cell, exit.Layer) is not { } free) return false;
            cell = free; exit = exit with { X = (uint)(free.X * 32 + 16), Y = (uint)(free.Y * 32 + 16) };
            BinaryPrimitives.WriteUInt32LittleEndian(recruit.Record.AsSpan(4), exit.X);
            BinaryPrimitives.WriteUInt32LittleEndian(recruit.Record.AsSpan(8), exit.Y);
        }
        recruit.Source.RawX = exit.X; recruit.Source.RawY = exit.Y; recruit.Source.RawLayer = exit.Layer;
        if (worker) OriginalWorkerDelivery.InitializeEmpty(recruit.Record, recruit.Parameters, _terrainCatalog.NextRandomByte());
        recruit.Record[0x17] |= 0x20;
        CountProduced(recruit);
        recruit.Route.Attach(_routes, recruit.Record); Actors.Add(recruit);
        Occupancy.WriteFootprint(recruit.Parameters[3], recruit.Parameters[4], exit.Layer, exit.X, exit.Y, recruit.Id);
        if (producer.Team != 0) { EnemyRecruitLeaves(recruit); return true; }
        LastRecruitId = recruit.Id; RecountPopulation(); Log(template.Name + " готов у выхода из " + producer.Source.TemplateName + "."); return true;
    }
}
