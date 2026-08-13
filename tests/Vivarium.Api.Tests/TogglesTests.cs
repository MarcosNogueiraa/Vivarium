using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Tests;

/// <summary>
/// Opt-out da coleta automática/Limpeza Automática de VIP (Habitat.AutoCollectEnabled/
/// AutoCleanEnabled, default true) e o selo de "peixe novo" (CreatureInstance.IsNew,
/// só true na coleta automática — coleta manual e breeding já têm seu próprio momento
/// de revelação).
/// </summary>
public class TogglesTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public TogglesTests(VivariumApiFactory factory) => _factory = factory;

    private record TankDto(bool AutoCollectEnabled, bool AutoCleanEnabled, List<QueueDto> Queue, List<CreatureRow> Creatures);
    private record QueueDto(long Id, bool IsReady);
    private record CreatureRow(long Id, bool IsNew);
    private record BackpackDto(int Capacity, List<CreatureRow> Creatures);

    private async Task<long> HabitatIdOf(long userId)
    {
        long id = 0;
        await _factory.WithDbAsync(async db =>
            id = (await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium")).Id);
        return id;
    }

    private Task GiveVip(long userId) => _factory.WithDbAsync(async db =>
    {
        db.VipSubscriptions.Add(new VipSubscription
        {
            UserId = userId,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(30),
            Status = SubscriptionStatus.Active,
        });
    });

    private Task MarkOnline(long habitatId) => _factory.WithDbAsync(async db =>
    {
        var habitat = await db.Habitats.FirstAsync(h => h.Id == habitatId);
        habitat.LastHeartbeatAt = DateTime.UtcNow;
    });

    private async Task<long> InserirItemProntoNaFila(long habitatId)
    {
        long itemId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var item = new GenerationQueueItem
            {
                HabitatId = habitatId,
                SpeciesId = 1,
                ReadyAt = DateTime.UtcNow.AddMinutes(-1),
                Status = QueueItemStatus.Pending,
                IsSick = false,
            };
            db.GenerationQueueItems.Add(item);
            await db.SaveChangesAsync();
            itemId = item.Id;
        });
        return itemId;
    }

    [Fact]
    public async Task NovaConta_TogglesComecamLigados()
    {
        var (client, _) = await _factory.RegisterAsync("togglesdefault");
        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank");
        Assert.True(tank!.AutoCollectEnabled);
        Assert.True(tank.AutoCleanEnabled);
    }

    [Fact]
    public async Task DesligarAutoCollect_VipOnlineComItemPronto_NaoColeta()
    {
        var (client, userId) = await _factory.RegisterAsync("toggleoff1");
        await GiveVip(userId);
        long habitatId = await HabitatIdOf(userId);
        await MarkOnline(habitatId);
        long itemId = await InserirItemProntoNaFila(habitatId);

        (await client.PostAsJsonAsync("/api/game/toggles", new { autoCollectEnabled = false, autoCleanEnabled = true }))
            .EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank"); // roda o tick
        Assert.False(tank!.AutoCollectEnabled);
        Assert.Contains(tank.Queue, q => q.Id == itemId && q.IsReady); // continua na fila, não coletou sozinho
    }

    [Fact]
    public async Task AutoCollectLigado_VipOnline_ColetaSozinhoEMarcaIsNew()
    {
        var (client, userId) = await _factory.RegisterAsync("toggleon1");
        await GiveVip(userId);
        long habitatId = await HabitatIdOf(userId);
        await MarkOnline(habitatId);
        await InserirItemProntoNaFila(habitatId);

        // GET /api/game/tank roda o tick — com toggle ligado (default), coleta sozinho.
        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank");
        var backpack = await client.GetFromJsonAsync<BackpackDto>("/api/game/backpack");

        // Tanque tem espaço (capacidade 3, conta nova) — os coletados foram pro tanque, não mochila.
        Assert.Contains(tank!.Creatures.Concat(backpack!.Creatures), c => c.IsNew);
    }

    [Fact]
    public async Task ColetaManual_NaoMarcaIsNew()
    {
        var (client, userId) = await _factory.RegisterAsync("manualcollect1");
        long habitatId = await HabitatIdOf(userId);
        long itemId = await InserirItemProntoNaFila(habitatId);

        (await client.PostAsync($"/api/game/collect/{itemId}", null)).EnsureSuccessStatusCode();

        long creatureId = 0;
        bool isNew = true;
        await _factory.WithDbAsync(async db =>
        {
            var c = await db.CreatureInstances.OrderByDescending(x => x.Id).FirstAsync(x => x.OwnerId == userId);
            creatureId = c.Id;
            isNew = c.IsNew;
        });
        Assert.False(isNew);
    }

    [Fact]
    public async Task DesligarAutoClean_VipOnlineAguaBaixa_NaoCompraFiltroSozinho()
    {
        var (client, userId) = await _factory.RegisterAsync("toggleoffclean1");
        await GiveVip(userId);
        long habitatId = await HabitatIdOf(userId);
        await MarkOnline(habitatId);
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.Id == habitatId);
            habitat.MaintenanceLevel = 0m; // abaixo de qualquer gatilho, inclusive o grátis 0%
        });

        (await client.PostAsJsonAsync("/api/game/toggles", new { autoCollectEnabled = true, autoCleanEnabled = false }))
            .EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank"); // roda o tick
        Assert.False(tank!.AutoCleanEnabled);

        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.Id == habitatId);
            Assert.True(habitat.MaintenanceLevel < 50m); // não foi restaurado pra 100 sozinho
        });
    }

    [Fact]
    public async Task MarkSeen_LimpaIsNew()
    {
        var (client, userId) = await _factory.RegisterAsync("markseen1");
        long creatureId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            var c = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, HabitatId = null,
                Seed = 111, TraitConfigVersion = 1, RarityScore = 4m, IsNew = true,
                TraitsJson = Vivarium.Core.Generation.TraitsSerialization.Serialize(Vivarium.Core.Generation.TraitGenerator.Generate(111)),
                CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(c);
            await db.SaveChangesAsync();
            creatureId = c.Id;
        });

        (await client.PostAsync($"/api/game/creatures/{creatureId}/mark-seen", null)).EnsureSuccessStatusCode();

        await _factory.WithDbAsync(async db =>
        {
            var c = await db.CreatureInstances.FirstAsync(x => x.Id == creatureId);
            Assert.False(c.IsNew);
        });
    }

    [Fact]
    public async Task MarkSeen_CriaturaDeOutroUsuario_404()
    {
        var (_, ownerId) = await _factory.RegisterAsync("markseenowner1");
        var (attacker, _) = await _factory.RegisterAsync("markseenattacker1");
        long creatureId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var c = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = ownerId, HabitatId = null,
                Seed = 222, TraitConfigVersion = 1, RarityScore = 4m, IsNew = true,
                TraitsJson = Vivarium.Core.Generation.TraitsSerialization.Serialize(Vivarium.Core.Generation.TraitGenerator.Generate(222)),
                CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(c);
            await db.SaveChangesAsync();
            creatureId = c.Id;
        });

        var resp = await attacker.PostAsync($"/api/game/creatures/{creatureId}/mark-seen", null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
