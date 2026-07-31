using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Vivarium.Api.Tests;

public class DailyRewardTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public DailyRewardTests(VivariumApiFactory factory) => _factory = factory;

    [Fact]
    public async Task UsuarioNovo_PodeResgatar()
    {
        var (client, _) = await _factory.RegisterAsync("diaria1");

        var status = await client.GetFromJsonAsync<StatusDto>("/api/game/daily-reward");

        Assert.True(status!.CanClaim);
        Assert.Equal(25m, status.Amount);
        Assert.Null(status.NextAvailableAtUtc);
    }

    [Fact]
    public async Task Resgatar_CreditaSaldoEDesabilitaNovoResgateNoMesmoDia()
    {
        var (client, _) = await _factory.RegisterAsync("diaria2");

        var claim = await client.PostAsync("/api/game/daily-reward/claim", null);
        claim.EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(125m, tank!.Wallet["SOFT"]); // 100 + 25

        var status = await client.GetFromJsonAsync<StatusDto>("/api/game/daily-reward");
        Assert.False(status!.CanClaim);
        Assert.NotNull(status.NextAvailableAtUtc);
    }

    [Fact]
    public async Task Resgatar_DuasVezesNoMesmoDia_SegundaRetorna400()
    {
        var (client, _) = await _factory.RegisterAsync("diaria3");

        (await client.PostAsync("/api/game/daily-reward/claim", null)).EnsureSuccessStatusCode();
        var second = await client.PostAsync("/api/game/daily-reward/claim", null);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task DiaSeguinte_PodeResgatarDeNovo()
    {
        var (client, userId) = await _factory.RegisterAsync("diaria4");
        (await client.PostAsync("/api/game/daily-reward/claim", null)).EnsureSuccessStatusCode();

        // Simula "ontem": empurra o último resgate um dia pra trás
        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.LastDailyRewardAt = user.LastDailyRewardAt!.Value.AddDays(-1);
        });

        var status = await client.GetFromJsonAsync<StatusDto>("/api/game/daily-reward");
        Assert.True(status!.CanClaim);

        var claim = await client.PostAsync("/api/game/daily-reward/claim", null);
        claim.EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(150m, tank!.Wallet["SOFT"]); // 100 + 25 + 25
    }

    [Fact]
    public async Task SemToken_Retorna401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/game/daily-reward");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public record StatusDto(bool CanClaim, decimal Amount, DateTime? NextAvailableAtUtc);
}
