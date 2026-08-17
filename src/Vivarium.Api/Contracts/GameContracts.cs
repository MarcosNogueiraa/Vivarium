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
    decimal FilterCapacity = 0m,
    bool IsVip = false,
    DateTime? VipEndAt = null,
    bool HasWaterSensor = false,
    decimal AutoCleanTriggerPercent = 0m,
    decimal WaterSensorMaxTriggerPercent = 0m,
    bool AutoCollectEnabled = true,
    bool AutoCleanEnabled = true);

public record SetAutoCleanTriggerRequest(decimal Percent);
public record SetTogglesRequest(bool AutoCollectEnabled, bool AutoCleanEnabled);

public record BackpackResponse(int Capacity, IReadOnlyList<CreatureDto> Creatures);

// Redesenho 17/08/2026 (era valor fixo Amount) — MinAmount/MaxAmount é a faixa da roleta (o
// valor exato só sai depois de resgatar); StreakBonusPercent já reflete o bônus que ESTE resgate
// vai aplicar (streak+1 em relação ao atual, se for consecutivo).
public record DailyRewardStatusDto(
    bool CanClaim, decimal MinAmount, decimal MaxAmount, int CurrentStreak, double StreakBonusPercent,
    double EggChancePercent, DateTime? NextAvailableAtUtc);

public record ClaimDailyRewardResponse(decimal Amount, decimal Wallet, int Streak, bool GotEgg);
