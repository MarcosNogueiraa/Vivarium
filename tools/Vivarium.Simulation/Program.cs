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

if (args.Length >= 1 && args[0] == "economy")
{
    EconomyReport();
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
          .Append(';').Append(p.PatternOpacity?.ToString("F6", inv) ?? "-");
    }
    sb.Append(';').Append(t.Movement.TailSpeed.ToString("F6", inv))
      .Append(';').Append(t.Movement.TailAmplitude.ToString("F6", inv))
      .Append(';').Append(t.Movement.FinSpeed.ToString("F6", inv))
      .Append(';').Append(t.Movement.FinAmplitude.ToString("F6", inv));
    sb.Append(';').Append(t.RarityScore.ToString("F6", inv));
    Console.WriteLine(sb.ToString());
}

static void EconomyReport()
{
    var cfg = TickConfig.Default;
    int interval = HabitatDefaults.GenerationIntervalMinutes;

    // Probabilidade de lendário (score >= 11.2) por peixe coletado
    int N = 200_000, leg = 0;
    var rng = new Random(7);
    for (int i = 0; i < N; i++)
        if ((double)TraitGenerator.Generate(rng.NextInt64()).RarityScore >= 11.2) leg++;
    double p = leg / (double)N;

    Console.WriteLine($"Geração: 1 peixe / {interval} min = {60.0 / interval:0.0}/h online");
    Console.WriteLine($"P(lendário, score>=11.2) = {p * 100:0.000}%\n");
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
    ReportTank("1 lendário (score 12)", MakeTank(1, 12m, false), cfg);

    Console.WriteLine("\nPreço do upgrade de tanque (base 50 × 1.5^(cap-3)):");
    for (int cap = 3; cap <= 9; cap++)
        Console.Write($"  cap {cap}->{cap + 1}: {Math.Ceiling(50 * Math.Pow(1.5, cap - 3)):0}");
    Console.WriteLine();
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
    double degPerHour = (double)(cfg.DegradationPerMinute * 60m) * (1 + (double)cfg.DegradationPerFishFactor * tank.Count);
    double upkeep = degPerHour * 0.2; // ~20 soft por 100 pontos de água (filtro)
    Console.WriteLine($"  {label,-26}: bruto {gross,7:0.0}/h   upkeep {upkeep,5:0.0}/h   líquido {(double)gross - upkeep,7:0.0}/h");
}

static double Percentile(double[] sorted, double p)
{
    double rank = p / 100.0 * (sorted.Length - 1);
    int lo = (int)rank;
    return lo >= sorted.Length - 1 ? sorted[^1] : sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]);
}
