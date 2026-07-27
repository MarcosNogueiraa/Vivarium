namespace Vivarium.Core.Domain;

// Nenhum trait individual é salvo: tudo é derivado do Seed + TraitConfigVersion
// on-demand pelo motor de geração (Vivarium.Core.Generation).
public class CreatureInstance
{
    public long Id { get; set; }
    public int SpeciesId { get; set; }
    public Species? Species { get; set; }
    public long OwnerId { get; set; }
    public User? Owner { get; set; }
    /// <summary>Null quando está listado no mercado ou em trânsito.</summary>
    public long? HabitatId { get; set; }
    public Habitat? Habitat { get; set; }
    public long Seed { get; set; }
    public int TraitConfigVersion { get; set; }
    public decimal RarityScore { get; set; }
    public long? ParentAId { get; set; }
    public CreatureInstance? ParentA { get; set; }
    public long? ParentBId { get; set; }
    public CreatureInstance? ParentB { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MarketListing
{
    public long Id { get; set; }
    public long CreatureInstanceId { get; set; }
    public CreatureInstance? CreatureInstance { get; set; }
    public long SellerId { get; set; }
    public User? Seller { get; set; }
    public long? BuyerId { get; set; }
    public User? Buyer { get; set; }
    public decimal PriceSoft { get; set; }
    public ListingStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
