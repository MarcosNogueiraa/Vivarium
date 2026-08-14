using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Tests;

/// <summary>Caixa de Entrada (CLAUDE.md §8.23/§8.24) — mensagens administrativas + entrega
/// pendente de peixe (compra no mercado / transferência direta).</summary>
public class InboxTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public InboxTests(VivariumApiFactory factory) => _factory = factory;

    private record InboxEntryRow(
        long Id, string Kind, string? Title, string? Body, string? SenderUsername,
        InboxCreatureRow? Creature, string? RewardCurrencyCode, decimal? RewardCurrencyAmount,
        DateTime? ReadAt, DateTime? ClaimedAt, DateTime CreatedAt);
    private record InboxCreatureRow(long Id);
    private record InboxListRow(List<InboxEntryRow> Entries);
    private record ClaimAllRow(int ClaimedCount, int FailedCount);
    private record AdminSendRow(int RecipientCount, List<string> NotFoundUsernames);
    private record TankWalletDto(Dictionary<string, decimal> Wallet);

    private async Task TornarAdmin(long userId)
    {
        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.IsAdmin = true;
        });
    }

    private async Task<long> CriarCriaturaNoTanque(long userId, decimal rarityScore = 5m, long seed = 424242)
    {
        long creatureId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            var creature = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, OriginalOwnerId = userId, HabitatId = habitat.Id,
                Seed = seed, TraitConfigVersion = 1, RarityScore = rarityScore,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(seed)),
                CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(creature);
            await db.SaveChangesAsync();
            creatureId = creature.Id;
        });
        return creatureId;
    }

    private async Task<long> ListarEComprarAsync(HttpClient seller, long creatureId, HttpClient buyer, decimal price = 10m)
    {
        var listResp = await seller.PostAsJsonAsync("/api/market/listings", new { creatureInstanceId = creatureId, priceSoft = price });
        listResp.EnsureSuccessStatusCode();
        long listingId = (await listResp.Content.ReadFromJsonAsync<MarketTests.CreatedDto>())!.Id;
        (await buyer.PostAsync($"/api/market/listings/{listingId}/buy", null)).EnsureSuccessStatusCode();
        return listingId;
    }

    private async Task FillTankAndBackpack(long userId, int tankCount, int backpackCount)
    {
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            for (int i = 0; i < tankCount; i++)
                db.CreatureInstances.Add(new CreatureInstance
                {
                    SpeciesId = 1, OwnerId = userId, OriginalOwnerId = userId, HabitatId = habitat.Id,
                    Seed = 61000 + i, TraitConfigVersion = 1, RarityScore = 3m,
                    TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(61000 + i)),
                    CreatedAt = DateTime.UtcNow,
                });
            for (int i = 0; i < backpackCount; i++)
                db.CreatureInstances.Add(new CreatureInstance
                {
                    SpeciesId = 1, OwnerId = userId, OriginalOwnerId = userId, HabitatId = null,
                    Seed = 62000 + i, TraitConfigVersion = 1, RarityScore = 3m,
                    TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(62000 + i)),
                    CreatedAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Comprar_ApareceNaCaixaComoMarketPurchase_ComSenderCorreto()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("inbox-vend2");
        var (buyer, _) = await _factory.RegisterAsync("inbox-comp2");
        long creatureId = await CriarCriaturaNoTanque(sellerId);

        await ListarEComprarAsync(seller, creatureId, buyer);

        var inbox = await buyer.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        var entry = Assert.Single(inbox!.Entries, e => e.Creature?.Id == creatureId);
        Assert.Equal("MarketPurchase", entry.Kind);
        Assert.Equal("inbox-vend2", entry.SenderUsername);
        Assert.Null(entry.ClaimedAt);
    }

    [Fact]
    public async Task Resgatar_ComEspaco_ColocaNoTanque()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("inbox-vend3");
        var (buyer, _) = await _factory.RegisterAsync("inbox-comp3");
        long creatureId = await CriarCriaturaNoTanque(sellerId);
        await ListarEComprarAsync(seller, creatureId, buyer);
        var inbox = await buyer.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        var entry = Assert.Single(inbox!.Entries, e => e.Creature?.Id == creatureId);

        var claim = await buyer.PostAsync($"/api/inbox/{entry.Id}/claim", null);
        claim.EnsureSuccessStatusCode();

        var tank = await buyer.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Contains(tank!.Creatures, c => c.Id == creatureId);

        var inboxAfter = await buyer.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        var entryAfter = inboxAfter!.Entries.Single(e => e.Id == entry.Id);
        Assert.NotNull(entryAfter.ClaimedAt); // continua na lista, só marcada
    }

    [Fact]
    public async Task Resgatar_SemEspaco_Retorna400EContinuaPendente()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("inbox-vend4");
        var (buyer, buyerId) = await _factory.RegisterAsync("inbox-comp4");
        long creatureId = await CriarCriaturaNoTanque(sellerId);
        await ListarEComprarAsync(seller, creatureId, buyer);
        // Enche tanque (3) e mochila (HabitatDefaults.BackpackCapacity) do comprador.
        await FillTankAndBackpack(buyerId, tankCount: 3, backpackCount: HabitatDefaults.BackpackCapacity);

        var inbox = await buyer.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        var entry = Assert.Single(inbox!.Entries, e => e.Creature?.Id == creatureId);

        var claim = await buyer.PostAsync($"/api/inbox/{entry.Id}/claim", null);
        Assert.Equal(HttpStatusCode.BadRequest, claim.StatusCode);

        await _factory.WithDbAsync(async db =>
        {
            var creature = await db.CreatureInstances.FirstAsync(c => c.Id == creatureId);
            Assert.True(creature.PendingInboxClaim);
        });
        var inboxAfter = await buyer.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        Assert.Null(inboxAfter!.Entries.Single(e => e.Id == entry.Id).ClaimedAt);
    }

    [Fact]
    public async Task Transferir_ApareceNaCaixaComoDirectTransfer()
    {
        var (sender, senderId) = await _factory.RegisterAsync("inbox-doa1");
        var (receiver, _) = await _factory.RegisterAsync("inbox-rec1");
        long creatureId = await CriarCriaturaNoTanque(senderId);

        (await sender.PostAsJsonAsync($"/api/game/creatures/{creatureId}/transfer", new { toUsername = "inbox-rec1" }))
            .EnsureSuccessStatusCode();

        var inbox = await receiver.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        var entry = Assert.Single(inbox!.Entries, e => e.Creature?.Id == creatureId);
        Assert.Equal("DirectTransfer", entry.Kind);
        Assert.Equal("inbox-doa1", entry.SenderUsername);
    }

    [Fact]
    public async Task PeixePendente_NaoPodeSerRetransferido()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("inbox-vend5");
        var (buyer, buyerId) = await _factory.RegisterAsync("inbox-comp5");
        await _factory.RegisterAsync("inbox-terceiro1");
        long creatureId = await CriarCriaturaNoTanque(sellerId);
        await ListarEComprarAsync(seller, creatureId, buyer);

        var response = await buyer.PostAsJsonAsync($"/api/game/creatures/{creatureId}/transfer", new { toUsername = "inbox-terceiro1" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PeixePendente_NaoPodeSerListadoNoMercado()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("inbox-vend6");
        var (buyer, _) = await _factory.RegisterAsync("inbox-comp6");
        long creatureId = await CriarCriaturaNoTanque(sellerId);
        await ListarEComprarAsync(seller, creatureId, buyer);

        var response = await buyer.PostAsJsonAsync("/api/market/listings", new { creatureInstanceId = creatureId, priceSoft = 5m });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PeixePendente_NaoPodeSerUsadoNaBreeding()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("inbox-vend7");
        var (buyer, buyerId) = await _factory.RegisterAsync("inbox-comp7");
        long pendingId = await CriarCriaturaNoTanque(sellerId);
        await ListarEComprarAsync(seller, pendingId, buyer);
        long ownId = await CriarCriaturaNoTanque(buyerId, seed: 313131);

        var response = await buyer.PostAsJsonAsync("/api/breeding/start", new { parentAId = pendingId, parentBId = ownId });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PeixePendente_NaoPodeSerVendidoAoNpc()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("inbox-vend8");
        var (buyer, _) = await _factory.RegisterAsync("inbox-comp8");
        long creatureId = await CriarCriaturaNoTanque(sellerId);
        await ListarEComprarAsync(seller, creatureId, buyer);

        var response = await buyer.PostAsync($"/api/game/creatures/{creatureId}/sell-vendor", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminMandaMensagem_Todos_TodoUsuarioGanhaUmaEntrada()
    {
        var (admin, adminId) = await _factory.RegisterAsync("inbox-admin1");
        await TornarAdmin(adminId);
        var (other, _) = await _factory.RegisterAsync("inbox-jog1");

        var response = await admin.PostAsJsonAsync("/api/admin/inbox/send", new
        {
            title = "Aviso", body = "Manutenção amanhã", audience = "All",
            usernames = (string[]?)null, rewardCurrencyCode = (string?)null, rewardCurrencyAmount = (decimal?)null,
        });
        response.EnsureSuccessStatusCode();

        var otherInbox = await other.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        Assert.Contains(otherInbox!.Entries, e => e.Kind == "AdminMessage" && e.Title == "Aviso");
    }

    [Fact]
    public async Task AdminMandaMensagem_NaoAdmin_Retorna403()
    {
        var (client, _) = await _factory.RegisterAsync("inbox-naoadmin1");

        var response = await client.PostAsJsonAsync("/api/admin/inbox/send", new
        {
            title = "x", body = "y", audience = "All",
            usernames = (string[]?)null, rewardCurrencyCode = (string?)null, rewardCurrencyAmount = (decimal?)null,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminMandaMensagem_Selecionada_ComUsernameInexistente_MandaProsValidosEListaOsNaoEncontrados()
    {
        var (admin, adminId) = await _factory.RegisterAsync("inbox-admin2");
        await TornarAdmin(adminId);
        var (valid, _) = await _factory.RegisterAsync("inbox-valido1");

        var response = await admin.PostAsJsonAsync("/api/admin/inbox/send", new
        {
            title = "Oi", body = "Mensagem", audience = "Selected",
            usernames = new[] { "inbox-valido1", "nao-existe-esse-usuario" },
            rewardCurrencyCode = (string?)null, rewardCurrencyAmount = (decimal?)null,
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AdminSendRow>();
        Assert.Equal(1, result!.RecipientCount);
        Assert.Contains("nao-existe-esse-usuario", result.NotFoundUsernames);

        var validInbox = await valid.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        Assert.Contains(validInbox!.Entries, e => e.Title == "Oi");
    }

    [Fact]
    public async Task MensagemComRecompensa_ResgatarCreditaCarteiraEAuditaTransactionLog()
    {
        var (admin, adminId) = await _factory.RegisterAsync("inbox-admin3");
        await TornarAdmin(adminId);
        var (player, playerId) = await _factory.RegisterAsync("inbox-jog2");

        await admin.PostAsJsonAsync("/api/admin/inbox/send", new
        {
            title = "Prêmio", body = "Toma uma grana", audience = "All",
            usernames = (string[]?)null, rewardCurrencyCode = "SOFT", rewardCurrencyAmount = 50m,
        });

        var inbox = await player.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        var entry = Assert.Single(inbox!.Entries, e => e.Title == "Prêmio");
        Assert.Equal("SOFT", entry.RewardCurrencyCode);
        Assert.Equal(50m, entry.RewardCurrencyAmount);

        (await player.PostAsync($"/api/inbox/{entry.Id}/claim", null)).EnsureSuccessStatusCode();

        var tank = await player.GetFromJsonAsync<TankWalletDto>("/api/game/tank");
        Assert.Equal(150m, tank!.Wallet["SOFT"]); // 100 inicial + 50

        await _factory.WithDbAsync(async db =>
        {
            var log = await db.TransactionLogs.SingleAsync(t => t.Type == TransactionType.InboxReward && t.ToUserId == playerId);
            Assert.Equal(50m, log.Amount);
        });
    }

    [Fact]
    public async Task MensagemSemRecompensa_ResgatarSoMarcaClaimed()
    {
        var (admin, adminId) = await _factory.RegisterAsync("inbox-admin4");
        await TornarAdmin(adminId);
        var (player, _) = await _factory.RegisterAsync("inbox-jog3");

        await admin.PostAsJsonAsync("/api/admin/inbox/send", new
        {
            title = "Só um aviso", body = "Nada pra resgatar", audience = "All",
            usernames = (string[]?)null, rewardCurrencyCode = (string?)null, rewardCurrencyAmount = (decimal?)null,
        });

        var inbox = await player.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        var entry = Assert.Single(inbox!.Entries, e => e.Title == "Só um aviso");
        var walletBefore = (await player.GetFromJsonAsync<TankWalletDto>("/api/game/tank"))!.Wallet["SOFT"];

        (await player.PostAsync($"/api/inbox/{entry.Id}/claim", null)).EnsureSuccessStatusCode();

        var walletAfter = (await player.GetFromJsonAsync<TankWalletDto>("/api/game/tank"))!.Wallet["SOFT"];
        Assert.Equal(walletBefore, walletAfter);
    }

    [Fact]
    public async Task OriginalOwnerId_NuncaMudaEntreDonoOriginalTransferidoERevendido()
    {
        var (a, aId) = await _factory.RegisterAsync("inbox-dono-a");
        var (b, bId) = await _factory.RegisterAsync("inbox-dono-b");
        var (c, cId) = await _factory.RegisterAsync("inbox-dono-c");
        long creatureId = await CriarCriaturaNoTanque(aId);

        // A -> B (transferência)
        (await a.PostAsJsonAsync($"/api/game/creatures/{creatureId}/transfer", new { toUsername = "inbox-dono-b" }))
            .EnsureSuccessStatusCode();
        var bInbox = await b.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        var bEntry = bInbox!.Entries.Single(e => e.Creature?.Id == creatureId);
        (await b.PostAsync($"/api/inbox/{bEntry.Id}/claim", null)).EnsureSuccessStatusCode();

        // B -> C (venda no mercado)
        var listResp = await b.PostAsJsonAsync("/api/market/listings", new { creatureInstanceId = creatureId, priceSoft = 5m });
        listResp.EnsureSuccessStatusCode();
        long listingId = (await listResp.Content.ReadFromJsonAsync<MarketTests.CreatedDto>())!.Id;
        (await c.PostAsync($"/api/market/listings/{listingId}/buy", null)).EnsureSuccessStatusCode();

        await _factory.WithDbAsync(async db =>
        {
            var creature = await db.CreatureInstances.FirstAsync(x => x.Id == creatureId);
            Assert.Equal(cId, creature.OwnerId); // dono atual mudou 2x
            Assert.Equal(aId, creature.OriginalOwnerId); // primeiro dono nunca mudou
        });
        _ = bId;
    }

    [Fact]
    public async Task ClaimAll_ResgataTodasAsPendentesEDevolveContagem()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("inbox-vend9");
        var (buyer, buyerId) = await _factory.RegisterAsync("inbox-comp9");
        long creature1 = await CriarCriaturaNoTanque(sellerId, seed: 71001);
        long creature2 = await CriarCriaturaNoTanque(sellerId, seed: 71002);
        await ListarEComprarAsync(seller, creature1, buyer, price: 5m);
        await ListarEComprarAsync(seller, creature2, buyer, price: 5m);
        // Mochila do comprador só tem espaço pra 1 dos 2 (tanque cheio primeiro).
        await FillTankAndBackpack(buyerId, tankCount: 3, backpackCount: HabitatDefaults.BackpackCapacity - 1);

        var response = await buyer.PostAsync("/api/inbox/claim-all", null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ClaimAllRow>();

        Assert.Equal(1, result!.ClaimedCount);
        Assert.Equal(1, result.FailedCount);
    }

    [Fact]
    public async Task MarkAllRead_MarcaReadAtEmTodasAsNaoLidas()
    {
        var (admin, adminId) = await _factory.RegisterAsync("inbox-admin5");
        await TornarAdmin(adminId);
        var (player, _) = await _factory.RegisterAsync("inbox-jog4");
        await admin.PostAsJsonAsync("/api/admin/inbox/send", new
        {
            title = "A", body = "a", audience = "All",
            usernames = (string[]?)null, rewardCurrencyCode = (string?)null, rewardCurrencyAmount = (decimal?)null,
        });
        await admin.PostAsJsonAsync("/api/admin/inbox/send", new
        {
            title = "B", body = "b", audience = "All",
            usernames = (string[]?)null, rewardCurrencyCode = (string?)null, rewardCurrencyAmount = (decimal?)null,
        });

        (await player.PostAsync("/api/inbox/mark-all-read", null)).EnsureSuccessStatusCode();

        var inbox = await player.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        Assert.All(inbox!.Entries, e => Assert.NotNull(e.ReadAt));
    }

    [Fact]
    public async Task ClearClaimed_RemoveSoAsJaResgatadas_PreservaPendentesMesmoLidas()
    {
        var (admin, adminId) = await _factory.RegisterAsync("inbox-admin6");
        await TornarAdmin(adminId);
        var (player, _) = await _factory.RegisterAsync("inbox-jog5");
        await admin.PostAsJsonAsync("/api/admin/inbox/send", new
        {
            title = "Resgatada", body = "x", audience = "All",
            usernames = (string[]?)null, rewardCurrencyCode = (string?)null, rewardCurrencyAmount = (decimal?)null,
        });
        await admin.PostAsJsonAsync("/api/admin/inbox/send", new
        {
            title = "Pendente", body = "y", audience = "All",
            usernames = (string[]?)null, rewardCurrencyCode = (string?)null, rewardCurrencyAmount = (decimal?)null,
        });

        var inbox = await player.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        var toClaim = inbox!.Entries.Single(e => e.Title == "Resgatada");
        (await player.PostAsync($"/api/inbox/{toClaim.Id}/claim", null)).EnsureSuccessStatusCode();
        // A "pendente" fica lida mas SEM resgatar — não deve ser apagada.
        (await player.PostAsync("/api/inbox/mark-all-read", null)).EnsureSuccessStatusCode();

        (await player.PostAsync("/api/inbox/clear-claimed", null)).EnsureSuccessStatusCode();

        var after = await player.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        Assert.DoesNotContain(after!.Entries, e => e.Title == "Resgatada");
        Assert.Contains(after.Entries, e => e.Title == "Pendente");
    }
}
