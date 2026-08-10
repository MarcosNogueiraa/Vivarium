using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Services;

/// <summary>
/// Cor da cauda é derivada do seed (imutável e determinística), mas gerar os traits
/// inteiros custa vários SHA256. Cacheamos por id de criatura pra não recalcular a
/// cada tick (chave por id, não por seed, porque filhotes precisam da ancestralidade
/// inteira pra derivar a cor certa — ver abaixo). Extraído de GameService (09/08/2026)
/// pra ser reaproveitado pelo LeaderboardService sem duplicar a lógica.
/// </summary>
public static class TailColorResolver
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, PartColor> Cache = new();

    /// <summary>
    /// Bug real corrigido (08/08/2026, reportado pelo usuário como "prejuízo mesmo com
    /// água 100%"): pra filhotes, isso sempre chamava `TraitGenerator.Generate(seed)`
    /// puro, ignorando a herança — o cliente (frontend/lib/generator.js `traitsOf`) já
    /// usava `breedTraits` corretamente pro filhote, então a cor de cauda calculada aqui
    /// podia divergir da cor exibida na tela. Como a cor de cauda decide o agrupamento de
    /// sinergia (`SynergyMultiplier`), essa divergência mudava o `coinsPerHour` de
    /// verdade (não só a exibição) sempre que o tanque tinha filhotes — parecia "água
    /// suja" porque `tankPotential()` (cliente, cor correta) e `coinsPerHour` (servidor,
    /// cor errada) caíam em grupos de sinergia diferentes, mesmo a 100% de água.
    /// </summary>
    public static PartColor Of(CreatureInstance c)
        => Cache.GetOrAdd(c.Id, _ =>
        {
            if (c.ParentASeed is { } parentASeed && c.ParentBSeed is { } parentBSeed)
            {
                var ancestryA = new TraitGenerator.ParentAncestry(parentASeed, c.ParentAGrandparentASeed, c.ParentAGrandparentBSeed);
                var ancestryB = new TraitGenerator.ParentAncestry(parentBSeed, c.ParentBGrandparentASeed, c.ParentBGrandparentBSeed);
                return TraitGenerator.BreedTraits(
                    c.Seed, ancestryA, ancestryB, c.TraitConfigVersion,
                    BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength, BreedingDefaults.GrandparentReachChance
                ).Tail.Color;
            }
            return TraitGenerator.Generate(c.Seed, c.TraitConfigVersion).Tail.Color;
        });
}
