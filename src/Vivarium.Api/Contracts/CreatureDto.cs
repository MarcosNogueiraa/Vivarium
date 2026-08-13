using Vivarium.Core.Domain;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Contracts;

/// <summary>
/// Criatura exposta na API. Seed vai como string: 63 bits não cabem num JSON number
/// (double trunca acima de 2^53). Use <see cref="From"/> pra mapear da entidade.
///
/// <paramref name="Traits"/>/<paramref name="BreedingSource"/> (13/08/2026): traits já
/// congelados no nascimento (<see cref="CreatureInstance.TraitsJson"/>) — o cliente lê direto
/// daqui pra exibir o peixe, sem recalcular nada do <see cref="Seed"/>. `BreedingSource` é null
/// pra peixe fresco ou pra filhote nascido antes desta migração (backfill não reconstrói a
/// origem por trait, só os valores finais).
/// </summary>
public record CreatureDto(
    long Id, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore, DateTime CreatedAt, bool IsBred,
    string? ParentASeed, string? ParentBSeed, int BreedCount,
    string? ParentAGrandparentASeed, string? ParentAGrandparentBSeed,
    string? ParentBGrandparentASeed, string? ParentBGrandparentBSeed,
    CreatureTraits? Traits, IReadOnlyList<TraitGenerator.TraitSourceEntry>? BreedingSource,
    bool IsNew)
{
    public static CreatureDto From(CreatureInstance c) =>
        new(c.Id, c.SpeciesId, c.Seed.ToString(), c.TraitConfigVersion, c.RarityScore, c.CreatedAt, c.ParentAId.HasValue,
            c.ParentASeed?.ToString(), c.ParentBSeed?.ToString(), c.BreedCount,
            c.ParentAGrandparentASeed?.ToString(), c.ParentAGrandparentBSeed?.ToString(),
            c.ParentBGrandparentASeed?.ToString(), c.ParentBGrandparentBSeed?.ToString(),
            c.TraitsJson is not null ? TraitsSerialization.DeserializeTraits(c.TraitsJson) : null,
            c.BreedingSourceJson is not null ? TraitsSerialization.DeserializeSource(c.BreedingSourceJson) : null,
            c.IsNew);
}
