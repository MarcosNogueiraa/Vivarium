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
    public BreedingStatus Status { get; set; }
    public long? ChildCreatureId { get; set; }
    public CreatureInstance? ChildCreature { get; set; }
}
