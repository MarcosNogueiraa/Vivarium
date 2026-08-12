namespace Vivarium.Core.Domain;

/// <summary>
/// Par em gestação. Enquanto InProgress, os dois pais moram no Habitat de
/// reprodução do usuário (HabitatId aqui, não no CreatureInstance — a criatura
/// já tem HabitatId próprio apontando pra esse mesmo habitat). Só existe 1
/// gestação em andamento por usuário no MVP (capacidade do habitat = 2).
/// </summary>
public class BreedingSlot
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User? User { get; set; }
    public long HabitatId { get; set; }
    public Habitat? Habitat { get; set; }
    public long ParentAId { get; set; }
    public CreatureInstance? ParentA { get; set; }
    public long ParentBId { get; set; }
    public CreatureInstance? ParentB { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime ReadyAt { get; set; }
    public decimal CostPaid { get; set; }
    /// <summary>
    /// Risco de morte de cada pai TRAVADO no momento do Start (já considerando descanso e,
    /// se escolhidos, o estabilizador/seguro) — usado tal qual na coleta, sem recalcular
    /// (evita deriva: descansar DURANTE a gestação não deveria contar, o pai está "ocupado").
    /// </summary>
    public decimal ParentADeathChance { get; set; }
    public decimal ParentBDeathChance { get; set; }
    /// <summary>Seguro de cruzamento comprado no Start (premium) — garantiu 0% de morte pros dois pais.</summary>
    public bool InsuranceUsed { get; set; }
    public BreedingStatus Status { get; set; }
    public long? ChildCreatureId { get; set; }
    public CreatureInstance? ChildCreature { get; set; }
    /// <summary>
    /// Resultado do risco de morte, gravado na coleta (§8.19, registro de cruzamentos) —
    /// antes disso o desfecho só existia na resposta da API (CollectBreedingResponse), nunca
    /// persistido; sem isso um histórico não teria como saber quem morreu em qual gestação.
    /// Null enquanto InProgress (só definido em CollectAsync).
    /// </summary>
    public bool? ParentADied { get; set; }
    public bool? ParentBDied { get; set; }
}
