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

    [Fact]
    public void Movimento_DentroDosRanges()
    {
        Assert.All(ManySeeds().Select(s => TraitGenerator.Generate(s)), t =>
        {
            Assert.InRange(t.Movement.TailSpeed, 0, 100);
            Assert.InRange(t.Movement.FinSpeed, 0, 100);
            Assert.InRange(t.Movement.TailAmplitude,
                TraitConfigV1.TailAmplitudeMin, TraitConfigV1.TailAmplitudeMax);
            Assert.InRange(t.Movement.FinAmplitude,
                TraitConfigV1.FinAmplitudeMin, TraitConfigV1.FinAmplitudeMax);
        });
    }

    [Fact]
    public void Movimento_ExtremosNaFrequenciaDaNormal()
    {
        // <10 ou >90 em normal(50,20) ≈ 4.55% dos peixes por velocidade
        var all = ManySeeds(20_000).Select(s => TraitGenerator.Generate(s)).ToList();
        double extremos = all.Count(t =>
            t.Movement.TailSpeed < TraitConfigV1.MovementSpeedExtremeLow
            || t.Movement.TailSpeed > TraitConfigV1.MovementSpeedExtremeHigh) / (double)all.Count;
        Assert.InRange(extremos, 0.035, 0.056);
    }

    [Fact]
    public void Movimento_ExtremoAumentaOScoreComPesoReduzido()
    {
        // Peixe com velocidade extrema carrega a contribuição 0.5 × -log10(P_banda)
        var comExtremo = ManySeeds(20_000).Select(s => TraitGenerator.Generate(s))
            .First(t => t.Movement.TailSpeed < TraitConfigV1.MovementSpeedExtremeLow);
        // contribuição esperada ≈ 0.5 × -log10(0.02275) ≈ 0.82; o score mínimo sem
        // movimento é ~2.6, então qualquer extremo deve passar disso
        Assert.True(comExtremo.RarityScore > 3.4);
    }

    [Fact]
    public void ShimmerLendario_DominaOScore()
    {
        // Com ShimmerScoreWeight = 2.5, o tier lendário (P=0.2%) sozinho contribui
        // 2.5 × -log10(0.002) ≈ 6.75; somado às partes (mínimo ~2.3) qualquer peixe
        // lendário passa de 8. Se o peso fosse 1, ficaria em ~5 e o teste falharia.
        var lendarios = ManySeeds(200_000)
            .Select(s => TraitGenerator.Generate(s))
            .Where(t => t.ShimmerTier == ShimmerTier.Legendary)
            .ToList();

        Assert.NotEmpty(lendarios);
        Assert.All(lendarios, t => Assert.True(t.RarityScore > 8.0,
            $"lendário deveria dominar o score, mas veio {t.RarityScore:0.00}"));
    }

    [Fact]
    public void BonusDeConjunto_Monocromatico_ReconstroiOScoreExato()
    {
        // Peixe deliberadamente simples pra tornar o score fechado e conferível:
        // sem shimmer, todas as partes sem padrão, 3 cores de base iguais e
        // velocidades não-extremas. Score = tier None (×peso) + 3×cor + 3×padrão None
        // + bônus de mesma-cor-3. Valida de uma vez: ShimmerScoreWeight, probabilidades
        // e SameColor3Bonus.
        var (seed, t) = ManySeeds(2_000_000)
            .Select(s => (s, t: TraitGenerator.Generate(s)))
            .First(x =>
                x.t.ShimmerTier == ShimmerTier.None
                && x.t.Tail.Pattern == PatternType.None
                && x.t.Dorsal.Pattern == PatternType.None
                && x.t.Pectoral.Pattern == PatternType.None
                && x.t.Tail.Color == x.t.Dorsal.Color
                && x.t.Dorsal.Color == x.t.Pectoral.Color
                && !Extreme(x.t.Movement.TailSpeed) && !Extreme(x.t.Movement.FinSpeed));

        // Probabilidade = peso / soma dos pesos da tabela (padrões não somam 100).
        double ColorP(PartColor c) => TraitConfigV1.PartColors.First(e => e.Value == c).Weight
            / TraitConfigV1.PartColors.Sum(e => e.Weight);
        double noneTierP = TraitConfigV1.ShimmerTiers.First(e => e.Value == ShimmerTier.None).Weight
            / TraitConfigV1.ShimmerTiers.Sum(e => e.Weight);
        double nonePatternP = TraitConfigV1.PatternTypes.First(e => e.Value == PatternType.None).Weight
            / TraitConfigV1.PatternTypes.Sum(e => e.Weight);

        double expected =
            TraitConfigV1.ShimmerScoreWeight * -Math.Log10(noneTierP)
            + 3 * -Math.Log10(ColorP(t.Tail.Color))
            + 3 * -Math.Log10(nonePatternP)
            + TraitConfigV1.SameColor3Bonus;

        Assert.Equal(expected, t.RarityScore, 9);

        static bool Extreme(double v) =>
            v < TraitConfigV1.MovementSpeedExtremeLow || v > TraitConfigV1.MovementSpeedExtremeHigh;
    }

    private static IEnumerable<long> ManySeeds(int count = 5_000)
        => Enumerable.Range(1, count).Select(i => (long)i * 7919);

    private static IEnumerable<PartTraits> AllParts(CreatureTraits t) => [t.Tail, t.Dorsal, t.Pectoral];
}

