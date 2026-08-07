using System.Text.Json;
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

        var ownedIds = await OwnedItemDefinitionIdsAsync(userId);
        var items = (await db.ItemDefinitions.ToListAsync())
            .Select(i => new ItemDto(
                i.Key, i.Name, i.Category.ToString(),
                CurrentPrice(i, habitat),
                ownedIds.Contains(i.Id)))
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

        if (item.Category == ItemCategory.AutoFilter && (await OwnedItemDefinitionIdsAsync(userId)).Contains(item.Id))
            return ServiceResult.Bad("Você já tem esse filtro");
        if (item.Category == ItemCategory.HabitatUpgrade && habitat.Capacity >= CapacityBands.MaxCapacity)
            return ServiceResult.Bad("Tanque já está na capacidade máxima");

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

    /// <summary>
    /// Upgrade de tanque tem custo crescente dentro da faixa de capacidade atual
    /// (`CapacityBands.BandFor`, 08/08/2026) — cada faixa (Aquário/Grande/Master) tem
    /// sua própria curva de preço, não uma extensão da anterior.
    /// </summary>
    private static decimal CurrentPrice(ItemDefinition item, Habitat habitat)
    {
        if (item.Category != ItemCategory.HabitatUpgrade)
            return item.PriceSoft;
        var band = CapacityBands.BandFor(habitat.Capacity);
        return Math.Ceiling(band.PriceBase * (decimal)Math.Pow(band.PriceGrowth, habitat.Capacity - band.MinCapacity));
    }

    private async Task<HashSet<int>> OwnedItemDefinitionIdsAsync(long userId)
        => (await db.UserInventories
            .Where(i => i.UserId == userId && i.Quantity > 0)
            .Select(i => i.ItemDefinitionId)
            .ToListAsync())
            .ToHashSet();
}

/// <summary>
/// Efeito de um <see cref="ItemDefinition"/>, lido de `EffectJson` (08/08/2026 — antes disso
/// o campo era decorativo, nunca desserializado). Só os campos relevantes por categoria vêm
/// preenchidos: `CapacityDelta` (HabitatUpgrade) ou `FilterCapacity` (AutoFilter).
/// </summary>
public sealed record ItemEffect(int? CapacityDelta, decimal? FilterCapacity)
{
    public static ItemEffect Parse(string effectJson)
        => JsonSerializer.Deserialize<ItemEffect>(effectJson, JsonOptions) ?? new ItemEffect(null, null);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
