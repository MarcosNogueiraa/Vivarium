namespace Vivarium.Api.Contracts;

public record ItemDto(string Key, string Name, string Category, decimal Price, bool Owned);
