using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Tests;

/// <summary>Progressão do jogador (18/08/2026, BACKLOG.md #7) — XP por contagem de ações
/// (coleta/breeding) e avatar escolhido entre os próprios peixes. Só social/cosmético.</summary>
public class LevelTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public LevelTests(VivariumApiFactory factory) => _factory = factory;

    private record MeDto(long UserId, string Username, string Email, long Xp, int Level, long CurrentLevelXp, long XpForNextLevel, double Progress01, CreatureDto? Avatar);
    private record CreatureDto(long Id, int SpeciesId, string Seed, decimal RarityScore);

    private async Task<long> HabitatIdOf(long userId)
    {
        long id = 0;
        await _factory.WithDbAsync(async db =>
            id = (await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium")).Id);
        return id;
    }

    private async Task<long> InserirItemProntoNaFila(long habitatId)
    {
        long itemId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var item = new GenerationQueueItem
            {
                HabitatId = habitatId, SpeciesId = 1, ReadyAt = DateTime.UtcNow.AddMinutes(-1),
                Status = QueueItemStatus.Pending, IsSick = false,
            };
            db.GenerationQueueItems.Add(item);
            await db.SaveChangesAsync();
            itemId = item.Id;
        });
        return itemId;
    }

    [Fact]
    public async Task Coleta_ItemPronto_Concede_FishCollectXp()
    {
        var (client, userId) = await _factory.RegisterAsync("xpcoleta1");
        long habitatId = await HabitatIdOf(userId);
        long itemId = await InserirItemProntoNaFila(habitatId);

        var meBefore = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.Equal(0, meBefore!.Xp);

        (await client.PostAsync($"/api/game/collect/{itemId}", null)).EnsureSuccessStatusCode();

        var meAfter = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.Equal(LevelConfig.Default.FishCollectXp, meAfter!.Xp);
    }

    [Fact]
    public async Task ColetaAutomaticaVip_ConcedeXpEmLote()
    {
        var (client, userId) = await _factory.RegisterAsync("xpvip1");
        long habitatId = await HabitatIdOf(userId);

        await _factory.WithDbAsync(async db =>
        {
            db.VipSubscriptions.Add(new VipSubscription
            {
                UserId = userId, StartAt = DateTime.UtcNow.AddDays(-1), EndAt = DateTime.UtcNow.AddDays(30),
                Status = SubscriptionStatus.Active,
            });
            var habitat = await db.Habitats.FirstAsync(h => h.Id == habitatId);
            habitat.LastHeartbeatAt = DateTime.UtcNow; // online
        });
        await InserirItemProntoNaFila(habitatId);

        // GET do tanque roda o tick lazy — VIP online coleta sozinho o item novo + o peixe
        // inicial do registro (2 no total), e concede XP em UMA chamada, não uma por peixe.
        var tank = await client.GetAsync("/api/game/tank");
        tank.EnsureSuccessStatusCode();

        var me = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.Equal(2 * LevelConfig.Default.FishCollectXp, me!.Xp);
    }

    [Fact]
    public async Task Breeding_Collect_Concede_BreedingCollectXp()
    {
        var (client, userId) = await _factory.RegisterAsync("xpbreed1");
        long habitatId = await HabitatIdOf(userId);

        long parentAId = 0, parentBId = 0;
        await _factory.WithDbAsync(async db =>
        {
            int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == softId);
            wallet.Amount += 1000m;

            var a = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, OriginalOwnerId = userId, HabitatId = habitatId,
                Seed = 111, TraitConfigVersion = 1, RarityScore = 5m,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(111)), CreatedAt = DateTime.UtcNow,
            };
            var b = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, OriginalOwnerId = userId, HabitatId = habitatId,
                Seed = 222, TraitConfigVersion = 1, RarityScore = 6m,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(222)), CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(a);
            db.CreatureInstances.Add(b);
            await db.SaveChangesAsync();
            parentAId = a.Id;
            parentBId = b.Id;
        });

        (await client.PostAsJsonAsync("/api/breeding/start", new { parentAId, parentBId })).EnsureSuccessStatusCode();
        await _factory.WithDbAsync(async db =>
        {
            var slot = await db.BreedingSlots.FirstAsync(s => s.UserId == userId && s.Status == BreedingStatus.InProgress);
            slot.ReadyAt = DateTime.UtcNow.AddMinutes(-1);
        });

        (await client.PostAsync("/api/breeding/collect", null)).EnsureSuccessStatusCode();

        var me = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.Equal(LevelConfig.Default.BreedingCollectXp, me!.Xp);
    }

    [Fact]
    public async Task SetAvatar_ComPeixeProprio_Funciona()
    {
        var (client, userId) = await _factory.RegisterAsync("avatar1");
        long habitatId = await HabitatIdOf(userId);
        long creatureId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var c = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, OriginalOwnerId = userId, HabitatId = habitatId,
                Seed = 333, TraitConfigVersion = 1, RarityScore = 7m,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(333)), CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(c);
            await db.SaveChangesAsync();
            creatureId = c.Id;
        });

        var response = await client.PutAsJsonAsync("/api/account/avatar", new { creatureInstanceId = creatureId });
        response.EnsureSuccessStatusCode();

        var me = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.NotNull(me!.Avatar);
        Assert.Equal(creatureId, me.Avatar!.Id);
    }

    [Fact]
    public async Task SetAvatar_ComPeixeDeOutroUsuario_Retorna400()
    {
        var (_, ownerId) = await _factory.RegisterAsync("avatardono1");
        var (client, _) = await _factory.RegisterAsync("avatarintruso1");
        long ownerHabitatId = await HabitatIdOf(ownerId);
        long creatureId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var c = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = ownerId, OriginalOwnerId = ownerId, HabitatId = ownerHabitatId,
                Seed = 444, TraitConfigVersion = 1, RarityScore = 4m,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(444)), CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(c);
            await db.SaveChangesAsync();
            creatureId = c.Id;
        });

        var response = await client.PutAsJsonAsync("/api/account/avatar", new { creatureInstanceId = creatureId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetAvatar_ComNull_Limpa()
    {
        var (client, userId) = await _factory.RegisterAsync("avatarlimpa1");
        long habitatId = await HabitatIdOf(userId);
        long creatureId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var c = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, OriginalOwnerId = userId, HabitatId = habitatId,
                Seed = 555, TraitConfigVersion = 1, RarityScore = 4m,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(555)), CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(c);
            await db.SaveChangesAsync();
            creatureId = c.Id;
        });
        (await client.PutAsJsonAsync("/api/account/avatar", new { creatureInstanceId = creatureId })).EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync("/api/account/avatar", new { creatureInstanceId = (long?)null })).EnsureSuccessStatusCode();

        var me = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.Null(me!.Avatar);
    }
}
