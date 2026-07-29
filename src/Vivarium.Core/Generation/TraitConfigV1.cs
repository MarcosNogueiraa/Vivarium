namespace Vivarium.Core.Generation;

/// <summary>
/// Versão 1 dos pesos de trait (tabelas do CLAUDE.md, seções 2-4), hardcoded por
/// enquanto; migra para a tabela TraitWeightConfig do banco quando o backend existir.
/// CreatureInstance.TraitConfigVersion aponta para esta versão.
/// </summary>
public static class TraitConfigV1
{
    public const int Version = 1;

    public static readonly IReadOnlyList<WeightedValue<ShimmerTier>> ShimmerTiers =
    [
        new(ShimmerTier.None, 78.0),
        new(ShimmerTier.Subtle, 15.0),
        new(ShimmerTier.Vibrant, 5.5),
        new(ShimmerTier.Rare, 1.3),
        new(ShimmerTier.Legendary, 0.2),
    ];

    public static readonly IReadOnlyDictionary<ShimmerTier, ShimmerColor[]> ShimmerColorsByTier =
        new Dictionary<ShimmerTier, ShimmerColor[]>
        {
            [ShimmerTier.Subtle] = [ShimmerColor.Gold, ShimmerColor.Silver, ShimmerColor.Bluish],
            [ShimmerTier.Vibrant] = [ShimmerColor.Emerald, ShimmerColor.Purple, ShimmerColor.Pink],
            [ShimmerTier.Rare] = [ShimmerColor.Rainbow, ShimmerColor.AbsoluteBlack],
            [ShimmerTier.Legendary] = [ShimmerColor.Iridescent],
        };

    public static readonly IReadOnlyDictionary<ShimmerTier, (double Min, double Max)> ShimmerOpacityByTier =
        new Dictionary<ShimmerTier, (double, double)>
        {
            [ShimmerTier.Subtle] = (10, 25),
            [ShimmerTier.Vibrant] = (30, 50),
            [ShimmerTier.Rare] = (55, 75),
            [ShimmerTier.Legendary] = (80, 100),
        };

    public static readonly IReadOnlyList<WeightedValue<PartColor>> PartColors =
    [
        new(PartColor.Orange, 22.0),
        new(PartColor.Blue, 20.0),
        new(PartColor.Red, 18.0),
        new(PartColor.Yellow, 16.0),
        new(PartColor.Green, 14.0),
        new(PartColor.Purple, 6.0),
        new(PartColor.Black, 3.0),
        new(PartColor.PureWhite, 1.0),
    ];

    // Padrões mais raros no geral (None domina mais); novos padrões com pesos
    // baixos (raros = valiosos). Ocelo/Mármore são a caça top.
    public static readonly IReadOnlyList<WeightedValue<PatternType>> PatternTypes =
    [
        new(PatternType.None, 76.0),
        new(PatternType.Stripe, 8.0),
        new(PatternType.Dot, 8.0),
        new(PatternType.Scales, 3.0),
        new(PatternType.Rays, 1.6),
        new(PatternType.Chevron, 1.2),
        new(PatternType.Net, 0.9),
        new(PatternType.Gradient, 0.6),
        new(PatternType.Mottled, 0.35),
        new(PatternType.Ocellus, 0.2),
        new(PatternType.Marble, 0.05),
    ];

    /// <summary>Corpo (shimmer) é a área de destaque: sua contribuição no score é multiplicada.</summary>
    public const double ShimmerScoreWeight = 2.5;

    // Bônus de conjunto coeso (correlação entre as 3 partes), somado ao score.
    public const double SamePattern2Bonus = 1.0;   // mesmo padrão (≠None) em 2 partes
    public const double SamePattern3Bonus = 2.5;   // ...nas 3
    public const double SameColor2Bonus = 0.8;     // mesma cor de base em 2 partes
    public const double SameColor3Bonus = 2.0;     // ...nas 3 (monocromático)

    /// <summary>Pontos percentuais somados à cor correlacionada quando o corpo é Tier 2+.</summary>
    public const double CorrelationBoostPoints = 15.0;

    // Tamanho do padrão: normal(50, 20) clampada em [0,100]; extremos <10 ou >90 (~2.3% cada).
    public const double PatternSizeMean = 50.0;
    public const double PatternSizeStdDev = 20.0;
    public const double PatternSizeExtremeLow = 10.0;
    public const double PatternSizeExtremeHigh = 90.0;

    // Opacidade do padrão: uniforme 20-90; extremos <30 ou >80.
    public const double PatternOpacityMin = 20.0;
    public const double PatternOpacityMax = 90.0;
    public const double PatternOpacityExtremeLow = 30.0;
    public const double PatternOpacityExtremeHigh = 80.0;

    // Movimento (ranges calibrados visualmente no protótipo):
    // velocidades 0-100 em normal(50,20); extremos <10 ou >90 entram no score
    // com peso reduzido. Amplitudes uniformes, só estética (fora do score).
    public const double MovementSpeedMean = 50.0;
    public const double MovementSpeedStdDev = 20.0;
    public const double MovementSpeedExtremeLow = 10.0;
    public const double MovementSpeedExtremeHigh = 90.0;
    public const double MovementScoreWeight = 0.5;
    public const double TailAmplitudeMin = 0.20;
    public const double TailAmplitudeMax = 0.75;
    public const double FinAmplitudeMin = 0.15;
    public const double FinAmplitudeMax = 0.75;

    /// <summary>Cor da paleta mais próxima do tom do brilho, para a regra de correlação.</summary>
    public static PartColor ClosestPartColor(ShimmerColor shimmer) => shimmer switch
    {
        ShimmerColor.Gold => PartColor.Yellow,
        ShimmerColor.Silver => PartColor.PureWhite,
        ShimmerColor.Bluish => PartColor.Blue,
        ShimmerColor.Emerald => PartColor.Green,
        ShimmerColor.Purple => PartColor.Purple,
        ShimmerColor.Pink => PartColor.Red,
        ShimmerColor.Rainbow => PartColor.PureWhite,
        ShimmerColor.AbsoluteBlack => PartColor.Black,
        ShimmerColor.Iridescent => PartColor.PureWhite,
        _ => throw new ArgumentOutOfRangeException(nameof(shimmer)),
    };
}
