namespace Vivarium.Api.Contracts;

public record VipStatusDto(bool Active, DateTime? EndAt, Dictionary<int, decimal> Packages);

public record VipSubscribeRequest(int Days);
