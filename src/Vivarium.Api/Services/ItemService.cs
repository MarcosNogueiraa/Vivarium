using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;

namespace Vivarium.Api.Services;

/// <summary>
/// Catálogo e compra de itens (filtro, filtro automático, upgrade de tanque).
/// Lógica de negócio fora dos handlers HTTP; devolve <see cref="ServiceResult"/>
/// que o endpoint traduz.
/// </summary>
public class ItemService(VivariumDbContext db, GameService game)
{
    public async Task<ServiceResult> ListAsync(long userId)
    {
        var habitat = await game.FindHabitatAsync(userId);
        if (habitat is null)
            return ServiceResult.NotFound("Habitat não encontrado");

        var owned = await OwnedAutoFilterAsync(userId);
        var items = (await db.ItemDefinitions.ToListAsync())
            .Select(i => new ItemDto(
                i.Key, i.Name, i.Category.ToString(),
                CurrentPrice(i, habitat),
                i.Category == ItemCategory.AutoFilter && owned))
            .ToList();
        return ServiceResult.Success(items);
    }

    public async Task<ServiceResult> BuyAsync(long userId, string key)
    {
        var now = DateTime.UtcNow;

        var item = await db.ItemDefinitions.FirstOrDefaultAsync(i => i.Key == key);
        if (item is null)
            return ServiceResult.NotFound("Item não encontrado");
        var habitat = await game.FindHabitatAsync(userId);
        if (habitat is null)
            return ServiceResult.NotFound("Habitat não encontrado");

        if (item.Category == ItemCategory.AutoFilter && await OwnedAutoFilterAsync(userId))
            return ServiceResult.Bad("Você já tem o filtro automático");

        // Tick antes: a degradação pendente é aplicada antes de restaurar/pagar
        await game.ApplyTickAsync(habitat, now);

        decimal price = CurrentPrice(item, habitat);
        int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
        var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == softId);
        if (wallet.Amount < price)
            return ServiceResult.Bad("Saldo insuficiente");
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
            return ServiceResult.Conflict("Compra concorrente — tente de novo.");
        }
        return ServiceResult.Success(new
        {
            paid = price,
            maintenanceLevel = habitat.MaintenanceLevel,
            capacity = habitat.Capacity,
        });
    }

    /// <summary>Upgrade de tanque tem custo crescente (~1.5x por nível — CLAUDE.md 8.4).</summary>
    private static decimal CurrentPrice(ItemDefinition item, Habitat habitat)
        => item.Category == ItemCategory.HabitatUpgrade
            ? Math.Ceiling(item.PriceSoft
                * (decimal)Math.Pow(1.5, habitat.Capacity - HabitatDefaults.Capacity))
            : item.PriceSoft;

    private Task<bool> OwnedAutoFilterAsync(long userId)
        => db.UserInventories.AnyAsync(i =>
            i.UserId == userId
            && i.Quantity > 0
            && i.ItemDefinition!.Category == ItemCategory.AutoFilter);
}
