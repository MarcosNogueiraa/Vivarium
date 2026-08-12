namespace Vivarium.Api.Contracts;

/// <summary>
/// UseStabilizer/UseInsurance (31/07/2026): opt-in pra mitigar o risco de morte dos pais.
/// Estabilizador (soft) reduz o risco pela metade; seguro (premium) garante 0% — se os dois
/// vierem marcados, o seguro tem prioridade (o estabilizador não é cobrado nem faz diferença).
/// </summary>
public record StartBreedingRequest(long ParentAId, long ParentBId, bool UseStabilizer = false, bool UseInsurance = false);

public record BreedingSlotDto(
    long Id, CreatureDto ParentA, CreatureDto ParentB, DateTime StartedAt, DateTime ReadyAt, bool IsReady,
    decimal CostPaid, decimal RushCostPremium,
    decimal ParentADeathChance, decimal ParentBDeathChance, bool InsuranceUsed);

public record BreedingStatusResponse(bool Active, BreedingSlotDto? Slot);

/// <summary>Prévia sem custo/compromisso: chances do filho + custo/tempo/risco antes de confirmar.</summary>
public record BreedingQuoteDto(
    decimal CostSoft, double GestationHours, DateTime EstimatedReadyAt,
    IReadOnlyDictionary<string, double> ChildTierProbabilities,
    int ParentABreedCount, double ParentADeathChance,
    int ParentBBreedCount, double ParentBDeathChance,
    decimal StabilizerCostSoft, double StabilizerReductionFactor, decimal InsuranceCostPremium);

/// <summary>Resultado da coleta: o filhote + se algum dos pais não sobreviveu à gestação.</summary>
public record CollectBreedingResponse(CreatureDto Child, bool ParentADied, bool ParentBDied);

/// <summary>
/// Registro de cruzamentos (§8.19) — 1 linha por gestação já coletada, mais recente primeiro.
/// Reaproveita os dados que o BreedingSlot já guarda (nada novo além de ParentADied/BDied,
/// que passaram a ser gravados na coleta) — pensado tanto pro jogador consultar quanto pra
/// troubleshooting (mesma forma que já respondia "por que meu peixe X nasceu assim").
/// </summary>
public record BreedingHistoryEntryDto(
    long Id, CreatureDto ParentA, CreatureDto ParentB, CreatureDto? Child,
    DateTime StartedAt, DateTime ReadyAt, decimal CostPaid,
    bool ParentADied, bool ParentBDied, bool InsuranceUsed);
