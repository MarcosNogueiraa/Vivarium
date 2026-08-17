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
    }
}
