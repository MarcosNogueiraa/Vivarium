using System.Globalization;
using Vivarium.Core.Gameplay;
using Vivarium.Core.Generation;

// Simulação de validação dos pesos (CLAUDE.md, próximo passo 1):
// gera N seeds, compara distribuição real vs esperada e mostra a curva de rarity score.
//
// Modo alternativo: `dump [N]` imprime traits em formato canônico (1 linha por seed),
// usado pra verificar que ports do motor (ex: o JS do protótipo Canvas) batem com o C#.

if (args.Length >= 1 && args[0] == "dump")
{
    int count = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 1000;
    for (int i = 1; i <= count; i++)
    {
        DumpLine(i * 7919L - i);
        DumpLine(-(i * 104729L + 3));
    }
    return;
}

if (args.Length >= 1 && args[0] == "seed")
{
    if (args.Length < 2 || !long.TryParse(args[1], out var oneSeed))
    {
        Console.WriteLine("Uso: dotnet run --project tools/Vivarium.Simulation -- seed <seed>");
        return;
    }
    DumpLine(oneSeed);
    return;
}

if (args.Length >= 1 && args[0] == "best")
{
    int count = args.Length > 1 && int.TryParse(args[1], out var bcount) ? bcount : 5_000_000;
    int top = args.Length > 2 && int.TryParse(args[2], out var bt) ? bt : 5;
    BestSeeds(count, top);
    return;
}

// `bestpattern <count> <top> <minPartsComPadrao> [PatternType]` — igual ao `best`, mas só
// considera seeds em que pelo menos N partes (cauda/dorsal/peitoral) saíram com um padrão
// específico (default Marble, o mais raro — 0.05% por parte). Busca paralela porque
// filtrar por um padrão raro em 1 parte só já exige ~667 amostras em média por acerto, e
// 2+ partes exige ordens de magnitude mais.
if (args.Length >= 1 && args[0] == "bestpattern")
{
    long count = args.Length > 1 && long.TryParse(args[1], out var pc) ? pc : 20_000_000L;
    int top = args.Length > 2 && int.TryParse(args[2], out var pt) ? pt : 5;
    int minParts = args.Length > 3 && int.TryParse(args[3], out var pm) ? pm : 1;
    var wantedPattern = args.Length > 4 && Enum.TryParse<PatternType>(args[4], out var pp) ? pp : PatternType.Marble;
    BestByPattern(count, top, minParts, wantedPattern);
    return;
}

if (args.Length >= 1 && args[0] == "economy")
{
    EconomyReport();
    return;
}

if (args.Length >= 1 && args[0] == "breed")
{
    BreedReport();
    return;
}

if (args.Length >= 1 && args[0] == "simulate")
{
    SimulateReport();
    return;
}

if (args.Length >= 1 && args[0] == "breeddump")
{
    int count = args.Length > 1 && int.TryParse(args[1], out var bc) ? bc : 500;
    for (int i = 1; i <= count; i++)
    {
        long parentASeed = i * 7919L - i;
        long parentBSeed = -(i * 104729L + 3);
        long childSeed = i * 131071L + 17;
        BreedDumpLine(childSeed, parentASeed, parentBSeed);
    }
    return;
}

if (args.Length >= 1 && args[0] == "grandparentdump")
{
    // Crosscheck do mecanismo de avós (31/07/2026) contra o port JS — mesmo princípio do
    // breeddump: seeds determinísticos, alguns pares COM ancestralidade de avós (pai é
    // filhote), outros sem (pai fresco), pra cobrir os dois ramos de EffectiveParentTraits.
    int count = args.Length > 1 && int.TryParse(args[1], out var gc) ? gc : 500;
    for (int i = 1; i <= count; i++)
    {
        long parentASeed = i * 7919L - i;
        long parentBSeed = -(i * 104729L + 3);
        long childSeed = i * 131071L + 17;
        // Metade dos casos com avós no lado A, outra metade também no lado B — cobre pai
        // fresco/pai-filhote nas duas posições.
        long? gpAA = i % 2 == 0 ? i * 271L + 11 : null;
        long? gpAB = i % 2 == 0 ? -(i * 379L + 13) : null;
        long? gpBA = i % 3 == 0 ? i * 431L + 17 : null;
        long? gpBB = i % 3 == 0 ? -(i * 521L + 19) : null;
        GrandparentDumpLine(childSeed, parentASeed, gpAA, gpAB, parentBSeed, gpBA, gpBB);
    }
    return;
}

int n = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 100_000;
Console.WriteLine($"Gerando {n:N0} criaturas...\n");

var all = new List<CreatureTraits>(n);
var rng = new Random(12345);
for (int i = 0; i < n; i++)
    all.Add(TraitGenerator.Generate(rng.NextInt64()));

// --- Shimmer tier ---
Console.WriteLine("SHIMMER DO CORPO           esperado    real");
Print(all, t => t.ShimmerTier, TraitConfigV1.ShimmerTiers);

// --- Cor das partes (sem correlação seria a tabela base; correlação distorce de leve) ---
Console.WriteLine("\nCOR DAS PARTES (3 por criatura)  base    real");
var partColors = all.SelectMany(t => new[] { t.Tail, t.Dorsal, t.Pectoral }).ToList();
foreach (var entry in TraitConfigV1.PartColors)
{
    double real = partColors.Count(p => p.Color == entry.Value) / (double)partColors.Count * 100;
    Console.WriteLine($"  {entry.Value,-22} {entry.Weight,6:0.0}%  {real,6:0.00}%");
}

// --- Padrão ---
Console.WriteLine("\nTIPO DE PADRÃO             esperado    real");
foreach (var entry in TraitConfigV1.PatternTypes)
{
    double real = partColors.Count(p => p.Pattern == entry.Value) / (double)partColors.Count * 100;
    Console.WriteLine($"  {entry.Value,-22} {entry.Weight,6:0.0}%  {real,6:0.00}%");
}

var patterned = partColors.Where(p => p.Pattern != PatternType.None).ToList();
double sizeExtreme = patterned.Count(p => p.PatternSize < TraitConfigV1.PatternSizeExtremeLow
                                       || p.PatternSize > TraitConfigV1.PatternSizeExtremeHigh)
                     / (double)patterned.Count * 100;
