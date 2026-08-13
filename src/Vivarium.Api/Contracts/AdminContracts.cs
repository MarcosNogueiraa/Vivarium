namespace Vivarium.Api.Contracts;

public record GrantPremiumRequest(decimal Amount);

public record AdjustWalletRequest(string Username, string CurrencyCode, string Mode, decimal Amount);
