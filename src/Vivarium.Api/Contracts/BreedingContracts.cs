namespace Vivarium.Api.Contracts;

public record StartBreedingRequest(long ParentAId, long ParentBId);

public record BreedingSlotDto(
    long Id, CreatureDto ParentA, CreatureDto ParentB, DateTime StartedAt, DateTime ReadyAt, bool IsReady);

public record BreedingStatusResponse(bool Active, BreedingSlotDto? Slot);
