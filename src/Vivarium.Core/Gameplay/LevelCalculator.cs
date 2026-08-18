namespace Vivarium.Core.Gameplay;

/// <summary>
/// Curva de XP → nível do jogador (puro, sem I/O — mesma filosofia de
/// <c>IncomeCalculator</c>/<c>DailyRewardCalculator</c>). Nível é sempre derivado ao vivo do
/// <c>User.Xp</c>, nunca armazenado separado (evita drift entre os dois).
/// </summary>
public static class LevelCalculator
{
    /// <summary>XP total acumulado pra ALCANÇAR <paramref name="level"/> (nível 1 = 0 XP).</summary>
    public static long XpForLevel(int level, LevelConfig cfg)
    {
        if (level <= 1) return 0;
        return (long)Math.Round(cfg.LevelBaseXp * Math.Pow(level - 1, cfg.LevelExponent), MidpointRounding.AwayFromZero);
    }

    /// <summary>Maior nível cujo XpForLevel não ultrapassa <paramref name="xp"/>.</summary>
    public static int LevelForXp(long xp, LevelConfig cfg)
    {
        if (xp <= 0) return 1;

        // Estimativa por inversão da curva, corrigida por caminhada curta (o arredondamento
        // de XpForLevel não é uma bijeção perfeita, então a estimativa pode errar por 1-2).
        double estimate = 1 + Math.Pow(xp / (double)cfg.LevelBaseXp, 1.0 / cfg.LevelExponent);
        int level = Math.Max(1, (int)Math.Round(estimate));

        while (level > 1 && XpForLevel(level, cfg) > xp)
            level--;
        while (XpForLevel(level + 1, cfg) <= xp)
            level++;

        return level;
    }

    /// <summary>Nível atual, XP dentro do nível, XP necessário pro próximo, e fração 0-1 pra barra de progresso.</summary>
    public static (int Level, long CurrentLevelXp, long XpForNextLevel, double Progress01) ProgressOf(long xp, LevelConfig cfg)
    {
        int level = LevelForXp(xp, cfg);
        long floorXp = XpForLevel(level, cfg);
        long nextLevelXp = XpForLevel(level + 1, cfg);
        long currentLevelXp = xp - floorXp;
        long xpForNextLevel = nextLevelXp - floorXp;
        double progress = xpForNextLevel > 0 ? Math.Clamp(currentLevelXp / (double)xpForNextLevel, 0.0, 1.0) : 1.0;
        return (level, currentLevelXp, xpForNextLevel, progress);
    }
}
