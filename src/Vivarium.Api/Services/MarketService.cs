using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Services;

/// <summary>
/// Regras do mercado interno (listar, cancelar, comprar). Lógica de negócio fora
/// dos handlers HTTP; devolve <see cref="ServiceResult"/> que o endpoint traduz.
/// Sem taxa de mercado no MVP; compra é transacional + auditada no TransactionLog.
/// </summary>
public class MarketService(VivariumDbContext db, GameService game)
{
    public async Task<List<ListingDto>> ListingsAsync(int skip, int take)
    {
        take = Math.Clamp(take, 1, 100);
        return (await db.MarketListings
            .Where(m => m.Status == ListingStatus.Active)
            .OrderByDescending(m => m.CreatedAt)
            .Skip(Math.Max(0, skip)).Take(take)
            .Select(m => new
            {
                m.Id, m.PriceSoft, m.SellerId, SellerName = m.Seller!.Username,
                CreatureId = m.CreatureInstanceId, m.CreatureInstance!.SpeciesId,
                m.CreatureInstance.Seed, m.CreatureInstance.TraitConfigVersion,
                m.CreatureInstance.RarityScore, m.CreatureInstance.ParentAId,
                m.CreatureInstance.ParentASeed, m.CreatureInstance.ParentBSeed,
                m.CreatureInstance.ParentAGrandparentASeed, m.CreatureInstance.ParentAGrandparentBSeed,
                m.CreatureInstance.ParentBGrandparentASeed, m.CreatureInstance.ParentBGrandparentBSeed,
                m.CreatureInstance.TraitsJson, m.CreatureInstance.BreedingSourceJson,
            })
            .ToListAsync())
            .Select(m => new ListingDto(
                m.Id, m.PriceSoft, m.SellerId, m.SellerName,
                m.CreatureId, m.SpeciesId, m.Seed.ToString(),
                m.TraitConfigVersion, m.RarityScore, m.ParentAId.HasValue,
                m.ParentASeed?.ToString(), m.ParentBSeed?.ToString(),
                m.ParentAGrandparentASeed?.ToString(), m.ParentAGrandparentBSeed?.ToString(),
                m.ParentBGrandparentASeed?.ToString(), m.ParentBGrandparentBSeed?.ToString(),
                m.TraitsJson is not null ? TraitsSerialization.DeserializeTraits(m.TraitsJson) : null,
                m.BreedingSourceJson is not null ? TraitsSerialization.DeserializeSource(m.BreedingSourceJson) : null))
            .ToList();
    }

