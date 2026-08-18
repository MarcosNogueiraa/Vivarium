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
    private readonly record struct RankedRow(long UserId, string Username, decimal Value);

    /// <summary>
    /// Paginação real via SQL (18/08/2026, BACKLOG.md #7) — não carrega mais todo mundo em
    /// memória. "rarity" soma via GroupBy (traduzível pra SQL); "income" lê
    /// <see cref="Habitat.CoinsPerHourSnapshot"/> (a sinergia por cor não é traduzível, por
    /// isso o snapshot gravado a cada tick em <c>GameService.ApplyTickAsync</c>).
    /// </summary>
    public async Task<ServiceResult> GetLeaderboardAsync(long requestingUserId, string metric, int page, int pageSize)
    {
        if (metric != "rarity" && metric != "income")
            return ServiceResult.Bad("Métrica inválida — use 'rarity' ou 'income'.");
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // GroupBy sobre a navegação opcional CreatureInstance.Habitat (FK nullable), e o record
        // struct RankedRow construído dentro do Select, não traduzem de forma confiável quando
        // compostos com Where/CountAsync (EF reconstrói o objeto inteiro dentro do predicado em
        // vez de empurrar só a comparação — achado rodando os testes). Tipo anônimo + subquery
        // correlacionada por habitat (partindo de Habitats, mesma base do "income") é o padrão
        // que o EF Core traduz de forma confiável nos dois provedores (SQLite/Postgres).
        var query = metric == "rarity"
            ? db.Habitats
                .Where(h => h.HabitatType!.Code == "Aquarium")
                .Select(h => new
                {
                    h.UserId,
                    Username = h.User!.Username,
                    Value = db.CreatureInstances.Where(c => c.HabitatId == h.Id).Sum(c => (decimal?)c.RarityScore) ?? 0m,
                })
            : db.Habitats
                .Where(h => h.HabitatType!.Code == "Aquarium")
                .Select(h => new { h.UserId, Username = h.User!.Username, Value = h.CoinsPerHourSnapshot });

        int totalCount = await query.CountAsync();

        decimal selfValue = await query
            .Where(r => r.UserId == requestingUserId)
            .Select(r => r.Value)
            .FirstOrDefaultAsync();
        int selfRank = await query.CountAsync(r => r.Value > selfValue) + 1;

        var pageRows = (await query
            .OrderByDescending(r => r.Value)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync())
            .Select(r => new RankedRow(r.UserId, r.Username, r.Value))
            .ToList();

        // Rank de cada linha da página = quantos estão estritamente na frente + 1 — empate
        // compartilha rank, de propósito (decidido pelo usuário, mais simples e correto).
        var entries = new List<LeaderboardEntryDto>(pageRows.Count);
        foreach (var row in pageRows)
        {
            int rank = row.Value == selfValue && row.UserId == requestingUserId
                ? selfRank
                : await query.CountAsync(r => r.Value > row.Value) + 1;
            entries.Add(new LeaderboardEntryDto(rank, row.Username, row.Value, row.UserId == requestingUserId, 0, null));
        }

        await AttachLevelsAndAvatarsAsync(entries, pageRows);

        return ServiceResult.Success(new LeaderboardResponse(metric, page, pageSize, totalCount, entries, selfRank, selfValue));
    }

    /// <summary>Enriquece as entradas da página com nível + avatar. Quem não escolheu avatar
    /// manualmente mostra o peixe de maior score que possui (18/08/2026, pedido do usuário —
    /// todo mundo aparece com um ícone no Ranking, não só quem configurou). Batch em 3 queries
    /// (nunca N+1): usuários da página, peixe de maior score de quem não tem avatar (subquery
    /// correlacionada por usuário — GroupBy+First não traduz de forma confiável no SQLite,
    /// achado nesta mesma sessão), e as criaturas finais de uma vez só.</summary>
    private async Task AttachLevelsAndAvatarsAsync(List<LeaderboardEntryDto> entries, List<RankedRow> rows)
    {
        var userIds = rows.Select(r => r.UserId).ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Xp, u.AvatarCreatureInstanceId })
            .ToListAsync();
        var usersById = users.ToDictionary(u => u.Id);

        var fallbackUserIds = users.Where(u => !u.AvatarCreatureInstanceId.HasValue).Select(u => u.Id).ToList();
        var topFishIdByUser = fallbackUserIds.Count == 0
            ? new Dictionary<long, long>()
            : (await db.Users
                .Where(u => fallbackUserIds.Contains(u.Id))
                .Select(u => new
                {
                    u.Id,
                    TopFishId = db.CreatureInstances
                        .Where(c => c.OwnerId == u.Id)
                        .OrderByDescending(c => c.RarityScore)
                        .Select(c => (long?)c.Id)
                        .FirstOrDefault(),
                })
                .Where(x => x.TopFishId != null)
                .ToListAsync())
                .ToDictionary(x => x.Id, x => x.TopFishId!.Value);

        var creatureIds = users.Where(u => u.AvatarCreatureInstanceId.HasValue)
            .Select(u => u.AvatarCreatureInstanceId!.Value)
            .Concat(topFishIdByUser.Values)
            .Distinct()
            .ToList();
        var creaturesById = creatureIds.Count == 0
            ? []
            : (await db.CreatureInstances.Where(c => creatureIds.Contains(c.Id)).ToListAsync())
                .ToDictionary(c => c.Id);

        for (int i = 0; i < entries.Count; i++)
        {
            var u = usersById[rows[i].UserId];
            int level = LevelCalculator.ProgressOf(u.Xp, LevelConfig.Default).Level;
            long? avatarCreatureId = u.AvatarCreatureInstanceId
                ?? (topFishIdByUser.TryGetValue(u.Id, out var topId) ? topId : null);
            CreatureDto? avatar = avatarCreatureId is { } id && creaturesById.TryGetValue(id, out var creature)
                ? CreatureDto.From(creature)
                : null;
            entries[i] = entries[i] with { Level = level, Avatar = avatar };
        }
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
