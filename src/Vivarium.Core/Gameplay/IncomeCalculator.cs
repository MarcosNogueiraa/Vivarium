using Vivarium.Core.Generation;

namespace Vivarium.Core.Gameplay;

/// <summary>Entrada por peixe pro cálculo de renda: raridade + cor da cauda (pra sinergia).</summary>
public readonly record struct FishIncome(decimal RarityScore, PartColor TailColor);

/// <summary>
/// Renda passiva de moedas (soft) por raridade + sinergia de cor. Lógica pura:
/// recebe os peixes e o estado da janela, devolve quanto foi ganho. Toda renda é
/// calculada aqui e no servidor — o cliente nunca envia valor (anti-cheat).
/// </summary>
public static class IncomeCalculator
{
    /// <summary>Moedas por hora que um peixe rende (base), exponencial na raridade.</summary>
    public static double CoinsPerHour(decimal rarityScore, TickConfig config)
        => config.IncomeBasePerHour
           * Math.Exp(config.IncomeGrowth * ((double)rarityScore - config.IncomeRefScore));

    /// <summary>Fator da água na renda (0–1): água suja rende menos, água 0 rende ~0.</summary>
    public static double WaterFactor(decimal maintenanceLevel, TickConfig config)
    {
        double q = Math.Clamp((double)maintenanceLevel / 100.0, 0, 1);
        return Math.Pow(q, config.IncomeWaterExp);
    }

    /// <summary>Multiplicador de sinergia pra N peixes da mesma cor: 1 + s·(N-1), com teto.</summary>
    public static double SynergyMultiplier(int sameColorCount, TickConfig config)
        => 1.0 + Math.Min(config.SynergyMaxBonus, config.SynergyPerMatch * Math.Max(0, sameColorCount - 1));

    /// <summary>Renda/hora de cada peixe (base × sinergia), já agrupada por cor de cauda.</summary>
    private static double[] PerFishRates(IReadOnlyList<FishIncome> fish, TickConfig config)
    {
        var counts = new Dictionary<PartColor, int>();
        foreach (var f in fish)
            counts[f.TailColor] = counts.GetValueOrDefault(f.TailColor) + 1;

        var rates = new double[fish.Count];
        for (int i = 0; i < fish.Count; i++)
            rates[i] = CoinsPerHour(fish[i].RarityScore, config) * SynergyMultiplier(counts[fish[i].TailColor], config);
        return rates;
    }

    /// <summary>Soma das taxas/hora dos peixes (raridade + sinergia) já com o fator água.</summary>
    public static decimal TankRatePerHour(IReadOnlyList<FishIncome> fish, decimal maintenanceLevel, TickConfig config)
    {
        double water = WaterFactor(maintenanceLevel, config);
        double total = 0;
        foreach (var r in PerFishRates(fish, config))
            total += r;
        return (decimal)(total * water);
    }

    /// <summary>
    /// Moedas ganhas na janela do tick. Online full, offline a offlineRate e com teto
    /// (IncomeOfflineCapMinutes). Usa a água do início da janela (o tick roda com
    /// frequência; ausência longa é dominada pelo teto offline).
    /// </summary>
    public static decimal Accrue(
        IReadOnlyList<FishIncome> fish,
        decimal maintenanceLevel,
        decimal onlineMinutes,
        decimal offlineMinutes,
        decimal onlineRate,
        decimal offlineRate,
        TickConfig config)
    {
        decimal cappedOffline = Math.Min(Math.Max(0, offlineMinutes), config.IncomeOfflineCapMinutes);
        decimal effectiveMinutes = Math.Max(0, onlineMinutes) * onlineRate + cappedOffline * offlineRate;
        if (effectiveMinutes <= 0)
            return 0;

        double water = WaterFactor(maintenanceLevel, config);
        double perHour = 0;
        foreach (var r in PerFishRates(fish, config))
            perHour += r;

        return (decimal)(perHour * water) * (effectiveMinutes / 60m);
    }
}
