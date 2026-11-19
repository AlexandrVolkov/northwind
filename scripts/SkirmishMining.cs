using System.Buffers.Binary;
using Godot;
namespace Northwind;

/// <summary>Bridge from the verified ore kernels (OriginalOreRules) to the experimental skirmish.</summary>
public sealed partial class Skirmish
{
    /// <summary>Participant statistics +1A08 (gold) and +1A0C (metal) fed by 11EB0/11ECB.</summary>
    public uint ProducedGold { get; private set; }
    public uint ProducedMetal { get; private set; }
    /// <summary>Low byte of the global update counter read as [19EA] by B47F/CC7B.</summary>
    private byte _globalTick;
    private byte[] WorkerVariant(int index) => Convert.FromHexString(_templates.Armies["ARMY__02.DAT"].Templates[index].RawRecordHex);
    public static string OreName(byte cargo) => cargo switch { 1 => "золото", 2 => "металл", _ => $"тип {cargo}" };

    /// <summary>Player order: WRK action 4 (86B6) with the mine in OBE[1C].</summary>
    public bool WorkMine(IEnumerable<uint> ids, uint mineId)
    {
        if (Paused || Outcome != "" || Find(mineId) is not { Team: 0 } mine || mine.Source.TemplateName != "Шахта") return false;
        int count = 0;
        foreach (var worker in ids.Select(Find).OfType<SkirmishActor>().Where(a => a.Team == 0 && a.Worker && a.Record[0x31] is 0 or 1 or 2))
        {
            worker.GatherTree = null; worker.PreferredMill = 0; worker.HarvestPrepared = false; worker.Goal = null; worker.Target = 0;
            worker.Route.Clear(); worker.ReplanTimer = 0; worker.Record[0x2d] &= 15; worker.Record[0x17] &= 0xbf;
            BinaryPrimitives.WriteUInt32LittleEndian(worker.Record.AsSpan(0x1c), mine.Id);
            BinaryPrimitives.WriteUInt32LittleEndian(worker.Record.AsSpan(0x32), 0);
            worker.Record[0x15] = 0; worker.Record[0x30] = 0; worker.MineJob = true; count++;
        }
        if (count > 0) Log($"В шахту: {count} раб. Руда ({OreName(mine.Record[0x23])}) пойдёт в замок.");
        return count > 0;
    }

    private SkirmishActor? EntryVisitor(SkirmishActor building)
        => Find(OriginalWorkerDelivery.WaitingWorker(Occupancy.Width, Occupancy.Height, Occupancy.Cells, building.Record[0x0d],
            (uint)building.Position.X, (uint)building.Position.Y, building.Parameters));

    private void TickMining()
    {
        _globalTick = unchecked((byte)(_globalTick + 1));
        foreach (var mine in Actors.Where(a => a.Alive && a.Source.TemplateName == "Шахта" && a.ConstructionRemaining <= 0).ToArray())
        {
            var visitor = EntryVisitor(mine);
            uint before = BinaryPrimitives.ReadUInt32LittleEndian(mine.Record.AsSpan(0x24));
            var result = OriginalOreRules.Extract(mine.Record, mine.Parameters[0x78], _globalTick, visitor != null ? visitor.Record : Span<byte>.Empty,
                (record, cargo) =>
                {
                    bool granted = OriginalOreRules.Grant(record, visitor!.Parameters, WorkerVariant(OriginalOreRules.OreWorker),
                        WorkerVariant(OriginalOreRules.TimberWorker), cargo, _terrainCatalog.NextRandomByte);
                    if (granted) ApplyWorkerTemplate(visitor);
                    return granted;
                },
                (cargo, radius) =>
                {
                    if (OriginalOreRules.Deplete(Map.Cells, (byte)Map.Width, (byte)Map.Height, (uint)mine.Position.X, (uint)mine.Position.Y,
                        cargo, radius, _terrainCatalog) >= 0) TerrainRevision++;
                });
            if (result == OreExtraction.Granted && before >= 100 && BinaryPrimitives.ReadUInt32LittleEndian(mine.Record.AsSpan(0x24)) == 0 && mine.Team == 0)
                Log("Шахта истощена: руда закончилась.");
        }
        foreach (var castle in Actors.Where(a => a.Alive && a.Source.TemplateName == "Замок").ToArray()) CastleReceive(castle);
    }

