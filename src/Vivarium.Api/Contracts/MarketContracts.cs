namespace Vivarium.Api.Contracts;

public record CreateListingRequest(long CreatureInstanceId, decimal PriceSoft);

// Seed como string: 63 bits não cabem num JSON number (double trunca acima de 2^53)
public record ListingDto(
    long Id, decimal PriceSoft, long SellerId, string SellerName,
    long CreatureId, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore, bool IsBred);
