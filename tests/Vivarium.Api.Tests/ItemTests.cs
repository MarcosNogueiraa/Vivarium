using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Gameplay;

namespace Vivarium.Api.Tests;

public class ItemTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public ItemTests(VivariumApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Catalogo_ListaOsOitoItensDoMvp()
    {
        var (client, _) = await _factory.RegisterAsync("lojista1");

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");

        Assert.Equal(8, items!.Count);
        Assert.Contains(items, i => i.Key == "filter_basic" && i.Price == 20m);
        Assert.Contains(items, i => i.Key == "auto_filter" && i.Price == 500m && !i.Owned);
        Assert.Contains(items, i => i.Key == "auto_filter_2" && i.Price == 1200m && !i.Owned);
        Assert.Contains(items, i => i.Key == "auto_filter_3" && i.Price == 2500m && !i.Owned);
        Assert.Contains(items, i => i.Key == "tank_upgrade" && i.Price == 50m && !i.Locked);
        // Aquário Grande/Master: preço fixo (TransitionCost), bloqueados até a capacidade
        // atual (3, recém-registrado) chegar no respectivo teto.
        Assert.Contains(items, i => i.Key == "aquario_grande" && i.Price == 4000m && i.Locked && !i.Owned);
        Assert.Contains(items, i => i.Key == "aquario_master" && i.Price == 12000m && i.Locked && !i.Owned);
        // Sensor de Qualidade da Água (§8.18): preço por faixa (Aquário = 800), nunca bloqueado.
        Assert.Contains(items, i => i.Key == "water_sensor" && i.Price == 800m && !i.Locked && !i.Owned);
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
    public async Task NoTetoDaFaixa_TankUpgradeFicaBloqueadoEAquarioGrandeVirouComprável()
    {
        var (client, userId) = await _factory.RegisterAsync("lojista9");
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            habitat.Capacity = 5; // teto do Aquário
        });

        // "Upgrade de tanque" (curva suave) não serve mais pra cruzar de faixa — bloqueado.
        var blocked = await client.PostAsync("/api/items/tank_upgrade/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.True(items!.First(i => i.Key == "tank_upgrade").Locked);
        Assert.False(items!.First(i => i.Key == "aquario_grande").Locked);
    }

    [Fact]
    public async Task ComprarAquarioGrande_ProdutoSeparado_CobraOCustoDeTransicao()
    {
        var (client, userId) = await _factory.RegisterAsync("lojista10");
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            habitat.Capacity = 5; // teto do Aquário — só agora "Aquário Grande" fica comprável
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == 1);
            wallet.Amount = 10000m;
        });

        var response = await client.PostAsync("/api/items/aquario_grande/buy", null);
        response.EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(6, tank!.Capacity);
        Assert.Equal(10000m - CapacityBands.AquarioGrande.TransitionCost, tank.Wallet["SOFT"]);

        // Depois de trocar, o item vira "owned" (não compra de novo) e "Upgrade de tanque"
        // volta a valer, já dentro do Aquário Grande.
        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.True(items!.First(i => i.Key == "aquario_grande").Owned);
        Assert.False(items!.First(i => i.Key == "tank_upgrade").Locked);
    }

    [Fact]
    public async Task ComprarAquarioGrande_AntesDoTeto_Retorna400()
    {
        var (client, _) = await _factory.RegisterAsync("lojista11");
        // Capacidade inicial é 3, ainda longe do teto do Aquário (5) — "Aquário Grande"
        // deveria estar bloqueado (Locked), não comprável.
        var response = await client.PostAsync("/api/items/aquario_grande/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
    public async Task ComprarFiltroNivel2_NaoBloqueiaMesmoJaTendoNivel1()
    {
        var (client, userId) = await _factory.RegisterAsync("lojista7");
        await _factory.WithDbAsync(async db =>
        {
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == 1);
            wallet.Amount = 5000m;
        });

        (await client.PostAsync("/api/items/auto_filter/buy", null)).EnsureSuccessStatusCode();
        var second = await client.PostAsync("/api/items/auto_filter_2/buy", null);
        second.EnsureSuccessStatusCode();

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.True(items!.First(i => i.Key == "auto_filter").Owned);
        Assert.True(items!.First(i => i.Key == "auto_filter_2").Owned);
    }

    [Fact]
    public async Task UpgradeNoTetoDaCapacidade_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("lojista8");
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            habitat.Capacity = 15; // teto absoluto (CapacityBands.MaxCapacity)
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == 1);
            wallet.Amount = 100000m;
        });

        var response = await client.PostAsync("/api/items/tank_upgrade/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ItemInexistente_Retorna404()
    {
        var (client, _) = await _factory.RegisterAsync("lojista6");

        var response = await client.PostAsync("/api/items/nao_existe/buy", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public record ItemDto(
        string Key, string Name, string Category, decimal Price, bool Owned,
        bool Locked = false, string? LockedReason = null);
}