    private void CastleReceive(SkirmishActor castle)
    {
        var carrier = EntryVisitor(castle);
        // 1243A goods other than ore use item slots that are not ported; such a visitor is left for the next update.
        if (carrier != null && carrier.Record.Length > 0x31 && carrier.Record[0x31] > 2) return;
        // The enemy castle credits the enemy's metal and gold (participant +18/+20); its statistics are not kept (adapter).
        var wallet = Wallet(castle.Team);
        var credit = castle.Team == 0 ? new OreCredit(Metal, Gold, ProducedMetal, ProducedGold) : new OreCredit(wallet.First, wallet.Third, 0, 0);
        byte cargoBefore = carrier?.Record.Length > 0x31 ? carrier.Record[0x31] : (byte)0;
        if (OriginalOreRules.CastleReceive(castle.Record, _globalTick, carrier != null ? carrier.Record : Span<byte>.Empty,
                carrier != null ? carrier.Parameters : ReadOnlySpan<byte>.Empty, ref credit) && cargoBefore != 0 && castle.Team == 0)
            Log($"В замок доставлено 100: {OreName(cargoBefore)}.");
        if (castle.Team == 0) { Metal = credit.Metal; Gold = credit.Gold; ProducedMetal = credit.MetalStatistics; ProducedGold = credit.GoldStatistics; }
        else EnemyResources = wallet with { First = credit.Metal, Third = credit.Gold };
    }

    private void TickOreWorker(SkirmishActor worker)
    {
        // ARMY__02.WRK A37..A43: a living ore carrier without cargo first becomes the empty worker.
        if (worker.Source.TemplateIndex == OriginalOreRules.OreWorker && worker.Record[0x31] == 0)
        {
            OriginalOreRules.ReturnEmpty(worker.Record, WorkerVariant(OriginalOreRules.EmptyWorker), _terrainCatalog.NextRandomByte());
            ApplyWorkerTemplate(worker);
        }
        if (!worker.MineJob)
        {
            worker.JobText = worker.Record[0x31] is 1 or 2 ? $"Несёт руду ({OreName(worker.Record[0x31])}) · ПКМ по шахте" : "Без задания";
            MoveTick(worker); return;
        }
        OriginalOreRules.CarrierStep(worker.Record, false, new MiningWorld(this, worker));
        uint target = BinaryPrimitives.ReadUInt32LittleEndian(worker.Record.AsSpan(0x1c));
        if ((worker.Record[0x15] & 15) == 1 && target == 0) { worker.MineJob = false; worker.JobText = "Шахта недоступна · укажите другую"; }
        else worker.JobText = (worker.Record[0x15] & 15, worker.Record[0x31]) switch
        {
            (3, _) => "Ждёт и повторит попытку",
            (2, not 0) => $"Несёт {OreName(worker.Record[0x31])} в замок",
            (_, not 0) => "Ищет замок для сдачи руды",
            _ => (worker.Record[0x30] & 15) == 3 ? "Ждёт мешок у входа шахты" : "Идёт в шахту",
        };
        MoveTick(worker);
    }

    private sealed class MiningWorld(Skirmish battle, SkirmishActor worker) : IOreCarrierWorld
    {
        public ReadOnlySpan<byte> Record(uint reference) => battle.Find(reference) is { } actor ? actor.Record : default;
        public OreApproach Approach()
        {
            var target = battle.Find(BinaryPrimitives.ReadUInt32LittleEndian(worker.Record.AsSpan(0x1c)));
            if (target == null || target.Team != worker.Team) { worker.Record[0x17] &= 0xbf; return OreApproach.Failed; }
            bool accepts = target.Source.TemplateName switch
            {
                "Шахта" => OriginalOreRules.MineAccepts(worker.Parameters),
                "Замок" => OriginalOreRules.CastleAccepts(target.Record, worker.Record, worker.Parameters),
                _ => false,
            };
            var result = OriginalWorkerApproach.Step(worker.Record, target.Record, worker.Parameters, target.Parameters,
                (uint)(battle.Map.Width * 32), (uint)(battle.Map.Height * 32), accepts);
            if (result.StopRequested) { worker.Route.Clear(); if (!worker.Stepping) worker.Record[0x2d] = 0; }
            if (result.MoveRequested)
            {
                battle.Plan(worker, new Vector2I(worker.Record[0x22], worker.Record[0x23]));
                if (worker.Route.Count == 0) worker.Record[0x2d] = 0;
            }
            return result.Status switch
            {
                WorkerApproachStatus.Pending => OreApproach.Pending,
                WorkerApproachStatus.Completed => OreApproach.Completed,
                _ => OreApproach.Failed,
            };
        }
        public uint FindReceiver() => OriginalWorkerDelivery.FindReceiver((uint)worker.Position.X, (uint)worker.Position.Y,
            battle.Actors.Where(a => a.Team == worker.Team).Select(ReceiverCandidate), 0x508, 0xf08);
        // 8B0F is a bridge: step to the first free neighbouring cell so the entry is released.
        public void StepAside()
        {
            worker.Route.Clear();
            foreach (var d in new[] { Vector2I.Left, Vector2I.Right, Vector2I.Down, Vector2I.Up, new(-1, 1), new(1, 1), new(-1, -1), new(1, -1) })
                if (battle.CanStand(worker, worker.Cell + d) && battle.Plan(worker, worker.Cell + d)) return;
        }
    }
}
