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
    public const decimal CostSoft = 150m;
    public const double MutationChance = 0.08;
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
}
