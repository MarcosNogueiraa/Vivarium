namespace Vivarium.Core.Gameplay;

/// <summary>
/// Parâmetros do loop de gameplay (CLAUDE.md seção 8). Valores default do MVP;
/// ajustáveis via simulação sem tocar na lógica.
/// </summary>
public sealed record TickConfig
{
    /// <summary>Sem heartbeat há mais que isso = tanque offline.</summary>
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>Degradação base (~-1 de qualidade a cada 20 min com 0 peixes).</summary>
    public decimal DegradationPerMinute { get; init; } = 1m / 20m;
    /// <summary>Degradação escala com o nº de peixes: base·(1 + k·nºpeixes). k moderado.</summary>
    public decimal DegradationPerFishFactor { get; init; } = 0.10m;

    /// <summary>Abaixo disso a velocidade de geração cai.</summary>
    public decimal LowMaintenanceThreshold { get; init; } = 40m;
    public decimal LowMaintenanceRateFactor { get; init; } = 0.5m;

    /// <summary>Abaixo disso, itens novos da fila podem nascer doentes.</summary>
    public decimal SickMaintenanceThreshold { get; init; } = 15m;
    public double SickChance { get; init; } = 0.10;

    // --- Farm de moedas (renda passiva por raridade) ---
    // coinsPorHora(score) = IncomeBasePerHour * exp(IncomeGrowth * (score - IncomeRefScore))
    // Base reduzida (ritmo lento) mantendo o topo íngreme (lendário valioso).
    public double IncomeBasePerHour { get; init; } = 2.0;
    public double IncomeGrowth { get; init; } = 0.49;
    public double IncomeRefScore { get; init; } = 4.0;
    /// <summary>Fator água na renda: (maint/100)^IncomeWaterExp. Água suja corta a renda.</summary>
    public double IncomeWaterExp { get; init; } = 0.7;
    /// <summary>Teto de minutos offline que rendem (8h) — evita acúmulo infinito.</summary>
    public decimal IncomeOfflineCapMinutes { get; init; } = 480m;

    // --- Sinergia por cor de cauda (forte): N peixes de mesma cor → cada um
    // multiplica a renda por 1 + SynergyPerMatch·(N-1), com teto SynergyMaxBonus.
    public double SynergyPerMatch { get; init; } = 0.15;
    public double SynergyMaxBonus { get; init; } = 0.80;

    public static readonly TickConfig Default = new();
}

/// <summary>Valores iniciais da economia de um jogador novo.</summary>
public static class EconomyDefaults
{
    public const decimal StartingSoftBalance = 100m;
    public const decimal StartingPremiumBalance = 0m;
}

/// <summary>Valores iniciais de um habitat novo (tanque inicial do MVP).</summary>
public static class HabitatDefaults
{
    public const int Capacity = 3;
    /// <summary>Storage de criaturas fora do tanque (não farmam). Base pro breeding.</summary>
    public const int BackpackCapacity = 50;
    public const int QueueCap = 5;
    /// <summary>Geração mais lenta (ritmo lento + lendário ~1/mês). Era 15.</summary>
    public const int GenerationIntervalMinutes = 25;
    public const decimal OnlineGenerationRate = 1.0m;
    public const decimal OfflineGenerationRate = 0.45m;
    public const decimal MaintenanceLevel = 100m;
}
