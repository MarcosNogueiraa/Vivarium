namespace Vivarium.Api.Contracts;

public record StartBreedingRequest(long ParentAId, long ParentBId);

public record BreedingSlotDto(
    long Id, CreatureDto ParentA, CreatureDto ParentB, DateTime StartedAt, DateTime ReadyAt, bool IsReady, decimal CostPaid);

public record BreedingStatusResponse(bool Active, BreedingSlotDto? Slot);

/// <summary>Prévia sem custo/compromisso: chances do filho + custo/tempo/risco antes de confirmar.</summary>
public record BreedingQuoteDto(
    decimal CostSoft, double GestationHours, DateTime EstimatedReadyAt,
    IReadOnlyDictionary<string, double> ChildTierProbabilities,
    int ParentABreedCount, double ParentADeathChance,
    int ParentBBreedCount, double ParentBDeathChance);

/// <summary>Resultado da coleta: o filhote + se algum dos pais não sobreviveu à gestação.</summary>
public record CollectBreedingResponse(CreatureDto Child, bool ParentADied, bool ParentBDied);
