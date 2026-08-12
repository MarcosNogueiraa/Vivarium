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
    /// <summary>
    /// Moedas por hora que um peixe rende (base), exponencial na raridade até o piso do
    /// Lendário; acima disso, taper (crescimento reduzido, contínuo) — ver
    /// <see cref="TickConfig.IncomeLegendaryTaperScore"/>.
    /// </summary>
    public static double CoinsPerHour(decimal rarityScore, TickConfig config)
    {
        double score = (double)rarityScore;
        double taperScore = config.IncomeLegendaryTaperScore;
        if (score <= taperScore)
            return config.IncomeBasePerHour * Math.Exp(config.IncomeGrowth * (score - config.IncomeRefScore));

        double floorAtTaper = config.IncomeBasePerHour * Math.Exp(config.IncomeGrowth * (taperScore - config.IncomeRefScore));
        return floorAtTaper * Math.Exp(config.IncomeLegendaryTaperGrowth * (score - taperScore));
    }

    /// <summary>
    /// Fator da água na renda (0–1): sem perda de IncomeWaterPlateau a 100% (água "quase
    /// perfeita" não é punida); abaixo do patamar, cai numa curva suave até ~0 em água podre.
    /// </summary>
    public static double WaterFactor(decimal maintenanceLevel, TickConfig config)
    {
        double q = Math.Clamp((double)maintenanceLevel / 100.0, 0, 1);
        if (q >= config.IncomeWaterPlateau) return 1.0;
        double scaled = q / config.IncomeWaterPlateau;
        return Math.Pow(scaled, config.IncomeWaterExp);
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
    /// (IncomeOfflineCapMinutes). O fator de água é a MÉDIA do início e do fim da janela:
    /// numa ausência longa a água decai (às vezes até 0), então a renda offline reflete
    /// esse decaimento em vez de creditar tudo a "água cheia". Online com ticks frequentes,
    /// início≈fim, então é igual ao valor instantâneo.
    /// </summary>
    public static decimal Accrue(
        IReadOnlyList<FishIncome> fish,
        decimal maintenanceStart,
        decimal maintenanceEnd,
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

        double water = 0.5 * (WaterFactor(maintenanceStart, config) + WaterFactor(maintenanceEnd, config));
        double perHour = 0;
        foreach (var r in PerFishRates(fish, config))
            perHour += r;

        return (decimal)(perHour * water) * (effectiveMinutes / 60m);
    }
}
