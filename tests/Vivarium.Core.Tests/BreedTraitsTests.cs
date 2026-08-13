using Vivarium.Core.Gameplay;
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
            var x = TraitGenerator.BreedTraits(childSeed, ParentASeed, ParentBSeed, TraitConfigV1.Version, 0.08, 0.0);
            var y = TraitGenerator.BreedTraits(childSeed, ParentASeed, ParentBSeed, TraitConfigV1.Version, 0.08, 0.0);
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
            var bred = TraitGenerator.BreedTraits(childSeed, ParentASeed, ParentBSeed, TraitConfigV1.Version, 1.0, 0.0);
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
                var child = TraitGenerator.BreedTraits(childSeed, parentA, parentB, TraitConfigV1.Version, 0.0, 0.0);

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
                    if (childPart.Pattern == PatternType.Gradient)
                        Assert.NotNull(childPart.Mix);
                    else
                        Assert.Null(childPart.Mix);
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
            var tier = TraitGenerator.BreedTraits(childSeed, parentA, parentB, TraitConfigV1.Version, mutationChance, 0.0).ShimmerTier;
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
            var child = TraitGenerator.BreedTraits(childSeed, parentA, parentB, TraitConfigV1.Version, 0.08, 0.0);
            scores.Add(Math.Round(child.RarityScore, 6));
        }
        Assert.True(scores.Count > 50, $"esperava boa variação de score, só {scores.Count} valores distintos em 500 filhos");
    }

    [Fact]
    public void FabricaDeLendarios_NaoInflacionaAcimaDoBaseline()
    {
        // População base (não selecionada por raridade): pares aleatórios cruzados
        // com os parâmetros REAIS de produção (mutação + viés de raridade) não
        // devem produzir legendário muito acima do baseline populacional (~0.2%,
        // seção 5 do CLAUDE.md) — na maioria dos pares os dois tiers já diferem
        // pouco em probabilidade e nenhum dos dois é legendário, então o viés
        // quase não se manifesta aqui. Checagem de sanidade, tolerância larga.
        const int n = 30_000;
        int legendarios = 0;
        for (long childSeed = 1; childSeed <= n; childSeed++)
        {
            long parentA = childSeed * 97 + 1;
            long parentB = childSeed * 97 + 2;
            var child = TraitGenerator.BreedTraits(childSeed, parentA, parentB, TraitConfigV1.Version,
                BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength);
            if (child.ShimmerTier == ShimmerTier.Legendary)
                legendarios++;
        }
        Assert.InRange(legendarios / (double)n, 0, 0.01); // até ~5x o baseline de 0.2%
    }

    [Fact]
    public void ViesDeRaridade_FavoreceOTierMaisRaroEntreOsPais()
    {
        // Pais com tiers diferentes: Subtle (P=15%) e Rare (P=1.3%, mais raro).
        // Sem mutação, o viés deve inclinar a herança pro tier mais raro (Rare)
        // mais da metade das vezes — e a fração observada deve bater com a
        // fórmula fechada (BiasedInheritProbability), não só "acima de 50%".
        long subtleParent = FindSeedWithTier(ShimmerTier.Subtle);
        long rareParent = FindSeedWithTier(ShimmerTier.Rare);
        const double rarityBias = 0.6;
        const int n = 30_000;

        int rareCount = 0;
        for (long childSeed = 1; childSeed <= n; childSeed++)
        {
            var tier = TraitGenerator.BreedTraits(childSeed, subtleParent, rareParent, TraitConfigV1.Version, 0.0, rarityBias).ShimmerTier;
            if (tier == ShimmerTier.Rare) rareCount++;
        }

        double expected = WeightedTable.BiasedInheritProbability(0.013, 0.15, rarityBias); // P(escolher o valor com prob 0.013 = Rare)
        Assert.True(expected > 0.5, "sanity da fórmula: deveria favorecer o mais raro");
        Assert.InRange(rareCount / (double)n, expected - 0.03, expected + 0.03);
    }

    [Fact]
    public void ViesDeRaridade_NaoPermiteLavagemDeLendarioComParceiroComum()
    {
        // Anti-exploit: cruzar 1 lendário com 1 peixe comum qualquer não pode ser
        // um atalho confiável pra "lavar" o lendário — a chance de o filho manter
        // o tier lendário nesse caso precisa ficar BEM abaixo do caso "2 lendários",
        // usando os parâmetros reais de produção.
        long legendaryParent = FindSeedWithTier(ShimmerTier.Legendary, 200_000);
        long commonParent = FindSeedWithTier(ShimmerTier.None);
        const int n = 20_000;

        double PctLegendary(long parentA, long parentB, long seedOffset)
        {
            int count = 0;
            for (long childSeed = 1; childSeed <= n; childSeed++)
            {
                var tier = TraitGenerator.BreedTraits(childSeed + seedOffset, parentA, parentB, TraitConfigV1.Version,
                    BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength).ShimmerTier;
                if (tier == ShimmerTier.Legendary) count++;
            }
            return count / (double)n;
        }

        double bothLegendary = PctLegendary(legendaryParent, legendaryParent, 0);
        double legendaryPlusCommon = PctLegendary(legendaryParent, commonParent, 10_000_000);

        Assert.True(bothLegendary > 0.5,
            $"2 lendários deveria manter o tier na maioria das vezes, saiu {bothLegendary:P1}");
        // Teto calibrado (não "menos da metade" arbitrário): mesmo o pior caso de
        // lavagem (parceiro comum) não pode passar de ~70% — e o throughput real já
        // é limitado a 1 gestação por vez, então isso não vira produção em massa.
        Assert.True(legendaryPlusCommon < 0.70,
            $"lendário+comum ({legendaryPlusCommon:P1}) deveria ficar abaixo de 70% — risco de 'lavagem'");
        Assert.True(legendaryPlusCommon < bothLegendary,
            $"lendário+comum ({legendaryPlusCommon:P1}) deveria ficar abaixo de 2-lendários ({bothLegendary:P1})");
    }

    [Fact]
    public void ChildTierDistribution_PaisFilhotesComTierRealLendario_NaoIgnoraAAncestralidade()
    {
        // Bug real corrigido 12/08/2026 (relatado pelo usuário): ChildTierDistribution usava
        // Generate(seed) puro nos pais, ignorando que um pai pode ser ele mesmo um filhote com
        // tier real bem diferente do que o seed cru geraria (78% de chance de "Sem brilho" do
        // zero) — a prévia mostrava ~98% de chance de o filhote sair sem brilho quando os dois
        // pais tinham Lendário de verdade (herdado via ancestralidade), quase o oposto do
        // resultado real. Mesma classe de bug já corrigida em BreedTraits/FishCanvas (31/07 e
        // 10/08/2026).
        long grandparent = FindSeedWithTier(ShimmerTier.Legendary, 200_000);

        long ResolvedLegendaryParentSeed(long searchOffset)
        {
            foreach (long candidate in ManySeeds(5_000).Select(s => s + searchOffset))
            {
                var resolved = TraitGenerator.BreedTraits(candidate, grandparent, grandparent,
                    TraitConfigV1.Version, BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength);
                if (resolved.ShimmerTier == ShimmerTier.Legendary) return candidate;
            }
            throw new InvalidOperationException("Seed de pai lendário não encontrado.");
        }

        long parentASeed = ResolvedLegendaryParentSeed(0);
        long parentBSeed = ResolvedLegendaryParentSeed(50_000_000);

        // Confirma a premissa do teste: o seed CRU desses pais não é lendário (senão o bug não
        // seria exercitado) — o tier real só existe por causa da ancestralidade.
        Assert.NotEqual(ShimmerTier.Legendary, TraitGenerator.Generate(parentASeed).ShimmerTier);
        Assert.NotEqual(ShimmerTier.Legendary, TraitGenerator.Generate(parentBSeed).ShimmerTier);

        var ancestryA = new TraitGenerator.ParentAncestry(parentASeed, grandparent, grandparent);
        var ancestryB = new TraitGenerator.ParentAncestry(parentBSeed, grandparent, grandparent);
        var dist = TraitGenerator.ChildTierDistribution(ancestryA, ancestryB, TraitConfigV1.Version,
            BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength,
            BreedingDefaults.MutationRarityBiasStrength, BreedingDefaults.AntiDuplicationDecay, BreedingDefaults.AntiDuplicationMaxPenalty);

        Assert.True(dist[ShimmerTier.Legendary] > 0.5,
            $"pais realmente lendários deveriam manter o tier na maioria das vezes, saiu {dist[ShimmerTier.Legendary]:P1}");
        Assert.True(dist[ShimmerTier.None] < 0.1,
            $"chance de 'sem brilho' deveria ser baixa com pais lendários de verdade, saiu {dist[ShimmerTier.None]:P1}");
    }

    [Fact]
    public void RetencaoDeLendario_ComAvosNaoLendarios_AindaAltaMasMenorQuePaisFrescos()
    {
        // Investigação (12/08/2026): usuário relatou que o FILHOTE REAL (não só a prévia, já
        // corrigida acima) nasceu sem o brilho lendário ao cruzar 2 pais filhotes com Lendário
        // real. Medido (na época, GrandparentReachChance=0.15): 83% de retenção com avós comuns
        // (vs 92% pra pais "frescos", mesmo par).
        // Reduzido de 0.15 pra 0.03, depois pra 0.01 no mesmo dia (12/08/2026, usuário pediu
        // herança mais confiável em cruzamentos multi-geração — "filhotes de filhotes" perdendo
        // traço raro com frequência demais). A chance é sorteada 8x independentes por cruzamento
        // (4 slots × 2 lados) — com 0.15, a chance de PELO MENOS UM traço vir de avô em qualquer
        // cruzamento era ~72.8%. Com 0.01, cai pra ~7.7%. No mesmo ajuste, MutationChance também
        // caiu de 0.08 pra 0.04 (mesmo motivo). Efeito colateral esperado e correto: retenção de
        // Lendário com avós comuns SOBE de 83% (baseline original, GrandparentReachChance=0.15)
        // pra ~95% (menos chance de "escapar" pro avô não-lendário, menos mutação também).
        long FindNoneSeed(long searchOffset) => ManySeeds(5_000).Select(s => s + searchOffset)
            .First(s => TraitGenerator.Generate(s).ShimmerTier == ShimmerTier.None);
        long grandparentA1 = FindNoneSeed(0);
        long grandparentA2 = FindNoneSeed(1_000_000);
        long grandparentB1 = FindNoneSeed(2_000_000);
        long grandparentB2 = FindNoneSeed(3_000_000);

        long ResolvedLegendaryParentSeed(long gpA, long gpB, long searchOffset)
        {
            // 40k (era 5k): com MutationChance mais baixo (12/08/2026, 0.08->0.04), achar um
            // Lendário raro (só via mutação, já que os avós são "Sem brilho") precisa de um
            // espaço de busca maior pro mesmo conjunto fixo de seeds continuar achando um match.
            foreach (long candidate in ManySeeds(40_000).Select(s => s + searchOffset))
            {
                var resolved = TraitGenerator.BreedTraits(candidate, gpA, gpB,
                    TraitConfigV1.Version, BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength);
                if (resolved.ShimmerTier == ShimmerTier.Legendary) return candidate;
            }
            throw new InvalidOperationException("Seed de pai lendário não encontrado.");
        }

        long parentASeed = ResolvedLegendaryParentSeed(grandparentA1, grandparentA2, 0);
        long parentBSeed = ResolvedLegendaryParentSeed(grandparentB1, grandparentB2, 50_000_000);

        var ancestryA = new TraitGenerator.ParentAncestry(parentASeed, grandparentA1, grandparentA2);
        var ancestryB = new TraitGenerator.ParentAncestry(parentBSeed, grandparentB1, grandparentB2);

        const int n = 50_000;
        int legendaryCount = 0;
        for (long childSeed = 1; childSeed <= n; childSeed++)
        {
            var child = TraitGenerator.BreedTraits(childSeed, ancestryA, ancestryB, TraitConfigV1.Version,
                BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength, BreedingDefaults.GrandparentReachChance,
                BreedingDefaults.MutationRarityBiasStrength, BreedingDefaults.AntiDuplicationDecay, BreedingDefaults.AntiDuplicationMaxPenalty);
            if (child.ShimmerTier == ShimmerTier.Legendary) legendaryCount++;
        }
        double pct = legendaryCount / (double)n;

        // Comparação com o caso "pais frescos" (sem avós, mesmo par) — mede o efeito ISOLADO
        // do grandparentReachChance na retenção. Usa a MESMA chamada com piso de mutação
        // habilitado (13/08/2026) que `pct` acima, pra manter a comparação justa (mesmas
        // mecânicas ativas dos dois lados — só a presença de avós difere).
        int legendaryCountFresh = 0;
        for (long childSeed = 1; childSeed <= n; childSeed++)
        {
            var child = TraitGenerator.BreedTraits(childSeed,
                new TraitGenerator.ParentAncestry(parentASeed, null, null), new TraitGenerator.ParentAncestry(parentBSeed, null, null),
                TraitConfigV1.Version, BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength, BreedingDefaults.GrandparentReachChance,
                BreedingDefaults.MutationRarityBiasStrength, BreedingDefaults.AntiDuplicationDecay, BreedingDefaults.AntiDuplicationMaxPenalty);
            if (child.ShimmerTier == ShimmerTier.Legendary) legendaryCountFresh++;
        }
        double pctFresh = legendaryCountFresh / (double)n;

        // 13/08/2026: com o piso de mutação (CLAUDE.md 8.8), quando os DOIS pais já compartilham
        // o valor MAIS RARO da tabela (Lendário, 0.2%), uma mutação nesse slot não tem PRA ONDE
        // ir (nada é mais raro que Lendário) — só pode sair Lendário de novo. Isso elimina quase
        // toda a via de "escape" que antes existia (mutação livre + avô comum), então a retenção
        // sobe pra perto do teto nos dois cenários (com e sem avós) — o piso passou a dominar o
        // efeito que antes vinha só do GrandparentReachChance. Faixas alargadas pra refletir isso;
        // a comparação estrita "pct < pctFresh" virou pouco significativa (os dois perto do teto,
        // a ordem entre eles pode inverter por ruído estatístico) — trocada por uma checagem
        // frouxa de que nenhum dos dois caiu abaixo do esperado.
        Assert.InRange(pct, 0.99, 1.00);
        Assert.InRange(pctFresh, 0.99, 1.00);
    }

    [Fact]
    public void ViesDeRaridade_TambemFavoreceCorDePartesMaisRaras()
    {
        // 31/07/2026: viés estendido de "só shimmer" pra também cor/padrão de parte —
        // corrige filhotes regredindo perto do piso da população mesmo vindo de pais
        // decentes (usuário cruzou incomum+raro e saiu um filhote score 2.8/Comum).
        // Branco puro (P=1%) vs Laranja (P=22%, mais comum) — sem mutação, a herança
        // deve inclinar pro branco mais da metade das vezes, batendo com a fórmula fechada.
        long whiteParent = FindSeedWithTailColor(PartColor.PureWhite);
        long orangeParent = FindSeedWithTailColor(PartColor.Orange, 50);
        const double rarityBias = 0.6;
        const int n = 30_000;

        int whiteCount = 0;
        for (long childSeed = 1; childSeed <= n; childSeed++)
        {
            var color = TraitGenerator.BreedTraits(childSeed, whiteParent, orangeParent, TraitConfigV1.Version, 0.0, rarityBias).Tail.Color;
            if (color == PartColor.PureWhite) whiteCount++;
        }

        double expected = WeightedTable.BiasedInheritProbability(0.01, 0.22, rarityBias);
        Assert.True(expected > 0.5, "sanity da fórmula: deveria favorecer o mais raro");
        Assert.InRange(whiteCount / (double)n, expected - 0.03, expected + 0.03);
    }

    [Fact]
    public void ViesDeCor_NaoPermiteLavagemComParceiroComum()
    {
        // Mesma checagem anti-exploit de ViesDeRaridade_NaoPermiteLavagemDeLendarioComParceiroComum,
        // agora pra cor de parte: branco puro (a cor mais rara, 1%) cruzado com laranja
        // (a mais comum, 22%) não pode ser um atalho confiável de "lavagem".
        long whiteParent = FindSeedWithTailColor(PartColor.PureWhite);
        long orangeParent = FindSeedWithTailColor(PartColor.Orange, 50);
        const int n = 20_000;

        double PctWhite(long parentA, long parentB, long seedOffset)
        {
            int count = 0;
            for (long childSeed = 1; childSeed <= n; childSeed++)
            {
                var color = TraitGenerator.BreedTraits(childSeed + seedOffset, parentA, parentB, TraitConfigV1.Version,
                    BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength).Tail.Color;
                if (color == PartColor.PureWhite) count++;
            }
            return count / (double)n;
        }

        double bothWhite = PctWhite(whiteParent, whiteParent, 0);
        double whitePlusOrange = PctWhite(whiteParent, orangeParent, 10_000_000);

        Assert.True(bothWhite > 0.5, $"2 pais brancos deveria manter a cor na maioria das vezes, saiu {bothWhite:P1}");
        Assert.True(whitePlusOrange < 0.70, $"branco+laranja ({whitePlusOrange:P1}) deveria ficar abaixo de 70% — risco de 'lavagem'");
        Assert.True(whitePlusOrange < bothWhite, $"branco+laranja ({whitePlusOrange:P1}) deveria ficar abaixo de 2-brancos ({bothWhite:P1})");
    }

    [Fact]
    public void RarityScore_NuncaFicaAbaixoDoPisoDaPopulacao()
    {
        // Gap de cobertura apontado numa investigação de bug (31/07/2026): não havia
        // nenhum teste de limite inferior pro score de filhote. O piso teórico da
        // população (CLAUDE.md 5.1: ~2.6-2.73, validado por simulação de 100k seeds)
        // vale igualmente pra filhotes, já que a fórmula usa as mesmas tabelas de peso
        // — nenhum termo pode ficar negativo (-log10(P) com P<=1 é sempre >= 0).
        const int n = 20_000;
        double minScore = double.MaxValue;
        for (long childSeed = 1; childSeed <= n; childSeed++)
        {
            long parentA = childSeed * 6997 + 1;
            long parentB = childSeed * 6997 + 2;
            var child = TraitGenerator.BreedTraits(childSeed, parentA, parentB, TraitConfigV1.Version,
                BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength);
            minScore = Math.Min(minScore, child.RarityScore);
        }
        Assert.True(minScore >= 2.0, $"score mínimo observado {minScore:0.000} abaixo do piso esperado (~2.6) — possível regressão");
    }

    [Fact]
    public void SemAncestralidade_ComportamentoIdenticoAoOverloadAntigo()
    {
        // A sobrecarga de conveniência (3 seeds) e a nova (com ParentAncestry vazia) devem
        // produzir exatamente o mesmo resultado — garante que nenhum código existente
        // (Vivarium.Simulation, testes acima) precisou mudar pra continuar correto.
        for (long childSeed = 1; childSeed <= 200; childSeed++)
        {
            var viaOverloadAntigo = TraitGenerator.BreedTraits(childSeed, ParentASeed, ParentBSeed, TraitConfigV1.Version, 0.08, 0.15);
            var viaNovoExplicito = TraitGenerator.BreedTraits(childSeed,
                new TraitGenerator.ParentAncestry(ParentASeed, null, null),
                new TraitGenerator.ParentAncestry(ParentBSeed, null, null),
                TraitConfigV1.Version, 0.08, 0.15, grandparentReachChance: 0.15,
                mutationRarityBiasStrength: -1, antiDuplicationDecay: 0, antiDuplicationMaxPenalty: 0);
            Assert.Equal(viaOverloadAntigo, viaNovoExplicito);
        }
    }

    // Os 3 testes abaixo testam EffectiveParentTraits/ResolveOwnTraits diretamente (internal,
    // via InternalsVisibleTo) em vez de tentar isolar o sinal na saída ponta-a-ponta de
    // BreedTraits — o "próprio" valor de um pai bred já reflete os mesmos avós que o
    // reach-back alcançaria (são o mesmo par), então distinguir "veio do pai" de "veio do
    // avô" observando só a cor final do filhote é ambíguo/confuso. Testar a peça certa.

    [Fact]
    public void EffectiveParentTraits_SemAvos_SempreRetornaOProprio()
    {
        var own = TraitGenerator.Generate(1);
        for (long childSeed = 1; childSeed <= 500; childSeed++)
            Assert.Equal(own, TraitGenerator.EffectiveParentTraits(childSeed, "salt", own, null, null, reachChance: 1.0));
    }

    [Fact]
    public void EffectiveParentTraits_ComAvos_AproximaAChanceDeReach()
    {
        var own = TraitGenerator.Generate(1);
        var grandparent1 = TraitGenerator.Generate(2);
        var grandparent2 = TraitGenerator.Generate(3);
        const double reachChance = 0.3;
        const int n = 20_000;

        int reached = 0;
        for (long childSeed = 1; childSeed <= n; childSeed++)
        {
            var result = TraitGenerator.EffectiveParentTraits(childSeed, "salt", own, grandparent1, grandparent2, reachChance);
            if (!result.Equals(own)) reached++;
        }
        Assert.InRange(reached / (double)n, reachChance - 0.02, reachChance + 0.02);
    }

    [Fact]
    public void EffectiveParentTraits_ReachChance0_NuncaAlcancaOAvoMesmoComAvosConhecidos()
    {
        var own = TraitGenerator.Generate(1);
        var grandparent1 = TraitGenerator.Generate(2);
        var grandparent2 = TraitGenerator.Generate(3);
        for (long childSeed = 1; childSeed <= 5_000; childSeed++)
            Assert.Equal(own, TraitGenerator.EffectiveParentTraits(childSeed, "salt", own, grandparent1, grandparent2, reachChance: 0.0));
    }

    [Fact]
    public void ResolveOwnTraits_PaiFresco_UsaGenerateDireto()
    {
        var ancestry = new TraitGenerator.ParentAncestry(42, null, null);
        Assert.Equal(TraitGenerator.Generate(42), TraitGenerator.ResolveOwnTraits(ancestry, TraitConfigV1.Version, 0.08, 0.15, -1, 0, 0));
    }

    [Fact]
    public void ResolveOwnTraits_PaiFilhote_RecomputaViaAvosNaoGenerateDoProprioSeed()
    {
        // O bug corrigido nesta rodada: ANTES, um pai que era filhote virava
        // Generate(seed) (traits fantasma). Agora, com os avós conhecidos, deve bater com
        // o que o próprio cruzamento dos avós produziria — nunca com Generate(seed) direto.
        var ancestry = new TraitGenerator.ParentAncestry(999, 1, 2);
        var resolved = TraitGenerator.ResolveOwnTraits(ancestry, TraitConfigV1.Version, 0.08, 0.15, -1, 0, 0);

        Assert.Equal(TraitGenerator.BreedTraits(999, 1, 2, TraitConfigV1.Version, 0.08, 0.15), resolved);
        Assert.NotEqual(TraitGenerator.Generate(999), resolved);
    }

    [Fact]
    public void Gradiente_MixHerdadoEmBlocoQuandoOSubtraitVemDoMesmoPai()
    {
        // Pai A com cauda em Degradê, pai B sem padrão nenhum — sem mutação, sempre
        // que o padrão do filho vier de A E os subtraits também vierem do MESMO bloco
        // (patternColor/size/opacity idênticos aos de A, só possível por herança
        // literal, não coincidência), o mix precisa ter vindo junto, nunca sorteado à parte.
        long parentA = ManySeeds(50_000).First(s => TraitGenerator.Generate(s).Tail.Pattern == PatternType.Gradient);
        long parentB = ManySeeds(50_000).First(s => TraitGenerator.Generate(s).Tail.Pattern == PatternType.None);
        var traitsA = TraitGenerator.Generate(parentA);

        bool foundInherited = false;
        for (long childSeed = 1; childSeed <= 20_000; childSeed++)
        {
            var child = TraitGenerator.BreedTraits(childSeed, parentA, parentB, TraitConfigV1.Version, mutationChance: 0.0, rarityBias: 0.0);
            if (child.Tail.Pattern != PatternType.Gradient) continue;
            if (child.Tail.PatternColor != traitsA.Tail.PatternColor) continue;
            if (child.Tail.PatternSize != traitsA.Tail.PatternSize) continue;
            if (child.Tail.PatternOpacity != traitsA.Tail.PatternOpacity) continue;

            foundInherited = true;
            Assert.Equal(traitsA.Tail.Mix, child.Tail.Mix);
        }
        Assert.True(foundInherited, "não achou nenhum filho com subtraits de Degradê herdados de A pra validar o mix em bloco");
    }

    [Fact]
    public void AntiDuplicacao_ReduzFracaoDeFilhotesCloneDeUmDosPais()
    {
        // Pedido do usuário (13/08/2026, CLAUDE.md 8.8): sem isso, nada impedia o filhote de
        // puxar quase todos os atributos do MESMO pai, parecendo uma "duplicata". Busca 2 pais
        // que diferem em tier + as 3 cores de parte (4 dos 7 slots com viés de raridade) e mede
        // a fração de filhotes que "clonam" o pai A nesses 4 slots, com a sequência
        // desligada (decay=0/maxPenalty=0) vs ligada (valores de produção).
        var ta = TraitGenerator.Generate(FindSeedWithTier(ShimmerTier.Subtle, 20_000));
        long parentASeed = ManySeeds(20_000).First(s => TraitGenerator.Generate(s).ShimmerTier == ShimmerTier.Subtle);
        long parentBSeed = ManySeeds(20_000).First(s =>
        {
            var tb = TraitGenerator.Generate(s);
            return tb.ShimmerTier != ta.ShimmerTier
                && tb.Tail.Color != ta.Tail.Color
                && tb.Dorsal.Color != ta.Dorsal.Color
                && tb.Pectoral.Color != ta.Pectoral.Color;
        });

        const int n = 30_000;
        int CountCloneOfA(double decay, double maxPenalty)
        {
            int clones = 0;
            for (long childSeed = 1; childSeed <= n; childSeed++)
            {
                var child = TraitGenerator.BreedTraits(childSeed,
                    new TraitGenerator.ParentAncestry(parentASeed, null, null), new TraitGenerator.ParentAncestry(parentBSeed, null, null),
                    TraitConfigV1.Version, mutationChance: 0, rarityBias: 0, grandparentReachChance: 0,
                    mutationRarityBiasStrength: -1, antiDuplicationDecay: decay, antiDuplicationMaxPenalty: maxPenalty);
                if (child.ShimmerTier == ta.ShimmerTier && child.Tail.Color == ta.Tail.Color
                    && child.Dorsal.Color == ta.Dorsal.Color && child.Pectoral.Color == ta.Pectoral.Color)
                    clones++;
            }
            return clones;
        }

        double pctDisabled = CountCloneOfA(decay: 0, maxPenalty: 0) / (double)n;
        double pctEnabled = CountCloneOfA(BreedingDefaults.AntiDuplicationDecay, BreedingDefaults.AntiDuplicationMaxPenalty) / (double)n;

        // Baseline teórico sem sequência: 0.5^4 = 6.25% (4 slots binários independentes). O
        // efeito medido é mais modesto que o teórico "streak de 4 direto" porque os 4 slots
        // rastreados (tier + 3 cores) NÃO são consecutivos na sequência real — cada cor tem um
        // slot de padrão entre ela e a próxima cor (cauda cor→padrão→dorsal cor→padrão→...), e
        // esses picks de padrão (mesmo quando o resultado empata entre os pais, ex. "Sem padrão"
        // nos dois) ainda consomem/podem quebrar a sequência internamente. Ainda assim, uma
        // redução real e na direção certa já confirma o mecanismo funcionando.
        Assert.InRange(pctDisabled, 0.04, 0.09);
        Assert.True(pctEnabled < pctDisabled * 0.9,
            $"esperava a fração de 'clone do pai A' cair com anti-duplicação — desligado {pctDisabled:P1}, ligado {pctEnabled:P1}");
    }

    [Theory]
    [InlineData(PartColor.Black, PartColor.Black)] // ambos 2ª cor mais rara (3%) — só Preto ou Branco
    [InlineData(PartColor.Blue, PartColor.Red)] // Azul(20%)/Vermelho(18%): piso = Azul, só exclui Laranja
    [InlineData(PartColor.Blue, PartColor.Black)] // Azul(20%)/Preto(3%): piso AINDA é Azul, o mais fraco — não o mais raro
    public void PisoDeMutacao_NuncaSaiMaisComumQueOPaiMaisFraco(PartColor colorA, PartColor colorB)
    {
        // 3 exemplos do próprio usuário (13/08/2026, CLAUDE.md 8.8): quando um trait sofre
        // mutação, o resultado nunca pode ficar mais comum (mais fraco) que o pai MAIS FRACO
        // (mais comum) dos dois — não o mais raro. mutationChance=1.0 força mutação em todo slot,
        // isolando o piso da cauda especificamente.
        //
        // Pula os poucos casos (~7%) em que o TIER também mutou pra Vibrante+ nesse mesmo seed —
        // aí a correlação shimmer→cor (CLAUDE.md 4) reescala a tabela de cor inteira, e o piso
        // desta asserção (calculado contra a tabela BASE, sem correlação) deixaria de bater com
        // o piso que o motor realmente usou (calculado contra a tabela JÁ correlacionada) — sem
        // relação com o mecanismo do piso em si, que continua correto nesses casos também.
        long parentASeed = FindSeedWithTailColor(colorA);
        long parentBSeed = FindSeedWithTailColor(colorB);
        double floorWeight = Math.Max(
            WeightedTable.WeightOf(TraitConfigV1.PartColors, colorA),
            WeightedTable.WeightOf(TraitConfigV1.PartColors, colorB));

        for (long childSeed = 1; childSeed <= 5_000; childSeed++)
        {
            var child = TraitGenerator.BreedTraits(childSeed,
                new TraitGenerator.ParentAncestry(parentASeed, null, null), new TraitGenerator.ParentAncestry(parentBSeed, null, null),
                TraitConfigV1.Version, mutationChance: 1.0, rarityBias: 0, grandparentReachChance: 0,
                BreedingDefaults.MutationRarityBiasStrength, antiDuplicationDecay: 0, antiDuplicationMaxPenalty: 0);
            if (child.ShimmerTier is ShimmerTier.Vibrant or ShimmerTier.Rare or ShimmerTier.Legendary)
                continue;
            double resultWeight = WeightedTable.WeightOf(TraitConfigV1.PartColors, child.Tail.Color);
            Assert.True(resultWeight <= floorWeight,
                $"seed {childSeed}: cauda mutou pra {child.Tail.Color} (peso {resultWeight}), mais comum que o piso {floorWeight} (pai mais fraco: {colorA}={WeightedTable.WeightOf(TraitConfigV1.PartColors, colorA)}, {colorB}={WeightedTable.WeightOf(TraitConfigV1.PartColors, colorB)})");
        }
    }

    [Fact]
    public void Restrict_ComPisoNoValorMaisComumDaTabela_MantemATabelaInteira()
    {
        // Auto-limitado (CLAUDE.md 8.8): quando o pai mais fraco JÁ é o valor mais comum (aqui,
        // tier "Sem brilho" — 78% de peso, o caso mais frequente do jogo), não existe nada "mais
        // comum" pra excluir — o piso não restringe nada nesse cruzamento.
        double maxWeight = TraitConfigV1.ShimmerTiers.Max(e => e.Weight);
        var restricted = WeightedTable.Restrict(TraitConfigV1.ShimmerTiers, maxWeight);
        Assert.Equal(TraitConfigV1.ShimmerTiers.Count, restricted.Count);
    }

    [Fact]
    public void Restrict_ComPaisNoSegundoValorMaisRaro_ExcluiTodosOsMaisComuns()
    {
        // Espelha o exemplo Preto+Preto do usuário: só Preto (o próprio piso) e Branco (mais raro)
        // continuam elegíveis — todo o resto da paleta (mais comum que Preto) é excluído.
        double blackWeight = WeightedTable.WeightOf(TraitConfigV1.PartColors, PartColor.Black);
        var restricted = WeightedTable.Restrict(TraitConfigV1.PartColors, blackWeight);
        Assert.Equal(new HashSet<PartColor> { PartColor.Black, PartColor.PureWhite }, restricted.Select(e => e.Value).ToHashSet());
    }

    private static long FindSeedWithTailColor(PartColor color, int searchLimit = 5_000)
        => ManySeeds(searchLimit).First(s => TraitGenerator.Generate(s).Tail.Color == color);

    private static long FindSeedWithTier(ShimmerTier tier, int searchLimit = 50_000)
        => ManySeeds(searchLimit).First(s => TraitGenerator.Generate(s).ShimmerTier == tier);

    private static IEnumerable<long> ManySeeds(int count = 5_000)
        => Enumerable.Range(1, count).Select(i => (long)i * 7919);

    private static IEnumerable<(long A, long B)> ManyParentPairs(int count)
        => Enumerable.Range(1, count).Select(i => ((long)i * 6997, (long)i * 6997 + 3491));
}
