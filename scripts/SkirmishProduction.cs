using System.Buffers.Binary;
using Godot;
namespace Northwind;

public sealed class ProductionData
{
    public Dictionary<int, byte> RecruitmentIntervals { get; set; } = [];
}

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
    private TemplateEntry? QueuedTemplate(SkirmishActor producer) => CountQueue(producer) == 0 ? null : _templates.Armies["ARMY__02.DAT"].Templates[producer.Record[0x22]];
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
    { if (producer == PlayerCastle) RecruitmentBlocked = blocked; else if (blocked) _blockedBarracks.Add(producer.Id); else _blockedBarracks.Remove(producer.Id); }
    private void TickProduction()
    {
        TickProduction(PlayerCastle);
        foreach (var barracks in Actors.Where(IsBarracks).ToArray()) TickProduction(barracks);
    }
    private void TickProduction(SkirmishActor producer)
    {
        if (!producer.Alive || producer.ConstructionRemaining > 0 || QueuedTemplate(producer) is not { } template) return;
        var record = producer.Record;
        if (record[0x15] == 0)
        {
            var payment = OriginalConstructionRules.TryDebitUpfront(Resources, Price(template));
            if (!payment.Accepted) return;
            SetResources(payment.Balances); OriginalProductionRules.StartPaid(record); SetBlocked(producer, false); return;
        }
        if (!OriginalProductionRules.Tick(record, _production.RecruitmentIntervals[record[0x22]], ProductionSpeedShift, template.InitialCondition)) return;
        if (!ReleaseRecruit(producer, template)) { SetBlocked(producer, true); return; }
        OriginalProductionRules.Complete(record); SetBlocked(producer, false);
    }
    private bool ReleaseRecruit(SkirmishActor producer, TemplateEntry template)
    {
        var exit = RecruitExit(producer, template); var cell = new Vector2I((int)(exit.X >> 5), (int)(exit.Y >> 5));
        if (!Inside(cell)) return false;
        bool worker = template.TemplateIndex == 10;
        var recruit = NewEconomyActor(template.TemplateIndex, cell, worker, !worker);
        recruit.Record[0x0d] = exit.Layer;
        BinaryPrimitives.WriteUInt32LittleEndian(recruit.Record.AsSpan(4), exit.X);
        BinaryPrimitives.WriteUInt32LittleEndian(recruit.Record.AsSpan(8), exit.Y);
        if (!CanStand(recruit, cell) || _reservations.ContainsKey((cell, exit.Layer))) return false;
        recruit.Source.RawX = exit.X; recruit.Source.RawY = exit.Y; recruit.Source.RawLayer = exit.Layer;
        if (worker) OriginalWorkerDelivery.InitializeEmpty(recruit.Record, recruit.Parameters, _terrainCatalog.NextRandomByte());
        recruit.Record[0x17] |= 0x20;
        recruit.Route.Attach(_routes, recruit.Record); Actors.Add(recruit);
        Occupancy.WriteFootprint(recruit.Parameters[3], recruit.Parameters[4], exit.Layer, exit.X, exit.Y, recruit.Id);
        LastRecruitId = recruit.Id; RecountPopulation(); Log(template.Name + " готов у выхода из " + producer.Source.TemplateName + "."); return true;
    }
}
