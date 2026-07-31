using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Tests;

/// <summary>Venda ao NPC (vendor, §8.12) — sink de duplicatas/comuns, preço baixo, instantâneo.</summary>
public class VendorSaleTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public VendorSaleTests(VivariumApiFactory factory) => _factory = factory;

    private record TankDto(bool Online, decimal MaintenanceLevel, int Capacity, int QueueCap, List<object> Queue, List<CreatureRow> Creatures, Dictionary<string, decimal> Wallet);
    private record BackpackDto(int Capacity, List<CreatureRow> Creatures);
    private record CreatureRow(long Id, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore, DateTime CreatedAt);
    private record SellResult(decimal Price, decimal Wallet);

    private async Task<long> HabitatIdOf(long userId)
    {
        long id = 0;
        await _factory.WithDbAsync(async db =>
            id = (await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium")).Id);
        return id;
    }

    private async Task<long> CreateOwnedCreature(long userId, decimal rarityScore, long seed, bool inTank)
    {
        long? habitatId = inTank ? await HabitatIdOf(userId) : null;
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

    [Fact]
    public async Task VenderDoTanque_CreditaSoftERemoveDoTanque()
    {
        var (client, userId) = await _factory.RegisterAsync("vendor1");
        long creatureId = await CreateOwnedCreature(userId, 5m, 4001, inTank: true);

        var tankBefore = await client.GetFromJsonAsync<TankDto>("/api/game/tank");
        decimal walletBefore = tankBefore!.Wallet["SOFT"];

        var resp = await client.PostAsync($"/api/game/creatures/{creatureId}/sell-vendor", null);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<SellResult>();

        Assert.True(result!.Price > 0);
        Assert.Equal(walletBefore + result.Price, result.Wallet);

        var tankAfter = await client.GetFromJsonAsync<TankDto>("/api/game/tank");
        Assert.Empty(tankAfter!.Creatures);
        Assert.Equal(walletBefore + result.Price, tankAfter.Wallet["SOFT"]);
    }

    [Fact]
    public async Task VenderDaMochila_RemoveDaMochila()
    {
        var (client, userId) = await _factory.RegisterAsync("vendor2");
        long creatureId = await CreateOwnedCreature(userId, 5m, 4002, inTank: false);

        var resp = await client.PostAsync($"/api/game/creatures/{creatureId}/sell-vendor", null);
        resp.EnsureSuccessStatusCode();

        var backpack = await client.GetFromJsonAsync<BackpackDto>("/api/game/backpack");
        Assert.Empty(backpack!.Creatures);
    }

    [Fact]
    public async Task PrecoEscalaComARaridade()
    {
        var (client, userId) = await _factory.RegisterAsync("vendor3");
        long comumId = await CreateOwnedCreature(userId, 5m, 4003, inTank: false);
        long raroId = await CreateOwnedCreature(userId, 9m, 4004, inTank: false);

        var comum = await (await client.PostAsync($"/api/game/creatures/{comumId}/sell-vendor", null))
            .Content.ReadFromJsonAsync<SellResult>();
        var raro = await (await client.PostAsync($"/api/game/creatures/{raroId}/sell-vendor", null))
            .Content.ReadFromJsonAsync<SellResult>();

        Assert.True(raro!.Price > comum!.Price);
    }

    [Fact]
    public async Task VenderCriaturaListadaNoMercado_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("vendor4");
        long creatureId = await CreateOwnedCreature(userId, 5m, 4005, inTank: true);
        (await client.PostAsJsonAsync("/api/market/listings", new { creatureInstanceId = creatureId, priceSoft = 100m }))
            .EnsureSuccessStatusCode();

        var resp = await client.PostAsync($"/api/game/creatures/{creatureId}/sell-vendor", null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task VenderDeNovo_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("vendor5");
        long creatureId = await CreateOwnedCreature(userId, 5m, 4006, inTank: false);
        (await client.PostAsync($"/api/game/creatures/{creatureId}/sell-vendor", null)).EnsureSuccessStatusCode();

        var resp = await client.PostAsync($"/api/game/creatures/{creatureId}/sell-vendor", null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task VenderCriaturaDeOutroUsuario_Retorna404()
    {
        var (clientA, userA) = await _factory.RegisterAsync("vendor6a");
        var (clientB, _) = await _factory.RegisterAsync("vendor6b");
        long creatureId = await CreateOwnedCreature(userA, 5m, 4007, inTank: false);

        var resp = await clientB.PostAsync($"/api/game/creatures/{creatureId}/sell-vendor", null);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
