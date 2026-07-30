using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Data;

public class VivariumDbContext(DbContextOptions<VivariumDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<VipSubscription> VipSubscriptions => Set<VipSubscription>();
    public DbSet<CurrencyType> CurrencyTypes => Set<CurrencyType>();
    public DbSet<WalletBalance> WalletBalances => Set<WalletBalance>();
    public DbSet<HabitatType> HabitatTypes => Set<HabitatType>();
    public DbSet<Habitat> Habitats => Set<Habitat>();
    public DbSet<Species> Species => Set<Species>();
    public DbSet<TraitWeightConfig> TraitWeightConfigs => Set<TraitWeightConfig>();
    public DbSet<GenerationQueueItem> GenerationQueueItems => Set<GenerationQueueItem>();
    public DbSet<CreatureInstance> CreatureInstances => Set<CreatureInstance>();
    public DbSet<ItemDefinition> ItemDefinitions => Set<ItemDefinition>();
    public DbSet<UserInventory> UserInventories => Set<UserInventory>();
    public DbSet<MarketListing> MarketListings => Set<MarketListing>();
    public DbSet<TransactionLog> TransactionLogs => Set<TransactionLog>();
    public DbSet<BreedingSlot> BreedingSlots => Set<BreedingSlot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Concorrência otimista nas linhas de dinheiro/posse/estado: evita
        // corrida (compra dupla da mesma listagem, double-credit de renda,
        // preço de item). xmin é coluna de sistema do Postgres — no SQLite dos
        // testes não existe, por isso é condicional ao provider.
        if (Database.IsNpgsql())
        {
            // Mapeia a coluna de sistema xmin do Postgres como token de concorrência.
            foreach (var type in new[]
                     { typeof(Habitat), typeof(WalletBalance), typeof(MarketListing), typeof(CreatureInstance) })
            {
                modelBuilder.Entity(type).Property<uint>("xmin")
                    .HasColumnName("xmin").HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            }
        }

        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.Username).HasMaxLength(32);
            e.Property(u => u.Email).HasMaxLength(256);
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<VipSubscription>(e =>
        {
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(s => new { s.UserId, s.Status });
        });

        modelBuilder.Entity<CurrencyType>(e =>
        {
            e.Property(c => c.Code).HasMaxLength(16);
            e.Property(c => c.Name).HasMaxLength(64);
            e.HasIndex(c => c.Code).IsUnique();
            e.HasData(
                new CurrencyType { Id = 1, Code = "SOFT", Name = "Moeda Soft" },
                new CurrencyType { Id = 2, Code = "PREMIUM", Name = "Moeda Premium" });
        });

        modelBuilder.Entity<WalletBalance>(e =>
        {
            e.Property(w => w.Amount).HasPrecision(18, 2);
            e.HasIndex(w => new { w.UserId, w.CurrencyTypeId }).IsUnique();
        });

        modelBuilder.Entity<HabitatType>(e =>
        {
            e.Property(h => h.Code).HasMaxLength(32);
            e.Property(h => h.Name).HasMaxLength(64);
            e.HasIndex(h => h.Code).IsUnique();
            e.HasData(
                new HabitatType { Id = 1, Code = "Aquarium", Name = "Aquário" },
                new HabitatType { Id = 2, Code = "Breeding", Name = "Ninho" });
        });

        modelBuilder.Entity<Habitat>(e =>
        {
            e.Property(h => h.MaintenanceLevel).HasPrecision(5, 2);
            e.Property(h => h.GenerationProgressMinutes).HasPrecision(10, 4);
            e.Property(h => h.CoinAccrual).HasPrecision(18, 6);
            e.Property(h => h.OnlineGenerationRate).HasPrecision(5, 2);
            e.Property(h => h.OfflineGenerationRate).HasPrecision(5, 2);
            e.HasIndex(h => h.UserId);
        });

        modelBuilder.Entity<Species>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(64);
            e.Property(s => s.BaseSpriteKey).HasMaxLength(64);
            e.HasData(new Species { Id = 1, HabitatTypeId = 1, Name = "Tetra Base", BaseSpriteKey = "fish_base_gray" });
        });

        modelBuilder.Entity<TraitWeightConfig>(e =>
        {
            e.Property(t => t.PartType).HasConversion<string>().HasMaxLength(16);
            e.Property(t => t.TraitCategory).HasConversion<string>().HasMaxLength(24);
            e.HasIndex(t => new { t.SpeciesId, t.PartType, t.TraitCategory, t.Version });
        });

        modelBuilder.Entity<GenerationQueueItem>(e =>
        {
            e.Property(q => q.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(q => new { q.HabitatId, q.Status });
        });

        modelBuilder.Entity<CreatureInstance>(e =>
        {
            e.Property(c => c.RarityScore).HasPrecision(10, 4);
            e.HasIndex(c => c.OwnerId);
            e.HasIndex(c => c.HabitatId);
            e.HasOne(c => c.ParentA).WithMany().HasForeignKey(c => c.ParentAId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.ParentB).WithMany().HasForeignKey(c => c.ParentBId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ItemDefinition>(e =>
        {
            e.Property(i => i.Key).HasMaxLength(64);
            e.Property(i => i.Name).HasMaxLength(128);
            e.Property(i => i.Category).HasConversion<string>().HasMaxLength(24);
            e.Property(i => i.PriceSoft).HasPrecision(18, 2);
            e.Property(i => i.PricePremium).HasPrecision(18, 2);
            e.HasIndex(i => i.Key).IsUnique();
            e.HasData(
                new ItemDefinition
                {
                    Id = 1, Key = "filter_basic", Name = "Filtro",
                    Category = ItemCategory.Filter,
                    EffectJson = """{"restoreMaintenance":100}""", PriceSoft = 20m,
                },
                new ItemDefinition
                {
                    Id = 2, Key = "auto_filter", Name = "Filtro Automático",
                    Category = ItemCategory.AutoFilter,
                    EffectJson = """{"autoFilter":true}""", PriceSoft = 500m,
                },
                new ItemDefinition
                {
                    // Preço dinâmico: PriceSoft é o base; cobra base × 1.5^(capacidade - inicial)
                    Id = 3, Key = "tank_upgrade", Name = "Expansão do Tanque",
                    Category = ItemCategory.HabitatUpgrade,
                    EffectJson = """{"capacityDelta":1}""", PriceSoft = 50m,
                });
        });

        modelBuilder.Entity<UserInventory>(e =>
        {
            e.HasIndex(i => new { i.UserId, i.ItemDefinitionId }).IsUnique();
        });

        modelBuilder.Entity<MarketListing>(e =>
        {
            e.Property(m => m.PriceSoft).HasPrecision(18, 2);
            e.Property(m => m.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(m => m.Status);
            e.HasIndex(m => m.SellerId);
            e.HasOne(m => m.Seller).WithMany().HasForeignKey(m => m.SellerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Buyer).WithMany().HasForeignKey(m => m.BuyerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TransactionLog>(e =>
        {
            e.Property(t => t.Type).HasConversion<string>().HasMaxLength(24);
            e.Property(t => t.Amount).HasPrecision(18, 2);
            e.HasIndex(t => t.FromUserId);
            e.HasIndex(t => t.ToUserId);
            e.HasIndex(t => t.CreatedAt);
        });

        modelBuilder.Entity<BreedingSlot>(e =>
        {
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(s => s.CostPaid).HasPrecision(18, 2);
            e.HasIndex(s => new { s.UserId, s.Status });
            e.HasIndex(s => s.HabitatId);
            e.HasOne(s => s.ParentA).WithMany().HasForeignKey(s => s.ParentAId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.ParentB).WithMany().HasForeignKey(s => s.ParentBId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.ChildCreature).WithMany().HasForeignKey(s => s.ChildCreatureId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