double opacityExtreme = patterned.Count(p => p.PatternOpacity < TraitConfigV1.PatternOpacityExtremeLow
                                          || p.PatternOpacity > TraitConfigV1.PatternOpacityExtremeHigh)
                        / (double)patterned.Count * 100;
Console.WriteLine($"\n  Tamanho extremo (<10 ou >90): {sizeExtreme:0.00}% dos padrões (esperado ~4.6%)");
Console.WriteLine($"  Opacidade extrema (<30 ou >80): {opacityExtreme:0.00}% dos padrões (esperado ~28.6%)");

double tailExtreme = all.Count(t => t.Movement.TailSpeed < 10 || t.Movement.TailSpeed > 90) / (double)n * 100;
double finExtreme = all.Count(t => t.Movement.FinSpeed < 10 || t.Movement.FinSpeed > 90) / (double)n * 100;
Console.WriteLine($"  Cauda extrema (<10 ou >90): {tailExtreme:0.00}% (esperado ~4.6%)");
Console.WriteLine($"  Nadadeira extrema (<10 ou >90): {finExtreme:0.00}% (esperado ~4.6%)");

// --- Rarity score ---
var scores = all.Select(t => t.RarityScore).OrderBy(s => s).ToArray();
Console.WriteLine("\nRARITY SCORE (percentis)");
foreach (var p in new[] { 1, 10, 25, 50, 75, 90, 99, 99.8, 99.99 })
    Console.WriteLine($"  p{p,-6} {Percentile(scores, p),6:0.00}");
Console.WriteLine($"  min    {scores[0],6:0.00}\n  max    {scores[^1],6:0.00}");

// Cortes que produzem a pirâmide clássica: 50% comum / 30% incomum / 15% raro / 4.8% épico / 0.2% lendário
Console.WriteLine("\nCORTES SUGERIDOS (pirâmide 50/30/15/4.8/0.2):");
Console.WriteLine($"  Comum    < {Percentile(scores, 50):0.00}");
Console.WriteLine($"  Incomum  < {Percentile(scores, 80):0.00}");
Console.WriteLine($"  Raro     < {Percentile(scores, 95):0.00}");
Console.WriteLine($"  Épico    < {Percentile(scores, 99.8):0.00}");
Console.WriteLine($"  Lendário ≥ {Percentile(scores, 99.8):0.00}");

// Faixas originais do CLAUDE.md, pra comparação
Console.WriteLine("\nFAIXAS ORIGINAIS DO CLAUDE.md (0-2 / 2-4 / 4-7 / 7-10 / 10+):");
double[] cuts = [2, 4, 7, 10];
string[] nomes = ["Comum", "Incomum", "Raro", "Épico", "Lendário"];
for (int i = 0; i <= cuts.Length; i++)
{
    double lo = i == 0 ? double.MinValue : cuts[i - 1];
    double hi = i == cuts.Length ? double.MaxValue : cuts[i];
    double pct = scores.Count(s => s >= lo && s < hi) / (double)n * 100;
    Console.WriteLine($"  {nomes[i],-9} {pct,6:0.00}%");
}

static void Print<T>(List<CreatureTraits> all, Func<CreatureTraits, T> selector, IReadOnlyList<WeightedValue<T>> expected)
    where T : notnull
{
    foreach (var entry in expected)
    {
        double real = all.Count(t => selector(t)!.Equals(entry.Value)) / (double)all.Count * 100;
        Console.WriteLine($"  {entry.Value,-22} {entry.Weight,6:0.0}%  {real,6:0.00}%");
    }
}

static void DumpLine(long seed)
{
    var t = TraitGenerator.Generate(seed);
    var inv = CultureInfo.InvariantCulture;
    var sb = new System.Text.StringBuilder();
    sb.Append(seed).Append(';').Append(t.ShimmerTier)
      .Append(';').Append(t.ShimmerColor?.ToString() ?? "-")
      .Append(';').Append(t.ShimmerOpacity.ToString("F6", inv));
    foreach (var p in new[] { t.Tail, t.Dorsal, t.Pectoral })
    {
        sb.Append(';').Append(p.Color).Append(';').Append(p.Pattern)
          .Append(';').Append(p.PatternColor?.ToString() ?? "-")
          .Append(';').Append(p.PatternSize?.ToString("F6", inv) ?? "-")
          .Append(';').Append(p.PatternOpacity?.ToString("F6", inv) ?? "-")
          .Append(';').Append(p.Mix?.ToString() ?? "-");
    }
    sb.Append(';').Append(t.Movement.TailSpeed.ToString("F6", inv))
      .Append(';').Append(t.Movement.TailAmplitude.ToString("F6", inv))
      .Append(';').Append(t.Movement.FinSpeed.ToString("F6", inv))
      .Append(';').Append(t.Movement.FinAmplitude.ToString("F6", inv));
    sb.Append(';').Append(t.RarityScore.ToString("F6", inv));
    Console.WriteLine(sb.ToString());
}

static void BreedDumpLine(long childSeed, long parentASeed, long parentBSeed)
{
    var t = TraitGenerator.BreedTraits(childSeed, parentASeed, parentBSeed, TraitConfigV1.Version,
        BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength);
    var inv = CultureInfo.InvariantCulture;
    var sb = new System.Text.StringBuilder();
    sb.Append(childSeed).Append(';').Append(parentASeed).Append(';').Append(parentBSeed)
      .Append(';').Append(t.ShimmerTier)
      .Append(';').Append(t.ShimmerColor?.ToString() ?? "-")
      .Append(';').Append(t.ShimmerOpacity.ToString("F6", inv));
    foreach (var p in new[] { t.Tail, t.Dorsal, t.Pectoral })
    {
        sb.Append(';').Append(p.Color).Append(';').Append(p.Pattern)
          .Append(';').Append(p.PatternColor?.ToString() ?? "-")
          .Append(';').Append(p.PatternSize?.ToString("F6", inv) ?? "-")
          .Append(';').Append(p.PatternOpacity?.ToString("F6", inv) ?? "-")
          .Append(';').Append(p.Mix?.ToString() ?? "-");
    }
    sb.Append(';').Append(t.Movement.TailSpeed.ToString("F6", inv))
      .Append(';').Append(t.Movement.TailAmplitude.ToString("F6", inv))
      .Append(';').Append(t.Movement.FinSpeed.ToString("F6", inv))
      .Append(';').Append(t.Movement.FinAmplitude.ToString("F6", inv));
    sb.Append(';').Append(t.RarityScore.ToString("F6", inv));
    Console.WriteLine(sb.ToString());
}

