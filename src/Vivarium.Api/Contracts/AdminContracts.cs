using Vivarium.Core.Domain;

namespace Vivarium.Api.Contracts;

public record GrantPremiumRequest(decimal Amount);

public record AdjustWalletRequest(string Username, string CurrencyCode, string Mode, decimal Amount);

public record AdminSendInboxMessageRequest(
    string Title, string Body, InboxAudience Audience,
    IReadOnlyList<string>? Usernames, string? RewardCurrencyCode, decimal? RewardCurrencyAmount,
    // Lista com repetição = quantidade (ex: ["egg_common","egg_common","egg_legendary"]) — vários
    // ovos, de tiers diferentes até, na mesma mensagem (17/08/2026).
    IReadOnlyList<string>? RewardEggKeys = null);
