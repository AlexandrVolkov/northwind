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
    public int RecruitmentCount
    { get { int count = 0; foreach (byte slot in PlayerCastle.Record.AsSpan(0x22, 5)) if (slot != 0xff) count++; return count; } }
    public bool RecruitmentPaid => RecruitmentCount > 0 && PlayerCastle.Record[0x15] != 0;
    public bool RecruitmentBlocked { get; private set; }
    public uint LastRecruitId { get; private set; }
    // The full participant/effect recount is not connected. Oleg has no speed buildings in this slice.
    public byte ProductionSpeedShift => 0;
    public TemplateEntry? RecruitmentTemplate => RecruitmentCount == 0 ? null : _templates.Armies["ARMY__02.DAT"].Templates[PlayerCastle.Record[0x22]];
    public string RecruitmentText
    {
        get
        {
            if (RecruitmentTemplate is not { } template) return "Очередь найма пуста";
            string names = string.Join(" → ", PlayerCastle.Record.AsSpan(0x22, 5).ToArray().Where(t => t != 0xff)
                .Select(t => _templates.Armies["ARMY__02.DAT"].Templates[t].Name));
            string state = !RecruitmentPaid ? (CanAfford(template) ? "Ожидает начала подготовки" : MissingResources(template))
                : RecruitmentBlocked ? "Готов. Освободите выход из замка."
                : $"Подготовка: {PlayerCastle.Record[0x27]}/{template.InitialCondition}";
            return $"Найм {RecruitmentCount}/5: {names}\n{state}";
        }
    }
    public ProductionExit RecruitExit(TemplateEntry template) => OriginalProductionRules.Exit(PlayerCastle.Record, PlayerCastle.Parameters, Convert.FromHexString(template.RawRecordHex));
    private void InitializeProduction() => _production = GameData.Read<ProductionData>("res://data/original_production.json");
    public bool QueueWorker() => QueueRecruit(10);
    public bool QueueMilitia() => QueueRecruit(0);
    private bool QueueRecruit(byte template)
    {
        if (Paused || Outcome != "" || !PlayerCastle.Alive) return false;
        if (!OriginalProductionRules.Enqueue(PlayerCastle.Record, template)) { Log("Очередь замка заполнена: пять заказов."); return false; }
        Log(_templates.Armies["ARMY__02.DAT"].Templates[template].Name + " добавлен в очередь. Оплата — при начале подготовки."); return true;
    }
    public bool CancelRecruitment()
    {
        if (Paused || Outcome != "" || !PlayerCastle.Alive || RecruitmentTemplate is not { } template) return false;
        if (RecruitmentPaid)
            SetResources(OriginalProductionRules.Refund(Resources, Price(template), PlayerCastle.Record[0x27], template.InitialCondition));
        // UI composition: native active-action refund, then clear all five slots.
        // Native cancellation alone retains the queue until a separate clear command.
        PlayerCastle.Record.AsSpan(0x22, 5).Fill(0xff); PlayerCastle.Record[0x15] = PlayerCastle.Record[0x27] = 0;
        PlayerCastle.Record[0x0f] = 0x24; PlayerCastle.Record[0x14] &= 0xf8;
        RecruitmentBlocked = false; Log("Очередь найма очищена. Люди текущего заказа возвращены; материалы — по исходной формуле прогресса."); return true;
    }
    private void TickProduction()
    {
        if (!PlayerCastle.Alive || RecruitmentTemplate is not { } template) return;
        var record = PlayerCastle.Record;
        if (!RecruitmentPaid)
        {
            var payment = OriginalConstructionRules.TryDebitUpfront(Resources, Price(template));
            if (!payment.Accepted) return; // Queued, unpaid orders retry without a debit or a reset.
            SetResources(payment.Balances); OriginalProductionRules.StartPaid(record); RecruitmentBlocked = false; return;
        }
        if (!OriginalProductionRules.Tick(record, _production.RecruitmentIntervals[record[0x22]], ProductionSpeedShift, template.InitialCondition)) return;
        if (!ReleaseRecruit(template)) { RecruitmentBlocked = true; return; }
        OriginalProductionRules.Complete(record); RecruitmentBlocked = false;
    }
    private bool ReleaseRecruit(TemplateEntry template)
    {
        var exit = RecruitExit(template); var cell = new Vector2I((int)(exit.X >> 5), (int)(exit.Y >> 5));
        // Only the two currently supported 1x1 recruits. AC63/A758 and the original pool allocator remain separate work.
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
        LastRecruitId = recruit.Id; RecountPopulation(); Log(template.Name + " готов у выхода из замка."); return true;
    }
}
