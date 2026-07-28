namespace Vivarium.Core.Gameplay;

/// <summary>
/// Renda passiva de moedas (soft) por raridade dos peixes. Lógica pura: recebe os
/// scores e o estado da janela, devolve quanto foi ganho. Toda renda é calculada
/// aqui e no servidor — o cliente nunca envia valor (anti-cheat).
/// </summary>
public static class IncomeCalculator
{
    /// <summary>Moedas por hora que um peixe rende, exponencial na raridade.</summary>
    public static double CoinsPerHour(decimal rarityScore, TickConfig config)
        => config.IncomeBasePerHour
           * Math.Exp(config.IncomeGrowth * ((double)rarityScore - config.IncomeRefScore));

    /// <summary>Fator da água na renda (0–1): água suja rende menos, água 0 rende ~0.</summary>
    public static double WaterFactor(decimal maintenanceLevel, TickConfig config)
    {
        double q = Math.Clamp((double)maintenanceLevel / 100.0, 0, 1);
        return Math.Pow(q, config.IncomeWaterExp);
    }

    /// <summary>Soma das taxas/hora dos peixes já com o fator água (pra exibir "+X/h").</summary>
    public static decimal TankRatePerHour(IEnumerable<decimal> rarityScores, decimal maintenanceLevel, TickConfig config)
    {
        double water = WaterFactor(maintenanceLevel, config);
        double total = 0;
        foreach (var s in rarityScores)
            total += CoinsPerHour(s, config);
        return (decimal)(total * water);
    }

    /// <summary>
    /// Moedas ganhas na janela do tick. Online full, offline a offlineRate e com teto
    /// (IncomeOfflineCapMinutes). Usa a água do início da janela (o tick roda com
    /// frequência; ausência longa é dominada pelo teto offline).
    /// </summary>
    public static decimal Accrue(
        IEnumerable<decimal> rarityScores,
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
        foreach (var s in rarityScores)
            perHour += CoinsPerHour(s, config);

        return (decimal)(perHour * water) * (effectiveMinutes / 60m);
    }
}
