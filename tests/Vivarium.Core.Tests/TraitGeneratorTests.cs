using Vivarium.Core.Generation;

namespace Vivarium.Core.Tests;

public class TraitGeneratorTests
{
    [Fact]
    public void MesmoSeed_SempreProduzOsMesmosTraits()
    {
        for (long seed = 1; seed <= 200; seed++)
        {
            var a = TraitGenerator.Generate(seed);
            var b = TraitGenerator.Generate(seed);
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void VersaoDeConfigDesconhecida_Lanca()
    {
        Assert.Throws<ArgumentException>(() => TraitGenerator.Generate(42, configVersion: 999));
    }

    [Fact]
    public void CorpoSemBrilho_NaoTemCorNemOpacidade()
    {
        var semBrilho = ManySeeds().Select(s => TraitGenerator.Generate(s))
            .Where(t => t.ShimmerTier == ShimmerTier.None);

        Assert.All(semBrilho, t =>
        {
            Assert.Null(t.ShimmerColor);
            Assert.Equal(0, t.ShimmerOpacity);
        });
    }

    [Fact]
    public void CorpoComBrilho_TemCorDoTierEOpacidadeNaFaixa()
    {
        var comBrilho = ManySeeds().Select(s => TraitGenerator.Generate(s))
            .Where(t => t.ShimmerTier != ShimmerTier.None).ToList();

        Assert.NotEmpty(comBrilho);
        Assert.All(comBrilho, t =>
        {
            Assert.NotNull(t.ShimmerColor);
            Assert.Contains(t.ShimmerColor!.Value, TraitConfigV1.ShimmerColorsByTier[t.ShimmerTier]);
            var (min, max) = TraitConfigV1.ShimmerOpacityByTier[t.ShimmerTier];
            Assert.InRange(t.ShimmerOpacity, min, max);
        });
    }

    [Fact]
    public void CorDoPadrao_NuncaIgualACorDaBase()
    {
        foreach (var traits in ManySeeds().Select(s => TraitGenerator.Generate(s)))
        {
            foreach (var part in AllParts(traits))
            {
                if (part.Pattern == PatternType.None)
                {
                    Assert.Null(part.PatternColor);
                    Assert.Null(part.PatternSize);
                    Assert.Null(part.PatternOpacity);
                }
                else
                {
                    Assert.NotNull(part.PatternColor);
                    Assert.NotEqual(part.Color, part.PatternColor);
                    Assert.InRange(part.PatternSize!.Value, 0, 100);
                    Assert.InRange(part.PatternOpacity!.Value,
                        TraitConfigV1.PatternOpacityMin, TraitConfigV1.PatternOpacityMax);
                }
            }
        }
    }

    [Fact]
    public void RarityScore_SemprePositivoEFinito()
    {
        Assert.All(ManySeeds().Select(s => TraitGenerator.Generate(s)), t =>
        {
            Assert.True(double.IsFinite(t.RarityScore));
            Assert.True(t.RarityScore > 0);
        });
    }

    [Fact]
    public void DistribuicaoDeShimmer_BateComOsPesos()
    {
        const int n = 50_000;
        var counts = new Dictionary<ShimmerTier, int>();
        for (long seed = 1; seed <= n; seed++)
        {
            var tier = TraitGenerator.Generate(seed).ShimmerTier;
            counts[tier] = counts.GetValueOrDefault(tier) + 1;
        }

        // Tolerância folgada: valida a ordem de grandeza, não o valor exato.
        Assert.InRange(counts[ShimmerTier.None] / (double)n, 0.76, 0.80);
        Assert.InRange(counts[ShimmerTier.Subtle] / (double)n, 0.13, 0.17);
        Assert.InRange(counts[ShimmerTier.Vibrant] / (double)n, 0.045, 0.065);
        Assert.InRange(counts[ShimmerTier.Rare] / (double)n, 0.009, 0.017);
        Assert.InRange(counts[ShimmerTier.Legendary] / (double)n, 0.001, 0.0035);
    }

    [Fact]
    public void CorrelacaoTier2Mais_AumentaFrequenciaDaCorCombinando()
    {
        // Corpo preto absoluto (Tier 3) deve puxar "Black" nas partes de ~3% para ~18%.
        var pretos = ManySeeds(200_000)
            .Select(s => TraitGenerator.Generate(s))
            .Where(t => t.ShimmerColor == ShimmerColor.AbsoluteBlack)
            .ToList();

        Assert.NotEmpty(pretos);
        double freqBlack = pretos.SelectMany(AllParts).Count(p => p.Color == PartColor.Black)
            / (double)(pretos.Count * 3);
        Assert.InRange(freqBlack, 0.13, 0.23);
    }

    private static IEnumerable<long> ManySeeds(int count = 5_000)
        => Enumerable.Range(1, count).Select(i => (long)i * 7919);

    private static IEnumerable<PartTraits> AllParts(CreatureTraits t) => [t.Tail, t.Dorsal, t.Pectoral];
}

