namespace Vivarium.Core.Gameplay;

/// <summary>
/// Parâmetros do loop de gameplay (CLAUDE.md seção 8). Valores default do MVP;
/// ajustáveis via simulação sem tocar na lógica.
/// </summary>
public sealed record TickConfig
{
    /// <summary>Sem heartbeat há mais que isso = tanque offline.</summary>
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>~-1 de qualidade a cada 20 min.</summary>
    public decimal DegradationPerMinute { get; init; } = 1m / 20m;

    /// <summary>Abaixo disso a velocidade de geração cai.</summary>
    public decimal LowMaintenanceThreshold { get; init; } = 40m;
    public decimal LowMaintenanceRateFactor { get; init; } = 0.5m;

    /// <summary>Abaixo disso, itens novos da fila podem nascer doentes.</summary>
    public decimal SickMaintenanceThreshold { get; init; } = 15m;
    public double SickChance { get; init; } = 0.10;

    public static readonly TickConfig Default = new();
}

/// <summary>Valores iniciais de um habitat novo (tanque inicial do MVP).</summary>
public static class HabitatDefaults
{
    public const int Capacity = 3;
    public const int QueueCap = 5;
    public const int GenerationIntervalMinutes = 15;
    public const decimal OnlineGenerationRate = 1.0m;
    public const decimal OfflineGenerationRate = 0.45m;
    public const decimal MaintenanceLevel = 100m;
}
