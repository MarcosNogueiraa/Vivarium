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
        // Só lança pra uma versão MAIOR que a atual (dado corrompido/impossível — não pode vir
        // "do futuro"). Versão igual ou anterior sempre usa a config atual, sem erro: só existe
        // uma TraitConfigV1 hardcoded, nunca houve suporte real a múltiplas versões — travar em
        // qualquer mismatch (como era antes, 12/08/2026) vira uma mina que detona TODA criatura
        // já existente (TraitConfigVersion desatualizado no banco) assim que Version subir, até
        // alguém rodar fix-scores — outage real, não só na ferramenta admin.
        if (configVersion > TraitConfigV1.Version)
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

    /// <summary>De qual lado (ou mutação) um slot do filhote veio — CLAUDE.md §8.19/§8.19.1 (13/08/2026).</summary>
    public enum BreedSource { ParentA, ParentB, Mutation }

    /// <summary>
    /// Um registro de origem de UM slot do filhote (tier de brilho, ou cor/padrão de uma parte).
    /// <paramref name="Part"/> é null pro tier (não pertence a nenhuma parte).
    /// </summary>
    public readonly record struct TraitSourceEntry(string Key, string? Part, BreedSource Source);

    /// <summary>
    /// Cruza dois peixes JÁ RESOLVIDOS — <paramref name="ownA"/>/<paramref name="ownB"/> são os
    /// traits REAIS dos pais (o que cada um exibe hoje na tela), não seeds. Cada trait é herdado
    /// 50/50 de um dos pais ou, com pequena chance, mutado. RarityScore é recalculado a partir da
    /// probabilidade real de cada valor herdado/mutado — nunca copiado dos pais. Subtraits
    /// condicionais (cor/opacidade de shimmer, cor/tamanho/opacidade de padrão) seguem a MESMA
    /// fonte do trait pai (tier ou tipo de padrão) pra nunca herdar um subtrait de um pai que não
    /// o tinha. O tier de brilho do corpo E a cor/padrão de cada parte usam
    /// <paramref name="rarityBias"/> pra favorecer o valor mais raro entre os pais. Movimento
    /// continua 50/50 puro — contribui pouco pro score.
    ///
    /// **13/08/2026 — traits congelados no nascimento (CLAUDE.md §8.19.1):** até aqui, um pai que
    /// era ele mesmo um filhote precisava ser RECONSTRUÍDO a partir do seed dele + até 2 gerações
    /// de avós denormalizadas — limitado por profundidade, e a fonte de uma sequência real de bugs
    /// (o valor reconstruído podia divergir do que o pai realmente exibia). Agora o CHAMADOR
    /// (`BreedingService`) já resolve os pais uma vez, lê o `TraitsJson` congelado de cada um e
    /// passa os `CreatureTraits` prontos aqui — sem limite de profundidade, sem reconstrução,
    /// porque cada peixe já nasceu com o próprio valor definitivo gravado. O mecanismo de "puxar
    /// um traço de um avô" (`GrandparentReachChance`) foi removido junto — já estava em 0.1% de
    /// chance (a pedido do usuário) e exigiria carregar os avós como entidades extras, complexidade
    /// desproporcional ao efeito.
    ///
    /// <paramref name="mutationRarityBiasStrength"/>/<paramref name="antiDuplicationDecay"/>/
    /// <paramref name="antiDuplicationMaxPenalty"/>: ver
    /// <see cref="BreedingDefaults.MutationRarityBiasStrength"/>/<see cref="BreedingDefaults.AntiDuplicationDecay"/>/
    /// <see cref="BreedingDefaults.AntiDuplicationMaxPenalty"/> pro raciocínio completo.
    /// </summary>
    public static (CreatureTraits Traits, IReadOnlyList<TraitSourceEntry> Source) BreedTraits(
        long childSeed, CreatureTraits ownA, CreatureTraits ownB,
        double mutationChance, double rarityBias,
        double mutationRarityBiasStrength, double antiDuplicationDecay, double antiDuplicationMaxPenalty)
    {
        var trace = new List<TraitSourceEntry>(7);
        static BreedSource SourceOf(bool mutated, bool fromA) => mutated ? BreedSource.Mutation : fromA ? BreedSource.ParentA : BreedSource.ParentB;

        // Anti-duplicação (13/08/2026): 1 sequência POR cruzamento, atravessando os 7 slots com
        // viés de raridade abaixo (tier, cauda cor/padrão, dorsal cor/padrão, peitoral cor/padrão)
        // na MESMA ordem em que são computados. Movimento fica de fora (não usa InheritOrMutate).
        var streak = new DuplicationStreak(antiDuplicationDecay, antiDuplicationMaxPenalty);

        double score = 0;

        var tierPick = InheritOrMutate(childSeed, "body_shimmer", mutationChance, ownA.ShimmerTier, ownB.ShimmerTier,
            TraitConfigV1.ShimmerTiers, rarityBias, mutationRarityBiasStrength, streak);
        score += TraitConfigV1.ShimmerScoreWeight * SelfInformation(tierPick.Probability);
        ShimmerTier tier = tierPick.Value;
        trace.Add(new TraitSourceEntry("shimmerTier", null, SourceOf(tierPick.Mutated, tierPick.FromA)));

        ShimmerColor? shimmerColor = null;
        double shimmerOpacity = 0;
        if (tier != ShimmerTier.None)
        {
            var tierSource = tierPick.FromA ? ownA : ownB;
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

        var tail = BreedPart(childSeed, "tail", mutationChance, rarityBias, mutationRarityBiasStrength, streak, ownA.Tail, ownB.Tail, partColorTable, trace, ref score);
        var dorsal = BreedPart(childSeed, "dorsal", mutationChance, rarityBias, mutationRarityBiasStrength, streak, ownA.Dorsal, ownB.Dorsal, partColorTable, trace, ref score);
        var pectoral = BreedPart(childSeed, "pectoral", mutationChance, rarityBias, mutationRarityBiasStrength, streak, ownA.Pectoral, ownB.Pectoral, partColorTable, trace, ref score);

        score += SetBonus(tail, dorsal, pectoral);

        // Movimento: sem viés, sempre a partir dos traits REAIS do pai direto — já resolvidos.
        double tailSpeed = BreedContinuousNormal(childSeed, "tail_speed", mutationChance,
            ownA.Movement.TailSpeed, ownB.Movement.TailSpeed, TraitConfigV1.MovementSpeedMean, TraitConfigV1.MovementSpeedStdDev,
            TraitConfigV1.MovementSpeedInheritJitterStdDev);
        score += MovementExtremeInfo(tailSpeed);
        double tailAmplitude = BreedContinuousUniform(childSeed, "tail_wag_amplitude", mutationChance,
            ownA.Movement.TailAmplitude, ownB.Movement.TailAmplitude, TraitConfigV1.TailAmplitudeMin, TraitConfigV1.TailAmplitudeMax,
            TraitConfigV1.MovementAmplitudeInheritJitterStdDev);

        double finSpeed = BreedContinuousNormal(childSeed, "fin_speed", mutationChance,
            ownA.Movement.FinSpeed, ownB.Movement.FinSpeed, TraitConfigV1.MovementSpeedMean, TraitConfigV1.MovementSpeedStdDev,
            TraitConfigV1.MovementSpeedInheritJitterStdDev);
        score += MovementExtremeInfo(finSpeed);
        double finAmplitude = BreedContinuousUniform(childSeed, "fin_wag_amplitude", mutationChance,
            ownA.Movement.FinAmplitude, ownB.Movement.FinAmplitude, TraitConfigV1.FinAmplitudeMin, TraitConfigV1.FinAmplitudeMax,
            TraitConfigV1.MovementAmplitudeInheritJitterStdDev);

        var movement = new MovementTraits(tailSpeed, tailAmplitude, finSpeed, finAmplitude);

        var traits = new CreatureTraits(tier, shimmerColor, shimmerOpacity, tail, dorsal, pectoral, movement, score);
        return (traits, trace);
    }

    /// <summary>
    /// Conveniência pra cruzar 2 seeds FRESCOS diretamente (sem ancestralidade nenhuma) — usada
    /// por testes/simulação que não precisam de pais reais. Produção sempre usa a sobrecarga com
    /// `CreatureTraits` já resolvidos (`BreedingService`, que lê o `TraitsJson` dos pais).
    /// </summary>
    public static (CreatureTraits Traits, IReadOnlyList<TraitSourceEntry> Source) BreedTraits(
        long childSeed, long parentASeed, long parentBSeed, double mutationChance, double rarityBias)
        => BreedTraits(childSeed, Generate(parentASeed), Generate(parentBSeed), mutationChance, rarityBias,
            mutationRarityBiasStrength: -1, antiDuplicationDecay: 0, antiDuplicationMaxPenalty: 0);

    /// <summary>
    /// Probabilidade de cada tier de brilho sair no filho, dado os traits REAIS dos pais (já
    /// resolvidos, lidos do `TraitsJson` deles) — cálculo fechado, sem sortear nada (usado no
    /// preview "chances do filhote" antes de confirmar o cruzamento). Mesma matemática de
    /// <see cref="InheritOrMutate{T}"/>: com `mutationChance` de chance o tier vem do sorteio
    /// livre pela tabela base; senão, do viés entre os dois pais.
    ///
    /// **Simplificação aceita:** não modela o piso de mutação (`MutationRarityBiasStrength`) — o
    /// branch de mutação aqui ainda assume sorteio livre pela tabela cheia (`pBaseline`), então a
    /// prévia fica levemente otimista pra "sem brilho" e pessimista pros tiers raros quando pelo
    /// menos um pai já tem um tier não-trivial. Mesmo padrão de simplificação já aceito nesta
    /// função (boost de correlação cor↔shimmer também fica de fora).
    /// </summary>
    public static IReadOnlyDictionary<ShimmerTier, double> ChildTierDistribution(
        CreatureTraits ownA, CreatureTraits ownB, double mutationChance, double rarityBias)
    {
        double probA = WeightedTable.ProbabilityOf(TraitConfigV1.ShimmerTiers, ownA.ShimmerTier);
        double probB = WeightedTable.ProbabilityOf(TraitConfigV1.ShimmerTiers, ownB.ShimmerTier);
        double pFromA = WeightedTable.BiasedInheritProbability(probA, probB, rarityBias);
        double pFromB = 1 - pFromA;

        var result = new Dictionary<ShimmerTier, double>();
        foreach (var entry in TraitConfigV1.ShimmerTiers)
        {
            double pBaseline = entry.Weight / TraitConfigV1.ShimmerTiers.Sum(e => e.Weight);
            double pInherit = (entry.Value == ownA.ShimmerTier ? pFromA : 0) + (entry.Value == ownB.ShimmerTier ? pFromB : 0);
            result[entry.Value] = mutationChance * pBaseline + (1 - mutationChance) * pInherit;
        }
        return result;
    }

    private readonly record struct InheritedPick<T>(T Value, double Probability, bool Mutated, bool FromA);

    /// <summary>
    /// Estado da "sequência de duplicação" dentro de UM cruzamento (CLAUDE.md 8.8, 13/08/2026):
    /// conta quantos slots CONSECUTIVOS já vieram herdados do MESMO pai sem mutar — ver
    /// <see cref="BreedingDefaults.AntiDuplicationDecay"/> pro raciocínio completo. Uma instância
    /// nova por chamada de `BreedTraits`, nunca compartilhada entre cruzamentos diferentes.
    /// </summary>
    internal sealed class DuplicationStreak(double decay, double maxPenalty)
    {
        public int Count { get; private set; }
        public bool? SideA { get; private set; }

        /// <summary>
        /// Empurra o threshold de herança pra LONGE do lado que já vem ganhando — mas NUNCA
        /// inverte o lado que o viés de raridade já favorece (só encolhe até um "cara ou coroa"
        /// neutro em 0.5, no máximo). Bug real corrigido 13/08/2026 (relatado pelo usuário, conta
        /// EoNeng: filhotes saindo com score bem abaixo dos dois pais): a versão original
        /// multiplicava o threshold inteiro pela penalidade, o que podia SUBTRAIR do lado
        /// favorecido pela raridade além do ponto neutro — como `RarityBiasStrength` é
        /// deliberadamente sutil (threshold raramente passa de ~0.55-0.6, mesmo em pares bem
        /// díspares), uma sequência de só 2-3 heranças seguidas do MESMO pai (natural quando esse
        /// pai já tem os traços mais raros — exatamente o caso que a raridade deveria recompensar)
        /// já bastava pra zerar ou inverter esse sinal, empurrando sistematicamente o filhote pro
        /// pai MAIS FRACO em vez do mais raro. Agora a penalidade só encolhe a distância até 0.5
        /// (nunca ultrapassa) quando o lado que está "ganhando" a sequência é o MESMO que a
        /// raridade já favorece — nesse caso o pior resultado possível é um sorteio neutro, nunca
        /// uma inversão ativa a favor do pai errado. Quando o lado que ganha a sequência já é o
        /// lado MENOS favorecido pela raridade (aconteceu por sorte no sorteio, não por viés),
        /// a penalidade continua livre pra empurrar mais ainda nessa direção (não há sinal de
        /// raridade forte sendo contrariado).
        /// </summary>
        public double ApplyPenalty(double threshold)
        {
            if (SideA is not { } sideA) return threshold;
            double penalty = Math.Min(maxPenalty, 1 - Math.Pow(decay, Count));
            if (sideA)
            {
                double reduced = threshold * (1 - penalty);
                return threshold > 0.5 ? Math.Max(0.5, reduced) : reduced;
            }
            double increased = threshold + (1 - threshold) * penalty;
            return threshold < 0.5 ? Math.Min(0.5, increased) : increased;
        }

        public void Update(bool mutated, bool fromA)
        {
            if (mutated) { Count = 0; SideA = null; return; }
            if (SideA == fromA) Count++;
            else { SideA = fromA; Count = 1; }
        }

        /// <summary>Só pra teste — constrói já com um estado de sequência específico.</summary>
        internal static DuplicationStreak WithState(double decay, double maxPenalty, bool sideA, int count)
        {
            var streak = new DuplicationStreak(decay, maxPenalty);
            streak.Update(mutated: false, fromA: sideA);
            for (int i = 1; i < count; i++)
                streak.Update(mutated: false, fromA: sideA);
            return streak;
        }
    }

    /// <summary>
    /// Decide, por um hash independente, se o trait muta (sorteia do zero) ou é
    /// herdado de A/B. <paramref name="rarityBias"/> (0 = 50/50 puro) desloca a
    /// escolha de herança em favor do valor mais raro entre os pais.
    ///
    /// **Piso de mutação (13/08/2026):** quando <paramref name="mutationRarityBiasStrength"/> é
    /// negativo (sentinela — só a sobrecarga simples de 6 argumentos usa isso, pra testes legados
    /// que não conhecem o mecanismo), a mutação sorteia livre pela tabela cheia, igual sempre foi.
    /// Qualquer valor `>= 0` (produção sempre usa <see cref="BreedingDefaults.MutationRarityBiasStrength"/>)
    /// ATIVA o piso: o resultado nunca pode ficar mais comum que o pai mais fraco dos dois —
    /// restringe a tabela (<see cref="WeightedTable.Restrict{T}"/>) ao peso do pai mais fraco pra
    /// cima antes de sortear, com leve viés a favor do raro dentro do que sobra (o próprio valor
    /// de <paramref name="mutationRarityBiasStrength"/>, 0 = sem viés extra dentro do piso).
    ///
    /// **Anti-duplicação (13/08/2026):** <paramref name="streak"/>, se presente, empurra o
    /// threshold de herança pra longe do lado que já ganhou nos slots anteriores — ver
    /// <see cref="BreedingDefaults.AntiDuplicationDecay"/>.
    /// </summary>
    private static InheritedPick<T> InheritOrMutate<T>(
        long childSeed, string salt, double mutationChance,
        T valueA, T valueB, IReadOnlyList<WeightedValue<T>> table, double rarityBias = 0,
        double mutationRarityBiasStrength = -1, DuplicationStreak? streak = null)
    {
        bool mutated = DeterministicHash.Roll01(childSeed, salt + "_source") < mutationChance;
        if (mutated)
        {
            if (mutationRarityBiasStrength < 0)
            {
                var (freeValue, freeP) = WeightedTable.Pick(table, DeterministicHash.Roll01(childSeed, salt));
                streak?.Update(mutated: true, fromA: false);
                return new InheritedPick<T>(freeValue, freeP, true, false);
            }

            double floorWeight = Math.Max(WeightedTable.WeightOf(table, valueA), WeightedTable.WeightOf(table, valueB));
            var restricted = WeightedTable.Restrict(table, floorWeight);
            var (value, _) = WeightedTable.PickBiasedTowardRare(restricted, DeterministicHash.Roll01(childSeed, salt), mutationRarityBiasStrength);
            double p = WeightedTable.ProbabilityOf(table, value); // probabilidade REAL na tabela cheia, pro rarity score
            streak?.Update(mutated: true, fromA: false);
            return new InheritedPick<T>(value, p, true, false);
        }
        double probA = WeightedTable.ProbabilityOf(table, valueA);
        double probB = WeightedTable.ProbabilityOf(table, valueB);
        double threshold = WeightedTable.BiasedInheritProbability(probA, probB, rarityBias);
        if (streak is not null) threshold = streak.ApplyPenalty(threshold);
        bool fromA = DeterministicHash.Roll01(childSeed, salt + "_inherit") < threshold;
        streak?.Update(mutated: false, fromA: fromA);
        T v = fromA ? valueA : valueB;
        return new InheritedPick<T>(v, fromA ? probA : probB, false, fromA);
    }

    private static double BreedContinuousNormal(
        long childSeed, string salt, double mutationChance, double valueA, double valueB, double mean, double stdDev,
        double inheritJitterStdDev)
    {
        if (DeterministicHash.Roll01(childSeed, salt + "_source") < mutationChance)
            return NormalPick(childSeed, salt, mean, stdDev);
        double inherited = DeterministicHash.Roll01(childSeed, salt + "_inherit") < 0.5 ? valueA : valueB;
        // Jitter em vez de cópia exata (13/08/2026) — mesmo princípio já aplicado aos
        // subtraits de padrão (TraitConfigV1.PatternSubtraitInheritJitterStdDev).
        return NormalPick(childSeed, salt + "_jitter", inherited, inheritJitterStdDev);
    }

    private static double BreedContinuousUniform(
        long childSeed, string salt, double mutationChance, double valueA, double valueB, double min, double max,
        double inheritJitterStdDev)
    {
        if (DeterministicHash.Roll01(childSeed, salt + "_source") < mutationChance)
            return min + DeterministicHash.Roll01(childSeed, salt) * (max - min);
        double inherited = DeterministicHash.Roll01(childSeed, salt + "_inherit") < 0.5 ? valueA : valueB;
        return NormalPick(childSeed, salt + "_jitter", inherited, inheritJitterStdDev, min, max);
    }

    private static PartTraits BreedPart(
        long childSeed, string partSalt, double mutationChance, double rarityBias, double mutationRarityBiasStrength,
        DuplicationStreak? streak,
        PartTraits a, PartTraits b, IReadOnlyList<WeightedValue<PartColor>> colorTable,
        List<TraitSourceEntry> trace, ref double score)
    {
        var colorPick = InheritOrMutate(childSeed, partSalt + "_color", mutationChance, a.Color, b.Color, colorTable,
            rarityBias, mutationRarityBiasStrength, streak);
        score += SelfInformation(colorPick.Probability);
        PartColor color = colorPick.Value;
        trace.Add(new TraitSourceEntry("color", partSalt,
            colorPick.Mutated ? BreedSource.Mutation : colorPick.FromA ? BreedSource.ParentA : BreedSource.ParentB));

        var patternPick = InheritOrMutate(childSeed, partSalt + "_pattern", mutationChance, a.Pattern, b.Pattern, TraitConfigV1.PatternTypes,
            rarityBias, mutationRarityBiasStrength, streak);
        score += SelfInformation(patternPick.Probability);
        PatternType pattern = patternPick.Value;
        trace.Add(new TraitSourceEntry("pattern", partSalt,
            patternPick.Mutated ? BreedSource.Mutation : patternPick.FromA ? BreedSource.ParentA : BreedSource.ParentB));

        if (pattern == PatternType.None)
            return new PartTraits(color, pattern, null, null, null);

        var patternSource = patternPick.FromA ? a : b;
        bool subtraitsFromSource = !patternPick.Mutated && patternSource.Pattern == pattern
            && patternSource.PatternColor != color; // evita herdar cor de padrão igual à cor de base do FILHO

        PartColor patternColor;
        double patternSize;
        double patternOpacity;
        GradientMix? mix;
        if (subtraitsFromSource)
        {
            patternColor = patternSource.PatternColor!.Value;
            // Jitter em vez de cópia exata (13/08/2026, pedido do usuário) — variação pequena
            // em torno do valor do pai, não um clone. Ver TraitConfigV1.PatternSubtraitInheritJitterStdDev.
            patternSize = NormalPick(childSeed, partSalt + "_pattern_size_jitter",
                patternSource.PatternSize!.Value, TraitConfigV1.PatternSubtraitInheritJitterStdDev);
            patternOpacity = NormalPick(childSeed, partSalt + "_pattern_opacity_jitter",
                patternSource.PatternOpacity!.Value, TraitConfigV1.PatternSubtraitInheritJitterStdDev,
                TraitConfigV1.PatternOpacityMin, TraitConfigV1.PatternOpacityMax);
            mix = patternSource.Mix; // não-nulo quando o padrão herdado é Gradient, senão null
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
            mix = pattern == PatternType.Gradient
                ? WeightedTable.Pick(TraitConfigV1.GradientMixRatios, DeterministicHash.Roll01(childSeed, partSalt + "_pattern_mix")).Value
                : null;
        }

        var scoringPalette = TraitConfigV1.PartColors.Where(e => e.Value != color).ToArray();
        double patternColorP = WeightedTable.ProbabilityOf(scoringPalette, patternColor);
        score += SelfInformation(patternColorP);
        score += PatternSizeExtremeInfo(patternSize);
        score += PatternOpacityExtremeInfo(patternOpacity);

        // Score do mix (e a subtração da cor minoritária) sempre recalculado do
        // valor FINAL (herdado ou mutado), nunca copiado — mesmo princípio já
        // usado pros outros subtraits acima (patternColor/size/opacity).
        if (mix is { } m)
        {
            // Mesma ordem de operações de RollGradientMix (delta calculado à parte,
            // UMA soma só em `score`) — importa pra bater bit a bit com GeneratePart
            // quando mutationChance=1.0 (ponto flutuante não é associativo).
            double delta = SelfInformation(WeightedTable.ProbabilityOf(TraitConfigV1.GradientMixRatios, m));
            if (m == GradientMix.PatternDominant)
                delta -= SelfInformation(colorPick.Probability);
            else if (m == GradientMix.BaseDominant)
                delta -= SelfInformation(patternColorP);
            score += delta;
        }

        return new PartTraits(color, pattern, patternColor, patternSize, patternOpacity, mix);
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

        GradientMix? mix = null;
        if (pattern == PatternType.Gradient)
        {
            var (m, delta) = RollGradientMix(seed, partSalt, colorP, patternColorP);
            mix = m;
            score += delta;
        }

        return new PartTraits(color, pattern, patternColor, size, opacity, mix);
    }

    /// <summary>
    /// Sorteia a proporção de mistura do Degradê e devolve o ajuste de score: a
    /// probabilidade do mix em si sempre soma (é um trait novo, como qualquer
    /// outro), e — só quando o mix é assimétrico — a cor MINORITÁRIA é subtraída
    /// do score depois de já ter sido somada mais cedo (`colorP`/`patternColorP`
    /// já entraram incondicionalmente antes do padrão ser conhecido), deixando só
    /// a cor dominante (>50%) contando. Em Even (50/50) nenhuma correção é feita —
    /// as duas cores já contam normalmente, igual a qualquer outro padrão hoje.
    /// </summary>
    private static (GradientMix Mix, double ScoreDelta) RollGradientMix(
        long seed, string partSalt, double colorP, double patternColorP)
    {
        var (mix, mixP) = WeightedTable.Pick(TraitConfigV1.GradientMixRatios, DeterministicHash.Roll01(seed, partSalt + "_pattern_mix"));
        double delta = SelfInformation(mixP);
        if (mix == GradientMix.PatternDominant)
            delta -= SelfInformation(colorP);          // cor de base é a minoritária aqui
        else if (mix == GradientMix.BaseDominant)
            delta -= SelfInformation(patternColorP);    // cor do padrão é a minoritária aqui
        return (mix, delta);
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

    /// <summary>Normal(mean, sd) determinística via Box-Muller, clampada em [min,max] (default [0,100]).</summary>
    private static double NormalPick(long seed, string salt, double mean, double stdDev, double min = 0, double max = 100)
    {
        double u1 = 1.0 - DeterministicHash.Roll01(seed, salt);          // (0,1] evita log(0)
        double u2 = DeterministicHash.Roll01(seed, salt + "_phase");
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return Math.Clamp(mean + stdDev * z, min, max);
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
