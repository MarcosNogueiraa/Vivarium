using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Tests;

public class BreedingTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public BreedingTests(VivariumApiFactory factory) => _factory = factory;

    private record CreatureDto(long Id, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore, DateTime CreatedAt);
    private record BreedingSlotDto(long Id, CreatureDto ParentA, CreatureDto ParentB, DateTime StartedAt, DateTime ReadyAt, bool IsReady);
    private record BreedingStatusDto(bool Active, BreedingSlotDto? Slot);
    private record StartResultDto(long SlotId, DateTime ReadyAt);
    private record ErrorDto(string Error);

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

    private async Task GiveSoft(long userId, decimal amount)
    {
        await _factory.WithDbAsync(async db =>
        {
            int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == softId);
            wallet.Amount += amount;
        });
    }

    private async Task MakeSlotReadyNow(long userId)
    {
        await _factory.WithDbAsync(async db =>
        {
            var slot = await db.BreedingSlots.FirstAsync(s => s.UserId == userId && s.Status == BreedingStatus.InProgress);
            slot.ReadyAt = DateTime.UtcNow.AddMinutes(-1);
        });
    }

    [Fact]
    public async Task FluxoCompleto_StartECollect()
    {
        var (client, userId) = await _factory.RegisterAsync("breed1");
        await GiveSoft(userId, 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 111);
        long b = await CreateOwnedCreature(userId, 6m, 222);

        var status0 = await client.GetFromJsonAsync<BreedingStatusDto>("/api/breeding");
        Assert.False(status0!.Active);

        var startResp = await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b });
        startResp.EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Empty(tank!.Creatures); // pais saíram do tanque principal

        var status1 = await client.GetFromJsonAsync<BreedingStatusDto>("/api/breeding");
        Assert.True(status1!.Active);
        Assert.Equal(a, status1.Slot!.ParentA.Id);
        Assert.Equal(b, status1.Slot.ParentB.Id);
        Assert.False(status1.Slot.IsReady);

        var collectTooSoon = await client.PostAsync("/api/breeding/collect", null);
        Assert.Equal(HttpStatusCode.BadRequest, collectTooSoon.StatusCode);

        await MakeSlotReadyNow(userId);

        var collectResp = await client.PostAsync("/api/breeding/collect", null);
        collectResp.EnsureSuccessStatusCode();
        var child = await collectResp.Content.ReadFromJsonAsync<CreatureDto>();
        Assert.NotNull(child);

        var statusAfter = await client.GetFromJsonAsync<BreedingStatusDto>("/api/breeding");
        Assert.False(statusAfter!.Active);

        var tankAfter = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(3, tankAfter!.Creatures.Count); // filho + 2 pais de volta

        await _factory.WithDbAsync(async db =>
        {
            var childEntity = await db.CreatureInstances.FirstAsync(c => c.Id == child!.Id);
            Assert.Equal(a, childEntity.ParentAId);
            Assert.Equal(b, childEntity.ParentBId);
        });
    }

    [Fact]
    public async Task Start_ComPeixesIguais_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("breed2");
        await GiveSoft(userId, 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 333);

        var resp = await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = a });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Start_SaldoInsuficiente_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("breed3"); // saldo inicial 100 < CostSoft 150
        long a = await CreateOwnedCreature(userId, 5m, 444);
        long b = await CreateOwnedCreature(userId, 6m, 555);

        var resp = await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Start_PeixeJaEmGestacao_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("breed4");
        await GiveSoft(userId, 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 666);
        long b = await CreateOwnedCreature(userId, 6m, 777);
        long c = await CreateOwnedCreature(userId, 7m, 888);

        (await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b })).EnsureSuccessStatusCode();

        // 2ª gestação do mesmo usuário — bloqueada mesmo com peixe C livre
        var resp = await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = c });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Start_PeixeListadoNoMercado_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("breed5");
        await GiveSoft(userId, 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 999);
        long b = await CreateOwnedCreature(userId, 6m, 1010);

        var listResp = await client.PostAsJsonAsync("/api/market/listings", new { creatureInstanceId = a, priceSoft = 10m });
        listResp.EnsureSuccessStatusCode();

        var resp = await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Start_PeixeDeOutroUsuario_Retorna404()
    {
        var (owner, ownerId) = await _factory.RegisterAsync("breed6dono");
        long a = await CreateOwnedCreature(ownerId, 5m, 1111);

        var (attacker, attackerId) = await _factory.RegisterAsync("breed6atacante");
        await GiveSoft(attackerId, 1000m);
        long b = await CreateOwnedCreature(attackerId, 6m, 1212);

        var resp = await attacker.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Collect_DuasVezesSeguidas_SegundaFalha()
    {
        var (client, userId) = await _factory.RegisterAsync("breed7");
        await GiveSoft(userId, 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 1313);
        long b = await CreateOwnedCreature(userId, 6m, 1414);
        (await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b })).EnsureSuccessStatusCode();
        await MakeSlotReadyNow(userId);

        var first = await client.PostAsync("/api/breeding/collect", null);
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsync("/api/breeding/collect", null);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode); // não há mais gestação em andamento
    }
}
