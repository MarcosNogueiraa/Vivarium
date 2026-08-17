using Vivarium.Core.Domain;

namespace Vivarium.Api.Contracts;

public record GrantPremiumRequest(decimal Amount);

public record AdjustWalletRequest(string Username, string CurrencyCode, string Mode, decimal Amount);

public record AdminSendInboxMessageRequest(
    string Title, string Body, InboxAudience Audience,
    IReadOnlyList<string>? Usernames, string? RewardCurrencyCode, decimal? RewardCurrencyAmount,
    string? RewardEggKey = null);
