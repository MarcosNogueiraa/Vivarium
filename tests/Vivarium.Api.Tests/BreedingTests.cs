using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Tests;

public class BreedingTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public BreedingTests(VivariumApiFactory factory) => _factory = factory;

    private record CreatureDto(
        long Id, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore, DateTime CreatedAt,
        bool IsBred, string? ParentASeed, string? ParentBSeed, int BreedCount);
    private record BreedingSlotDto(long Id, CreatureDto ParentA, CreatureDto ParentB, DateTime StartedAt, DateTime ReadyAt, bool IsReady, decimal CostPaid);
    private record BreedingStatusDto(bool Active, BreedingSlotDto? Slot);
    private record StartResultDto(long SlotId, DateTime ReadyAt, decimal CostPaid);
    private record CollectBreedingResponse(CreatureDto Child, bool ParentADied, bool ParentBDied);
    private record BreedingQuoteDto(
        decimal CostSoft, double GestationHours, DateTime EstimatedReadyAt,
        Dictionary<string, double> ChildTierProbabilities,
        int ParentABreedCount, double ParentADeathChance,
        int ParentBBreedCount, double ParentBDeathChance);
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
        var result = await collectResp.Content.ReadFromJsonAsync<CollectBreedingResponse>();
        Assert.NotNull(result);
        var child = result!.Child;

        var statusAfter = await client.GetFromJsonAsync<BreedingStatusDto>("/api/breeding");
        Assert.False(statusAfter!.Active);

        // Risco de morte é não-determinístico (chance baixa por pai): o filho sempre
        // volta pro tanque, mas cada pai só volta se sobreviveu à gestação.
        int survivors = (result.ParentADied ? 0 : 1) + (result.ParentBDied ? 0 : 1);
        var tankAfter = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(1 + survivors, tankAfter!.Creatures.Count);

        // Conserta o bug relatado: o filhote precisa reconstruir os traits reais
        // (herdados via BreedTraits), não um peixe aleatório do próprio seed solto —
        // por isso o DTO precisa expor os seeds dos pais.
        Assert.True(child.IsBred);
        Assert.Equal("111", child.ParentASeed);
        Assert.Equal("222", child.ParentBSeed);

        await _factory.WithDbAsync(async db =>
        {
            var childEntity = await db.CreatureInstances.FirstAsync(c => c.Id == child!.Id);
            Assert.Equal(a, childEntity.ParentAId);
            Assert.Equal(b, childEntity.ParentBId);
            Assert.Equal(111, childEntity.ParentASeed);
            Assert.Equal(222, childEntity.ParentBSeed);
        });
    }

    [Fact]
    public async Task Quote_RetornaCustoGestacaoEChancesSemCobrarNada()
    {
        var (client, userId) = await _factory.RegisterAsync("breed8");
        long a = await CreateOwnedCreature(userId, 5m, 2020);
        long b = await CreateOwnedCreature(userId, 6m, 2121);

        var quote = await client.GetFromJsonAsync<BreedingQuoteDto>($"/api/breeding/quote?parentAId={a}&parentBId={b}");
        Assert.NotNull(quote);
        Assert.True(quote!.CostSoft > 0);
        Assert.True(quote.GestationHours > 0);
        Assert.Equal(0, quote.ParentABreedCount);
        Assert.Equal(0, quote.ParentBBreedCount);
        Assert.InRange(quote.ParentADeathChance, 0, 1);
        Assert.True(quote.ChildTierProbabilities.Values.Sum() is > 0.99 and < 1.01);

        // Prévia não cobra nem inicia nada — o par continua livre pra outras coisas.
        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(2, tank!.Creatures.Count);
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
