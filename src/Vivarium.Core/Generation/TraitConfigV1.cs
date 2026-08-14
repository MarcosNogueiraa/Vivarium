namespace Vivarium.Core.Generation;

/// <summary>
/// Versão 1 dos pesos de trait (tabelas do CLAUDE.md, seções 2-4), hardcoded por
/// enquanto; migra para a tabela TraitWeightConfig do banco quando o backend existir.
/// CreatureInstance.TraitConfigVersion aponta para esta versão.
/// </summary>
public static class TraitConfigV1
{
    // 1 -> 2 (12/08/2026): Degradê ganhou o subtrait GradientMix e a regra de score
    // assimétrica (só a cor dominante conta fora do Even) — muda o algoritmo de um
    // trait já existente, primeiro bump real da história do projeto. Depois do
    // deploy, rodar `Vivarium.AdminReset -- diff-scores` (auditoria) e `fix-scores`
    // (agora corrigido pra usar a versão atual do motor, não a gravada na linha —
    // ver AdminReset/Program.cs) em produção.
    // 2 -> 3 (13/08/2026): cor do brilho deixou de ser uniforme/decorativa — ganhou pesos
    // desiguais por tier e passou a contribuir pro RarityScore (ShimmerColorScoreWeight).
    // NOTA IMPORTANTE (13/08/2026, traits congelados no nascimento — CLAUDE.md §8.19.2):
    // esse bump de Version só importa pra auditoria/histórico — o motor NÃO recalcula
    // traits de criaturas existentes a partir da Version gravada (TraitsJson já é a fonte
    // de verdade, congelado no nascimento). O que sincroniza criaturas antigas de verdade é
    // rodar `Vivarium.AdminReset -- backfill-traits` depois do deploy (idempotente, já é
    // parte do processo padrão).
    // 3 -> 4 (14/08/2026): ShimmerTiers.Legendary reduzido 0,2%→0,02% (1 em 5.000, pirâmide
    // "Íngreme") — muda o peso de um trait já existente. Mesma nota de 2->3 vale aqui: como os
    // traits nascem congelados (TraitsJson), esse bump só importa pra auditoria/histórico —
    // rodar `Vivarium.AdminReset -- backfill-traits` antes E depois do deploy backend (janela
    // documentada em CLAUDE.md §8.22.1) e `audit-ancestry` depois.
    public const int Version = 4;

    // 14/08/2026: as faixas de EXIBIÇÃO (BANDS, score-based) já são recortadas por percentil —
    // por construção, sempre produzem exatamente a % alvo da pirâmide (Comum/Incomum/Raro/Épico/
    // Lendário), não importa o peso aqui. O que este peso controla é a chance REAL de sortear
    // brilho Legendary do zero — decisão do usuário: manter o caminho de cruzamento (mesma cor/
    // padrão nas 3 partes, já favorecido pelo viés de raridade da herança) como rota legítima e
    // igualmente válida até o topo, então só o Legendary foi reduzido (0,2%→0,02%, mesma conta
    // "1.000 jogadores × 1 peixe/h ÷ 5.000 ≈ 5 lendários/dia no servidor inteiro" usada pra
    // decidir o alvo) — Subtle/Vibrant/Rare/None ficam como estavam, sem necessidade de mexer.
    public static readonly IReadOnlyList<WeightedValue<ShimmerTier>> ShimmerTiers =
    [
        new(ShimmerTier.None, 78.0),
        new(ShimmerTier.Subtle, 15.0),
        new(ShimmerTier.Vibrant, 5.5),
        new(ShimmerTier.Rare, 1.3),
        new(ShimmerTier.Legendary, 0.02),
    ];

    // 13/08/2026: mais cores por tier + peso desigual dentro do tier (antes era sorteio
    // uniforme, nunca contribuía pro score) — ver ShimmerColorScoreWeight abaixo. Pesos
    // front-loaded, mesma filosofia de PartColors (a cor mais comum do tier domina, as
    // novas entram como opções mais raras).
    public static readonly IReadOnlyDictionary<ShimmerTier, IReadOnlyList<WeightedValue<ShimmerColor>>> ShimmerColorsByTier =
        new Dictionary<ShimmerTier, IReadOnlyList<WeightedValue<ShimmerColor>>>
        {
            [ShimmerTier.Subtle] =
            [
                new(ShimmerColor.Gold, 25.0), new(ShimmerColor.Silver, 22.0), new(ShimmerColor.Bluish, 20.0),
                new(ShimmerColor.Copper, 15.0), new(ShimmerColor.Bronze, 10.0), new(ShimmerColor.Pearl, 8.0),
            ],
            [ShimmerTier.Vibrant] =
            [
                new(ShimmerColor.Emerald, 30.0), new(ShimmerColor.Purple, 25.0), new(ShimmerColor.Pink, 20.0),
                new(ShimmerColor.Turquoise, 15.0), new(ShimmerColor.Amber, 10.0),
            ],
            [ShimmerTier.Rare] =
            [
                new(ShimmerColor.Rainbow, 40.0), new(ShimmerColor.AbsoluteBlack, 32.0),
                new(ShimmerColor.Crimson, 18.0), new(ShimmerColor.SteelBlue, 10.0),
            ],
            [ShimmerTier.Legendary] =
            [
                new(ShimmerColor.Iridescent, 65.0), new(ShimmerColor.Aurora, 35.0),
            ],
        };

