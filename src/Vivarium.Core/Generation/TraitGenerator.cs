namespace Vivarium.Core.Generation;

/// <summary>
/// Motor determinístico seed → traits. Todo trait é derivado de Hash(seed, salt)
/// independente, então o mesmo seed sempre produz o mesmo peixe, e traits novos
/// podem ser adicionados sem alterar os existentes.
/// </summary>
public static class TraitGenerator
{
    public static CreatureTraits Generate(long seed, int configVersion = TraitConfigV1.Version)
    {
        if (configVersion != TraitConfigV1.Version)
            throw new ArgumentException($"Versão de config desconhecida: {configVersion}", nameof(configVersion));

        // Acumula -log10(P) de cada trait sorteado; soma final = RarityScore.
        double score = 0;

        // Corpo é a área de destaque: contribuição do tier multiplicada por ShimmerScoreWeight.
        var (tier, tierP) = WeightedTable.Pick(TraitConfigV1.ShimmerTiers, DeterministicHash.Roll01(seed, "body_shimmer"));
        score += TraitConfigV1.ShimmerScoreWeight * SelfInformation(tierP);

        ShimmerColor? shimmerColor = null;
        double shimmerOpacity = 0;
        if (tier != ShimmerTier.None)
        {
            var palette = TraitConfigV1.ShimmerColorsByTier[tier];
            shimmerColor = palette[(int)(DeterministicHash.Roll01(seed, "body_shimmer_color") * palette.Length)];

            var (min, max) = TraitConfigV1.ShimmerOpacityByTier[tier];
            shimmerOpacity = min + DeterministicHash.Roll01(seed, "body_shimmer_opacity") * (max - min);
        }

        // Regra de correlação: corpo Tier 2+ dá +15pp à cor de parte mais próxima do brilho.
        PartColor? boosted = tier >= ShimmerTier.Vibrant && shimmerColor is { } sc
            ? TraitConfigV1.ClosestPartColor(sc)
            : null;
        var partColorTable = ApplyCorrelation(TraitConfigV1.PartColors, boosted);

        var tail = GeneratePart(seed, "tail", partColorTable, ref score);
        var dorsal = GeneratePart(seed, "dorsal", partColorTable, ref score);
        var pectoral = GeneratePart(seed, "pectoral", partColorTable, ref score);

        // Bônus de conjunto coeso: mesmo padrão (≠None) / mesma cor entre as partes.
        score += SetBonus(tail, dorsal, pectoral);

        // Movimento: velocidades em normal(50,20), extremos raros entram no
        // score com peso reduzido; amplitudes uniformes ficam fora do score
        double tailSpeed = NormalPick(seed, "tail_speed",
            TraitConfigV1.MovementSpeedMean, TraitConfigV1.MovementSpeedStdDev);
        score += MovementExtremeInfo(tailSpeed);
        double tailAmplitude = TraitConfigV1.TailAmplitudeMin
            + DeterministicHash.Roll01(seed, "tail_wag_amplitude")
            * (TraitConfigV1.TailAmplitudeMax - TraitConfigV1.TailAmplitudeMin);

        double finSpeed = NormalPick(seed, "fin_speed",
            TraitConfigV1.MovementSpeedMean, TraitConfigV1.MovementSpeedStdDev);
        score += MovementExtremeInfo(finSpeed);
        double finAmplitude = TraitConfigV1.FinAmplitudeMin
            + DeterministicHash.Roll01(seed, "fin_wag_amplitude")
            * (TraitConfigV1.FinAmplitudeMax - TraitConfigV1.FinAmplitudeMin);

        var movement = new MovementTraits(tailSpeed, tailAmplitude, finSpeed, finAmplitude);

        return new CreatureTraits(tier, shimmerColor, shimmerOpacity, tail, dorsal, pectoral, movement, score);
    }

    private static double MovementExtremeInfo(double speed)
    {
        double probability;
        if (speed < TraitConfigV1.MovementSpeedExtremeLow)
            probability = NormalCdf(TraitConfigV1.MovementSpeedExtremeLow,
                TraitConfigV1.MovementSpeedMean, TraitConfigV1.MovementSpeedStdDev);
        else if (speed > TraitConfigV1.MovementSpeedExtremeHigh)
            probability = 1 - NormalCdf(TraitConfigV1.MovementSpeedExtremeHigh,
                TraitConfigV1.MovementSpeedMean, TraitConfigV1.MovementSpeedStdDev);
        else
            return 0;
        return TraitConfigV1.MovementScoreWeight * SelfInformation(probability);
    }

