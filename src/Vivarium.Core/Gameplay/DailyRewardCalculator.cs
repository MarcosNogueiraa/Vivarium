namespace Vivarium.Core.Gameplay;

/// <summary>
/// Recompensa diária — redesenho 17/08/2026 (era valor fixo, sem streak, CLAUDE.md §8.10):
/// valor escalado pela renda do tanque + variância de roleta + bônus por dias consecutivos +
/// chance de ovo grátis. Lógica pura: randomness (roll01) sempre entra como parâmetro, nunca
/// gerada aqui (quem chama decide a fonte — produção usa RNG criptográfico, testes passam
/// valores fixos pra determinismo).
/// </summary>
public static class DailyRewardCalculator
{
    /// <summary>Valor base — N horas (config) da renda ATUAL do tanque, nunca abaixo do piso
    /// (pra quem tá começando, tanque vazio ou renda baixa).</summary>
    public static decimal BaseAmount(decimal coinsPerHour, TickConfig config)
        => Math.Max(config.DailyRewardMinSoft, coinsPerHour * config.DailyRewardIncomeHours);

    /// <summary>Faixa [min,max] que a roleta pode sortear em cima do valor base — usada pra
    /// mostrar uma prévia honesta antes do resgate (o valor exato só sai depois de resgatar).</summary>
    public static (decimal Min, decimal Max) RouletteRange(decimal baseAmount, TickConfig config)
    {
        decimal factor = (decimal)config.DailyRewardRouletteRange;
        return (Math.Round(baseAmount * (1 - factor)), Math.Round(baseAmount * (1 + factor)));
    }

    /// <summary>Sorteia o valor final da roleta em cima do base — <paramref name="roll01"/> em [0,1).</summary>
    public static decimal RouletteAmount(decimal baseAmount, double roll01, TickConfig config)
    {
        double range = config.DailyRewardRouletteRange;
        double multiplier = (1 - range) + roll01 * (2 * range);
        return Math.Round(baseAmount * (decimal)multiplier);
    }

    /// <summary>Multiplicador de bônus por dias consecutivos — streak=1 (sem sequência ainda)
    /// já é 1.0x (sem bônus); nunca reduz abaixo disso, streak só soma upside.</summary>
    public static double StreakMultiplier(int streak, TickConfig config)
        => 1.0 + Math.Min(config.DailyRewardStreakBonusCap, config.DailyRewardStreakBonusPerDay * Math.Max(0, streak - 1));

    /// <summary>
    /// Próximo valor de streak: se o último resgate foi ONTEM (calendário UTC), soma 1 (sequência
    /// continua); qualquer lacuna maior reseta pro dia 1. A pedido do usuário (17/08/2026): reseta
    /// de verdade ao faltar um dia — não é só "voltar a contar do zero sem perder nada", é perder
    /// o bônus acumulado mesmo, pra estimular presença diária de verdade. Isso NÃO contradiz "streak
    /// nunca pune" (CLAUDE.md, filosofia geral) — perder o bônus só volta pro valor BASE (nunca
    /// abaixo do piso), nunca deixa o jogador pior que a linha de base.
    /// </summary>
    public static int NextStreak(int previousStreak, DateTime? lastClaimedAtUtc, DateTime nowUtc)
        => lastClaimedAtUtc is { } last && last.Date == nowUtc.Date.AddDays(-1) ? previousStreak + 1 : 1;

    /// <summary>Ganhou o ovo bônus desta vez? <paramref name="roll01"/> em [0,1).</summary>
    public static bool RollsEgg(double roll01, TickConfig config) => roll01 < config.DailyRewardEggChance;

    /// <summary>Valor final creditado: base → roleta → streak, nessa ordem.</summary>
    public static decimal FinalAmount(decimal coinsPerHour, int streak, double rouletteRoll01, TickConfig config)
    {
        decimal baseAmount = BaseAmount(coinsPerHour, config);
        decimal rouletteAmount = RouletteAmount(baseAmount, rouletteRoll01, config);
        return Math.Round(rouletteAmount * (decimal)StreakMultiplier(streak, config));
    }
}
