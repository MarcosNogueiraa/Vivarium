namespace Vivarium.Core.Gameplay;

/// <summary>
/// Acelerar (pular) espera com moeda premium (CLAUDE.md 8.11) — a ÚNICA forma de
/// comprimir o tempo de geração/gestação, que por design é lento (anti-rush). Custo
/// proporcional ao tempo restante: quanto mais falta, mais caro pular tudo de uma vez.
/// Não há desconto por rarity aqui — a gestação de peixes raros já é mais longa
/// (BreedingCalculator), então o custo de acelerar já escala com a raridade
/// indiretamente (mais horas restantes = mais caro).
/// </summary>
public static class RushConfig
{
    /// <summary>Fila de geração: pra 60 min (1 peixe), pular custa ~9 premium.</summary>
    public const decimal PremiumPerMinuteQueue = 0.15m;

    /// <summary>Gestação: pra 24h (casal comum), pular custa 48 premium; pro teto de 240h, 480.</summary>
    public const decimal PremiumPerHourGestation = 2.0m;

    /// <summary>Nunca menos que isso, mesmo pra "quase pronto" — sem microtransação de centavos.</summary>
    public const decimal MinRushCostPremium = 1m;
}

public static class RushCalculator
{
    public static decimal QueueRushCost(decimal minutesRemaining)
        => Math.Max(RushConfig.MinRushCostPremium, Math.Ceiling(RushConfig.PremiumPerMinuteQueue * Math.Max(0, minutesRemaining)));

    public static decimal GestationRushCost(decimal hoursRemaining)
        => Math.Max(RushConfig.MinRushCostPremium, Math.Ceiling(RushConfig.PremiumPerHourGestation * Math.Max(0, hoursRemaining)));
}
