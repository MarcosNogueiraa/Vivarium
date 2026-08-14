using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;

namespace Vivarium.Api.Services;

/// <summary>
/// Ranking global (raridade total / renda por hora) e visita a aquário de outro
/// jogador — só leitura, sem SaveChanges. Ver CLAUDE.md pra contexto de produto.
/// </summary>
public class LeaderboardService(VivariumDbContext db)
{
    private readonly record struct HabitatSnapshot(long HabitatId, long UserId, string Username, decimal MaintenanceLevel);

    /// <summary>
    /// Snapshot de todos os aquários (não os habitats de reprodução) com raridade total e
    /// renda/hora — duas queries (habitats, depois criaturas desses habitats), sem N+1.
    /// Recalculado a cada request: pro tamanho atual do jogo (beta, poucas centenas de
    /// jogadores) é rápido o bastante sem cache; se crescer, cachear por alguns minutos é
    /// a melhoria natural — não implementado agora (otimização prematura pro estágio atual).
    /// </summary>
    private async Task<List<(long UserId, string Username, decimal RarityTotal, decimal CoinsPerHour)>> AllAquariumSnapshotsAsync()
    {
        var habitats = await db.Habitats
            .Where(h => h.HabitatType!.Code == "Aquarium")
            .Select(h => new HabitatSnapshot(h.Id, h.UserId, h.User!.Username, h.MaintenanceLevel))
            .ToListAsync();
        if (habitats.Count == 0)
            return [];

        var habitatIds = habitats.Select(h => h.HabitatId).ToList();
        var creatures = await db.CreatureInstances
            .Where(c => c.HabitatId != null && habitatIds.Contains(c.HabitatId!.Value))
            .Select(c => new CreatureInstance
            {
                Id = c.Id, Seed = c.Seed, RarityScore = c.RarityScore, TraitConfigVersion = c.TraitConfigVersion,
                HabitatId = c.HabitatId, TraitsJson = c.TraitsJson,
            })
            .ToListAsync();
        var byHabitat = creatures.GroupBy(c => c.HabitatId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<(long, string, decimal, decimal)>(habitats.Count);
        foreach (var h in habitats)
        {
            var fish = byHabitat.TryGetValue(h.HabitatId, out var list) ? list : [];
            decimal rarityTotal = fish.Sum(f => f.RarityScore);
            var incomeFish = fish.Select(f =>
            {
                var (tail, dorsal, pectoral) = PartColorsResolver.Of(f);
                return new FishIncome(f.RarityScore, tail, dorsal, pectoral);
            }).ToList();
            decimal coinsPerHour = IncomeCalculator.TankRatePerHour(incomeFish, h.MaintenanceLevel, TickConfig.Default);
            result.Add((h.UserId, h.Username, rarityTotal, coinsPerHour));
        }
        return result;
    }

    public async Task<ServiceResult> GetLeaderboardAsync(long requestingUserId, string metric)
    {
        if (metric != "rarity" && metric != "income")
            return ServiceResult.Bad("Métrica inválida — use 'rarity' ou 'income'.");

        var snapshot = await AllAquariumSnapshotsAsync();
        var ordered = metric == "rarity"
            ? snapshot.OrderByDescending(s => s.RarityTotal).ToList()
            : snapshot.OrderByDescending(s => s.CoinsPerHour).ToList();

        var entries = new List<LeaderboardEntryDto>(Math.Min(100, ordered.Count));
        LeaderboardEntryDto? selfOutsideTop = null;
        for (int i = 0; i < ordered.Count; i++)
        {
            int rank = i + 1;
            bool isSelf = ordered[i].UserId == requestingUserId;
            decimal value = metric == "rarity" ? ordered[i].RarityTotal : ordered[i].CoinsPerHour;
            if (i < 100)
            {
                entries.Add(new LeaderboardEntryDto(rank, ordered[i].Username, value, isSelf));
            }
            else if (isSelf)
            {
                selfOutsideTop = new LeaderboardEntryDto(rank, ordered[i].Username, value, true);
                break;
            }
        }
        return ServiceResult.Success(new LeaderboardResponse(metric, entries, selfOutsideTop));
    }

    public async Task<ServiceResult> GetSpectatorTankAsync(string username)
    {
        var habitat = await db.Habitats
            .Include(h => h.User)
            .FirstOrDefaultAsync(h => h.HabitatType!.Code == "Aquarium" && h.User!.Username == username);
        if (habitat is null)
            return ServiceResult.NotFound("Jogador não encontrado.");

        var creatures = await db.CreatureInstances
            .Where(c => c.HabitatId == habitat.Id)
            .ToListAsync();

        decimal rarityTotal = creatures.Sum(c => c.RarityScore);
        var incomeFish = creatures.Select(c =>
        {
            var (tail, dorsal, pectoral) = PartColorsResolver.Of(c);
            return new FishIncome(c.RarityScore, tail, dorsal, pectoral);
        }).ToList();
        decimal coinsPerHour = IncomeCalculator.TankRatePerHour(incomeFish, habitat.MaintenanceLevel, TickConfig.Default);

        var breeding = await SpectatorBreedingAsync(habitat.UserId);

        return ServiceResult.Success(new SpectatorTankResponse(
            habitat.User!.Username,
            habitat.MaintenanceLevel,
            CapacityBands.BandFor(habitat.Capacity).Name,
            rarityTotal,
            coinsPerHour,
            creatures.Select(CreatureDto.From).ToList(),
            breeding));
    }

    /// <summary>
    /// Gestação em andamento do jogador visitado (Ninho), sem tick nem SaveChanges — mesma
    /// filosofia read-only do resto da visita (8.16). Sem gestação ativa, devolve Active=false.
    /// </summary>
    private async Task<SpectatorBreedingDto> SpectatorBreedingAsync(long userId)
    {
        var slot = await db.BreedingSlots
            .Include(s => s.ParentA)
            .Include(s => s.ParentB)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == BreedingStatus.InProgress);
        if (slot is null)
            return new SpectatorBreedingDto(false, null, null, null, false);

        bool ready = slot.ReadyAt <= DateTime.UtcNow;
        return new SpectatorBreedingDto(true, CreatureDto.From(slot.ParentA!), CreatureDto.From(slot.ParentB!), slot.ReadyAt, ready);
    }
}
