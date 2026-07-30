using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Tests;

public class TransferTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public TransferTests(VivariumApiFactory factory) => _factory = factory;

    private async Task<long> CriarCriaturaNoTanque(long userId)
    {
        long creatureId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            var creature = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, HabitatId = habitat.Id,
                Seed = 777, TraitConfigVersion = 1, RarityScore = 4m,
                CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(creature);
            await db.SaveChangesAsync();
            creatureId = creature.Id;
        });
        return creatureId;
    }

    [Fact]
    public async Task Transferir_MudaDonoEVaiProTanqueDoDestinatario()
    {
        var (sender, senderId) = await _factory.RegisterAsync("doador1");
        var (receiver, receiverId) = await _factory.RegisterAsync("receptor1");
        long creatureId = await CriarCriaturaNoTanque(senderId);

        var response = await sender.PostAsJsonAsync(
            $"/api/game/creatures/{creatureId}/transfer", new { toUsername = "receptor1" });
        response.EnsureSuccessStatusCode();

        var receiverTank = await receiver.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Contains(receiverTank!.Creatures, c => c.Id == creatureId);

        var senderTank = await sender.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.DoesNotContain(senderTank!.Creatures, c => c.Id == creatureId);

        await _factory.WithDbAsync(async db =>
        {
            var log = await db.TransactionLogs.SingleAsync(t =>
                t.CreatureInstanceId == creatureId && t.Type == TransactionType.DirectTransfer);
            Assert.Equal(senderId, log.FromUserId);
            Assert.Equal(receiverId, log.ToUserId);
        });
    }

    [Fact]
    public async Task TransferirCriaturaListada_Retorna400()
    {
        var (sender, senderId) = await _factory.RegisterAsync("doador2");
        await _factory.RegisterAsync("receptor2");
        long creatureId = await CriarCriaturaNoTanque(senderId);

        var listing = await sender.PostAsJsonAsync("/api/market/listings", new
        {
            creatureInstanceId = creatureId, priceSoft = 10m,
        });
        listing.EnsureSuccessStatusCode();

        var response = await sender.PostAsJsonAsync(
            $"/api/game/creatures/{creatureId}/transfer", new { toUsername = "receptor2" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TransferirParaSiMesmo_Retorna400()
    {
        var (sender, senderId) = await _factory.RegisterAsync("doador3");
        long creatureId = await CriarCriaturaNoTanque(senderId);

        var response = await sender.PostAsJsonAsync(
            $"/api/game/creatures/{creatureId}/transfer", new { toUsername = "doador3" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TransferirCriaturaAlheia_Retorna404()
    {
        var (_, ownerId) = await _factory.RegisterAsync("doador4");
        var (thief, _) = await _factory.RegisterAsync("intruso1");
        await _factory.RegisterAsync("receptor4");
        long creatureId = await CriarCriaturaNoTanque(ownerId);

        var response = await thief.PostAsJsonAsync(
            $"/api/game/creatures/{creatureId}/transfer", new { toUsername = "receptor4" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