    public async Task<ServiceResult> CreateListingAsync(long userId, CreateListingRequest req)
    {
        if (req.PriceSoft <= 0)
            return ServiceResult.Bad("Preço deve ser maior que zero");

        var creature = await db.CreatureInstances
            .FirstOrDefaultAsync(c => c.Id == req.CreatureInstanceId && c.OwnerId == userId);
        if (creature is null)
            return ServiceResult.NotFound("Criatura não encontrada");
        if (creature.IsDead)
            return ServiceResult.Bad("Essa criatura não sobreviveu à gestação");
        if (creature.SoldAt is not null)
            return ServiceResult.Bad("Essa criatura já foi vendida ao NPC");
        // Bug real corrigido (12/08/2026, relatado pelo usuário): `HabitatId is null` também é
        // verdade pra uma criatura na MOCHILA (estado normal, não "em trânsito") — o check
        // bloqueava listar qualquer peixe guardado ali, sempre. O que realmente precisa ser
        // checado é se já existe uma listagem ATIVA, e se o peixe não está preso numa gestação.
        bool alreadyListed = await db.MarketListings.AnyAsync(m =>
            m.CreatureInstanceId == creature.Id && m.Status == ListingStatus.Active);
        if (alreadyListed)
            return ServiceResult.Bad("Criatura já está no mercado");
        bool breeding = await db.BreedingSlots.AnyAsync(s =>
            s.Status == BreedingStatus.InProgress && (s.ParentAId == creature.Id || s.ParentBId == creature.Id));
        if (breeding)
            return ServiceResult.Bad("Criatura está em gestação — não pode ser vendida agora");

        creature.HabitatId = null; // sai do tanque enquanto listada
        var listing = new MarketListing
        {
            CreatureInstanceId = creature.Id,
            SellerId = userId,
            PriceSoft = req.PriceSoft,
            Status = ListingStatus.Active,
            CreatedAt = DateTime.UtcNow,
        };
        db.MarketListings.Add(listing);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("Esse peixe mudou de estado — atualize e tente de novo.");
        }
        return ServiceResult.Success(new { listing.Id });
    }

    public async Task<ServiceResult> CancelAsync(long userId, long id)
    {
        var listing = await db.MarketListings
            .Include(m => m.CreatureInstance)
            .FirstOrDefaultAsync(m => m.Id == id && m.SellerId == userId);
        if (listing is null)
            return ServiceResult.NotFound("Listagem não encontrada");
        if (listing.Status != ListingStatus.Active)
            return ServiceResult.Bad("Listagem não está ativa");

        listing.Status = ListingStatus.Cancelled;
        listing.ResolvedAt = DateTime.UtcNow;
        await ReturnToOwnerTankIfSpaceAsync(listing.CreatureInstance!, userId);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("A listagem mudou — atualize e tente de novo.");
        }
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> BuyAsync(long buyerId, long id)
    {
        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync();

        var listing = await db.MarketListings
            .Include(m => m.CreatureInstance)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (listing is null || listing.Status != ListingStatus.Active)
            return ServiceResult.NotFound("Listagem não encontrada ou não está ativa");
        if (listing.SellerId == buyerId)
            return ServiceResult.Bad("Você não pode comprar sua própria listagem");

        var buyerHabitat = await game.FindHabitatAsync(buyerId);
        if (buyerHabitat is null)
            return ServiceResult.NotFound("Habitat não encontrado");
        // Espaço no comprador ANTES de cobrar (senão pagaria e o peixe sumiria).
        bool tankSpace = await db.CreatureInstances.CountAsync(c => c.HabitatId == buyerHabitat.Id) < buyerHabitat.Capacity;
        bool backpackSpace = await game.CountBackpackAsync(buyerId) < HabitatDefaults.BackpackCapacity;
        if (!tankSpace && !backpackSpace)
            return ServiceResult.Bad("Seu tanque e mochila estão cheios.");

        int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
        var buyerWallet = await db.WalletBalances
            .FirstAsync(w => w.UserId == buyerId && w.CurrencyTypeId == softId);
        if (buyerWallet.Amount < listing.PriceSoft)
            return ServiceResult.Bad("Saldo insuficiente");
        var sellerWallet = await db.WalletBalances
            .FirstAsync(w => w.UserId == listing.SellerId && w.CurrencyTypeId == softId);

        buyerWallet.Amount -= listing.PriceSoft;
        sellerWallet.Amount += listing.PriceSoft;

        var creature = listing.CreatureInstance!;
        creature.OwnerId = buyerId;
        creature.HabitatId = tankSpace ? buyerHabitat.Id : null; // mochila se o tanque estiver cheio

        listing.Status = ListingStatus.Sold;
        listing.BuyerId = buyerId;
        listing.ResolvedAt = now;

        db.TransactionLogs.Add(new TransactionLog
        {
            Type = TransactionType.MarketSale,
            FromUserId = buyerId,
            ToUserId = listing.SellerId,
            CreatureInstanceId = creature.Id,
            CurrencyTypeId = softId,
            Amount = listing.PriceSoft,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Concorrência: outro comprador levou a listagem (ou o saldo mudou).
            // A transação é descartada (rollback) — nada é cobrado nem transferido.
            return ServiceResult.Conflict("Essa listagem acabou de ser comprada ou alterada.");
        }
        return ServiceResult.Success(new { creatureId = creature.Id, paid = listing.PriceSoft });
    }

    /// <summary>Devolve a criatura ao tanque do dono se houver espaço; senão fica fora (HabitatId null).</summary>
    private async Task ReturnToOwnerTankIfSpaceAsync(CreatureInstance creature, long ownerId)
    {
        var habitat = await db.Habitats.FirstOrDefaultAsync(h => h.UserId == ownerId);
        if (habitat is null)
            return;
        int active = await db.CreatureInstances.CountAsync(c => c.HabitatId == habitat.Id);
        creature.HabitatId = active < habitat.Capacity ? habitat.Id : null;
    }
}