    /// <summary>
    /// Peso da contribuição da cor-dentro-do-tier pro RarityScore (13/08/2026) — antes a cor
    /// era pura decoração; agora soma `ShimmerColorScoreWeight * -log10(P)`, mesma mecânica de
    /// PartColor. Peso 1.0 (não amplificado como o tier, que já pesa 2.5×) — a cor é uma
    /// nuance dentro de um tier já raro, não deveria dominar o score sozinha.
    /// </summary>
    public const double ShimmerColorScoreWeight = 1.0;

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
    // Gradient: peso reduzido de 0.6 -> 0.4 (12/08/2026, calibração em andamento —
    // ver GradientMixRatios abaixo), delta de 0.2pp somado em None (não nos outros
    // padrões raros, pra não baratear Mármore/Ocelo sem querer).
    public static readonly IReadOnlyList<WeightedValue<PatternType>> PatternTypes =
    [
        new(PatternType.None, 76.2),
        new(PatternType.Stripe, 8.0),
        new(PatternType.Dot, 8.0),
        new(PatternType.Scales, 3.0),
        new(PatternType.Rays, 1.6),
        new(PatternType.Chevron, 1.2),
        new(PatternType.Net, 0.9),
        new(PatternType.Gradient, 0.4),
        new(PatternType.Mottled, 0.35),
        new(PatternType.Ocellus, 0.2),
        new(PatternType.Marble, 0.05),
    ];

    // Mistura de cores do Degradê (12/08/2026): 3 subtipos por proporção
    // base/padrão. Even (50/50) é o mais raro — as duas cores contam pro score
    // nesse caso; nos assimétricos, só a cor dominante conta (ver TraitGenerator).
    // Candidato inicial pra calibração via Vivarium.Simulation — não travado ainda.
    public static readonly IReadOnlyList<WeightedValue<GradientMix>> GradientMixRatios =
    [
        new(GradientMix.BaseDominant, 45.0),
        new(GradientMix.Even, 10.0),
        new(GradientMix.PatternDominant, 45.0),
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

    /// <summary>
    /// Desvio-padrão do jitter aplicado a tamanho/opacidade de padrão HERDADOS (13/08/2026,
    /// pedido do usuário) — sem isso, o filhote copiava o valor exato do pai (ex: pai com
    /// tamanho 90 sempre passava 90,000...). ~25% do espalhamento natural de cada trait
    /// (desvio 20 do tamanho; opacidade uniforme 20-90 tem espalhamento equivalente
    /// ~20 = (max-min)/√12) — variação perceptível sem "quebrar" a herança: um pai com
    /// padrão extremo ainda faz o filho nascer quase sempre extremo, só não é mais clone
    /// exato. Só se aplica quando o subtrait vem de herança — mutação já é sorteio livre.
    /// </summary>
    public const double PatternSubtraitInheritJitterStdDev = 5.0;

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

    /// <summary>
    /// Mesmo princípio do jitter de padrão (13/08/2026) — velocidade/amplitude de nado
    /// herdadas também copiavam o valor exato de um dos pais. Velocidade usa a mesma
    /// escala/desvio de PatternSize (normal 0-100, dp 20), então reaproveita o mesmo
    /// sigma=5 (~25% do espalhamento natural). Amplitude é uniforme numa faixa bem menor
    /// (~0.55-0.60) — 0.04 mantém a mesma proporção (~25% do espalhamento uniforme
    /// equivalente, faixa/√12 ≈ 0.16-0.17).
    /// </summary>
    public const double MovementSpeedInheritJitterStdDev = 5.0;
    public const double MovementAmplitudeInheritJitterStdDev = 0.04;

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
        // Cores novas de tier ≥ Vibrante (13/08/2026) — as 3 novas de Sutil (Copper/Bronze/
        // Pearl) não precisam de entrada aqui, a correlação nunca olha esse tier.
        ShimmerColor.Turquoise => PartColor.Blue,
        ShimmerColor.Amber => PartColor.Orange,
        ShimmerColor.Crimson => PartColor.Red,
        ShimmerColor.SteelBlue => PartColor.Blue,
        ShimmerColor.Aurora => PartColor.PureWhite,
        _ => throw new ArgumentOutOfRangeException(nameof(shimmer)),
    };
}
