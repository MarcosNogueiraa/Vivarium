namespace Vivarium.Api.Contracts;

/// <summary>Uma linha do ranking — Value é RarityScore total ou coinsPerHour, conforme a métrica pedida.</summary>
public record LeaderboardEntryDto(int Rank, string Username, decimal Value, bool IsSelf);

public record LeaderboardResponse(
    string Metric,
    IReadOnlyList<LeaderboardEntryDto> Entries,
    LeaderboardEntryDto? SelfOutsideTop);

/// <summary>Tanque de outro jogador, só leitura — sem fila/carteira/ações, só o que o AquariumCanvas + FishDetail precisam.</summary>
public record SpectatorTankResponse(
    string Username,
    decimal MaintenanceLevel,
    string CapacityBandName,
    decimal RarityTotal,
    decimal CoinsPerHour,
    IReadOnlyList<CreatureDto> Creatures);
