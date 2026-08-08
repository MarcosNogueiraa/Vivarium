namespace Vivarium.Api.Contracts;

public record QueueItemDto(long Id, DateTime ReadyAt, bool IsReady, bool IsSick, decimal RushCostPremium);

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
    int GenerationIntervalMinutes,
    bool IsAdmin = false,
    string CapacityBandName = "",
    int MaxCapacity = 0,
    decimal CapacityBandDegradationFactor = 1m,
    decimal FilterCapacity = 0m);

public record BackpackResponse(int Capacity, IReadOnlyList<CreatureDto> Creatures);

public record DailyRewardStatusDto(bool CanClaim, decimal Amount, DateTime? NextAvailableAtUtc);
