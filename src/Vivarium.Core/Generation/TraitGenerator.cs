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

    /// <summary>
    /// Cruza dois peixes: cada trait é herdado 50/50 de um dos pais ou, com pequena
    /// chance, mutado (sorteado do zero pelas mesmas tabelas de peso — legendário
    /// continua raro mesmo mutando, sem lógica assimétrica extra). RarityScore é
    /// recalculado a partir da probabilidade real de cada valor herdado/mutado —
    /// nunca copiado dos pais. Subtraits condicionais (cor/opacidade de shimmer,
    /// cor/tamanho/opacidade de padrão) seguem a MESMA fonte do trait pai (tier ou
    /// tipo de padrão) pra nunca herdar um subtrait de um pai que não o tinha.
    /// Só o tier de brilho do corpo usa <paramref name="rarityBias"/> pra favorecer
    /// o valor mais raro entre os pais (raridade "hereditária", sem virar garantia —
    /// mutação continua sem viés); os demais traits continuam 50/50 puro.
    /// </summary>
    public static CreatureTraits BreedTraits(
        long childSeed, long parentASeed, long parentBSeed, int configVersion, double mutationChance, double rarityBias)
    {
        if (configVersion != TraitConfigV1.Version)
            throw new ArgumentException($"Versão de config desconhecida: {configVersion}", nameof(configVersion));

        var a = Generate(parentASeed, configVersion);
        var b = Generate(parentBSeed, configVersion);

        double score = 0;

        var tierPick = InheritOrMutate(childSeed, "body_shimmer", mutationChance, a.ShimmerTier, b.ShimmerTier, TraitConfigV1.ShimmerTiers, rarityBias);
        score += TraitConfigV1.ShimmerScoreWeight * SelfInformation(tierPick.Probability);
        ShimmerTier tier = tierPick.Value;

        ShimmerColor? shimmerColor = null;
        double shimmerOpacity = 0;
        if (tier != ShimmerTier.None)
        {
            var tierSource = tierPick.FromA ? a : b;
            if (!tierPick.Mutated && tierSource.ShimmerTier == tier)
            {
                shimmerColor = tierSource.ShimmerColor;
                shimmerOpacity = tierSource.ShimmerOpacity;
            }
            else
            {
                var palette = TraitConfigV1.ShimmerColorsByTier[tier];
                shimmerColor = palette[(int)(DeterministicHash.Roll01(childSeed, "body_shimmer_color") * palette.Length)];
                var (min, max) = TraitConfigV1.ShimmerOpacityByTier[tier];
                shimmerOpacity = min + DeterministicHash.Roll01(childSeed, "body_shimmer_opacity") * (max - min);
            }
        }

        PartColor? boosted = tier >= ShimmerTier.Vibrant && shimmerColor is { } sc
            ? TraitConfigV1.ClosestPartColor(sc)
            : null;
        var partColorTable = ApplyCorrelation(TraitConfigV1.PartColors, boosted);

        var tail = BreedPart(childSeed, "tail", mutationChance, a.Tail, b.Tail, partColorTable, ref score);
        var dorsal = BreedPart(childSeed, "dorsal", mutationChance, a.Dorsal, b.Dorsal, partColorTable, ref score);
        var pectoral = BreedPart(childSeed, "pectoral", mutationChance, a.Pectoral, b.Pectoral, partColorTable, ref score);

        score += SetBonus(tail, dorsal, pectoral);

        double tailSpeed = BreedContinuousNormal(childSeed, "tail_speed", mutationChance,
            a.Movement.TailSpeed, b.Movement.TailSpeed, TraitConfigV1.MovementSpeedMean, TraitConfigV1.MovementSpeedStdDev);
        score += MovementExtremeInfo(tailSpeed);
        double tailAmplitude = BreedContinuousUniform(childSeed, "tail_wag_amplitude", mutationChance,
            a.Movement.TailAmplitude, b.Movement.TailAmplitude, TraitConfigV1.TailAmplitudeMin, TraitConfigV1.TailAmplitudeMax);

        double finSpeed = BreedContinuousNormal(childSeed, "fin_speed", mutationChance,
            a.Movement.FinSpeed, b.Movement.FinSpeed, TraitConfigV1.MovementSpeedMean, TraitConfigV1.MovementSpeedStdDev);
        score += MovementExtremeInfo(finSpeed);
        double finAmplitude = BreedContinuousUniform(childSeed, "fin_wag_amplitude", mutationChance,
            a.Movement.FinAmplitude, b.Movement.FinAmplitude, TraitConfigV1.FinAmplitudeMin, TraitConfigV1.FinAmplitudeMax);

        var movement = new MovementTraits(tailSpeed, tailAmplitude, finSpeed, finAmplitude);

        return new CreatureTraits(tier, shimmerColor, shimmerOpacity, tail, dorsal, pectoral, movement, score);
    }

    private readonly record struct InheritedPick<T>(T Value, double Probability, bool Mutated, bool FromA);

    /// <summary>
    /// Decide, por um hash independente, se o trait muta (sorteia do zero) ou é
    /// herdado de A/B. <paramref name="rarityBias"/> (0 = 50/50 puro) desloca a
    /// escolha de herança em favor do valor mais raro entre os pais — a mutação em
    /// si nunca é enviesada.
    /// </summary>
    private static InheritedPick<T> InheritOrMutate<T>(
        long childSeed, string salt, double mutationChance,
        T valueA, T valueB, IReadOnlyList<WeightedValue<T>> table, double rarityBias = 0)
    {
        bool mutated = DeterministicHash.Roll01(childSeed, salt + "_source") < mutationChance;
        if (mutated)
        {
            var (value, p) = WeightedTable.Pick(table, DeterministicHash.Roll01(childSeed, salt));
            return new InheritedPick<T>(value, p, true, false);
        }
        double probA = WeightedTable.ProbabilityOf(table, valueA);
        double probB = WeightedTable.ProbabilityOf(table, valueB);
        double threshold = WeightedTable.BiasedInheritProbability(probA, probB, rarityBias);
        bool fromA = DeterministicHash.Roll01(childSeed, salt + "_inherit") < threshold;
        T v = fromA ? valueA : valueB;
        return new InheritedPick<T>(v, fromA ? probA : probB, false, fromA);
    }

    private static double BreedContinuousNormal(
        long childSeed, string salt, double mutationChance, double valueA, double valueB, double mean, double stdDev)
    {
        if (DeterministicHash.Roll01(childSeed, salt + "_source") < mutationChance)
            return NormalPick(childSeed, salt, mean, stdDev);
        return DeterministicHash.Roll01(childSeed, salt + "_inherit") < 0.5 ? valueA : valueB;
    }

    private static double BreedContinuousUniform(
        long childSeed, string salt, double mutationChance, double valueA, double valueB, double min, double max)
    {
        if (DeterministicHash.Roll01(childSeed, salt + "_source") < mutationChance)
            return min + DeterministicHash.Roll01(childSeed, salt) * (max - min);
        return DeterministicHash.Roll01(childSeed, salt + "_inherit") < 0.5 ? valueA : valueB;
    }

    private static PartTraits BreedPart(
        long childSeed, string partSalt, double mutationChance,
        PartTraits a, PartTraits b, IReadOnlyList<WeightedValue<PartColor>> colorTable, ref double score)
    {
        var colorPick = InheritOrMutate(childSeed, partSalt + "_color", mutationChance, a.Color, b.Color, colorTable);
        score += SelfInformation(colorPick.Probability);
        PartColor color = colorPick.Value;

        var patternPick = InheritOrMutate(childSeed, partSalt + "_pattern", mutationChance, a.Pattern, b.Pattern, TraitConfigV1.PatternTypes);
        score += SelfInformation(patternPick.Probability);
        PatternType pattern = patternPick.Value;

        if (pattern == PatternType.None)
            return new PartTraits(color, pattern, null, null, null);

        var patternSource = patternPick.FromA ? a : b;
        bool subtraitsFromSource = !patternPick.Mutated && patternSource.Pattern == pattern
            && patternSource.PatternColor != color; // evita herdar cor de padrão igual à cor de base do FILHO

        PartColor patternColor;
        double patternSize;
        double patternOpacity;
        if (subtraitsFromSource)
        {
            patternColor = patternSource.PatternColor!.Value;
            patternSize = patternSource.PatternSize!.Value;
            patternOpacity = patternSource.PatternOpacity!.Value;
        }
        else
        {
            var patternPalette = TraitConfigV1.PartColors.Where(e => e.Value != color).ToArray();
            var (pc, _) = WeightedTable.Pick(patternPalette, DeterministicHash.Roll01(childSeed, partSalt + "_pattern_color"));
            patternColor = pc;
            patternSize = NormalPick(childSeed, partSalt + "_pattern_size", TraitConfigV1.PatternSizeMean, TraitConfigV1.PatternSizeStdDev);
            patternOpacity = TraitConfigV1.PatternOpacityMin
                + DeterministicHash.Roll01(childSeed, partSalt + "_pattern_opacity")
                * (TraitConfigV1.PatternOpacityMax - TraitConfigV1.PatternOpacityMin);
        }

        var scoringPalette = TraitConfigV1.PartColors.Where(e => e.Value != color).ToArray();
        score += SelfInformation(WeightedTable.ProbabilityOf(scoringPalette, patternColor));
        score += PatternSizeExtremeInfo(patternSize);
        score += PatternOpacityExtremeInfo(patternOpacity);

        return new PartTraits(color, pattern, patternColor, patternSize, patternOpacity);
    }

    private static double PatternSizeExtremeInfo(double size)
    {
        if (size < TraitConfigV1.PatternSizeExtremeLow)
            return SelfInformation(NormalCdf(TraitConfigV1.PatternSizeExtremeLow, TraitConfigV1.PatternSizeMean, TraitConfigV1.PatternSizeStdDev));
        if (size > TraitConfigV1.PatternSizeExtremeHigh)
            return SelfInformation(1 - NormalCdf(TraitConfigV1.PatternSizeExtremeHigh, TraitConfigV1.PatternSizeMean, TraitConfigV1.PatternSizeStdDev));
        return 0;
    }

    private static double PatternOpacityExtremeInfo(double opacity)
    {
        double range = TraitConfigV1.PatternOpacityMax - TraitConfigV1.PatternOpacityMin;
        if (opacity < TraitConfigV1.PatternOpacityExtremeLow)
            return SelfInformation((TraitConfigV1.PatternOpacityExtremeLow - TraitConfigV1.PatternOpacityMin) / range);
        if (opacity > TraitConfigV1.PatternOpacityExtremeHigh)
            return SelfInformation((TraitConfigV1.PatternOpacityMax - TraitConfigV1.PatternOpacityExtremeHigh) / range);
        return 0;
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