static void GrandparentDumpLine(long childSeed, long parentASeed, long? gpAA, long? gpAB, long parentBSeed, long? gpBA, long? gpBB)
{
    var ancestryA = new TraitGenerator.ParentAncestry(parentASeed, gpAA, gpAB);
    var ancestryB = new TraitGenerator.ParentAncestry(parentBSeed, gpBA, gpBB);
    var t = TraitGenerator.BreedTraits(childSeed, ancestryA, ancestryB, TraitConfigV1.Version,
        BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength, BreedingDefaults.GrandparentReachChance);
    var inv = CultureInfo.InvariantCulture;
    var sb = new System.Text.StringBuilder();
    sb.Append(childSeed).Append(';').Append(parentASeed).Append(';').Append(gpAA?.ToString() ?? "-").Append(';').Append(gpAB?.ToString() ?? "-")
      .Append(';').Append(parentBSeed).Append(';').Append(gpBA?.ToString() ?? "-").Append(';').Append(gpBB?.ToString() ?? "-")
      .Append(';').Append(t.ShimmerTier)
      .Append(';').Append(t.ShimmerColor?.ToString() ?? "-")
      .Append(';').Append(t.ShimmerOpacity.ToString("F6", inv));
    foreach (var p in new[] { t.Tail, t.Dorsal, t.Pectoral })
    {
        sb.Append(';').Append(p.Color).Append(';').Append(p.Pattern)
          .Append(';').Append(p.PatternColor?.ToString() ?? "-")
          .Append(';').Append(p.PatternSize?.ToString("F6", inv) ?? "-")
          .Append(';').Append(p.PatternOpacity?.ToString("F6", inv) ?? "-")
          .Append(';').Append(p.Mix?.ToString() ?? "-");
    }
    sb.Append(';').Append(t.Movement.TailSpeed.ToString("F6", inv))
      .Append(';').Append(t.Movement.TailAmplitude.ToString("F6", inv))
      .Append(';').Append(t.Movement.FinSpeed.ToString("F6", inv))
      .Append(';').Append(t.Movement.FinAmplitude.ToString("F6", inv));
    sb.Append(';').Append(t.RarityScore.ToString("F6", inv));
    Console.WriteLine(sb.ToString());
}

/// <summary>
/// Busca o seed de maior RarityScore entre N seeds sorteados aleatoriamente — não é o
/// "teoricamente perfeito" (nenhum seed real bate exatamente com isso, a chance conjunta é
/// minúscula demais pra qualquer busca viável), é o melhor achado de fato dentro do
/// orçamento de busca, mantendo o princípio de "tudo deriva de um seed real" (sem fabricar
/// traits). Imprime a mesma linha canônica do `dump` pro seed vencedor, pra conferência.
/// </summary>
static void BestSeeds(int count, int top)
{
    var rng = new Random();
    var best = new List<(long Seed, double Score)>();
    for (int i = 0; i < count; i++)
    {
        long seed = rng.NextInt64();
        var t = TraitGenerator.Generate(seed);
        best.Add((seed, t.RarityScore));
        if (best.Count > top * 4)
        {
            best = best.OrderByDescending(b => b.Score).Take(top).ToList();
        }
    }
    best = best.OrderByDescending(b => b.Score).Take(top).ToList();

    Console.WriteLine($"Melhor(es) seed(s) entre {count:N0} sorteados:\n");
    foreach (var (seed, score) in best)
    {
        Console.WriteLine($"seed={seed}  RarityScore={score:0.0000}");
        DumpLine(seed);
        Console.WriteLine();
    }
}

static void BestByPattern(long count, int top, int minParts, PatternType wantedPattern)
{
    int threads = Environment.ProcessorCount;
    long perThread = count / threads;
    var results = new System.Collections.Concurrent.ConcurrentBag<(long Seed, double Score, int Matches)>();

    Console.WriteLine($"Buscando entre {count:N0} seeds ({threads} threads, ~{perThread:N0} cada) por >= {minParts} parte(s) com padrão {wantedPattern}...\n");
    var sw = System.Diagnostics.Stopwatch.StartNew();

    System.Threading.Tasks.Parallel.For(0, threads, threadIndex =>
    {
        var rng = new Random(Guid.NewGuid().GetHashCode());
        var localBest = new List<(long Seed, double Score, int Matches)>();
        for (long i = 0; i < perThread; i++)
        {
            long seed = rng.NextInt64();
            var t = TraitGenerator.Generate(seed);
            int matches = 0;
            if (t.Tail.Pattern == wantedPattern) matches++;
            if (t.Dorsal.Pattern == wantedPattern) matches++;
            if (t.Pectoral.Pattern == wantedPattern) matches++;
            if (matches < minParts) continue;

            localBest.Add((seed, t.RarityScore, matches));
            if (localBest.Count > top * 4)
                localBest = localBest.OrderByDescending(b => b.Matches).ThenByDescending(b => b.Score).Take(top).ToList();
        }
        foreach (var r in localBest) results.Add(r);
    });

    sw.Stop();
    var best = results.OrderByDescending(b => b.Matches).ThenByDescending(b => b.Score).Take(top).ToList();

    Console.WriteLine($"Busca terminada em {sw.Elapsed.TotalSeconds:0.0}s — {results.Count} seed(s) bateram o filtro.\n");
    if (best.Count == 0)
    {
        Console.WriteLine("Nenhum seed encontrado com esse critério dentro do orçamento de busca — tente aumentar <count> ou reduzir <minParts>.");
        return;
    }
    foreach (var (seed, score, matches) in best)
    {
        Console.WriteLine($"seed={seed}  RarityScore={score:0.0000}  Partes com {wantedPattern}={matches}");
        DumpLine(seed);
        Console.WriteLine();
    }
}

