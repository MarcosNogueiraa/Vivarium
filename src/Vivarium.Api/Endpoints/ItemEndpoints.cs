using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Data;
using Vivarium.Api.Services;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;

namespace Vivarium.Api.Endpoints;

public static class ItemEndpoints
{
    public record ItemDto(string Key, string Name, string Category, decimal Price, bool Owned);

    public static void MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/items").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, VivariumDbContext db, GameService game) =>
        {
            long userId = TokenService.GetUserId(principal);
            var habitat = await game.FindHabitatAsync(userId);
            if (habitat is null)
                return Results.NotFound();

            var owned = await OwnedAutoFilterAsync(db, userId);
            var items = (await db.ItemDefinitions.ToListAsync())
                .Select(i => new ItemDto(
                    i.Key, i.Name, i.Category.ToString(),
                    CurrentPrice(i, habitat),
                    i.Category == ItemCategory.AutoFilter && owned))
                .ToList();
            return Results.Ok(items);
        });

        group.MapPost("/{key}/buy", async (
            string key, ClaimsPrincipal principal, VivariumDbContext db, GameService game) =>
        {
            long userId = TokenService.GetUserId(principal);
            var now = DateTime.UtcNow;

            var item = await db.ItemDefinitions.FirstOrDefaultAsync(i => i.Key == key);
            if (item is null)
                return Results.NotFound();
            var habitat = await game.FindHabitatAsync(userId);
            if (habitat is null)
                return Results.NotFound();

            if (item.Category == ItemCategory.AutoFilter && await OwnedAutoFilterAsync(db, userId))
                return Results.BadRequest(new { error = "Você já tem o filtro automático" });

            // Tick antes: a degradação pendente é aplicada antes de restaurar/pagar
            await game.ApplyTickAsync(habitat, now);

            decimal price = CurrentPrice(item, habitat);
            int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == softId);
            if (wallet.Amount < price)
                return Results.BadRequest(new { error = "Saldo insuficiente" });
            wallet.Amount -= price;

            switch (item.Category)
            {
                case ItemCategory.Filter:
                    habitat.MaintenanceLevel = 100m;
                    break;
                case ItemCategory.AutoFilter:
                    db.UserInventories.Add(new UserInventory
                    {
                        UserId = userId, ItemDefinitionId = item.Id, Quantity = 1,
                    });
                    break;
                case ItemCategory.HabitatUpgrade:
                    habitat.Capacity += 1;
                    break;
            }

            // Compra do jogo = dinheiro sai da economia (sink)
            db.TransactionLogs.Add(new TransactionLog
            {
                Type = TransactionType.ItemPurchase,
                FromUserId = userId,
                CurrencyTypeId = softId,
                Amount = price,
                CreatedAt = now,
            });

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "Compra concorrente — tente de novo." });
            }
            return Results.Ok(new
            {
                paid = price,
                maintenanceLevel = habitat.MaintenanceLevel,
                capacity = habitat.Capacity,
            });
        });
    }

    /// <summary>Upgrade de tanque tem custo crescente (~1.5x por nível — CLAUDE.md 8.4).</summary>
    private static decimal CurrentPrice(ItemDefinition item, Habitat habitat)
        => item.Category == ItemCategory.HabitatUpgrade
            ? Math.Ceiling(item.PriceSoft
                * (decimal)Math.Pow(1.5, habitat.Capacity - HabitatDefaults.Capacity))
            : item.PriceSoft;

    private static Task<bool> OwnedAutoFilterAsync(VivariumDbContext db, long userId)
        => db.UserInventories.AnyAsync(i =>
            i.UserId == userId
            && i.Quantity > 0
            && i.ItemDefinition!.Category == ItemCategory.AutoFilter);
}
