using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Api.Services;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;

namespace Vivarium.Api.Endpoints;

public static class MarketEndpoints
{
    public static void MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/market").RequireAuthorization();

        group.MapGet("/listings", async (VivariumDbContext db, int skip = 0, int take = 50) =>
        {
            take = Math.Clamp(take, 1, 100);
            var listings = (await db.MarketListings
                .Where(m => m.Status == ListingStatus.Active)
                .OrderByDescending(m => m.CreatedAt)
                .Skip(Math.Max(0, skip)).Take(take)
                .Select(m => new
                {
                    m.Id, m.PriceSoft, m.SellerId, SellerName = m.Seller!.Username,
                    CreatureId = m.CreatureInstanceId, m.CreatureInstance!.SpeciesId,
                    m.CreatureInstance.Seed, m.CreatureInstance.TraitConfigVersion,
                    m.CreatureInstance.RarityScore,
                })
                .ToListAsync())
                .Select(m => new ListingDto(
                    m.Id, m.PriceSoft, m.SellerId, m.SellerName,
                    m.CreatureId, m.SpeciesId, m.Seed.ToString(),
                    m.TraitConfigVersion, m.RarityScore))
                .ToList();
            return Results.Ok(listings);
        });

        group.MapPost("/listings", async (
            CreateListingRequest req, ClaimsPrincipal principal, VivariumDbContext db) =>
        {
            if (req.PriceSoft <= 0)
                return ApiError.BadRequest("Preço deve ser maior que zero");

            long userId = TokenService.GetUserId(principal);
            var creature = await db.CreatureInstances
                .FirstOrDefaultAsync(c => c.Id == req.CreatureInstanceId && c.OwnerId == userId);
            if (creature is null)
                return ApiError.NotFound("Criatura não encontrada");
            if (creature.HabitatId is null)
                return ApiError.BadRequest("Criatura já está no mercado ou em trânsito");

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
                return ApiError.Conflict("Esse peixe mudou de estado — atualize e tente de novo.");
            }
            return Results.Ok(new { listing.Id });
        });

        group.MapPost("/listings/{id:long}/cancel", async (
            long id, ClaimsPrincipal principal, VivariumDbContext db) =>
        {
            long userId = TokenService.GetUserId(principal);
            var listing = await db.MarketListings
                .Include(m => m.CreatureInstance)
                .FirstOrDefaultAsync(m => m.Id == id && m.SellerId == userId);
            if (listing is null)
                return Results.NotFound();
            if (listing.Status != ListingStatus.Active)
                return ApiError.BadRequest("Listagem não está ativa");

            listing.Status = ListingStatus.Cancelled;
            listing.ResolvedAt = DateTime.UtcNow;
            await ReturnToOwnerTankIfSpaceAsync(db, listing.CreatureInstance!, userId);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return ApiError.Conflict("A listagem mudou — atualize e tente de novo.");
            }
            return Results.Ok();
        });

        group.MapPost("/listings/{id:long}/buy", async (
            long id, ClaimsPrincipal principal, VivariumDbContext db, GameService game) =>
        {
            long buyerId = TokenService.GetUserId(principal);
            var now = DateTime.UtcNow;

            await using var transaction = await db.Database.BeginTransactionAsync();

            var listing = await db.MarketListings
                .Include(m => m.CreatureInstance)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (listing is null || listing.Status != ListingStatus.Active)
                return ApiError.NotFound("Listagem não encontrada ou não está ativa");
            if (listing.SellerId == buyerId)
                return ApiError.BadRequest("Você não pode comprar sua própria listagem");

            var buyerHabitat = await game.FindHabitatAsync(buyerId);
            if (buyerHabitat is null)
                return Results.NotFound();
            // Espaço no comprador ANTES de cobrar (senão pagaria e o peixe sumiria).
            bool tankSpace = await db.CreatureInstances.CountAsync(c => c.HabitatId == buyerHabitat.Id) < buyerHabitat.Capacity;
            bool backpackSpace = await game.CountBackpackAsync(buyerId) < HabitatDefaults.BackpackCapacity;
            if (!tankSpace && !backpackSpace)
                return ApiError.BadRequest("Seu tanque e mochila estão cheios.");

            int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
            var buyerWallet = await db.WalletBalances
                .FirstAsync(w => w.UserId == buyerId && w.CurrencyTypeId == softId);
            if (buyerWallet.Amount < listing.PriceSoft)
                return ApiError.BadRequest("Saldo insuficiente");
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
                return ApiError.Conflict("Essa listagem acabou de ser comprada ou alterada.");
            }
            return Results.Ok(new { creatureId = creature.Id, paid = listing.PriceSoft });
        });
    }

    /// <summary>Devolve a criatura ao tanque do dono se houver espaço; senão fica fora (HabitatId null).</summary>
    private static async Task ReturnToOwnerTankIfSpaceAsync(VivariumDbContext db, CreatureInstance creature, long ownerId)
    {
        var habitat = await db.Habitats.FirstOrDefaultAsync(h => h.UserId == ownerId);
        if (habitat is null)
            return;
        int active = await db.CreatureInstances.CountAsync(c => c.HabitatId == habitat.Id);
        creature.HabitatId = active < habitat.Capacity ? habitat.Id : null;
    }
}