static void EconomyReport()
{
    var cfg = TickConfig.Default;
    int interval = HabitatDefaults.GenerationIntervalMinutes;

    // Probabilidade de lendário (score >= 14.0, faixa v2) por peixe coletado
    const double LegendaryCut = 14.0;
    int N = 200_000, leg = 0;
    var rng = new Random(7);
    for (int i = 0; i < N; i++)
        if ((double)TraitGenerator.Generate(rng.NextInt64()).RarityScore >= LegendaryCut) leg++;
    double p = leg / (double)N;

    Console.WriteLine($"Geração: 1 peixe / {interval} min = {60.0 / interval:0.0}/h online");
    Console.WriteLine($"P(lendário, score>={LegendaryCut}) = {p * 100:0.000}%\n");
    Console.WriteLine("Cadência de lendário por perfil (coleta online contínua):");
    foreach (var (name, hrs) in new[] { ("casual 2h/dia", 2.0), ("ativo 8h/dia", 8.0), ("dedicado 16h/dia", 16.0) })
    {
        double perWeek = (60.0 / interval) * hrs * 7;
        double legPerWeek = perWeek * p;
        Console.WriteLine($"  {name,-16}: {perWeek,4:0} peixes/sem → {legPerWeek:0.00} lendário/sem"
            + (legPerWeek > 0 ? $" (~1 a cada {7 / legPerWeek:0} dias)" : ""));
    }

    Console.WriteLine("\nRenda líquida/h (água cheia; upkeep = manter a água a 100 sem auto-filtro):");
    ReportTank("3 comuns (cores variadas)", MakeTank(3, 4m, false), cfg);
    ReportTank("3 comuns MESMA cor", MakeTank(3, 4m, true), cfg);
    ReportTank("6 incomuns MESMA cor", MakeTank(6, 5.8m, true), cfg);
    ReportTank("10 raros variados", MakeTank(10, 7.5m, false), cfg);
    ReportTank("10 raros MESMA cor", MakeTank(10, 7.5m, true), cfg);
    ReportTank("25 raros variados", MakeTank(25, 7.5m, false), cfg);
    ReportTank("25 raros MESMA cor", MakeTank(25, 7.5m, true), cfg);
    ReportTank("1 lendário (score 15)", MakeTank(1, 15m, false), cfg);

    Console.WriteLine("\nPreço do upgrade de tanque por faixa de capacidade (CapacityBands):");
    foreach (var band in CapacityBands.All)
    {
        if (band.TransitionCost > 0)
            Console.WriteLine($"  >>> TROCA pra {band.Name}: {band.TransitionCost:0} soft (cap {band.MinCapacity}->{band.MinCapacity + 1}) — custo de gate, bem acima da curva suave");
        Console.Write($"  {band.Name} (base {band.PriceBase:0} × {band.PriceGrowth:0.00}^n, degradação x{band.DegradationBandFactor:0.00}): ");
        for (int cap = band.MinCapacity + (band.TransitionCost > 0 ? 1 : 0); cap < band.MaxCapacity; cap++)
            Console.Write($"cap {cap}->{cap + 1}: {CapacityBands.PriceForUpgrade(cap):0}  ");
        Console.WriteLine();
    }
}

static List<FishIncome> MakeTank(int n, decimal score, bool sameColor)
{
    var colors = Enum.GetValues<PartColor>();
    var list = new List<FishIncome>(n);
    for (int i = 0; i < n; i++)
        list.Add(new FishIncome(score, sameColor ? PartColor.Blue : colors[i % colors.Length]));
    return list;
}

static void ReportTank(string label, List<FishIncome> tank, TickConfig cfg)
{
    decimal gross = IncomeCalculator.TankRatePerHour(tank, 100m, cfg);
    decimal fishWeight = tank.Sum(f => f.RarityScore) / cfg.DegradationRarityRefScore;
    double degPerHour = (double)(cfg.DegradationPerMinute * 60m) * (1 + (double)cfg.DegradationPerFishFactor * (double)fishWeight);
    double upkeep = degPerHour * 0.2; // ~20 soft por 100 pontos de água (filtro)
    Console.WriteLine($"  {label,-26}: bruto {gross,7:0.0}/h   upkeep {upkeep,5:0.0}/h   líquido {(double)gross - upkeep,7:0.0}/h");
}

static void BreedReport()
{
    const int N = 50_000;
    var rngPop = new Random(2026);
    var baseline = new List<CreatureTraits>(N);
    for (int i = 0; i < N; i++)
        baseline.Add(TraitGenerator.Generate(rngPop.NextInt64()));
    double baseLegendaryPct = baseline.Count(t => t.ShimmerTier == ShimmerTier.Legendary) / (double)N * 100;

    var gestationHours = new List<double>(N);
    int childLegendary = 0, exceedsMaxParent = 0;
    var rngPairs = new Random(4242);
    for (int i = 0; i < N; i++)
    {
        long seedA = rngPairs.NextInt64();
        long seedB = rngPairs.NextInt64();
        var a = TraitGenerator.Generate(seedA);
        var b = TraitGenerator.Generate(seedB);
        long childSeed = rngPairs.NextInt64();
        var child = TraitGenerator.BreedTraits(childSeed, seedA, seedB, TraitConfigV1.Version,
            BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength);

        gestationHours.Add(BreedingCalculator.GestationHours((decimal)a.RarityScore, (decimal)b.RarityScore));
        if (child.ShimmerTier == ShimmerTier.Legendary) childLegendary++;
        if (child.RarityScore > Math.Max(a.RarityScore, b.RarityScore)) exceedsMaxParent++;
    }

    Console.WriteLine($"BREEDING — {N:N0} pares aleatórios (população base, não filtrada por raridade)\n");
    Console.WriteLine($"MutationChance = {BreedingDefaults.MutationChance:0.00}\n");
    Console.WriteLine($"Baseline legendário na população: {baseLegendaryPct:0.000}%");
    Console.WriteLine($"Legendário nos filhos:            {(childLegendary / (double)N * 100):0.000}%");
    Console.WriteLine($"Filhos que superam o score do pai mais raro: {(exceedsMaxParent / (double)N * 100):0.00}%\n");

    var gh = gestationHours.OrderBy(h => h).ToArray();
    Console.WriteLine("HORAS DE GESTAÇÃO (percentis, pares aleatórios da população base):");
    foreach (var p in new[] { 1, 10, 25, 50, 75, 90, 99 })
        Console.WriteLine($"  p{p,-4} {Percentile(gh, p),7:0.0}h");
    Console.WriteLine($"  min  {gh[0],7:0.0}h\n  max  {gh[^1],7:0.0}h");

    Console.WriteLine("\nCASOS DE REFERÊNCIA (score combinado -> horas):");
    ReportGestationPair("2 comuns (5+5)", 5m, 5m);
    ReportGestationPair("comum + raro (5+8)", 5m, 8m);
    ReportGestationPair("2 raros (8+8)", 8m, 8m);
    ReportGestationPair("2 épicos (11+11)", 11m, 11m);
    ReportGestationPair("2 lendários (15+15)", 15m, 15m);
    ReportGestationPair("2 lendários máx (18.9+18.9)", 18.9m, 18.9m);

    ReportTierBiasScenarios();
    ReportPartColorBiasScenarios();
    ReportGrandparentReachEffect();
    ReportCostSustainability();
    ReportDeathRiskCurve();
}