    private static PartTraits GeneratePart(
        long seed, string partSalt, IReadOnlyList<WeightedValue<PartColor>> colorTable, ref double score)
    {
        var (color, colorP) = WeightedTable.Pick(colorTable, DeterministicHash.Roll01(seed, partSalt + "_color"));
        score += SelfInformation(colorP);

        var (pattern, patternP) = WeightedTable.Pick(TraitConfigV1.PatternTypes, DeterministicHash.Roll01(seed, partSalt + "_pattern"));
        score += SelfInformation(patternP);

        if (pattern == PatternType.None)
            return new PartTraits(color, pattern, null, null, null);

        // Cor do padrão: mesma paleta, nunca igual à cor de base da parte.
        var patternPalette = TraitConfigV1.PartColors.Where(e => e.Value != color).ToArray();
        var (patternColor, patternColorP) = WeightedTable.Pick(patternPalette, DeterministicHash.Roll01(seed, partSalt + "_pattern_color"));
        score += SelfInformation(patternColorP);

        double size = NormalPick(seed, partSalt + "_pattern_size",
            TraitConfigV1.PatternSizeMean, TraitConfigV1.PatternSizeStdDev);
        if (size < TraitConfigV1.PatternSizeExtremeLow)
            score += SelfInformation(NormalCdf(TraitConfigV1.PatternSizeExtremeLow,
                TraitConfigV1.PatternSizeMean, TraitConfigV1.PatternSizeStdDev));
        else if (size > TraitConfigV1.PatternSizeExtremeHigh)
            score += SelfInformation(1 - NormalCdf(TraitConfigV1.PatternSizeExtremeHigh,
                TraitConfigV1.PatternSizeMean, TraitConfigV1.PatternSizeStdDev));

        double opacity = TraitConfigV1.PatternOpacityMin
            + DeterministicHash.Roll01(seed, partSalt + "_pattern_opacity")
            * (TraitConfigV1.PatternOpacityMax - TraitConfigV1.PatternOpacityMin);
        double opacityRange = TraitConfigV1.PatternOpacityMax - TraitConfigV1.PatternOpacityMin;
        if (opacity < TraitConfigV1.PatternOpacityExtremeLow)
            score += SelfInformation((TraitConfigV1.PatternOpacityExtremeLow - TraitConfigV1.PatternOpacityMin) / opacityRange);
        else if (opacity > TraitConfigV1.PatternOpacityExtremeHigh)
            score += SelfInformation((TraitConfigV1.PatternOpacityMax - TraitConfigV1.PatternOpacityExtremeHigh) / opacityRange);

        return new PartTraits(color, pattern, patternColor, size, opacity);
    }

    /// <summary>Bônus por conjunto coeso: mesmo padrão (≠None) ou mesma cor de base entre as 3 partes.</summary>
    private static double SetBonus(PartTraits tail, PartTraits dorsal, PartTraits pectoral)
    {
        double bonus = 0;

        // Padrões iguais (ignorando None): maior grupo de partes com o mesmo padrão.
        var patterns = new[] { tail.Pattern, dorsal.Pattern, pectoral.Pattern }
            .Where(p => p != PatternType.None).ToArray();
        int patMatch = patterns.Length == 0 ? 0 : patterns.GroupBy(p => p).Max(g => g.Count());
        if (patMatch == 3) bonus += TraitConfigV1.SamePattern3Bonus;
        else if (patMatch == 2) bonus += TraitConfigV1.SamePattern2Bonus;

        // Cores de base iguais: maior grupo de partes com a mesma cor.
        int colMatch = new[] { tail.Color, dorsal.Color, pectoral.Color }
            .GroupBy(c => c).Max(g => g.Count());
        if (colMatch == 3) bonus += TraitConfigV1.SameColor3Bonus;
        else if (colMatch == 2) bonus += TraitConfigV1.SameColor2Bonus;

        return bonus;
    }

    private static double SelfInformation(double probability) => -Math.Log10(probability);

    /// <summary>+15pp na cor correlacionada, resto renormalizado proporcionalmente (soma segue 100).</summary>
    private static IReadOnlyList<WeightedValue<PartColor>> ApplyCorrelation(
        IReadOnlyList<WeightedValue<PartColor>> table, PartColor? boosted)
    {
        if (boosted is null)
            return table;

        double boostedBase = table.First(e => e.Value == boosted).Weight;
        double boostedNew = boostedBase + TraitConfigV1.CorrelationBoostPoints;
        double othersScale = (100.0 - boostedNew) / (100.0 - boostedBase);

        return table
            .Select(e => e.Value == boosted ? e with { Weight = boostedNew } : e with { Weight = e.Weight * othersScale })
            .ToArray();
    }

    /// <summary>Normal(mean, sd) determinística via Box-Muller, clampada em [0,100].</summary>
    private static double NormalPick(long seed, string salt, double mean, double stdDev)
    {
        double u1 = 1.0 - DeterministicHash.Roll01(seed, salt);          // (0,1] evita log(0)
        double u2 = DeterministicHash.Roll01(seed, salt + "_phase");
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return Math.Clamp(mean + stdDev * z, 0, 100);
    }

    /// <summary>CDF da normal(mean, sd) (aprox. Abramowitz-Stegun 7.1.26).</summary>
    private static double NormalCdf(double x, double mean, double stdDev)
    {
        double z = (x - mean) / (stdDev * Math.Sqrt(2.0));
        return 0.5 * (1.0 + Erf(z));
    }

    private static double Erf(double x)
    {
        double sign = Math.Sign(x);
        x = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.3275911 * x);
        double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592)
                   * t * Math.Exp(-x * x);
        return sign * y;
    }
}
