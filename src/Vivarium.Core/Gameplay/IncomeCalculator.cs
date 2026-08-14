using Vivarium.Core.Generation;

namespace Vivarium.Core.Gameplay;

/// <summary>
/// Entrada por peixe pro cálculo de renda: raridade + cor das 3 partes (pra sinergia, 14/08/2026
/// — cauda/dorsal/peitoral contam separadamente, ver <see cref="IncomeCalculator.PartSynergyBonus"/>).
/// </summary>
public readonly record struct FishIncome(decimal RarityScore, PartColor TailColor, PartColor DorsalColor, PartColor PectoralColor);

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

    /// <summary>
    /// Bônus de sinergia (não o multiplicador — só a fração extra) pra N peixes com a MESMA cor
    /// numa única parte: 0 se N&lt;2; senão SynergyBaseBonus + SynergyPerExtraMatch·(N-2), com
    /// teto SynergyMaxBonus. Cauda/dorsal/peitoral usam essa MESMA fórmula, cada um contado
    /// separadamente (ver <see cref="PerFishRates"/>).
    /// </summary>
    public static double PartSynergyBonus(int sameColorCount, TickConfig config)
    {
        if (sameColorCount < 2) return 0;
        double bonus = config.SynergyBaseBonus + config.SynergyPerExtraMatch * (sameColorCount - 2);
        return Math.Min(config.SynergyMaxBonus, bonus);
    }

    /// <summary>
    /// Multiplicador de sinergia total do peixe: 1 + soma dos bônus das 3 partes (cauda, dorsal,
    /// peitoral), cada uma calculada dentro do próprio grupo de cor — não precisa as 3 partes
    /// baterem no mesmo grupo de peixes pra contar.
    /// </summary>
    public static double SynergyMultiplier(int tailCount, int dorsalCount, int pectoralCount, TickConfig config)
        => 1.0 + PartSynergyBonus(tailCount, config) + PartSynergyBonus(dorsalCount, config) + PartSynergyBonus(pectoralCount, config);

    /// <summary>Renda/hora de cada peixe (base × sinergia), já agrupada por cor de cada parte.</summary>
    private static double[] PerFishRates(IReadOnlyList<FishIncome> fish, TickConfig config)
    {
        var tailCounts = new Dictionary<PartColor, int>();
        var dorsalCounts = new Dictionary<PartColor, int>();
        var pectoralCounts = new Dictionary<PartColor, int>();
        foreach (var f in fish)
        {
            tailCounts[f.TailColor] = tailCounts.GetValueOrDefault(f.TailColor) + 1;
            dorsalCounts[f.DorsalColor] = dorsalCounts.GetValueOrDefault(f.DorsalColor) + 1;
            pectoralCounts[f.PectoralColor] = pectoralCounts.GetValueOrDefault(f.PectoralColor) + 1;
        }

        var rates = new double[fish.Count];
        for (int i = 0; i < fish.Count; i++)
        {
            var f = fish[i];
            double synergy = SynergyMultiplier(tailCounts[f.TailColor], dorsalCounts[f.DorsalColor], pectoralCounts[f.PectoralColor], config);
            rates[i] = CoinsPerHour(f.RarityScore, config) * synergy;
        }
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