static void ReportGestationPair(string label, decimal scoreA, decimal scoreB)
    => Console.WriteLine($"  {label,-28}: {BreedingCalculator.GestationHours(scoreA, scoreB),6:0.0}h");

/// <summary>
/// Custo dinâmico vs. renda/hora na mesma raridade: quantas horas de renda o
/// custo representa — pra checar que não é nem irrelevante (comum) nem
/// impagável (lendário), já que o tempo de gestação já é o sink pesado.
/// </summary>
static void ReportCostSustainability()
{
    Console.WriteLine("\nCUSTO DINÂMICO vs RENDA (score combinado -> custo, horas de renda pra pagar):");
    var cfg = TickConfig.Default;
    void ReportCostPair(string label, decimal scoreA, decimal scoreB)
    {
        decimal cost = BreedingCalculator.CostSoft(scoreA, scoreB);
        double incomePerHour = IncomeCalculator.CoinsPerHour(scoreA, cfg) + IncomeCalculator.CoinsPerHour(scoreB, cfg);
        double hoursToPay = incomePerHour > 0 ? (double)cost / incomePerHour : double.PositiveInfinity;
        Console.WriteLine($"  {label,-28}: {cost,7:0} soft   ({hoursToPay,5:0.0}h de renda do casal)");
    }
    ReportCostPair("2 comuns (5+5)", 5m, 5m);
    ReportCostPair("comum + raro (5+8)", 5m, 8m);
    ReportCostPair("2 raros (8+8)", 8m, 8m);
    ReportCostPair("2 épicos (11+11)", 11m, 11m);
    ReportCostPair("2 lendários (15+15)", 15m, 15m);
    ReportCostPair("2 lendários máx (18.9+18.9)", 18.9m, 18.9m);
}

/// <summary>
/// Curva de sobrevivência esperada: risco de morte por uso e a chance de um
/// peixe ainda estar vivo depois de N cruzamentos completados.
/// </summary>
static void ReportDeathRiskCurve()
{
    Console.WriteLine("\nRISCO DE MORTE POR CRUZAMENTO (n = gestações já completadas antes desta):");
    double survival = 1.0;
    for (int n = 0; n <= 10; n++)
    {
        double death = BreedingCalculator.DeathChance(n);
        survival *= (1 - death);
        Console.WriteLine($"  uso #{n + 1,-2} (n={n,-2}): risco {death * 100,5:0.0}%   sobrevivência acumulada até aqui: {survival * 100,5:0.0}%");
    }

    Console.WriteLine($"\nDESCANSO (RestHalfLifeDays = {BreedingDefaults.RestHalfLifeDays}) — mesmo BreedCount=6, risco cai com o tempo parado:");
    var now = DateTime.UtcNow;
    foreach (int days in new[] { 0, 5, 10, 20, 40 })
    {
        double eff = BreedingCalculator.EffectiveBreedCount(6, now.AddDays(-days), now);
        double death = BreedingCalculator.DeathChance(eff);
        Console.WriteLine($"  {days,2}d de descanso: BreedCount efetivo {eff,4:0.00}   risco {death * 100,5:0.0}%");
    }

    Console.WriteLine($"\nSEGURO DE CRUZAMENTO (InsuranceBasePremium={BreedingDefaults.InsuranceBasePremium}, PerRiskPercent={BreedingDefaults.InsurancePerRiskPercent}):");
    foreach (int n in new[] { 0, 2, 5, 8 })
    {
        double death = BreedingCalculator.DeathChance(n);
        decimal cost = BreedingCalculator.InsuranceCostPremium(death, death);
        Console.WriteLine($"  par com n={n} cada (risco {death * 100:0.0}% cada): seguro custa {cost:0.0} premium");
    }
}

