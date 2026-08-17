using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Tests;

public class MarketTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public MarketTests(VivariumApiFactory factory) => _factory = factory;

    private record InboxEntryRow(long Id, string Kind, InboxCreatureRow? Creature, string? Title, string? Body, string? SenderUsername);
    private record InboxCreatureRow(long Id);
    private record InboxListRow(List<InboxEntryRow> Entries);

    private async Task<long> CriarCriaturaNoTanque(long userId)
    {
        long creatureId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            var creature = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, OriginalOwnerId = userId, HabitatId = habitat.Id,
                Seed = 424242, TraitConfigVersion = 1, RarityScore = 5.5m,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(424242)),
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
                SpeciesId = 1, OwnerId = userId, OriginalOwnerId = userId, HabitatId = null,
                Seed = 484848, TraitConfigVersion = 1, RarityScore = 4.4m,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(484848)),
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
        var listings = await buyer.GetFromJsonAsync<MarketListingsResponseDto>("/api/market/listings");
        Assert.Contains(listings!.Listings, l => l.Id == listingId && l.PriceSoft == 40m && l.SellerName == "vendedor1");

        var buyResponse = await buyer.PostAsync($"/api/market/listings/{listingId}/buy", null);
        buyResponse.EnsureSuccessStatusCode();

        // Dinheiro/posse mudam de mãos na hora — só a entrega FÍSICA no tanque/mochila do
        // comprador que espera o resgate na Caixa de Entrada (CLAUDE.md §8.23/§8.24).
        var buyerTank = await buyer.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(60m, buyerTank!.Wallet["SOFT"]); // 100 - 40
        Assert.DoesNotContain(buyerTank.Creatures, c => c.Id == creatureId);

        var sellerTank = await seller.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(140m, sellerTank!.Wallet["SOFT"]); // 100 + 40
        Assert.DoesNotContain(sellerTank.Creatures, c => c.Id == creatureId);

        // Vendedor recebe uma notificação informativa na Caixa de Entrada (BACKLOG.md #1,
        // 16/08/2026) — o soft já foi creditado acima, essa entrada é só um aviso.
        var sellerInbox = await seller.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        var saleNotice = Assert.Single(sellerInbox!.Entries, e => e.Kind == "MarketSale");
        Assert.Contains("40", saleNotice.Body);
        Assert.Equal("comprador1", saleNotice.SenderUsername);
        Assert.Null(saleNotice.Creature); // não é entrega de peixe — não deve acionar posicionamento no resgate

        var inbox = await buyer.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        var entry = Assert.Single(inbox!.Entries, e => e.Creature?.Id == creatureId);
        Assert.Equal("MarketPurchase", entry.Kind);

        (await buyer.PostAsync($"/api/inbox/{entry.Id}/claim", null)).EnsureSuccessStatusCode();
        buyerTank = await buyer.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Contains(buyerTank!.Creatures, c => c.Id == creatureId);

        // Auditoria: 1 registro MarketSale no TransactionLog, gravado na hora da compra (não do resgate)
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

        var listings = await seller.GetFromJsonAsync<MarketListingsResponseDto>("/api/market/listings");
        // Anúncio do próprio requisitante: não aparece na grade paginada (Listings), só no
        // painel fixo (MyListings) — ver ListaDeOutros_NaoInclueMinhaListagem/PainelMine_*.
        Assert.Contains(listings!.MyListings, l => l.Id == listingId && l.CreatureId == creatureId);
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
        var listings = await seller.GetFromJsonAsync<MarketListingsResponseDto>("/api/market/listings");
        Assert.DoesNotContain(listings!.MyListings, l => l.Id == listingId);
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

    [Fact]
    public async Task Listar_AlemDoLimite_Retorna400()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("vendedor-limite");
        for (int i = 0; i < Vivarium.Core.Gameplay.MarketDefaults.MaxActiveListingsPerSeller; i++)
        {
            long id = await CriarCriaturaNoTanque(sellerId);
            await Listar(seller, id, 5m);
        }

        long extraId = await CriarCriaturaNoTanque(sellerId);
        var response = await seller.PostAsJsonAsync("/api/market/listings", new { creatureInstanceId = extraId, priceSoft = 5m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Paginacao_SkipTake_RetornaFatiaCertaETotalCount()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("vendedor-pag");
        var (viewer, _) = await _factory.RegisterAsync("visitante-pag");
        for (int i = 0; i < 5; i++)
        {
            long id = await CriarCriaturaNoTanque(sellerId);
            await Listar(seller, id, 10m + i);
        }

        var page1 = await viewer.GetFromJsonAsync<MarketListingsResponseDto>("/api/market/listings?skip=0&take=2");
        var page2 = await viewer.GetFromJsonAsync<MarketListingsResponseDto>("/api/market/listings?skip=2&take=2");

        Assert.Equal(2, page1!.Listings.Count);
        Assert.Equal(2, page2!.Listings.Count);
        Assert.True(page1.TotalCount >= 5);
        Assert.DoesNotContain(page1.Listings, l1 => page2.Listings.Any(l2 => l2.Id == l1.Id));
    }

    [Fact]
    public async Task MeusAnuncios_AparecemNoPainelFixoEContinuamCompraveisPorOutros()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("vendedor-mine");
        var (buyer, _) = await _factory.RegisterAsync("comprador-mine");
        long creatureId = await CriarCriaturaNoTanque(sellerId);
        long listingId = await Listar(seller, creatureId, 30m);

        var sellerView = await seller.GetFromJsonAsync<MarketListingsResponseDto>("/api/market/listings");
        Assert.Contains(sellerView!.MyListings, l => l.Id == listingId);
        Assert.DoesNotContain(sellerView.Listings, l => l.Id == listingId); // não duplica na grade geral pro dono
        Assert.True(sellerView.MyActiveListingsCount >= 1);
        Assert.Equal(Vivarium.Core.Gameplay.MarketDefaults.MaxActiveListingsPerSeller, sellerView.MaxActiveListings);

        var buyerView = await buyer.GetFromJsonAsync<MarketListingsResponseDto>("/api/market/listings");
        Assert.Contains(buyerView!.Listings, l => l.Id == listingId); // outro jogador continua vendo/comprando normalmente
        Assert.DoesNotContain(buyerView.MyListings, l => l.Id == listingId);
    }

    [Fact]
    public async Task OrdenarPorPreco_AscEDesc()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("vendedor-preco");
        var (viewer, _) = await _factory.RegisterAsync("visitante-preco");
        // Preços extremos e improváveis de colidir com outros testes da mesma fixture
        // compartilhada (ex: o teste de limite lista 50 vezes a 5m) — sem isso, empates de
        // preço com outras listagens já criadas deixam a ordem indeterminada.
        long cheapId = await CriarCriaturaNoTanque(sellerId);
        long expensiveId = await CriarCriaturaNoTanque(sellerId);
        await Listar(seller, cheapId, 0.01m);
        await Listar(seller, expensiveId, 123456m);

        var asc = await viewer.GetFromJsonAsync<MarketListingsResponseDto>("/api/market/listings?sort=price-asc&take=2");
        var desc = await viewer.GetFromJsonAsync<MarketListingsResponseDto>("/api/market/listings?sort=price-desc&take=2");

        Assert.Equal(cheapId, asc!.Listings[0].CreatureId);
        Assert.Equal(expensiveId, desc!.Listings[0].CreatureId);
    }

    [Fact]
    public async Task FiltroPorParte_CorEPadraoCombinam()
    {
        var (seller, sellerId) = await _factory.RegisterAsync("vendedor-filtro");
        var (viewer, _) = await _factory.RegisterAsync("visitante-filtro");

        // Traits construídos manualmente (não via seed aleatório) pra garantir cores
        // divergentes de propósito, sem depender do que um seed específico sorteia.
        PartTraits Part(PartColor color) => new(color, PatternType.None, null, null, null);
        var bluePart = Part(PartColor.Blue);
        var orangePart = Part(PartColor.Orange);
        var blueTraits = new CreatureTraits(ShimmerTier.None, null, 0, bluePart, orangePart, orangePart, new MovementTraits(50, 0.4, 50, 0.3), 5.0);
        var orangeTraits = new CreatureTraits(ShimmerTier.None, null, 0, orangePart, orangePart, orangePart, new MovementTraits(50, 0.4, 50, 0.3), 5.0);

        long blueId = 0, orangeId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == sellerId && h.HabitatType!.Code == "Aquarium");
            var blue = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = sellerId, OriginalOwnerId = sellerId, HabitatId = habitat.Id, Seed = 111111,
                TraitConfigVersion = 1, RarityScore = 5m, TraitsJson = TraitsSerialization.Serialize(blueTraits), CreatedAt = DateTime.UtcNow,
            };
            var orange = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = sellerId, OriginalOwnerId = sellerId, HabitatId = habitat.Id, Seed = 222222,
                TraitConfigVersion = 1, RarityScore = 5m, TraitsJson = TraitsSerialization.Serialize(orangeTraits), CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.AddRange(blue, orange);
            await db.SaveChangesAsync();
            blueId = blue.Id; orangeId = orange.Id;
        });

        await Listar(seller, blueId, 10m);
        await Listar(seller, orangeId, 10m);

        var filtered = await viewer.GetFromJsonAsync<MarketListingsResponseDto>("/api/market/listings?tailColor=Blue");

        Assert.Contains(filtered!.Listings, l => l.CreatureId == blueId);
        Assert.DoesNotContain(filtered.Listings, l => l.CreatureId == orangeId);

        // Multi-seleção (13/08/2026): "Blue,Orange" na mesma parte é OU — os dois batem.
        var either = await viewer.GetFromJsonAsync<MarketListingsResponseDto>("/api/market/listings?tailColor=Blue,Orange");
        Assert.Contains(either!.Listings, l => l.CreatureId == blueId);
        Assert.Contains(either.Listings, l => l.CreatureId == orangeId);
    }

    public record CreatedDto(long Id);
    public record ListingDto(
        long Id, decimal PriceSoft, long SellerId, string SellerName,
        long CreatureId, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore);
    public record MarketListingsResponseDto(
        IReadOnlyList<ListingDto> Listings, int TotalCount,
        IReadOnlyList<ListingDto> MyListings, int MyActiveListingsCount, int MaxActiveListings);
}
