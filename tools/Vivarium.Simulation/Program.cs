using Vivarium.Core.Generation;

// Simulação de validação dos pesos (CLAUDE.md, próximo passo 1):
// gera N seeds, compara distribuição real vs esperada e mostra a curva de rarity score.

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

static double Percentile(double[] sorted, double p)
{
    double rank = p / 100.0 * (sorted.Length - 1);
    int lo = (int)rank;
    return lo >= sorted.Length - 1 ? sorted[^1] : sorted[lo] + (rank - lo) * (sorted[lo + 1] - sorted[lo]);
}
