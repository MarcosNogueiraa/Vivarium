namespace Vivarium.Core.Generation;

/// <summary>
/// Motor determinístico seed → traits. Todo trait é derivado de Hash(seed, salt)
/// independente, então o mesmo seed sempre produz o mesmo peixe, e traits novos
/// podem ser adicionados sem alterar os existentes.
/// </summary>
public static class TraitGenerator
{
    /// <summary>
    /// TEMPORÁRIO/TESTE (13/08/2026, a pedido explícito do usuário): seed reservado que força
    /// o "peixe de atributos quase impossíveis" (branco+mármore nas 3 partes, brilho Lendário
    /// Iridescente, movimento extremo) pra visualização — NÃO deriva de hash, ignora o sorteio
    /// normal. Existe só pra mostrar como esse peixe ficaria; o próprio usuário confirmou que
    /// vai apagar o peixe de teste depois. Remover este bloco (e o espelho em generator.js)
    /// quando o teste terminar — não é uma feature permanente do jogo.
    /// </summary>
    public const long ShowcaseImpossibleSeed = 999999999999999999L;

    public static CreatureTraits Generate(long seed, int configVersion = TraitConfigV1.Version)
    {
        if (configVersion != TraitConfigV1.Version)
            throw new ArgumentException($"Versão de config desconhecida: {configVersion}", nameof(configVersion));

        if (seed == ShowcaseImpossibleSeed)
            return ForcedShowcaseTraits();

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
    /// O tier de brilho do corpo E a cor/padrão de cada parte usam <paramref name="rarityBias"/>
    /// pra favorecer o valor mais raro entre os pais (raridade "hereditária", sem virar garantia —
    /// mutação continua sem viés). Movimento (velocidade/amplitude de cauda e nadadeira) continua
    /// 50/50 puro — contribui pouco pro score (só os extremos, peso reduzido) e enviesar não traria
    /// ganho perceptível (31/07/2026: estendido de "só shimmer" pra também cor/padrão — corrige
    /// filhotes "regredindo" perto do piso da população mesmo vindo de pais decentes, já que antes
    /// só o brilho puxava pra raridade e cor/padrão de parte, que dominam o score, eram 50/50 cego).
    /// </summary>
    public static CreatureTraits BreedTraits(
        long childSeed, long parentASeed, long parentBSeed, int configVersion, double mutationChance, double rarityBias)
        => BreedTraits(childSeed, new ParentAncestry(parentASeed, null, null), new ParentAncestry(parentBSeed, null, null),
            configVersion, mutationChance, rarityBias, grandparentReachChance: 0);

    /// <summary>
    /// Um pai pro cruzamento: o próprio seed + (se ele mesmo for um filhote) os seeds dos
    /// AVÓS do lado do filho sendo gerado agora — habilita <see cref="GrandparentReachChance"/>
    /// e evita reconstruir esse pai com Generate(seed) quando ele na verdade é um filhote com
    /// traits reais diferentes (bug corrigido 31/07/2026 — CLAUDE.md 8.8).
    /// </summary>
    public readonly record struct ParentAncestry(long Seed, long? GrandparentASeed, long? GrandparentBSeed);

    /// <summary>
    /// Cruza dois peixes — mesma mecânica que a sobrecarga simples (mutação/herança/viés,
    /// documentados ali), mas quando um pai é ele mesmo um filhote (<paramref name="parentA"/>/
    /// <paramref name="parentB"/> trazem os seeds dos avós), cada "slot" (tier de brilho, cauda,
    /// dorsal, peitoral) tem <paramref name="grandparentReachChance"/> de chance de vir de um dos
    /// AVÓS em vez do pai direto — um traço "pulando uma geração", com chance MENOR que herdar do
    /// pai (31/07/2026, a pedido do usuário: em vez de só corrigir o bug de traits fantasma,
    /// virou mecânica de jogo). Resolve exatamente 2 gerações de profundidade (pai real + avô
    /// real); além disso, avô é tratado como Generate(seed) puro — a "genética" rastreada com
    /// precisão para aí, decisão de escopo deliberada.
    /// </summary>
    public static CreatureTraits BreedTraits(
        long childSeed, ParentAncestry parentA, ParentAncestry parentB,
        int configVersion, double mutationChance, double rarityBias, double grandparentReachChance)
    {
        if (configVersion != TraitConfigV1.Version)
            throw new ArgumentException($"Versão de config desconhecida: {configVersion}", nameof(configVersion));

        // Traits REAIS de cada pai: se ele é filhote (avós conhecidos), recomputa via a
        // sobrecarga simples (sem reach-back — só 1 nível abaixo); senão, Generate direto.
        CreatureTraits ownA = ResolveOwnTraits(parentA, configVersion, mutationChance, rarityBias);
        CreatureTraits ownB = ResolveOwnTraits(parentB, configVersion, mutationChance, rarityBias);
        CreatureTraits? gpA1 = parentA.GrandparentASeed is { } gA1 ? Generate(gA1, configVersion) : null;
        CreatureTraits? gpA2 = parentA.GrandparentBSeed is { } gA2 ? Generate(gA2, configVersion) : null;
        CreatureTraits? gpB1 = parentB.GrandparentASeed is { } gB1 ? Generate(gB1, configVersion) : null;
        CreatureTraits? gpB2 = parentB.GrandparentBSeed is { } gB2 ? Generate(gB2, configVersion) : null;

        CreatureTraits effA(string slotSalt) => EffectiveParentTraits(childSeed, slotSalt + "_a", ownA, gpA1, gpA2, grandparentReachChance);
        CreatureTraits effB(string slotSalt) => EffectiveParentTraits(childSeed, slotSalt + "_b", ownB, gpB1, gpB2, grandparentReachChance);

        double score = 0;

        var (shimmerA, shimmerB) = (effA("body_shimmer"), effB("body_shimmer"));
        var tierPick = InheritOrMutate(childSeed, "body_shimmer", mutationChance, shimmerA.ShimmerTier, shimmerB.ShimmerTier, TraitConfigV1.ShimmerTiers, rarityBias);
        score += TraitConfigV1.ShimmerScoreWeight * SelfInformation(tierPick.Probability);
        ShimmerTier tier = tierPick.Value;

        ShimmerColor? shimmerColor = null;
        double shimmerOpacity = 0;
        if (tier != ShimmerTier.None)
        {
            var tierSource = tierPick.FromA ? shimmerA : shimmerB;
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

        var (tailA, tailB) = (effA("tail"), effB("tail"));
        var tail = BreedPart(childSeed, "tail", mutationChance, rarityBias, tailA.Tail, tailB.Tail, partColorTable, ref score);
        var (dorsalA, dorsalB) = (effA("dorsal"), effB("dorsal"));
        var dorsal = BreedPart(childSeed, "dorsal", mutationChance, rarityBias, dorsalA.Dorsal, dorsalB.Dorsal, partColorTable, ref score);
        var (pectoralA, pectoralB) = (effA("pectoral"), effB("pectoral"));
        var pectoral = BreedPart(childSeed, "pectoral", mutationChance, rarityBias, pectoralA.Pectoral, pectoralB.Pectoral, partColorTable, ref score);

        score += SetBonus(tail, dorsal, pectoral);

        // Movimento: sem viés nem reach-back de avós (contribuição pequena no score — decisão
        // já tomada na rodada anterior), sempre a partir dos traits REAIS do pai direto.
        double tailSpeed = BreedContinuousNormal(childSeed, "tail_speed", mutationChance,
            ownA.Movement.TailSpeed, ownB.Movement.TailSpeed, TraitConfigV1.MovementSpeedMean, TraitConfigV1.MovementSpeedStdDev);
        score += MovementExtremeInfo(tailSpeed);
        double tailAmplitude = BreedContinuousUniform(childSeed, "tail_wag_amplitude", mutationChance,
            ownA.Movement.TailAmplitude, ownB.Movement.TailAmplitude, TraitConfigV1.TailAmplitudeMin, TraitConfigV1.TailAmplitudeMax);

        double finSpeed = BreedContinuousNormal(childSeed, "fin_speed", mutationChance,
            ownA.Movement.FinSpeed, ownB.Movement.FinSpeed, TraitConfigV1.MovementSpeedMean, TraitConfigV1.MovementSpeedStdDev);
        score += MovementExtremeInfo(finSpeed);
        double finAmplitude = BreedContinuousUniform(childSeed, "fin_wag_amplitude", mutationChance,
            ownA.Movement.FinAmplitude, ownB.Movement.FinAmplitude, TraitConfigV1.FinAmplitudeMin, TraitConfigV1.FinAmplitudeMax);

        var movement = new MovementTraits(tailSpeed, tailAmplitude, finSpeed, finAmplitude);

        return new CreatureTraits(tier, shimmerColor, shimmerOpacity, tail, dorsal, pectoral, movement, score);
    }

    /// <summary>
    /// Ver <see cref="ShowcaseImpossibleSeed"/> — TEMPORÁRIO/TESTE, remover depois. Constrói o
    /// peixe "quase impossível" (branco+mármore nas 3 partes, Lendário/Iridescente, movimento
    /// extremo) com valores fixos, mas calcula o RarityScore de verdade a partir da
    /// probabilidade real de cada valor nas mesmas tabelas de peso (nada de score inventado).
    /// </summary>
    private static CreatureTraits ForcedShowcaseTraits()
    {
        double score = 0;

        const ShimmerTier tier = ShimmerTier.Legendary;
        double tierP = WeightedTable.ProbabilityOf(TraitConfigV1.ShimmerTiers, tier);
        score += TraitConfigV1.ShimmerScoreWeight * SelfInformation(tierP);

        const ShimmerColor shimmerColor = ShimmerColor.Iridescent;
        var (_, opacityMax) = TraitConfigV1.ShimmerOpacityByTier[tier];
        double shimmerOpacity = opacityMax;

        PartColor boosted = TraitConfigV1.ClosestPartColor(shimmerColor);
        var partColorTable = ApplyCorrelation(TraitConfigV1.PartColors, boosted);

        PartTraits MakePart()
        {
            const PartColor color = PartColor.PureWhite;
            score += SelfInformation(WeightedTable.ProbabilityOf(partColorTable, color));

            const PatternType pattern = PatternType.Marble;
            score += SelfInformation(WeightedTable.ProbabilityOf(TraitConfigV1.PatternTypes, pattern));

            const PartColor patternColor = PartColor.Black;
            var patternPalette = TraitConfigV1.PartColors.Where(e => e.Value != color).ToArray();
            score += SelfInformation(WeightedTable.ProbabilityOf(patternPalette, patternColor));

            const double size = 100.0;
            score += PatternSizeExtremeInfo(size);

            const double opacity = TraitConfigV1.PatternOpacityMax;
            score += PatternOpacityExtremeInfo(opacity);

            return new PartTraits(color, pattern, patternColor, size, opacity);
        }

        var tail = MakePart();
        var dorsal = MakePart();
        var pectoral = MakePart();
        score += SetBonus(tail, dorsal, pectoral);

        const double tailSpeed = 100.0, finSpeed = 100.0;
        score += MovementExtremeInfo(tailSpeed);
        score += MovementExtremeInfo(finSpeed);
        var movement = new MovementTraits(tailSpeed, TraitConfigV1.TailAmplitudeMax, finSpeed, TraitConfigV1.FinAmplitudeMax);

        return new CreatureTraits(tier, shimmerColor, shimmerOpacity, tail, dorsal, pectoral, movement, score);
    }

    /// <summary>Traits reais de um pai — Generate direto se fresco, ou recomputa 1 nível (sem reach-back) se ele é filhote.</summary>
    internal static CreatureTraits ResolveOwnTraits(ParentAncestry ancestry, int configVersion, double mutationChance, double rarityBias)
        => ancestry.GrandparentASeed is { } gA && ancestry.GrandparentBSeed is { } gB
            ? BreedTraits(ancestry.Seed, gA, gB, configVersion, mutationChance, rarityBias)
            : Generate(ancestry.Seed, configVersion);

    /// <summary>
    /// Com <paramref name="reachChance"/> de chance (só se os avós existirem), troca o "candidato"
    /// desse lado pelo de um dos avós (50/50 entre eles) em vez dos traits reais do pai direto.
    /// Retorna o CreatureTraits INTEIRO (não um trait solto) pra manter subtraits coerentes com
    /// a mesma fonte, igual já acontece hoje entre pai A/B.
    /// </summary>
    internal static CreatureTraits EffectiveParentTraits(
        long childSeed, string salt, CreatureTraits own, CreatureTraits? grandparent1, CreatureTraits? grandparent2, double reachChance)
    {
        if (grandparent1 is null || grandparent2 is null) return own;
        if (DeterministicHash.Roll01(childSeed, salt + "_reach") >= reachChance) return own;
        bool first = DeterministicHash.Roll01(childSeed, salt + "_reach_which") < 0.5;
        return first ? grandparent1 : grandparent2;
    }

    /// <summary>
    /// Probabilidade de cada tier de brilho sair no filho, dado os pais e as
    /// constantes de mutação/viés — cálculo fechado, sem sortear nada (usado no
    /// preview "chances do filhote" antes de confirmar o cruzamento). Mesma
    /// matemática de <see cref="InheritOrMutate{T}"/>: com `mutationChance` de
    /// chance o tier vem do sorteio livre pela tabela base; senão, do viés entre
    /// os dois pais.
    ///
    /// <paramref name="parentA"/>/<paramref name="parentB"/> usam <see cref="ResolveOwnTraits"/>
    /// (não <see cref="Generate"/> direto) — bug real corrigido 12/08/2026: se um pai É ele mesmo
    /// um filhote (ex: um Épico que herdou brilho Lendário Iridescente dos avós), `Generate(seed)`
    /// puro devolve um tier ALEATÓRIO sem relação nenhuma com o tier REAL desse pai (78% de chance
    /// de sair "Sem brilho" do zero) — a prévia mostrava algo como 98% de chance de o filhote sair
    /// sem brilho quando na verdade a chance real de manter Lendário era alta. Mesma classe de bug
    /// já corrigida em `BreedTraits`/`FishCanvas` (31/07 e 10/08/2026) — sempre que uma criatura
    /// "completa" ganha ancestralidade, checar todo lugar que ainda deriva traits de um seed cru.
    /// </summary>
    public static IReadOnlyDictionary<ShimmerTier, double> ChildTierDistribution(
        ParentAncestry parentA, ParentAncestry parentB, int configVersion, double mutationChance, double rarityBias)
    {
        var a = ResolveOwnTraits(parentA, configVersion, mutationChance, rarityBias);
        var b = ResolveOwnTraits(parentB, configVersion, mutationChance, rarityBias);

        double probA = WeightedTable.ProbabilityOf(TraitConfigV1.ShimmerTiers, a.ShimmerTier);
        double probB = WeightedTable.ProbabilityOf(TraitConfigV1.ShimmerTiers, b.ShimmerTier);
        double pFromA = WeightedTable.BiasedInheritProbability(probA, probB, rarityBias);
        double pFromB = 1 - pFromA;

        var result = new Dictionary<ShimmerTier, double>();
        foreach (var entry in TraitConfigV1.ShimmerTiers)
        {
            double pBaseline = entry.Weight / TraitConfigV1.ShimmerTiers.Sum(e => e.Weight);
            double pInherit = (entry.Value == a.ShimmerTier ? pFromA : 0) + (entry.Value == b.ShimmerTier ? pFromB : 0);
            result[entry.Value] = mutationChance * pBaseline + (1 - mutationChance) * pInherit;
        }
        return result;
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
        long childSeed, string partSalt, double mutationChance, double rarityBias,
        PartTraits a, PartTraits b, IReadOnlyList<WeightedValue<PartColor>> colorTable, ref double score)
    {
        var colorPick = InheritOrMutate(childSeed, partSalt + "_color", mutationChance, a.Color, b.Color, colorTable, rarityBias);
        score += SelfInformation(colorPick.Probability);
        PartColor color = colorPick.Value;

        var patternPick = InheritOrMutate(childSeed, partSalt + "_pattern", mutationChance, a.Pattern, b.Pattern, TraitConfigV1.PatternTypes, rarityBias);
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
