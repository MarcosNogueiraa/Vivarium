using System.Globalization;
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

static double Percentile(double[] sorted, double p)
{
    double rank = p / 100.0 * (sorted.Length - 1);
    int lo = (int)rank;
    return lo >= sorted.Length - 1 ? sorted[^1] : sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]);
}
