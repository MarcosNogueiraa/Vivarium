using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Tests;

/// <summary>Acelerar fila/gestação pagando premium (8.11) — a única forma de pular a espera.</summary>
public class RushTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public RushTests(VivariumApiFactory factory) => _factory = factory;

    private record QueueItemDto(long Id, DateTime ReadyAt, bool IsReady, bool IsSick, decimal RushCostPremium);
    private record TankDto(bool Online, decimal MaintenanceLevel, int Capacity, int QueueCap, List<QueueItemDto> Queue);
    private record BreedingSlotDto(long Id, object ParentA, object ParentB, DateTime StartedAt, DateTime ReadyAt, bool IsReady, decimal CostPaid, decimal RushCostPremium);
    private record BreedingStatusDto(bool Active, BreedingSlotDto? Slot);
    private record CreatureRow(long Id, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore, DateTime CreatedAt);

    private async Task<long> HabitatIdOf(long userId)
    {
        long id = 0;
        await _factory.WithDbAsync(async db =>
            id = (await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium")).Id);
        return id;
    }

    private async Task<long> CreateOwnedCreature(long userId, decimal rarityScore, long seed)
    {
        long habitatId = await HabitatIdOf(userId);
        long creatureId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var c = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, HabitatId = habitatId,
                Seed = seed, TraitConfigVersion = 1, RarityScore = rarityScore, CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(c);
            await db.SaveChangesAsync();
            creatureId = c.Id;
        });
        return creatureId;
    }

    private async Task<long> CreateQueueItem(long userId, TimeSpan waitRemaining)
    {
        long habitatId = await HabitatIdOf(userId);
        long itemId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var item = new GenerationQueueItem
            {
                HabitatId = habitatId, SpeciesId = 1,
                ReadyAt = DateTime.UtcNow.Add(waitRemaining), Status = QueueItemStatus.Pending,
            };
            db.GenerationQueueItems.Add(item);
            await db.SaveChangesAsync();
            itemId = item.Id;
        });
        return itemId;
    }

    private async Task GiveCurrency(long userId, string code, decimal amount)
    {
        await _factory.WithDbAsync(async db =>
        {
            int currencyId = await db.CurrencyTypes.Where(c => c.Code == code).Select(c => c.Id).FirstAsync();
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == currencyId);
            wallet.Amount += amount;
        });
    }

    [Fact]
    public async Task Fila_CustoDeAcelerarApareceNoTanqueEEscalaComOTempoRestante()
    {
        var (client, userId) = await _factory.RegisterAsync("rush1");
        long itemId = await CreateQueueItem(userId, TimeSpan.FromMinutes(60));

        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank");

        var item = tank!.Queue.Single(q => q.Id == itemId);
        Assert.False(item.IsReady);
        Assert.InRange(item.RushCostPremium, 8m, 9m); // ~0.15/min * 60min
    }

    [Fact]
    public async Task Fila_AcelerarSemPremium_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("rush2");
        long itemId = await CreateQueueItem(userId, TimeSpan.FromMinutes(60));

        var resp = await client.PostAsync($"/api/game/queue/{itemId}/rush", null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Fila_AcelerarComPremium_FicaProntoNaHoraECobraOCusto()
    {
        var (client, userId) = await _factory.RegisterAsync("rush3");
        long itemId = await CreateQueueItem(userId, TimeSpan.FromMinutes(60));
        await GiveCurrency(userId, "PREMIUM", 100m);

        var rush = await client.PostAsync($"/api/game/queue/{itemId}/rush", null);
        rush.EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank");
        Assert.True(tank!.Queue.Single(q => q.Id == itemId).IsReady);

        var collect = await client.PostAsync($"/api/game/collect/{itemId}", null);
        collect.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Fila_AcelerarItemJaPronto_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("rush4");
        long itemId = await CreateQueueItem(userId, TimeSpan.FromMinutes(-1)); // já pronto
        await GiveCurrency(userId, "PREMIUM", 100m);

        var resp = await client.PostAsync($"/api/game/queue/{itemId}/rush", null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Gestacao_AcelerarComPremium_PermiteColetarNaHora()
    {
        var (client, userId) = await _factory.RegisterAsync("rush5");
        await GiveCurrency(userId, "SOFT", 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 3001);
        long b = await CreateOwnedCreature(userId, 6m, 3002);
        (await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b })).EnsureSuccessStatusCode();

        var statusBefore = await client.GetFromJsonAsync<BreedingStatusDto>("/api/breeding");
        Assert.False(statusBefore!.Slot!.IsReady);
        Assert.True(statusBefore.Slot.RushCostPremium > 0);

        var rushNoPremium = await client.PostAsync("/api/breeding/rush", null);
        Assert.Equal(HttpStatusCode.BadRequest, rushNoPremium.StatusCode);

        await GiveCurrency(userId, "PREMIUM", 1000m);
        var rush = await client.PostAsync("/api/breeding/rush", null);
        rush.EnsureSuccessStatusCode();

        var collect = await client.PostAsync("/api/breeding/collect", null);
        collect.EnsureSuccessStatusCode();
    }
}
