using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Data;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;

namespace Vivarium.Api.Services;

/// <summary>
/// Aplica a lógica pura de Vivarium.Core.Gameplay nas entidades. O tick roda
/// "lazy" dentro das chamadas de API (heartbeat/tanque/coleta) — sem job agendado.
/// Nada aqui chama SaveChanges: o endpoint salva no fim, uma transação por request.
/// </summary>
public class GameService(VivariumDbContext db)
{
    public async Task<Habitat?> FindHabitatAsync(long userId)
        => await db.Habitats.FirstOrDefaultAsync(h => h.UserId == userId);

    public async Task ApplyTickAsync(Habitat habitat, DateTime nowUtc)
    {
        int pending = await db.GenerationQueueItems
            .CountAsync(q => q.HabitatId == habitat.Id && q.Status == QueueItemStatus.Pending);
        bool hasAutoFilter = await db.UserInventories.AnyAsync(i =>
            i.UserId == habitat.UserId
            && i.Quantity > 0
            && i.ItemDefinition!.Category == ItemCategory.AutoFilter);

        var state = new HabitatTickState(
            LastTickAtUtc: habitat.LastTickAt,
            LastHeartbeatAtUtc: habitat.LastHeartbeatAt,
            MaintenanceLevel: habitat.MaintenanceLevel,
            GenerationProgressMinutes: habitat.GenerationProgressMinutes,
            GenerationIntervalMinutes: habitat.GenerationIntervalMinutes,
            OnlineGenerationRate: habitat.OnlineGenerationRate,
            OfflineGenerationRate: habitat.OfflineGenerationRate,
            QueueCap: habitat.QueueCap,
            PendingQueueCount: pending,
            HasAutoFilter: hasAutoFilter);

        var outcome = HabitatTicker.ProcessTick(state, nowUtc, Random.Shared, TickConfig.Default);
        habitat.LastTickAt = outcome.LastTickAtUtc;
        habitat.MaintenanceLevel = outcome.MaintenanceLevel;
        habitat.GenerationProgressMinutes = outcome.GenerationProgressMinutes;

        if (outcome.SpawnCount > 0)
        {
            int speciesId = await db.Species
                .Where(s => s.HabitatTypeId == habitat.HabitatTypeId)
                .Select(s => s.Id)
                .FirstAsync();
            foreach (bool sick in outcome.SpawnSickFlags)
            {
                db.GenerationQueueItems.Add(new GenerationQueueItem
                {
                    HabitatId = habitat.Id,
                    SpeciesId = speciesId,
                    ReadyAt = nowUtc,
                    Status = QueueItemStatus.Pending,
                    IsSick = sick,
                });
            }
        }

        // Diferencial VIP: coleta automática, mas só enquanto o tanque está online
        if (HabitatTicker.IsOnline(habitat.LastHeartbeatAt, nowUtc, TickConfig.Default)
            && await HasActiveVipAsync(habitat.UserId, nowUtc))
        {
            await CollectAllReadyAsync(habitat, nowUtc);
        }
    }

    public Task<bool> HasActiveVipAsync(long userId, DateTime nowUtc)
        => db.VipSubscriptions.AnyAsync(v =>
            v.UserId == userId
            && v.Status == SubscriptionStatus.Active
            && v.StartAt <= nowUtc
            && v.EndAt > nowUtc);

    public async Task<(CreatureInstance? Creature, string? Error)> CollectAsync(
        Habitat habitat, long queueItemId, DateTime nowUtc)
    {
        var item = await db.GenerationQueueItems
            .FirstOrDefaultAsync(q => q.Id == queueItemId && q.HabitatId == habitat.Id);
        if (item is null || item.Status != QueueItemStatus.Pending)
            return (null, "Item não encontrado ou já coletado");
        if (item.ReadyAt > nowUtc)
            return (null, "Item ainda não está pronto");
        if (await CountActiveCreaturesAsync(habitat) >= habitat.Capacity)
            return (null, "Tanque cheio — venda ou transfira um peixe antes de coletar");

        return (CollectOne(habitat, item, nowUtc), null);
    }

    private async Task CollectAllReadyAsync(Habitat habitat, DateTime nowUtc)
    {
        var ready = await db.GenerationQueueItems
            .Where(q => q.HabitatId == habitat.Id && q.Status == QueueItemStatus.Pending && q.ReadyAt <= nowUtc)
            .OrderBy(q => q.ReadyAt)
            .ToListAsync();
        // Itens gerados neste mesmo tick ainda não foram salvos — estão só no change tracker
        ready.AddRange(db.GenerationQueueItems.Local.Where(q =>
            q.Id == 0 && q.HabitatId == habitat.Id && q.Status == QueueItemStatus.Pending));

        int active = await CountActiveCreaturesAsync(habitat);
        foreach (var item in ready)
        {
            if (active >= habitat.Capacity)
                break;
            CollectOne(habitat, item, nowUtc);
            active++;
        }
    }

    private CreatureInstance CollectOne(Habitat habitat, GenerationQueueItem item, DateTime nowUtc)
    {
        var collected = CreatureCollector.Collect(item.IsSick, CreatureCollector.NewRandomSeed);
        item.Status = QueueItemStatus.Collected;

        var creature = new CreatureInstance
        {
            SpeciesId = item.SpeciesId,
            OwnerId = habitat.UserId,
            HabitatId = habitat.Id,
            Seed = collected.Seed,
            TraitConfigVersion = collected.TraitConfigVersion,
            RarityScore = collected.RarityScore,
            CreatedAt = nowUtc,
        };
        db.CreatureInstances.Add(creature);
        return creature;
    }

    private Task<int> CountActiveCreaturesAsync(Habitat habitat)
        => db.CreatureInstances.CountAsync(c => c.HabitatId == habitat.Id);
}
