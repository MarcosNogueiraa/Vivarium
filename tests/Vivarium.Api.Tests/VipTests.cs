using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Vivarium.Api.Tests;

/// <summary>VIP: pacotes de dias pagos em premium, sem renovação automática (11/08/2026).</summary>
public class VipTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public VipTests(VivariumApiFactory factory) => _factory = factory;

    private record VipStatusDto(bool Active, DateTime? EndAt, Dictionary<string, decimal> Packages);
    private record TankDto(bool IsVip, DateTime? VipEndAt);

    private async Task GiveCurrency(long userId, string code, decimal amount)
    {
        await _factory.WithDbAsync(async db =>
        {
            int currencyId = await db.CurrencyTypes.Where(c => c.Code == code).Select(c => c.Id).FirstAsync();
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == currencyId);
            wallet.Amount += amount;
        });
    }

    [Fact]
    public async Task Status_SemAssinatura_MostraPacotesENaoAtivo()
    {
        var (client, _) = await _factory.RegisterAsync("vip1");

        var status = await client.GetFromJsonAsync<VipStatusDto>("/api/vip");

        Assert.False(status!.Active);
        Assert.Null(status.EndAt);
        Assert.Equal(3, status.Packages.Count);
        Assert.Equal(7m, status.Packages["7"]);
        Assert.Equal(10m, status.Packages["15"]);
        Assert.Equal(15m, status.Packages["30"]);
    }

    [Fact]
    public async Task Subscribe_SemSaldo_Retorna400()
    {
        var (client, _) = await _factory.RegisterAsync("vip2");

        var resp = await client.PostAsJsonAsync("/api/vip/subscribe", new { days = 7 });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Subscribe_PacoteInvalido_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("vip3");
        await GiveCurrency(userId, "PREMIUM", 100m);

        var resp = await client.PostAsJsonAsync("/api/vip/subscribe", new { days = 3 });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Subscribe_ComSaldo_AtivaEDebitaOPremiumCerto()
    {
        var (client, userId) = await _factory.RegisterAsync("vip4");
        await GiveCurrency(userId, "PREMIUM", 100m);

        var resp = await client.PostAsJsonAsync("/api/vip/subscribe", new { days = 15 });
        resp.EnsureSuccessStatusCode();

        var status = await client.GetFromJsonAsync<VipStatusDto>("/api/vip");
        Assert.True(status!.Active);
        Assert.InRange(status.EndAt!.Value, DateTime.UtcNow.AddDays(15).AddMinutes(-1), DateTime.UtcNow.AddDays(15).AddMinutes(1));

        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank");
        Assert.True(tank!.IsVip);
        Assert.NotNull(tank.VipEndAt);

        await _factory.WithDbAsync(async db =>
        {
            int premiumId = await db.CurrencyTypes.Where(c => c.Code == "PREMIUM").Select(c => c.Id).FirstAsync();
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == premiumId);
            Assert.Equal(90m, wallet.Amount); // 100 - 10 (pacote de 15 dias)
        });
    }

    [Fact]
    public async Task Subscribe_ComAssinaturaJaAtiva_EstendeEmVezDeSobrescrever()
    {
        var (client, userId) = await _factory.RegisterAsync("vip5");
        await GiveCurrency(userId, "PREMIUM", 100m);

        (await client.PostAsJsonAsync("/api/vip/subscribe", new { days = 7 })).EnsureSuccessStatusCode();
        var first = await client.GetFromJsonAsync<VipStatusDto>("/api/vip");

        (await client.PostAsJsonAsync("/api/vip/subscribe", new { days = 7 })).EnsureSuccessStatusCode();
        var second = await client.GetFromJsonAsync<VipStatusDto>("/api/vip");

        Assert.True(second!.EndAt > first!.EndAt);
        Assert.InRange((second.EndAt!.Value - first.EndAt!.Value).TotalDays, 6.9, 7.1);
    }
}