/// <summary>
/// Viés de raridade no tier de brilho (RarityBiasStrength): compara "2 pais
/// Lendário" vs "1 Lendário + 1 comum qualquer" — a segunda tem que ficar bem
/// abaixo da primeira, senão cruzar com um peixe qualquer vira atalho de
/// "lavagem" de lendário (o exploit que a calibração precisa evitar).
/// </summary>
static void ReportTierBiasScenarios()
{
    const int n = 30_000;
    long legendarySeed = FindSeedWithTier(ShimmerTier.Legendary, 200_000);
    long commonSeed = FindSeedWithTier(ShimmerTier.None, 200);
    long rareSeed = FindSeedWithTier(ShimmerTier.Rare, 5_000);

    double PctLegendary(long seedA, long seedB, int seed)
    {
        var rng = new Random(seed);
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            long childSeed = rng.NextInt64();
            var child = TraitGenerator.BreedTraits(childSeed, seedA, seedB, TraitConfigV1.Version,
                BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength);
            if (child.ShimmerTier == ShimmerTier.Legendary) count++;
        }
        return count / (double)n * 100;
    }

    Console.WriteLine($"\nVIÉS DE RARIDADE NO TIER DE BRILHO (RarityBiasStrength = {BreedingDefaults.RarityBiasStrength:0.00}):");
    Console.WriteLine($"  2 pais Lendário       -> filho Lendário: {PctLegendary(legendarySeed, legendarySeed, 1):0.0}%");
    Console.WriteLine($"  Lendário + Raro       -> filho Lendário: {PctLegendary(legendarySeed, rareSeed, 2):0.0}%");
    Console.WriteLine($"  Lendário + comum      -> filho Lendário: {PctLegendary(legendarySeed, commonSeed, 3):0.0}%  <- deve ficar bem abaixo do primeiro (anti-lavagem)");
    Console.WriteLine($"  2 pais comuns         -> filho Lendário: {PctLegendary(commonSeed, commonSeed, 4):0.0}%  (baseline, deve ficar perto de 0.2%)");
}

/// <summary>
/// Simula um jogador sintético gastando em filtro + upgrade de tanque + breeding AO
/// MESMO TEMPO por vários dias, ao contrário de `economy`/`breed` que checam cada sink
/// isoladamente. Objetivo: ver se a economia trava (fica sem dinheiro pra nada) ou infla
/// (dinheiro sobra sem sink que absorva) quando os três competem pelo mesmo saldo.
///
/// Granularidade de 1 dia por tick (não minuto a minuto como o backend real): a água pode
/// bater 0 dentro do dia antes da compra de filtro "acontecer" no fim do dia — é uma
/// aproximação propositalmente conservadora (pior caso) da manutenção real, não uma réplica
/// exata dos números do backend. O que importa aqui é a tendência ao longo do tempo, não o
/// valor exato em um dia específico.
/// </summary>
static void SimulateReport()
{
    Console.WriteLine("SIMULAÇÃO DE TRAJETÓRIA — filtro + upgrade de tanque + breeding competindo pelo mesmo saldo\n");
    foreach (var (name, hoursOnline) in new[] { ("casual 2h/dia", 2.0), ("ativo 8h/dia", 8.0), ("dedicado 16h/dia", 16.0) })
        RunPlayerSimulation(name, hoursOnline, days: 120, seed: 20260730);
}

