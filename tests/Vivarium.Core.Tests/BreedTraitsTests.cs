using Vivarium.Core.Generation;

namespace Vivarium.Core.Tests;

public class BreedTraitsTests
{
    private const long ParentASeed = 111 * 7919;
    private const long ParentBSeed = 222 * 7919;

    [Fact]
    public void MesmoSeeds_SempreProduzOMesmoResultado()
    {
        for (long childSeed = 1; childSeed <= 200; childSeed++)
        {
            var x = TraitGenerator.BreedTraits(childSeed, ParentASeed, ParentBSeed, TraitConfigV1.Version, 0.08);
            var y = TraitGenerator.BreedTraits(childSeed, ParentASeed, ParentBSeed, TraitConfigV1.Version, 0.08);
            Assert.Equal(x, y);
        }
    }

    [Fact]
    public void MutationChance1_IgualAGerarDoZeroComOProprioSeedDoFilho()
    {
        // Com mutationChance=1.0 todo trait é sorteado do zero pelas mesmas tabelas/
        // salts que Generate usa — os pais viram irrelevantes e o resultado deve ser
        // idêntico a Generate(childSeed). Isso valida que toda a árvore de salts do
        // breeding está corretamente alinhada com a do motor normal.
        foreach (long childSeed in ManySeeds(300))
        {
            var bred = TraitGenerator.BreedTraits(childSeed, ParentASeed, ParentBSeed, TraitConfigV1.Version, 1.0);
            var generated = TraitGenerator.Generate(childSeed);
            Assert.Equal(generated, bred);
        }
    }

    [Fact]
    public void SemMutacao_TraitsDeTopoSempreVemDeUmDosPais()
    {
        foreach (var (parentA, parentB) in ManyParentPairs(80))
        {
            var a = TraitGenerator.Generate(parentA);
            var b = TraitGenerator.Generate(parentB);

            foreach (long childSeed in ManySeeds(20))
            {
                var child = TraitGenerator.BreedTraits(childSeed, parentA, parentB, TraitConfigV1.Version, 0.0);

                Assert.True(child.ShimmerTier == a.ShimmerTier || child.ShimmerTier == b.ShimmerTier);

                foreach (var (childPart, partA, partB) in new[]
                {
                    (child.Tail, a.Tail, b.Tail),
                    (child.Dorsal, a.Dorsal, b.Dorsal),
                    (child.Pectoral, a.Pectoral, b.Pectoral),
                })
                {
                    Assert.True(childPart.Color == partA.Color || childPart.Color == partB.Color);
                    Assert.True(childPart.Pattern == partA.Pattern || childPart.Pattern == partB.Pattern);
                    if (childPart.Pattern != PatternType.None)
                    {
                        // Invariante sempre válida (herdada ou não): padrão nunca tem a
                        // mesma cor da base do FILHO — por isso o subtrait às vezes cai
                        // num sorteio fresco mesmo sem mutação (colisão rara de cor).
                        Assert.NotEqual(childPart.Color, childPart.PatternColor);
                        Assert.InRange(childPart.PatternSize!.Value, 0, 100);
                        Assert.InRange(childPart.PatternOpacity!.Value,
                            TraitConfigV1.PatternOpacityMin, TraitConfigV1.PatternOpacityMax);
                    }
                }

                // Movimento (sem condicional de subtrait) sempre bate com um dos pais.
                Assert.True(child.Movement.TailSpeed == a.Movement.TailSpeed || child.Movement.TailSpeed == b.Movement.TailSpeed);
                Assert.True(child.Movement.FinSpeed == a.Movement.FinSpeed || child.Movement.FinSpeed == b.Movement.FinSpeed);
                Assert.True(child.Movement.TailAmplitude == a.Movement.TailAmplitude || child.Movement.TailAmplitude == b.Movement.TailAmplitude);
                Assert.True(child.Movement.FinAmplitude == a.Movement.FinAmplitude || child.Movement.FinAmplitude == b.Movement.FinAmplitude);
            }
        }
    }

