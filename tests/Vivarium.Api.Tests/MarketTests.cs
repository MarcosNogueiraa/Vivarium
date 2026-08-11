using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Tests;

public class MarketTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public MarketTests(VivariumApiFactory factory) => _factory = factory;

    private async Task<long> CriarCriaturaNoTanque(long userId)
    {
        long creatureId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            var creature = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, HabitatId = habitat.Id,
                Seed = 424242, TraitConfigVersion = 1, RarityScore = 5.5m,
                CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(creature);
            await db.SaveChangesAsync();
            creatureId = creature.Id;
        });
        return creatureId;
    }

    private async Task<long> CriarCriaturaNaMochila(long userId)
    {
        long creatureId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var creature = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, HabitatId = null,
                Seed = 484848, TraitConfigVersion = 1, RarityScore = 4.4m,
                CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(creature);
            await db.SaveChangesAsync();
            creatureId = creature.Id;
        });
        return creatureId;
    }

    private static async Task<long> Listar(HttpClient seller, long creatureId, decimal price)
    {
        var response = await seller.PostAsJsonAsync("/api/market/listings", new
        {
            creatureInstanceId = creatureId, priceSoft = price,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreatedDto>();
        return body!.Id;
    }

    [Fact]
    public async Task FluxoCompleto_ListarComprarTransferePosseESaldo()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("vendedor1");
        var (buyer, buyerId) = await _factory.RegisterAsync("comprador1");
        long creatureId = await CriarCriaturaNoTanque(sellerId);

        long listingId = await Listar(seller, creatureId, 40m);

        // Listagem visível no mercado
        var listings = await buyer.GetFromJsonAsync<List<ListingDto>>("/api/market/listings");
        Assert.Contains(listings!, l => l.Id == listingId && l.PriceSoft == 40m && l.SellerName == "vendedor1");

        var buyResponse = await buyer.PostAsync($"/api/market/listings/{listingId}/buy", null);
        buyResponse.EnsureSuccessStatusCode();

        var buyerTank = await buyer.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(60m, buyerTank!.Wallet["SOFT"]); // 100 - 40
        Assert.Contains(buyerTank.Creatures, c => c.Id == creatureId);

        var sellerTank = await seller.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(140m, sellerTank!.Wallet["SOFT"]); // 100 + 40
        Assert.DoesNotContain(sellerTank.Creatures, c => c.Id == creatureId);

        // Auditoria: 1 registro MarketSale no TransactionLog
        await _factory.WithDbAsync(async db =>
        {
            var log = await db.TransactionLogs.SingleAsync(t => t.CreatureInstanceId == creatureId);
            Assert.Equal(TransactionType.MarketSale, log.Type);
            Assert.Equal(buyerId, log.FromUserId);
            Assert.Equal(sellerId, log.ToUserId);
            Assert.Equal(40m, log.Amount);
        });
    }

    [Fact]
    public async Task Listar_TiraACriaturaDoTanque()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("vendedor2");
        long creatureId = await CriarCriaturaNoTanque(sellerId);

        await Listar(seller, creatureId, 10m);

        var tank = await seller.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.DoesNotContain(tank!.Creatures, c => c.Id == creatureId);
    }

    [Fact]
    public async Task Listar_CriaturaDaMochila_Funciona()
    {
        // Bug real corrigido (12/08/2026, relatado pelo usuário): listar direto da mochila
        // (HabitatId null — estado normal, não "em trânsito") retornava erro sempre, porque o
        // check antigo tratava `HabitatId is null` como sinônimo de "já está no mercado".
        var (seller, sellerId) = await _factory.RegisterAsync("vendedor-mochila");
        long creatureId = await CriarCriaturaNaMochila(sellerId);

        long listingId = await Listar(seller, creatureId, 15m);

        var listings = await seller.GetFromJsonAsync<List<ListingDto>>("/api/market/listings");
        Assert.Contains(listings!, l => l.Id == listingId && l.CreatureId == creatureId);
    }

    [Fact]
    public async Task Listar_MesmaCriaturaDuasVezes_Retorna400NaSegunda()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("vendedor-duplo");
        long creatureId = await CriarCriaturaNoTanque(sellerId);
        await Listar(seller, creatureId, 10m);

        var second = await seller.PostAsJsonAsync("/api/market/listings", new { creatureInstanceId = creatureId, priceSoft = 20m });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Cancelar_DevolveAoTanque()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("vendedor3");
        long creatureId = await CriarCriaturaNoTanque(sellerId);
        long listingId = await Listar(seller, creatureId, 10m);

        var response = await seller.PostAsync($"/api/market/listings/{listingId}/cancel", null);
        response.EnsureSuccessStatusCode();

        var tank = await seller.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Contains(tank!.Creatures, c => c.Id == creatureId);

        // Não aparece mais no mercado
        var listings = await seller.GetFromJsonAsync<List<ListingDto>>("/api/market/listings");
        Assert.DoesNotContain(listings!, l => l.Id == listingId);
    }

    [Fact]
    public async Task ComprarPropriaListagem_Retorna400()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("vendedor4");
        long creatureId = await CriarCriaturaNoTanque(sellerId);
        long listingId = await Listar(seller, creatureId, 10m);

        var response = await seller.PostAsync($"/api/market/listings/{listingId}/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SaldoInsuficiente_Retorna400()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("vendedor5");
        var (buyer, _) = await _factory.RegisterAsync("duro1");
        long creatureId = await CriarCriaturaNoTanque(sellerId);
        long listingId = await Listar(seller, creatureId, 5000m);

        var response = await buyer.PostAsync($"/api/market/listings/{listingId}/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListarCriaturaDeOutroJogador_Retorna404()
    {
        var (_, ownerId) = await _factory.RegisterAsync("dono1");
        var (thief, _) = await _factory.RegisterAsync("ladrao1");
        long creatureId = await CriarCriaturaNoTanque(ownerId);

        var response = await thief.PostAsJsonAsync("/api/market/listings", new
        {
            creatureInstanceId = creatureId, priceSoft = 10m,
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public record CreatedDto(long Id);
    public record ListingDto(
        long Id, decimal PriceSoft, long SellerId, string SellerName,
        long CreatureId, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore);
}