static void RunPlayerSimulation(string label, double hoursOnlinePerDay, int days, int seed)
{
    // Espelha os preços reais de ItemService/SeedItemDefinitions — manter em sincronia.
    const decimal FilterCost = 20m;
    // Níveis de filtro automático (08/08/2026): cada um cobre uma capacidade de peixes
    // (peso por raridade); o melhor nível possuído prevalece, não empilha.
    var filterTiers = new (decimal Capacity, decimal Cost)[] { (5m, 500m), (10m, 1200m), (18m, 2500m) };
    decimal TankUpgradePrice(int capacity) => CapacityBands.PriceForUpgrade(capacity);

    var cfg = TickConfig.Default;
    var rng = new Random(seed);

    decimal wallet = EconomyDefaults.StartingSoftBalance;
    int capacity = HabitatDefaults.Capacity;
    decimal maintenance = HabitatDefaults.MaintenanceLevel;
    decimal progress = 0m;
    decimal filterCapacity = 0m;

    var tank = new List<(long Seed, decimal Score, PartColor TailColor, int BreedCount)>();
    int backpackOverflow = 0;

    decimal spentFilter = 0, spentUpgrade = 0, spentBreeding = 0, totalEarned = 0;
    int filtersBought = 0, upgradesBought = 0, breedingsStarted = 0, breedingDeaths = 0, legendariesFound = 0, fishCollected = 0;
    var walletAtCheckpoint = new Dictionary<int, decimal>();

    (int ReadyDay, long SeedA, long SeedB, decimal ScoreA, decimal ScoreB, int BreedCountA, int BreedCountB)? gestation = null;

    // Tick de HORA em hora (não de dia em dia): com tick diário, um perfil bem ativo
    // (ex: dedicado 16h/dia) acumula progresso pra dezenas de peixes num tick só, mas o
    // QueueCap (5) descarta esse excedente (`Fila cheia não acumula estoque`, HabitatTicker) —
    // um artefato da granularidade grossa, não um limite real (o backend real tica a cada
    // heartbeat/refresh, muito mais frequente). Tick por hora resolve isso mantendo o cálculo
    // exatamente igual ao backend (mesma HabitatTicker.ProcessTick/IncomeCalculator.Accrue).
    var day0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    for (int hour = 0; hour < days * 24; hour++)
    {
        int day = hour / 24;
        bool onlineThisHour = (hour % 24) < hoursOnlinePerDay;

        var hourStart = day0.AddHours(hour);
        var hourEnd = hourStart.AddHours(1);
        DateTime? lastHeartbeat = onlineThisHour ? hourEnd : null; // fresco = online; sem heartbeat = offline

        var state = new HabitatTickState(
            LastTickAtUtc: hourStart, LastHeartbeatAtUtc: lastHeartbeat,
            MaintenanceLevel: maintenance, GenerationProgressMinutes: progress,
            GenerationIntervalMinutes: HabitatDefaults.GenerationIntervalMinutes,
            OnlineGenerationRate: HabitatDefaults.OnlineGenerationRate,
            OfflineGenerationRate: HabitatDefaults.OfflineGenerationRate,
            QueueCap: HabitatDefaults.QueueCap, PendingQueueCount: 0, // atento: coleta tudo na mesma hora
            FilterCapacity: filterCapacity, ActiveFishWeight: tank.Sum(f => f.Score) / cfg.DegradationRarityRefScore,
            CapacityBandDegradationFactor: CapacityBands.BandFor(capacity).DegradationBandFactor);

        var outcome = HabitatTicker.ProcessTick(state, hourEnd, rng, cfg);
        progress = outcome.GenerationProgressMinutes;

        var fishIncome = tank.Select(f => new FishIncome(f.Score, f.TailColor)).ToList();
        decimal earned = IncomeCalculator.Accrue(fishIncome, outcome.MaintenanceAtStart, outcome.MaintenanceLevel,
            outcome.OnlineMinutes, outcome.OfflineMinutes,
            HabitatDefaults.OnlineGenerationRate, HabitatDefaults.OfflineGenerationRate, cfg);
        wallet += earned;
        totalEarned += earned;
        maintenance = outcome.MaintenanceLevel;

        for (int i = 0; i < outcome.SpawnCount; i++)
        {
            var collected = CreatureCollector.Collect(outcome.SpawnSickFlags[i], () => rng.NextInt64());
            fishCollected++;
            if (collected.RarityScore >= 14.0m) legendariesFound++;
            var traits = TraitGenerator.Generate(collected.Seed, collected.TraitConfigVersion);
            if (tank.Count < capacity)
                tank.Add((collected.Seed, collected.RarityScore, traits.Tail.Color, 0));
            else
                backpackOverflow++;
        }

        // Resolve gestação pronta (antes de decidir novas compras/breeding do dia)
        if (gestation is { } g && day >= g.ReadyDay)
        {
            bool aDied = rng.NextDouble() < BreedingCalculator.DeathChance(g.BreedCountA);
            bool bDied = rng.NextDouble() < BreedingCalculator.DeathChance(g.BreedCountB);
            breedingDeaths += (aDied ? 1 : 0) + (bDied ? 1 : 0);

            void ReturnParent(long parentSeed, decimal score, int breedCountBefore)
            {
                var t = TraitGenerator.Generate(parentSeed);
                if (tank.Count < capacity) tank.Add((parentSeed, score, t.Tail.Color, breedCountBefore + 1));
                else backpackOverflow++;
            }
            if (!aDied) ReturnParent(g.SeedA, g.ScoreA, g.BreedCountA);
            if (!bDied) ReturnParent(g.SeedB, g.ScoreB, g.BreedCountB);

            long childSeed = rng.NextInt64();
            var child = TraitGenerator.BreedTraits(childSeed, g.SeedA, g.SeedB, TraitConfigV1.Version,
                BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength);
            fishCollected++;
            if ((decimal)child.RarityScore >= 14.0m) legendariesFound++;
            if (tank.Count < capacity) tank.Add((childSeed, (decimal)child.RarityScore, child.Tail.Color, 0));
            else backpackOverflow++;

            gestation = null;
        }

        // Upkeep: filtro quando a água está baixa (auto-filtro só reduz a taxa de degradação
        // pela metade, não substitui o filtro manual — sem isso a água ainda zera e trava a
        // renda pra sempre, já que WaterFactor(0)=0. Bug real que essa simulação revelou.)
        if (maintenance < cfg.LowMaintenanceThreshold && wallet >= FilterCost)
        {
            wallet -= FilterCost; spentFilter += FilterCost; filtersBought++;
            maintenance = 100m;
        }

        // Decisões de menor frequência (upgrade/breeding/checkpoint): uma vez por dia, no
        // fim do dia — evita comprar upgrade várias vezes ou reiniciar breeding a cada hora.
        if ((hour % 24) == 23)
        {
            if (Environment.GetEnvironmentVariable("SIM_DEBUG") == "1" && label.StartsWith("casual"))
                Console.Error.WriteLine($"dia {day + 1,3}: wallet={wallet,8:0.00} tank={tank.Count,2} cap={capacity,2} overflow={backpackOverflow,5} maint={maintenance,6:0.0} gestation={(gestation is null ? "-" : "ativa")}");
            // Sobe de nível de filtro quando o tanque pesa mais que a cobertura atual e
            // sobra uma folga confortável (evita zerar o caixa por isso).
            decimal fishWeight = tank.Sum(f => f.Score) / cfg.DegradationRarityRefScore;
            var nextTier = filterTiers.FirstOrDefault(t => t.Capacity > filterCapacity);
            if (fishWeight > filterCapacity && nextTier != default && wallet >= nextTier.Cost * 1.5m)
            {
                wallet -= nextTier.Cost; spentUpgrade += nextTier.Cost; filterCapacity = nextTier.Capacity;
            }

            // Upgrade de tanque quando peixes estão transbordando pra mochila e dá pra pagar
            // (respeitando o teto absoluto das faixas de capacidade, CapacityBands.MaxCapacity).
            bool wantsUpgrade = backpackOverflow > 0 && capacity < CapacityBands.MaxCapacity;
            decimal upgradePrice = wantsUpgrade ? TankUpgradePrice(capacity) : 0m;
            if (wantsUpgrade && wallet >= upgradePrice)
            {
                wallet -= upgradePrice; spentUpgrade += upgradePrice; upgradesBought++; capacity++;
                wantsUpgrade = false;
            }

            // Jogador racional: com o transbordo persistindo e o upgrade ainda fora de
            // alcance, prioriza POUPAR pra ele (pausa novas gestações) em vez de manter
            // gastando em breeding e nunca juntar o suficiente pro custo de troca de faixa
            // (`TransitionCost`, 08/08/2026) — sem isso a simulação nunca via ninguém trocar
            // de faixa em 120 dias, mesmo o perfil dedicado, porque breeding consumia o
            // caixa todo dia antes de acumular.
            bool savingForUpgrade = wantsUpgrade && wallet < upgradePrice;

            // Breeding: cruza os 2 melhores do tanque quando não há gestação ativa e dá pra pagar
            if (!savingForUpgrade && gestation is null && tank.Count >= 2)
            {
                var pair = tank.OrderByDescending(f => f.Score).Take(2).ToList();
                decimal cost = BreedingCalculator.CostSoft(pair[0].Score, pair[1].Score);
                if (wallet >= cost)
                {
                    wallet -= cost; spentBreeding += cost; breedingsStarted++;
                    tank.Remove(pair[0]); tank.Remove(pair[1]);
                    double hours = BreedingCalculator.GestationHours(pair[0].Score, pair[1].Score);
                    int readyDay = day + Math.Max(1, (int)Math.Ceiling(hours / 24.0));
                    gestation = (readyDay, pair[0].Seed, pair[1].Seed, pair[0].Score, pair[1].Score, pair[0].BreedCount, pair[1].BreedCount);
                }
            }

            if ((day + 1) % 10 == 0) walletAtCheckpoint[day + 1] = wallet;
        }
    }

    Console.WriteLine($"{label} — {days} dias, tanque começa em {HabitatDefaults.Capacity}, cap {capacity} peixe(s) ao final:");
    Console.WriteLine($"  Carteira final: {wallet,10:0} soft   (ganho total {totalEarned,10:0})");
    foreach (var (d, w) in walletAtCheckpoint.OrderBy(kv => kv.Key))
        Console.WriteLine($"    saldo no dia {d,3}: {w,10:0}");
    Console.WriteLine($"  Peixes coletados: {fishCollected} ({legendariesFound} lendário(s)) — no tanque: {tank.Count}, na mochila (transbordo): {backpackOverflow}");
    Console.WriteLine($"  Filtros comprados: {filtersBought} ({spentFilter:0} soft){(filterCapacity > 0 ? $" + auto-filtro (cobre {filterCapacity})" : "")}");
    Console.WriteLine($"  Upgrades de tanque: {upgradesBought} ({spentUpgrade:0} soft (inclui filtros automáticos), capacidade {HabitatDefaults.Capacity}->{capacity})");
    Console.WriteLine($"  Gestações iniciadas: {breedingsStarted} ({spentBreeding:0} soft) — mortes de pais: {breedingDeaths}");
    Console.WriteLine();
}

