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
    /// <summary>
    /// Seed dos pais denormalizado no filho (imutável, nunca muda) — evita join só
    /// pra reconstruir os traits herdados via BreedTraits. Null se não for filhote.
    /// </summary>
    public long? ParentASeed { get; set; }
    public long? ParentBSeed { get; set; }
    /// <summary>
    /// Seeds dos AVÓS (pais de ParentA/ParentB), denormalizados do próprio pai no momento da
    /// criação — mesma ideia de ParentASeed/BSeed, só uma geração mais fundo. Habilita a chance
    /// de herdar um traço de um avô em vez do pai direto (`BreedingDefaults.GrandparentReachChance`)
    /// e permite reconstruir os traits REAIS deste filhote depois (se ele virar pai de outro
    /// cruzamento) sem usar Generate(seed) num pai que na verdade é filhote (bug corrigido 31/07/2026).
    /// Null se o respectivo pai (A ou B) não era ele mesmo um filhote.
    /// </summary>
    public long? ParentAGrandparentASeed { get; set; }
    public long? ParentAGrandparentBSeed { get; set; }
    public long? ParentBGrandparentASeed { get; set; }
    public long? ParentBGrandparentBSeed { get; set; }
    /// <summary>Nº de gestações que este peixe já completou como pai/mãe (risco de morte cresce com o uso).</summary>
    public int BreedCount { get; set; }
    public bool IsDead { get; set; }
    public DateTime? DiedAt { get; set; }
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
