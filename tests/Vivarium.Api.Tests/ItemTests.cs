using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Vivarium.Api.Tests;

public class ItemTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public ItemTests(VivariumApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Catalogo_ListaOsTresItensDoMvp()
    {
        var (client, _) = await _factory.RegisterAsync("lojista1");

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");

        Assert.Equal(3, items!.Count);
        Assert.Contains(items, i => i.Key == "filter_basic" && i.Price == 20m);
        Assert.Contains(items, i => i.Key == "auto_filter" && i.Price == 500m && !i.Owned);
        Assert.Contains(items, i => i.Key == "tank_upgrade" && i.Price == 50m);
    }

    [Fact]
    public async Task ComprarFiltro_RestauraQualidadeECobra()
    {
        var (client, userId) = await _factory.RegisterAsync("lojista2");
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            habitat.MaintenanceLevel = 30m;
        });

        var response = await client.PostAsync("/api/items/filter_basic/buy", null);
        response.EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.True(tank!.MaintenanceLevel >= 99m);
        Assert.Equal(80m, tank.Wallet["SOFT"]); // 100 - 20
    }

    [Fact]
    public async Task ComprarUpgrade_AumentaCapacidadeEPrecoSobe50PorCento()
    {
        var (client, _) = await _factory.RegisterAsync("lojista3");

        var response = await client.PostAsync("/api/items/tank_upgrade/buy", null);
        response.EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(4, tank!.Capacity);
        Assert.Equal(50m, tank.Wallet["SOFT"]); // 100 - 50

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.Equal(75m, items!.First(i => i.Key == "tank_upgrade").Price); // 50 × 1.5
    }

    [Fact]
    public async Task AutoFilter_SaldoInsuficiente_Retorna400()
    {
        var (client, _) = await _factory.RegisterAsync("lojista4");

        // Custa 500, saldo inicial é 100
        var response = await client.PostAsync("/api/items/auto_filter/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AutoFilter_CompraDuplicada_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("lojista5");
        await _factory.WithDbAsync(async db =>
        {
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == 1);
            wallet.Amount = 2000m;
        });

        (await client.PostAsync("/api/items/auto_filter/buy", null)).EnsureSuccessStatusCode();

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.True(items!.First(i => i.Key == "auto_filter").Owned);

        var second = await client.PostAsync("/api/items/auto_filter/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task ItemInexistente_Retorna404()
    {
        var (client, _) = await _factory.RegisterAsync("lojista6");

        var response = await client.PostAsync("/api/items/nao_existe/buy", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public record ItemDto(string Key, string Name, string Category, decimal Price, bool Owned);
}