/// <summary>
/// Mesma checagem anti-lavagem de <see cref="ReportTierBiasScenarios"/>, mas pra cor de
/// parte (31/07/2026: viés estendido de "só shimmer" pra também cor/padrão — CLAUDE.md 8.8).
/// Branco puro (1%, a cor mais rara) vs. Laranja (22%, a mais comum) é o par mais extremo
/// possível — se mesmo esse não virar quase-garantia, os demais pares (menos extremos)
/// também não viram.
/// </summary>
static void ReportPartColorBiasScenarios()
{
    const int n = 30_000;
    long whiteSeed = FindSeedWithTailColor(PartColor.PureWhite, 5_000);
    long orangeSeed = FindSeedWithTailColor(PartColor.Orange, 20);

    double PctWhite(long seedA, long seedB, int seed)
    {
        var rng = new Random(seed);
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            long childSeed = rng.NextInt64();
            var child = TraitGenerator.BreedTraits(childSeed, seedA, seedB, TraitConfigV1.Version,
                BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength);
            if (child.Tail.Color == PartColor.PureWhite) count++;
        }
        return count / (double)n * 100;
    }

    Console.WriteLine($"\nVIÉS DE RARIDADE NA COR DA CAUDA (mesmo RarityBiasStrength, agora também em cor/padrão):");
    Console.WriteLine($"  2 pais Branco puro    -> filho Branco puro: {PctWhite(whiteSeed, whiteSeed, 5):0.0}%");
    Console.WriteLine($"  Branco puro + Laranja -> filho Branco puro: {PctWhite(whiteSeed, orangeSeed, 6):0.0}%  <- deve ficar bem abaixo do primeiro (anti-lavagem)");
    Console.WriteLine($"  2 pais Laranja        -> filho Branco puro: {PctWhite(orangeSeed, orangeSeed, 7):0.0}%  (baseline, deve ficar perto de 0% — mutação só)");
}

static long FindSeedWithTailColor(PartColor color, int searchLimit)
{
    for (long s = 1; s <= searchLimit; s++)
        if (TraitGenerator.Generate(s).Tail.Color == color) return s;
    throw new InvalidOperationException($"Nenhum seed com cor de cauda {color} nos primeiros {searchLimit}");
}

/// <summary>
/// Mede o efeito prático do "reach-back" de avós (31/07/2026, CLAUDE.md 8.8) direto no
/// mecanismo interno (`EffectiveParentTraits`), não na saída ponta-a-ponta — o valor "próprio"
/// de um pai bred já reflete os mesmos avós que o reach alcançaria, então isolar o sinal
/// observando só a cor final do filhote é ambíguo (mesma lição dos testes automatizados).
/// </summary>
static void ReportGrandparentReachEffect()
{
    var own = TraitGenerator.Generate(FindSeedWithTailColor(PartColor.Orange, 20));
    var grandparent1 = TraitGenerator.Generate(FindSeedWithTailColor(PartColor.PureWhite, 5_000));
    var grandparent2 = TraitGenerator.Generate(FindSeedWithTailColor(PartColor.Blue, 20));
    const int n = 30_000;

    int Reached(double reachChance, int seed)
    {
        var rng = new Random(seed);
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            long childSeed = rng.NextInt64();
            var result = TraitGenerator.EffectiveParentTraits(childSeed, "tail", own, grandparent1, grandparent2, reachChance);
            if (!result.Equals(own)) count++;
        }
        return count;
    }

    Console.WriteLine($"\nCHANCE DE HERDAR TRAÇO DE UM AVÔ (GrandparentReachChance = {BreedingDefaults.GrandparentReachChance:0.00}):");
    Console.WriteLine($"  Reach observado (produção): {Reached(BreedingDefaults.GrandparentReachChance, 1) / (double)n * 100:0.0}%  (esperado ~{BreedingDefaults.GrandparentReachChance * 100:0.0}%)");
    Console.WriteLine($"  Reach observado (0.0, controle): {Reached(0.0, 2) / (double)n * 100:0.0}%  (deve ficar em 0%)");
}

static long FindSeedWithTier(ShimmerTier tier, int searchLimit)
{
    for (long s = 1; s <= searchLimit; s++)
        if (TraitGenerator.Generate(s).ShimmerTier == tier) return s;
    throw new InvalidOperationException($"Nenhum seed com tier {tier} nos primeiros {searchLimit}");
}

static double Percentile(double[] sorted, double p)
{
    double rank = p / 100.0 * (sorted.Length - 1);
    int lo = (int)rank;
    return lo >= sorted.Length - 1 ? sorted[^1] : sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]);
}
