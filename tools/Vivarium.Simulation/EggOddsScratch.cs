using Vivarium.Core.Generation;

namespace Vivarium.Simulation;

/// <summary>Mede o multiplicador real de chance de cada tier de ovo (§7.21) vs. coleta normal
/// (bias=0) — `dotnet run --project tools/Vivarium.Simulation -- eggodds`. Usado pra calibrar
/// os `biasStrength` dos 3 tiers de ovo com números reais, não só a fórmula.</summary>
public static class EggOddsScratch
{
    public static void Run()
    {
        const int n = 3_000_000;
        double[] biases = [0.0, 0.15, 0.35, 0.55];
        var counts = new Dictionary<double, Dictionary<ShimmerTier, int>>();
        foreach (var b in biases) counts[b] = new Dictionary<ShimmerTier, int>();

        var rnd = new Random(12345);
        for (int i = 0; i < n; i++)
        {
            long seed = rnd.NextInt64();
            foreach (var b in biases)
            {
                var t = TraitGenerator.GenerateBiased(seed, b).ShimmerTier;
                counts[b][t] = counts[b].GetValueOrDefault(t) + 1;
            }
        }

        Console.WriteLine($"Amostra: {n} seeds por bias");
        foreach (ShimmerTier tier in Enum.GetValues<ShimmerTier>())
        {
            double baseline = counts[0.0].GetValueOrDefault(tier) / (double)n;
            Console.WriteLine($"\n{tier} — baseline {baseline:P4}");
            foreach (var b in biases)
            {
                double p = counts[b].GetValueOrDefault(tier) / (double)n;
                double mult = baseline > 0 ? p / baseline : double.NaN;
                Console.WriteLine($"  bias={b:0.00}: {p:P4} ({mult:0.00}x baseline)");
            }
        }

        RunBands(n, biases);
    }

    /// <summary>Compara opções de bias mais altas pro Ovo Lendário (17/08/2026, achado real:
    /// bias=0.55 só dava ~30% de chance de Raro+, Incomum continuava sendo o resultado mais
    /// provável mesmo no ovo mais caro).</summary>
    public static void RunBiasOptions()
    {
        const int n = 3_000_000;
        double[] biases = [0.55, 0.60, 0.65, 0.70, 0.75, 0.80, 0.85, 0.90, 0.95, 1.0];
        RunBands(n, biases);
    }

    // Cortes de BANDS espelhados de MarketService.BandNameOf / CLAUDE.md §5 — banda de score,
    // não tier de brilho isolado (o que o jogador realmente vê como "raridade" do peixe).
    private static string BandOf(double score) => score switch
    {
        < 5.45 => "Comum",
        < 12.04 => "Incomum",
        < 13.78 => "Raro",
        < 16.60 => "Épico",
        _ => "Lendário",
    };

    private static void RunBands(int n, double[] biases)
    {
        var counts = new Dictionary<double, Dictionary<string, int>>();
        foreach (var b in biases) counts[b] = new Dictionary<string, int>();

        var rnd = new Random(54321);
        for (int i = 0; i < n; i++)
        {
            long seed = rnd.NextInt64();
            foreach (var b in biases)
            {
                string band = BandOf(TraitGenerator.GenerateBiased(seed, b).RarityScore);
                counts[b][band] = counts[b].GetValueOrDefault(band) + 1;
            }
        }

        Console.WriteLine($"\n\n=== Banda de raridade (score, não só tier de brilho) — {n} seeds por bias ===");
        foreach (string band in new[] { "Comum", "Incomum", "Raro", "Épico", "Lendário" })
        {
            Console.WriteLine($"\n{band}:");
            foreach (var b in biases)
            {
                double p = counts[b].GetValueOrDefault(band) / (double)n;
                Console.WriteLine($"  bias={b:0.00}: {p:P4}");
            }
        }
    }
}
