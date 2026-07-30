namespace Vivarium.Core.Gameplay;

/// <summary>
/// Parâmetros do breeding (CLAUDE.md 8.8). A gestação escala com a raridade
/// combinada dos pais — mesma linguagem exponencial da renda (IncomeCalculator):
/// casais comuns cruzam rápido, casais raros/lendários levam dias. Isso é o sink
/// de tempo que a economia estava sem (ver revisão de economia, gap #1).
/// </summary>
public static class BreedingDefaults
{
    public const double BaseGestationHours = 8.0;
    public const double GestationGrowth = 0.12;
    public const double GestationRefScore = 10.0;
    public const double MinGestationHours = 4.0;
    public const double MaxGestationHours = 240.0;
    public const double MutationChance = 0.08;

    /// <summary>
    /// Viés de raridade na herança do tier de brilho (0 = 50/50 puro, 1 = pesa
    /// pelo inverso exato da probabilidade). Calibrado por simulação (`Vivarium.Simulation
    /// breed`) pra favorecer "raro cruza com raro dá raro" sem virar atalho de
    /// "lavagem" de lendário cruzando com um peixe comum qualquer.
    /// </summary>
    public const double RarityBiasStrength = 0.15;

    // --- Custo dinâmico (soft) ---
    // O tempo de gestação já é o sink principal pra pares raros (um lendário
    // parado ~88h custa ~65k de renda perdida); o custo em moeda só precisa ser
    // um toque adicional, não o sink pesado. Mesma forma exponencial da gestação.
    public const decimal BaseCostSoft = 150m;
    public const double CostGrowth = 0.10;
    public const double CostRefScore = 10.0;
    public const decimal MinCostSoft = 100m;
    public const decimal MaxCostSoft = 5000m;

    // --- Risco de morte crescente por cruzamento ---
    // Cada gestação completada aumenta o risco do PRÓXIMO cruzamento — sem limite
    // fixo, mas nunca garantido (nunca chega em 100%). n = nº de gestações já
    // completadas por esse peixe (BreedCount antes desta coleta).
    public const double BaseDeathChance = 0.05;
    public const double MaxDeathChance = 0.85;
    public const double DeathRiskGrowth = 0.25;
}

public static class BreedingCalculator
{
    /// <summary>Horas de gestação: exponencial na soma dos rarity scores dos pais, com teto.</summary>
    public static double GestationHours(decimal parentAScore, decimal parentBScore)
    {
        double combined = (double)(parentAScore + parentBScore);
        double hours = BreedingDefaults.BaseGestationHours
            * Math.Exp(BreedingDefaults.GestationGrowth * (combined - BreedingDefaults.GestationRefScore));
        return Math.Clamp(hours, BreedingDefaults.MinGestationHours, BreedingDefaults.MaxGestationHours);
    }

    /// <summary>Custo em soft pra iniciar a gestação: exponencial na soma dos rarity scores dos pais, com teto.</summary>
    public static decimal CostSoft(decimal parentAScore, decimal parentBScore)
    {
        double combined = (double)(parentAScore + parentBScore);
        double cost = (double)BreedingDefaults.BaseCostSoft
            * Math.Exp(BreedingDefaults.CostGrowth * (combined - BreedingDefaults.CostRefScore));
        return Math.Clamp((decimal)cost, BreedingDefaults.MinCostSoft, BreedingDefaults.MaxCostSoft);
    }

    /// <summary>
    /// Chance de um pai não sobreviver à gestação, dado quantas ele já completou
    /// antes desta (n=0 na primeira vez). Cresce em direção a MaxDeathChance sem
    /// nunca alcançar 100% — sempre sobra uma margem de sorte.
    /// </summary>
    public static double DeathChance(int completedBreedCount)
    {
        double n = completedBreedCount;
        return BreedingDefaults.BaseDeathChance
            + (BreedingDefaults.MaxDeathChance - BreedingDefaults.BaseDeathChance)
            * (1 - Math.Exp(-BreedingDefaults.DeathRiskGrowth * n));
    }
}
