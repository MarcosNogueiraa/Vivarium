using Vivarium.Core.Domain;

namespace Vivarium.Api.Contracts;

/// <summary>
/// Criatura exposta na API. Seed vai como string: 63 bits não cabem num JSON number
/// (double trunca acima de 2^53). Use <see cref="From"/> pra mapear da entidade.
/// </summary>
public record CreatureDto(
    long Id, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore, DateTime CreatedAt, bool IsBred)
{
    public static CreatureDto From(CreatureInstance c) =>
        new(c.Id, c.SpeciesId, c.Seed.ToString(), c.TraitConfigVersion, c.RarityScore, c.CreatedAt, c.ParentAId.HasValue);
}
