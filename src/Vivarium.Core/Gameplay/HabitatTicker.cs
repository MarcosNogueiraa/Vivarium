namespace Vivarium.Core.Gameplay;

/// <summary>Snapshot do habitat que o tick precisa ler (mapeado da entidade pelo chamador).</summary>
public sealed record HabitatTickState(
    DateTime LastTickAtUtc,
    DateTime? LastHeartbeatAtUtc,
    decimal MaintenanceLevel,
    decimal GenerationProgressMinutes,
    int GenerationIntervalMinutes,
    decimal OnlineGenerationRate,
    decimal OfflineGenerationRate,
    int QueueCap,
    int PendingQueueCount,
    /// <summary>
    /// Capacidade de peixes coberta pelo melhor filtro automático do jogador (0 = sem
    /// filtro). Substitui o antigo bool binário (08/08/2026) — acima dessa capacidade o
    /// benefício tapera suavemente em vez de sumir (ver <see cref="HabitatTicker.ProcessTick"/>).
    /// </summary>
    decimal FilterCapacity,
    /// <summary>Soma de `rarityScore/DegradationRarityRefScore` de cada peixe ativo — "nº efetivo" pra degradação (08/08/2026, era contagem simples).</summary>
    decimal ActiveFishWeight,
    /// <summary>Fator de degradação da faixa de capacidade atual do tanque (`CapacityBands.BandFor`, 08/08/2026).</summary>
    decimal CapacityBandDegradationFactor);

/// <summary>Resultado do tick: novos valores a persistir + quantos itens entram na fila.</summary>
public sealed record TickOutcome(
    DateTime LastTickAtUtc,
    decimal MaintenanceLevel,
    decimal GenerationProgressMinutes,
    IReadOnlyList<bool> SpawnSickFlags,
    decimal OnlineMinutes,
    decimal OfflineMinutes,
    decimal MaintenanceAtStart)
{
    public int SpawnCount => SpawnSickFlags.Count;
}

/// <summary>
/// Lógica pura do tick de habitat: sem banco, sem relógio próprio — recebe o
/// estado e o "agora", devolve o que mudou. O chamador (API) aplica na entidade.
/// </summary>
public static class HabitatTicker
{
    public static bool IsOnline(DateTime? lastHeartbeatUtc, DateTime nowUtc, TickConfig config)
        => lastHeartbeatUtc is { } hb && nowUtc - hb <= config.HeartbeatTimeout;

    public static TickOutcome ProcessTick(HabitatTickState state, DateTime nowUtc, Random rng, TickConfig config)
    {
        if (nowUtc <= state.LastTickAtUtc)
            return new TickOutcome(state.LastTickAtUtc, state.MaintenanceLevel, state.GenerationProgressMinutes,
                [], 0, 0, state.MaintenanceLevel);

        decimal elapsedMinutes = (decimal)(nowUtc - state.LastTickAtUtc).TotalMinutes;

        // Janela dividida em online/offline: online do início até o último heartbeat;
        // se o heartbeat ainda está fresco, a janela inteira conta como online.
        decimal onlineMinutes;
        if (IsOnline(state.LastHeartbeatAtUtc, nowUtc, config))
            onlineMinutes = elapsedMinutes;
        else if (state.LastHeartbeatAtUtc is { } hb)
            onlineMinutes = Math.Clamp((decimal)(hb - state.LastTickAtUtc).TotalMinutes, 0, elapsedMinutes);
        else
            onlineMinutes = 0;
        decimal offlineMinutes = elapsedMinutes - onlineMinutes;

        // Degradação escala com o peso (raridade) dos peixes: tanque cheio/rico suja
        // mais rápido (manutenção vira ralo contínuo proporcional ao tamanho/renda).
        decimal fishFactor = 1m + config.DegradationPerFishFactor * state.ActiveFishWeight;

        // Filtro em nível (08/08/2026): cobertura total reduz a degradação pela metade
        // (mesmo benefício de sempre); acima da capacidade do filtro, o benefício tapera
        // suavemente de volta a 1.0 conforme o excedente cresce — sem penhasco, mesmo
        // princípio do IncomeWaterPlateau/IncomeWaterExp (CLAUDE.md §8.6).
        decimal filterFactor = state.FilterCapacity <= 0
            ? 1m
            : state.ActiveFishWeight <= state.FilterCapacity
                ? 0.5m
                : 0.5m + 0.5m * (1m - (decimal)Math.Pow(
                    (double)(state.FilterCapacity / state.ActiveFishWeight), config.FilterTaperExponent));

        decimal degradation = config.DegradationPerMinute * fishFactor * state.CapacityBandDegradationFactor
            * elapsedMinutes * filterFactor;
        decimal newMaintenance = Math.Max(0, state.MaintenanceLevel - degradation);

        // Fator de manutenção usa o nível no início da janela (simplificação:
        // o tick roda com frequência suficiente pra transição não pesar).
        decimal maintenanceFactor = state.MaintenanceLevel < config.LowMaintenanceThreshold
            ? config.LowMaintenanceRateFactor
            : 1m;

        decimal progress = state.GenerationProgressMinutes
            + (onlineMinutes * state.OnlineGenerationRate + offlineMinutes * state.OfflineGenerationRate)
            * maintenanceFactor;

        int space = Math.Max(0, state.QueueCap - state.PendingQueueCount);
        int spawn = Math.Clamp((int)(progress / state.GenerationIntervalMinutes), 0, space);
        progress -= spawn * state.GenerationIntervalMinutes;
        // Fila cheia não acumula estoque de progresso: no máximo 1 item "quase pronto"
        progress = Math.Min(progress, state.GenerationIntervalMinutes);

        bool sickEligible = state.MaintenanceLevel < config.SickMaintenanceThreshold;
        var sickFlags = new bool[spawn];
        for (int i = 0; i < spawn; i++)
            sickFlags[i] = sickEligible && rng.NextDouble() < config.SickChance;

        return new TickOutcome(nowUtc, newMaintenance, progress, sickFlags,
            onlineMinutes, offlineMinutes, state.MaintenanceLevel);
    }
}
