using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Tests;

public class BackpackTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public BackpackTests(VivariumApiFactory factory) => _factory = factory;

    private record BackpackDto(int Capacity, List<AuthTests.CreatureDto> Creatures);

    private async Task<long> HabitatIdOf(long userId)
    {
        long id = 0;
        await _factory.WithDbAsync(async db => id = (await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium")).Id);
        return id;
    }

    private async Task FillTank(long userId, long habitatId, int count)
    {
        await _factory.WithDbAsync(db =>
        {
            for (int i = 0; i < count; i++)
                db.CreatureInstances.Add(new CreatureInstance
                {
                    SpeciesId = 1, OwnerId = userId, OriginalOwnerId = userId, HabitatId = habitatId,
                    Seed = 5000 + i, TraitConfigVersion = 1, RarityScore = 4m,
                    TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(5000 + i)),
                    CreatedAt = DateTime.UtcNow,
                });
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ColetarComTanqueCheio_VaiParaMochila()
    {
        var (client, userId) = await _factory.RegisterAsync("mochila1");
        long habitatId = await HabitatIdOf(userId);
        await FillTank(userId, habitatId, 3); // tanque cheio (capacidade 3)

        (await client.PostAsync("/api/dev/spawn", null)).EnsureSuccessStatusCode();
        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        var item = tank!.Queue.First(q => q.IsReady);
        (await client.PostAsync($"/api/game/collect/{item.Id}", null)).EnsureSuccessStatusCode();

        var backpack = await client.GetFromJsonAsync<BackpackDto>("/api/game/backpack");
        Assert.Single(backpack!.Creatures);

        tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(3, tank!.Creatures.Count); // tanque continua cheio, não estourou
    }

    [Fact]
    public async Task StoreEDeploy_MovemEntreTanqueEMochila()
    {
        var (client, userId) = await _factory.RegisterAsync("mochila2");
        long habitatId = await HabitatIdOf(userId);
        await FillTank(userId, habitatId, 1);
        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        long creatureId = tank!.Creatures.Single().Id;

        (await client.PostAsync($"/api/game/creatures/{creatureId}/store", null)).EnsureSuccessStatusCode();
        tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Empty(tank!.Creatures);
        var backpack = await client.GetFromJsonAsync<BackpackDto>("/api/game/backpack");
        Assert.Single(backpack!.Creatures);

        (await client.PostAsync($"/api/game/creatures/{creatureId}/deploy", null)).EnsureSuccessStatusCode();
        tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Single(tank!.Creatures);
        backpack = await client.GetFromJsonAsync<BackpackDto>("/api/game/backpack");
        Assert.Empty(backpack!.Creatures);
    }

    private record InboxEntryRow(long Id, string Kind, InboxCreatureRow? Creature);
    private record InboxCreatureRow(long Id);
    private record InboxListRow(List<InboxEntryRow> Entries);

    [Fact]
    public async Task Comprar_VaiProCaixaDeEntrada_ResgatarComTanqueCheioCaiNaMochila()
    {
        // 14/08/2026 (CLAUDE.md §8.23/§8.24): comprar não checa mais espaço — o peixe sempre
        // vai pra Caixa de Entrada; falta de espaço no tanque só afeta ONDE o resgate coloca o
        // peixe (mochila em vez de tanque), nunca bloqueia a compra em si.
        var (seller, sellerId) = await _factory.RegisterAsync("mvendedor");
        long creatureId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == sellerId && h.HabitatType!.Code == "Aquarium");
            var c = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = sellerId, OriginalOwnerId = sellerId, HabitatId = habitat.Id,
                Seed = 8888, TraitConfigVersion = 1, RarityScore = 5m,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(8888)),
                CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(c);
            await db.SaveChangesAsync();
            creatureId = c.Id;
        });
        var listResp = await seller.PostAsJsonAsync("/api/market/listings", new { creatureInstanceId = creatureId, priceSoft = 10m });
        listResp.EnsureSuccessStatusCode();
        long listingId = (await listResp.Content.ReadFromJsonAsync<MarketTests.CreatedDto>())!.Id;

        var (buyer, buyerId) = await _factory.RegisterAsync("mcomprador");
        long buyerHabitat = await HabitatIdOf(buyerId);
        await FillTank(buyerId, buyerHabitat, 3); // tanque do comprador cheio — mesmo assim a compra passa

        (await buyer.PostAsync($"/api/market/listings/{listingId}/buy", null)).EnsureSuccessStatusCode();

        var backpackBeforeClaim = await buyer.GetFromJsonAsync<BackpackDto>("/api/game/backpack");
        Assert.DoesNotContain(backpackBeforeClaim!.Creatures, c => c.Id == creatureId);

        var inbox = await buyer.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        var entry = Assert.Single(inbox!.Entries, e => e.Creature?.Id == creatureId);
        Assert.Equal("MarketPurchase", entry.Kind);

        (await buyer.PostAsync($"/api/inbox/{entry.Id}/claim", null)).EnsureSuccessStatusCode();

        var backpack = await buyer.GetFromJsonAsync<BackpackDto>("/api/game/backpack");
        Assert.Contains(backpack!.Creatures, c => c.Id == creatureId);
        var tank = await buyer.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(3, tank!.Creatures.Count); // tanque continua cheio, não estourou — foi pra mochila
    }
}
