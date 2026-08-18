namespace Vivarium.Api.Contracts;

/// <summary>Uma linha do ranking — Value é RarityScore total ou CoinsPerHourSnapshot, conforme
/// a métrica pedida. Rank vem de "quantos estão na frente + 1" (18/08/2026, BACKLOG.md #7) —
/// empate exato compartilha o mesmo rank, de propósito (mais simples e correto que desempate
/// arbitrário).</summary>
public record LeaderboardEntryDto(int Rank, string Username, decimal Value, bool IsSelf, int Level, CreatureDto? Avatar);

/// <summary>
/// Paginação real via SQL (18/08/2026, BACKLOG.md #7) — troca o antigo "top 100 fixo +
/// SelfOutsideTop" (tudo calculado em memória) por página navegável de verdade, pensando em
/// milhares de jogadores. <see cref="SelfRank"/>/<see cref="SelfValue"/> sempre vêm preenchidos
/// (independente da página vista), calculados via CountAsync — não exigem carregar a lista toda.
/// </summary>
public record LeaderboardResponse(
    string Metric,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<LeaderboardEntryDto> Entries,
    int SelfRank,
    decimal SelfValue);

/// <summary>
/// Gestação em andamento do jogador visitado, só leitura — deliberadamente NÃO expõe
/// custo pago, risco de morte travado nem seguro/estabilizador usado (informação
/// financeira/privada do dono, sem utilidade pra quem só está de visita). Só os pais
/// e o tempo restante, o suficiente pro AquariumCanvas tema "breeding" renderizar.
/// </summary>
public record SpectatorBreedingDto(bool Active, CreatureDto? ParentA, CreatureDto? ParentB, DateTime? ReadyAt, bool IsReady);

/// <summary>Tanque de outro jogador, só leitura — sem fila/carteira/ações, só o que o AquariumCanvas + FishDetail precisam.</summary>
public record SpectatorTankResponse(
    string Username,
    decimal MaintenanceLevel,
    string CapacityBandName,
    decimal RarityTotal,
    decimal CoinsPerHour,
    IReadOnlyList<CreatureDto> Creatures,
    SpectatorBreedingDto Breeding);
