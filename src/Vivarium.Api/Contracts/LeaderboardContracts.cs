namespace Vivarium.Api.Contracts;

/// <summary>Uma linha do ranking — Value é RarityScore total ou coinsPerHour, conforme a métrica pedida.</summary>
public record LeaderboardEntryDto(int Rank, string Username, decimal Value, bool IsSelf);

public record LeaderboardResponse(
    string Metric,
    IReadOnlyList<LeaderboardEntryDto> Entries,
    LeaderboardEntryDto? SelfOutsideTop);

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
