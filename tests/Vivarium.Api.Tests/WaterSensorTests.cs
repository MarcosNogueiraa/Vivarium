using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Tests;

/// <summary>
/// Limpeza Automática (VIP) + Sensor de Qualidade da Água (§8.18). Cobre o fluxo funcional e os
/// pontos de segurança levantados no plano: gatilho grátis em 0% para qualquer VIP, preço do
/// sensor por faixa (nunca vindo do cliente), validação de range, VIP-gating server-side, saldo
/// nunca fica negativo, e a limpeza automática nunca acontece sem VIP mesmo com sensor comprado.
/// </summary>
public class WaterSensorTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public WaterSensorTests(VivariumApiFactory factory) => _factory = factory;

    private record TankDto(decimal MaintenanceLevel, bool HasWaterSensor, decimal AutoCleanTriggerPercent, decimal WaterSensorMaxTriggerPercent);
    private record ItemDto(string Key, string Name, string Category, decimal Price, bool Owned, bool Locked, string? LockedReason);

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

    private Task SetMaintenance(long habitatId, decimal value) => _factory.WithDbAsync(async db =>
    {
        var habitat = await db.Habitats.FirstAsync(h => h.Id == habitatId);
        habitat.MaintenanceLevel = value;
    });

    private async Task<decimal> SoftBalanceOf(long userId)
    {
        decimal amount = 0m;
        await _factory.WithDbAsync(async db =>
        {
            int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
            amount = (await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == softId)).Amount;
        });
        return amount;
    }

    private Task SetSoftBalance(long userId, decimal amount) => _factory.WithDbAsync(async db =>
    {
        int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
        var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == softId);
        wallet.Amount = amount;
    });

    // ---------- Compra do Sensor ----------

    [Fact]
    public async Task Comprar_CobraPrecoDaFaixaAquarioEMarcaOwned()
    {
        var (client, userId) = await _factory.RegisterAsync("sensor1");
        await SetSoftBalance(userId, 5000m);

        var resp = await client.PostAsync("/api/items/water_sensor/buy", null);
        resp.EnsureSuccessStatusCode();

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.Contains(items!, i => i.Key == "water_sensor" && i.Price == 800m && i.Owned && !i.Locked);

        Assert.Equal(4200m, await SoftBalanceOf(userId)); // 5000 - 800
    }

    [Fact]
    public async Task Comprar_PrecoCresceComAFaixaDoAquario()
    {
        var (client, userId) = await _factory.RegisterAsync("sensor2");
        long habitatId = await HabitatIdOf(userId);
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.Id == habitatId);
            habitat.Capacity = 6; // faixa Aquário Grande
        });

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.Contains(items!, i => i.Key == "water_sensor" && i.Price == 2000m);
    }

    [Fact]
    public async Task Comprar_Duplicado_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("sensor3");
        await SetSoftBalance(userId, 5000m);

        (await client.PostAsync("/api/items/water_sensor/buy", null)).EnsureSuccessStatusCode();
        var second = await client.PostAsync("/api/items/water_sensor/buy", null);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        // Só debitou uma vez — corrida/dupla compra não pode custar soft duas vezes.
        Assert.Equal(4200m, await SoftBalanceOf(userId));
    }

    [Fact]
    public async Task Comprar_SemSaldo_Retorna400ENaoMarcaOwned()
    {
        var (client, _) = await _factory.RegisterAsync("sensor4"); // começa com 100 soft, preço é 800

        var resp = await client.PostAsync("/api/items/water_sensor/buy", null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.Contains(items!, i => i.Key == "water_sensor" && !i.Owned);
    }

    // ---------- Configurar o gatilho ----------

    [Fact]
    public async Task ConfigurarGatilho_SemSensor_Retorna400()
    {
        var (client, _) = await _factory.RegisterAsync("gatilho1");

        var resp = await client.PostAsJsonAsync("/api/game/water-sensor/trigger", new { percent = 40m });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(81)]
    [InlineData(1000)]
    public async Task ConfigurarGatilho_ForaDoIntervalo_Retorna400(decimal percent)
    {
        var (client, userId) = await _factory.RegisterAsync($"gatilho2_{percent}");
        await SetSoftBalance(userId, 5000m);
        (await client.PostAsync("/api/items/water_sensor/buy", null)).EnsureSuccessStatusCode();

        var resp = await client.PostAsJsonAsync("/api/game/water-sensor/trigger", new { percent });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ConfigurarGatilho_ComSensor_PersisteEApareceNoTanque()
    {
        var (client, userId) = await _factory.RegisterAsync("gatilho3");
        await SetSoftBalance(userId, 5000m);
        (await client.PostAsync("/api/items/water_sensor/buy", null)).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/game/water-sensor/trigger", new { percent = 55m })).EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank");
        Assert.True(tank!.HasWaterSensor);
        Assert.Equal(55m, tank.AutoCleanTriggerPercent);
        Assert.Equal(80m, tank.WaterSensorMaxTriggerPercent);
    }

    [Fact]
    public async Task ConfigurarGatilho_NoTeto80_Aceita()
    {
        var (client, userId) = await _factory.RegisterAsync("gatilho4");
        await SetSoftBalance(userId, 5000m);
        (await client.PostAsync("/api/items/water_sensor/buy", null)).EnsureSuccessStatusCode();

        var resp = await client.PostAsJsonAsync("/api/game/water-sensor/trigger", new { percent = 80m });

        resp.EnsureSuccessStatusCode();
    }

    // ---------- Ownership: cada jogador só mexe no próprio aquário ----------

    [Fact]
    public async Task ConfigurarGatilho_NaoAfetaOutroUsuario()
    {
        var (clientA, userA) = await _factory.RegisterAsync("isola_a");
        var (clientB, userB) = await _factory.RegisterAsync("isola_b");
        await SetSoftBalance(userA, 5000m);
        await SetSoftBalance(userB, 5000m);
        (await clientA.PostAsync("/api/items/water_sensor/buy", null)).EnsureSuccessStatusCode();
        (await clientB.PostAsync("/api/items/water_sensor/buy", null)).EnsureSuccessStatusCode();

        (await clientA.PostAsJsonAsync("/api/game/water-sensor/trigger", new { percent = 70m })).EnsureSuccessStatusCode();

        var tankB = await clientB.GetFromJsonAsync<TankDto>("/api/game/tank");
        Assert.Equal(0m, tankB!.AutoCleanTriggerPercent); // B nunca configurou — não foi afetado por A
    }

    // ---------- Limpeza automática no tick ----------

    [Fact]
    public async Task LimpezaAutomatica_VipOnlineAguaAbaixoDeZero_CompraFiltroSozinha()
    {
        var (client, userId) = await _factory.RegisterAsync("limpa1");
        long habitatId = await HabitatIdOf(userId);
        await GiveVip(userId);
        await MarkOnline(habitatId);
        await SetMaintenance(habitatId, 0m); // pior caso: já no fundo, sem sensor (gatilho fixo 0)
        decimal before = await SoftBalanceOf(userId);

        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank");

        Assert.Equal(100m, tank!.MaintenanceLevel);
        Assert.Equal(before - 20m, await SoftBalanceOf(userId)); // preço do filter_basic

        await _factory.WithDbAsync(async db =>
        {
            bool logged = await db.TransactionLogs.AnyAsync(t =>
                t.FromUserId == userId && t.Type == TransactionType.ItemPurchase && t.Amount == 20m);
            Assert.True(logged);
        });
    }

    [Fact]
    public async Task LimpezaAutomatica_ComSensorEGatilhoConfigurado_RespeitaOValorEscolhido()
    {
        var (client, userId) = await _factory.RegisterAsync("limpa2");
        long habitatId = await HabitatIdOf(userId);
        await SetSoftBalance(userId, 5000m);
        (await client.PostAsync("/api/items/water_sensor/buy", null)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/game/water-sensor/trigger", new { percent = 50m })).EnsureSuccessStatusCode();
        await GiveVip(userId);
        await MarkOnline(habitatId);

        // Acima do gatilho (60 > 50): não limpa.
        await SetMaintenance(habitatId, 60m);
        var tankAcima = await client.GetFromJsonAsync<TankDto>("/api/game/tank");
        Assert.True(tankAcima!.MaintenanceLevel < 100m);

        // Abaixo do gatilho (40 <= 50): limpa sozinho.
        await SetMaintenance(habitatId, 40m);
        var tankAbaixo = await client.GetFromJsonAsync<TankDto>("/api/game/tank");
        Assert.Equal(100m, tankAbaixo!.MaintenanceLevel);
    }

    [Fact]
    public async Task LimpezaAutomatica_SemSaldo_NaoCompraENaoFicaNegativo()
    {
        var (client, userId) = await _factory.RegisterAsync("limpa3");
        long habitatId = await HabitatIdOf(userId);
        await GiveVip(userId);
        await MarkOnline(habitatId);
        await SetMaintenance(habitatId, 0m);
        await SetSoftBalance(userId, 5m); // menos que o preço do filtro (20)

        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank");

        Assert.True(tank!.MaintenanceLevel < 100m); // continua sujo — não comprou
        Assert.Equal(5m, await SoftBalanceOf(userId)); // saldo intacto, nunca negativo
    }

    [Fact]
    public async Task LimpezaAutomatica_SemVip_NuncaLimpaSozinhaMesmoComSensorConfigurado()
    {
        var (client, userId) = await _factory.RegisterAsync("limpa4");
        long habitatId = await HabitatIdOf(userId);
        await SetSoftBalance(userId, 5000m);
        (await client.PostAsync("/api/items/water_sensor/buy", null)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/game/water-sensor/trigger", new { percent = 80m })).EnsureSuccessStatusCode();
        await MarkOnline(habitatId); // online, mas SEM VIP
        await SetMaintenance(habitatId, 10m);
        decimal before = await SoftBalanceOf(userId);

        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank");

        Assert.True(tank!.MaintenanceLevel < 100m);
        Assert.Equal(before, await SoftBalanceOf(userId)); // nenhum débito automático sem VIP
    }

    [Fact]
    public async Task LimpezaAutomatica_VipOffline_NaoLimpaAteVoltarOnline()
    {
        var (client, userId) = await _factory.RegisterAsync("limpa5");
        long habitatId = await HabitatIdOf(userId);
        await GiveVip(userId);
        // Sem heartbeat recente = offline.
        await SetMaintenance(habitatId, 0m);

        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank");

        Assert.True(tank!.MaintenanceLevel < 100m);
    }
}
