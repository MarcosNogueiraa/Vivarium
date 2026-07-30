namespace Vivarium.Api.Contracts;

public record QueueItemDto(long Id, DateTime ReadyAt, bool IsReady, bool IsSick);

public record TransferRequest(string ToUsername);

public record TankResponse(
    bool Online,
    decimal MaintenanceLevel,
    int Capacity,
    int QueueCap,
    IReadOnlyList<QueueItemDto> Queue,
    IReadOnlyList<CreatureDto> Creatures,
    Dictionary<string, decimal> Wallet,
    decimal CoinsPerHour,
    decimal GenerationProgressMinutes,
    int GenerationIntervalMinutes);

public record BackpackResponse(int Capacity, IReadOnlyList<CreatureDto> Creatures);
