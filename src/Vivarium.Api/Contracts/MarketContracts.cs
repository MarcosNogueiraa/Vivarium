using Vivarium.Core.Generation;

namespace Vivarium.Api.Contracts;

public record CreateListingRequest(long CreatureInstanceId, decimal PriceSoft);

// Seed como string: 63 bits não cabem num JSON number (double trunca acima de 2^53)
//
// Traits/BreedingSource (13/08/2026, mesmo motivo de CreatureDto): traits congelados no
// nascimento — sem eles, o cliente (traitsOf) não tem como desenhar o peixe (fica com
// creature.traits === undefined, FishCanvas quebra silenciosamente/canvas em branco).
// Achado real: essa migração adicionou os campos em CreatureDto mas não aqui, deixando
// TODA carta do Mercado sem renderizar o peixe.
public record ListingDto(
    long Id, decimal PriceSoft, long SellerId, string SellerName,
    long CreatureId, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore, bool IsBred,
    string? ParentASeed, string? ParentBSeed,
    string? ParentAGrandparentASeed, string? ParentAGrandparentBSeed,
    string? ParentBGrandparentASeed, string? ParentBGrandparentBSeed,
    CreatureTraits? Traits, IReadOnlyList<TraitGenerator.TraitSourceEntry>? BreedingSource);

/// <summary>
/// Resposta de <c>GET /api/market/listings</c> (13/08/2026) — envelope estruturado em vez de um
/// array plano: <see cref="Listings"/> é a página filtrada/ordenada (exclui o próprio vendedor,
/// que tem seu painel fixo à parte); <see cref="MyListings"/> são TODOS os anúncios ativos do
/// requisitante (sujeitos aos mesmos filtros de aparência/raridade, sem paginação — é um painel
/// de controle, não faz sentido paginar); <see cref="MyActiveListingsCount"/> é o total REAL
/// (sem filtro) do vendedor, pro contador "X/Y" bater com o limite de verdade mesmo com um
/// filtro escondendo alguns.
/// </summary>
public record MarketListingsResponse(
    IReadOnlyList<ListingDto> Listings, int TotalCount,
    IReadOnlyList<ListingDto> MyListings, int MyActiveListingsCount, int MaxActiveListings);