    [Fact]
    public void TaxaDeMutacao_BateComOParametroDentroDaTolerancia()
    {
        // Pais com tiers distintos e incomuns (Subtle=15%, Rare=1.3%): a chance de um
        // sorteio fresco (mutação) coincidir com qualquer um dos dois é baixa (~16.3%),
        // então a fração de filhos com tier fora de {Subtle,Rare} aproxima bem
        // mutationChance × (1 − 0.163).
        long parentA = FindSeedWithTier(ShimmerTier.Subtle);
        long parentB = FindSeedWithTier(ShimmerTier.Rare);
        const double mutationChance = 0.5;
        const int n = 30_000;

        int foraDosPais = 0;
        for (long childSeed = 1; childSeed <= n; childSeed++)
        {
            var tier = TraitGenerator.BreedTraits(childSeed, parentA, parentB, TraitConfigV1.Version, mutationChance).ShimmerTier;
            if (tier != ShimmerTier.Subtle && tier != ShimmerTier.Rare)
                foraDosPais++;
        }

        double matchProbability = 0.15 + 0.013; // pesos de Subtle e Rare em TraitConfigV1
        double expected = mutationChance * (1 - matchProbability);
        Assert.InRange(foraDosPais / (double)n, expected - 0.03, expected + 0.03);
    }

    [Fact]
    public void RarityScore_RecalculadoViaProbabilidadeDosValoresReais_NaoUmaConstante()
    {
        // Não dá pra afirmar "nunca bate com o score de um pai" (contribuições de
        // tier/padrão None são constantes globais e coincidem com frequência) — o
        // teste real de "não é cópia" é MutationChance1_IgualAGerarDoZeroComOProprioSeedDoFilho,
        // que já prova que o score é recomputado pela mesma engine de Generate.
        // Aqui só valida que o score varia com os traits reais (não é sempre a mesma
        // constante nem sempre a média fixa dos pais).
        var scores = new HashSet<double>();
        for (long childSeed = 1; childSeed <= 500; childSeed++)
        {
            long parentA = childSeed * 13 + 1;
            long parentB = childSeed * 13 + 2;
            var child = TraitGenerator.BreedTraits(childSeed, parentA, parentB, TraitConfigV1.Version, 0.08);
            scores.Add(Math.Round(child.RarityScore, 6));
        }
        Assert.True(scores.Count > 50, $"esperava boa variação de score, só {scores.Count} valores distintos em 500 filhos");
    }

    [Fact]
    public void FabricaDeLendarios_NaoInflacionaAcimaDoBaseline()
    {
        // População base (não selecionada por raridade): pares aleatórios cruzados
        // com a chance de mutação default não devem produzir legendário muito acima
        // do baseline populacional (~0.2%, seção 5 do CLAUDE.md) — checagem de
        // sanidade, tolerância larga (não é trava rígida de produto).
        const int n = 30_000;
        int legendarios = 0;
        for (long childSeed = 1; childSeed <= n; childSeed++)
        {
            long parentA = childSeed * 97 + 1;
            long parentB = childSeed * 97 + 2;
            var child = TraitGenerator.BreedTraits(childSeed, parentA, parentB, TraitConfigV1.Version, BreedTraitsDefaultMutation);
            if (child.ShimmerTier == ShimmerTier.Legendary)
                legendarios++;
        }
        Assert.InRange(legendarios / (double)n, 0, 0.01); // até ~5x o baseline de 0.2%
    }

    private const double BreedTraitsDefaultMutation = 0.08;

    private static long FindSeedWithTier(ShimmerTier tier)
        => ManySeeds(50_000).First(s => TraitGenerator.Generate(s).ShimmerTier == tier);

    private static IEnumerable<long> ManySeeds(int count = 5_000)
        => Enumerable.Range(1, count).Select(i => (long)i * 7919);

    private static IEnumerable<(long A, long B)> ManyParentPairs(int count)
        => Enumerable.Range(1, count).Select(i => ((long)i * 6997, (long)i * 6997 + 3491));
}
