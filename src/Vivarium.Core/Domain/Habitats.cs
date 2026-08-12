namespace Vivarium.Core.Domain;

// Habitat genérico, não Tank fixo: o mesmo motor de tick/geração serve pra
// aquário hoje e terrário/orquídea no futuro, só mudando dados de configuração.
public class HabitatType
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
}

public class Habitat
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User? User { get; set; }
    public int HabitatTypeId { get; set; }
    public HabitatType? HabitatType { get; set; }
    public int Capacity { get; set; }
    /// <summary>Generalização de "qualidade da água" (0–100).</summary>
    public decimal MaintenanceLevel { get; set; }
    public int QueueCap { get; set; }
    public int GenerationIntervalMinutes { get; set; }
    public decimal OnlineGenerationRate { get; set; }
    public decimal OfflineGenerationRate { get; set; }
    /// <summary>Resto de progresso de geração entre ticks (minutos efetivos acumulados).</summary>
    public decimal GenerationProgressMinutes { get; set; }
    /// <summary>Fração de moedas farmadas ainda não creditada (credita inteiros, guarda o resto).</summary>
    public decimal CoinAccrual { get; set; }
    public DateTime LastTickAt { get; set; }
    public DateTime? LastHeartbeatAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Sensor de Qualidade da Água (item permanente, por aquário — CLAUDE.md §8.18): habilita
    /// configurar <see cref="AutoCleanTriggerPercent"/> acima de 0. Sem o sensor, a Limpeza
    /// Automática de VIP (ver <c>GameService.ApplyAutoCleanAsync</c>) usa gatilho fixo em 0%.
    /// </summary>
    public bool HasWaterSensor { get; set; }
    /// <summary>
    /// Gatilho (0–<c>TickConfig.WaterSensorMaxTriggerPercent</c>) de água abaixo do qual a
    /// Limpeza Automática de VIP compra um Filtro sozinha. Só tem efeito com
    /// <see cref="HasWaterSensor"/> == true; por padrão fica em 0 (dormente).
    /// </summary>
    public decimal AutoCleanTriggerPercent { get; set; }
}

public class Species
{
    public int Id { get; set; }
    public int HabitatTypeId { get; set; }
    public HabitatType? HabitatType { get; set; }
    public required string Name { get; set; }
    public required string BaseSpriteKey { get; set; }
}

// Pesos versionados com vigência: mudar pesos não altera peixes antigos,
// porque cada CreatureInstance guarda a versão usada no nascimento.
public class TraitWeightConfig
{
    public int Id { get; set; }
    public int? SpeciesId { get; set; }
    public Species? Species { get; set; }
    public TraitPartType PartType { get; set; }
    public TraitCategory TraitCategory { get; set; }
    public required string ConfigJson { get; set; }
    public int Version { get; set; }
    public DateTime EffectiveFrom { get; set; }
}

public class GenerationQueueItem
{
    public long Id { get; set; }
    public long HabitatId { get; set; }
    public Habitat? Habitat { get; set; }
    public int SpeciesId { get; set; }
    public Species? Species { get; set; }
    public DateTime ReadyAt { get; set; }
    public QueueItemStatus Status { get; set; }
    /// <summary>Nasceu com a qualidade da água crítica: coleta com desvantagem de raridade.</summary>
    public bool IsSick { get; set; }
}
