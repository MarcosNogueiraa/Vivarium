using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;
using Vivarium.Core.Generation;

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
                SpeciesId = 1, OwnerId = userId, OriginalOwnerId = userId, HabitatId = habitat.Id,
                Seed = 777, TraitConfigVersion = 1, RarityScore = 4m,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(777)),
                CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(creature);
            await db.SaveChangesAsync();
            creatureId = creature.Id;
        });
        return creatureId;
    }

    private record InboxEntryRow(long Id, string Kind, string? SenderUsername, InboxCreatureRow? Creature, DateTime? ClaimedAt);
    private record InboxCreatureRow(long Id);
    private record InboxListRow(List<InboxEntryRow> Entries);
    private record BackpackDto(int Capacity, List<AuthTests.CreatureDto> Creatures);

    [Fact]
    public async Task Transferir_VaiProCaixaDeEntradaDoDestinatario_NaoDireto()
    {
        // 14/08/2026 (CLAUDE.md §8.23/§8.24): peixe transferido não cai mais direto no
        // tanque/mochila do destinatário — fica pendente na Caixa de Entrada até resgatar.
        var (sender, senderId) = await _factory.RegisterAsync("doador1");
        var (receiver, receiverId) = await _factory.RegisterAsync("receptor1");
        long creatureId = await CriarCriaturaNoTanque(senderId);

        var response = await sender.PostAsJsonAsync(
            $"/api/game/creatures/{creatureId}/transfer", new { toUsername = "receptor1" });
        response.EnsureSuccessStatusCode();

        var senderTank = await sender.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.DoesNotContain(senderTank!.Creatures, c => c.Id == creatureId);

        var receiverTank = await receiver.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.DoesNotContain(receiverTank!.Creatures, c => c.Id == creatureId);
        var receiverBackpack = await receiver.GetFromJsonAsync<BackpackDto>("/api/game/backpack");
        Assert.DoesNotContain(receiverBackpack!.Creatures, c => c.Id == creatureId);

        var inbox = await receiver.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        var entry = Assert.Single(inbox!.Entries, e => e.Creature?.Id == creatureId);
        Assert.Equal("DirectTransfer", entry.Kind);
        Assert.Equal("doador1", entry.SenderUsername);
        Assert.Null(entry.ClaimedAt);

        await _factory.WithDbAsync(async db =>
        {
            var log = await db.TransactionLogs.SingleAsync(t =>
                t.CreatureInstanceId == creatureId && t.Type == TransactionType.DirectTransfer);
            Assert.Equal(senderId, log.FromUserId);
            Assert.Equal(receiverId, log.ToUserId);

            var creature = await db.CreatureInstances.FirstAsync(c => c.Id == creatureId);
            Assert.True(creature.PendingInboxClaim);
            Assert.Equal(receiverId, creature.OwnerId);
        });

        // Resgatar na Caixa de Entrada — só aí o peixe vai de fato pro tanque.
        var claimResponse = await receiver.PostAsync($"/api/inbox/{entry.Id}/claim", null);
        claimResponse.EnsureSuccessStatusCode();

        var receiverTankAfterClaim = await receiver.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Contains(receiverTankAfterClaim!.Creatures, c => c.Id == creatureId);
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
