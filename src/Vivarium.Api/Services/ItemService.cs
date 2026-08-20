using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;
using Vivarium.Core.Generation;

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
            .Select(i =>
            {
                if (i.Category == ItemCategory.HabitatUpgrade)
                {
                    var (price, owned, locked, reason) = TankItemState(i.Key, habitat);
                    return new ItemDto(i.Key, i.Name, i.Category.ToString(), price, owned, locked, reason);
                }
                if (i.Category == ItemCategory.WaterSensor)
                {
                    var (price, owned, locked, reason) = WaterSensorState(habitat);
                    return new ItemDto(i.Key, i.Name, i.Category.ToString(), price, owned, locked, reason);
                }
                if (i.Category == ItemCategory.Egg)
                {
                    // Consumível, nunca "possuído" — sempre comprável de novo, preço em premium.
                    return new ItemDto(i.Key, i.Name, i.Category.ToString(), i.PricePremium ?? 0m, false, Currency: "PREMIUM");
                }
                if (i.Category == ItemCategory.Filter)
                {
                    // Preço cresce por faixa (18/08/2026) — sem isso, 20 soft virava irrisório
                    // no Aquário Master enquanto a renda cresce muito mais rápido que isso.
                    return new ItemDto(i.Key, i.Name, i.Category.ToString(), CapacityBands.BandFor(habitat.Capacity).FilterBasicPrice, false);
                }
                return new ItemDto(i.Key, i.Name, i.Category.ToString(), i.PriceSoft, ownedIds.Contains(i.Id));
            })
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

        decimal price;
        if (item.Category == ItemCategory.AutoFilter)
        {
            if ((await OwnedItemDefinitionIdsAsync(userId)).Contains(item.Id))
                return ServiceResult.Bad("Você já tem esse filtro");
            price = item.PriceSoft;
        }
        else if (item.Category == ItemCategory.HabitatUpgrade)
        {
            var (tankPrice, owned, locked, reason) = TankItemState(item.Key, habitat);
            if (owned) return ServiceResult.Bad("Você já tem esse aquário");
            if (locked) return ServiceResult.Bad(reason ?? "Ainda não disponível");
            price = tankPrice;
        }
        else if (item.Category == ItemCategory.WaterSensor)
        {
            var (sensorPrice, owned, _, _) = WaterSensorState(habitat);
            if (owned) return ServiceResult.Bad("Você já tem o Sensor de Qualidade da Água");
            price = sensorPrice;
        }
        else if (item.Category == ItemCategory.Egg)
        {
            price = item.PricePremium ?? 0m;
        }
        else if (item.Category == ItemCategory.Filter)
        {
            price = CapacityBands.BandFor(habitat.Capacity).FilterBasicPrice;
        }
        else
        {
            price = item.PriceSoft;
        }

        // Ovo de peixe (BACKLOG.md #3, 17/08/2026): gera o peixe ANTES de mexer em carteira —
        // se não houver espaço no tanque/mochila, a compra é bloqueada sem debitar nada (mesma
        // regra "direto, como a fila normal" já usada na coleta manual, CLAUDE.md §7.1/§7.7).
        // A geração em si não tem efeito colateral (só cria o objeto em memória; só entra no
        // change tracker via db.CreatureInstances.Add mais abaixo, depois do pagamento).
        CreatureInstance? eggCreature = null;
        if (item.Category == ItemCategory.Egg)
        {
            eggCreature = await game.GenerateEggCreatureAsync(userId, habitat, item, now);
            if (eggCreature is null)
                return ServiceResult.Bad("Tanque e mochila cheios — libere espaço antes de comprar um ovo.");
        }

        // Tick antes: a degradação pendente é aplicada antes de restaurar/pagar
        await game.ApplyTickAsync(habitat, now);

        string currencyCode = item.Category == ItemCategory.Egg ? "PREMIUM" : "SOFT";
        int currencyId = await db.CurrencyTypes.Where(c => c.Code == currencyCode).Select(c => c.Id).FirstAsync();
        var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == currencyId);
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
            case ItemCategory.WaterSensor:
                habitat.HasWaterSensor = true;
                break;
            case ItemCategory.Egg:
                db.CreatureInstances.Add(eggCreature!);
                break;
        }

        // Compra do jogo = dinheiro sai da economia (sink)
        db.TransactionLogs.Add(new TransactionLog
        {
            Type = TransactionType.ItemPurchase,
            FromUserId = userId,
            CurrencyTypeId = currencyId,
            Amount = price,
            CreatedAt = now,
        });

        await game.AwardXpAsync(userId, LevelConfig.Default.ItemPurchaseXp);

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
            creature = eggCreature is not null ? CreatureDto.From(eggCreature) : null,
        });
    }

    /// <summary>
    /// Estado (preço/dono/bloqueado) de um dos 3 produtos de tanque — SEPARADOS na loja
    /// (08/08/2026, pedido do usuário): "Upgrade de tanque" (curva suave, só dentro da
    /// faixa atual) e um item PRÓPRIO por faixa de destino ("Aquário Grande"/"Aquário
    /// Master", preço fixo = `CapacityBand.TransitionCost`) — trocar de aquário deixa de
    /// ser "mais um clique no mesmo botão" e vira uma decisão de compra distinta.
    /// </summary>
    private static (decimal Price, bool Owned, bool Locked, string? LockedReason) TankItemState(string key, Habitat habitat)
    {
        if (key == "tank_upgrade")
        {
            if (habitat.Capacity >= CapacityBands.MaxCapacity)
                return (0m, false, true, "Capacidade máxima do aquário atingida.");
            var band = CapacityBands.BandFor(habitat.Capacity);
            if (habitat.Capacity >= band.MaxCapacity)
                return (0m, false, true, $"Tanque no limite do {band.Name} — compre o próximo aquário pra continuar.");
            return (CapacityBands.SmoothPrice(habitat.Capacity), false, false, null);
        }

        var targetBand = key == "aquario_grande" ? CapacityBands.AquarioGrande : CapacityBands.AquarioMaster;
        if (habitat.Capacity > targetBand.MinCapacity)
            return (targetBand.TransitionCost, true, false, null); // já trocou pra esse aquário (ou além)
        if (habitat.Capacity < targetBand.MinCapacity)
            return (targetBand.TransitionCost, false, true, $"Disponível ao atingir capacidade {targetBand.MinCapacity}.");
        return (targetBand.TransitionCost, false, false, null); // no teto exato — comprável agora
    }

    /// <summary>
    /// Estado (preço/dono) do Sensor de Qualidade da Água (CLAUDE.md §8.18) — compra única por
    /// aquário (armazenada em <see cref="Habitat.HasWaterSensor"/>, não em UserInventory, mesmo
    /// motivo de <c>tank_upgrade</c>/<c>aquario_grande</c>: não faz sentido acumular quantidade).
    /// Preço cresce com a faixa de capacidade atual ("aquário maior, mais caro") — nunca bloqueado
    /// (diferente de aquario_grande/master, que exigem atingir uma capacidade mínima).
    /// </summary>
    private static (decimal Price, bool Owned, bool Locked, string? LockedReason) WaterSensorState(Habitat habitat)
        => (CapacityBands.BandFor(habitat.Capacity).WaterSensorPrice, habitat.HasWaterSensor, false, null);

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
/// preenchidos: `CapacityDelta` (HabitatUpgrade), `FilterCapacity` (AutoFilter) ou
/// `EggBiasStrength` (Egg, BACKLOG.md #3, 17/08/2026 — força do viés de raridade repassada pra
/// <see cref="Vivarium.Core.Gameplay.CreatureCollector.CollectBiased"/>).
/// </summary>
public sealed record ItemEffect(int? CapacityDelta, decimal? FilterCapacity, double? EggBiasStrength = null)
{
    public static ItemEffect Parse(string effectJson)
        => JsonSerializer.Deserialize<ItemEffect>(effectJson, JsonOptions) ?? new ItemEffect(null, null);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
