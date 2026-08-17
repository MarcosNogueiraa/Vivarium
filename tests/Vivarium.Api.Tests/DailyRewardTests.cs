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
        // Tanque vazio → base = piso (25); roleta ±40% → faixa [15,35].
        Assert.Equal(15m, status.MinAmount);
        Assert.Equal(35m, status.MaxAmount);
        Assert.Equal(1, status.CurrentStreak);
        Assert.Equal(0, status.StreakBonusPercent);
        Assert.Equal(3, status.EggChancePercent);
        Assert.Null(status.NextAvailableAtUtc);
    }

    [Fact]
    public async Task Resgatar_CreditaSaldoDentroDaFaixaEDesabilitaNovoResgateNoMesmoDia()
    {
        var (client, _) = await _factory.RegisterAsync("diaria2");

        var claim = await client.PostAsync("/api/game/daily-reward/claim", null);
        claim.EnsureSuccessStatusCode();
        var claimed = await claim.Content.ReadFromJsonAsync<ClaimDto>();

        Assert.InRange(claimed!.Amount, 15m, 35m);
        Assert.Equal(1, claimed.Streak);

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(100m + claimed.Amount, tank!.Wallet["SOFT"]);

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
    public async Task DiaSeguinte_StreakSobeComBonusEACreditaOValorCerto()
    {
        var (client, userId) = await _factory.RegisterAsync("diaria4");
        var first = await client.PostAsync("/api/game/daily-reward/claim", null);
        first.EnsureSuccessStatusCode();
        var firstClaimed = await first.Content.ReadFromJsonAsync<ClaimDto>();

        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.LastDailyRewardAt = user.LastDailyRewardAt!.Value.AddDays(-1);
        });

        var status = await client.GetFromJsonAsync<StatusDto>("/api/game/daily-reward");
        Assert.True(status!.CanClaim);
        Assert.Equal(2, status.CurrentStreak);
        Assert.Equal(5.0, status.StreakBonusPercent, 3);

        var second = await client.PostAsync("/api/game/daily-reward/claim", null);
        second.EnsureSuccessStatusCode();
        var secondClaimed = await second.Content.ReadFromJsonAsync<ClaimDto>();
        Assert.Equal(2, secondClaimed!.Streak);

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(100m + firstClaimed!.Amount + secondClaimed.Amount, tank!.Wallet["SOFT"]);
    }

    [Fact]
    public async Task LacunaDeDoisDias_QuebraOStreak_VoltaParaUm()
    {
        var (client, userId) = await _factory.RegisterAsync("diaria5");
        (await client.PostAsync("/api/game/daily-reward/claim", null)).EnsureSuccessStatusCode();

        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.LastDailyRewardAt = user.LastDailyRewardAt!.Value.AddDays(-2);
        });

        var status = await client.GetFromJsonAsync<StatusDto>("/api/game/daily-reward");
        Assert.True(status!.CanClaim);
        Assert.Equal(1, status.CurrentStreak);
        Assert.Equal(0, status.StreakBonusPercent);

        var claim = await client.PostAsync("/api/game/daily-reward/claim", null);
        claim.EnsureSuccessStatusCode();
        var claimed = await claim.Content.ReadFromJsonAsync<ClaimDto>();
        Assert.Equal(1, claimed!.Streak);
    }

    [Fact]
    public async Task ChanceDeOvo_EventualmenteConcedeUmOvoNaCaixaDeEntrada()
    {
        var (client, userId) = await _factory.RegisterAsync("diaria6");

        ClaimDto? lastClaimed = null;
        for (int i = 0; i < 200 && lastClaimed?.GotEgg != true; i++)
        {
            if (i > 0)
            {
                await _factory.WithDbAsync(async db =>
                {
                    var user = await db.Users.FirstAsync(u => u.Id == userId);
                    user.LastDailyRewardAt = user.LastDailyRewardAt!.Value.AddDays(-1);
                });
            }

            var claim = await client.PostAsync("/api/game/daily-reward/claim", null);
            claim.EnsureSuccessStatusCode();
            lastClaimed = await claim.Content.ReadFromJsonAsync<ClaimDto>();
        }

        Assert.True(lastClaimed?.GotEgg, "200 resgates com 3% de chance cada — nenhum ovo saiu, algo está errado na chamada.");
        Assert.Equal("egg_rare", lastClaimed!.EggItemKey);

        var inbox = await client.GetFromJsonAsync<InboxListRow>("/api/inbox/");
        Assert.Contains(inbox!.Entries, e => e.Kind == "DailyRewardEgg");
    }

    private record InboxEntryRow(long Id, string Kind);
    private record InboxListRow(List<InboxEntryRow> Entries);

    [Fact]
    public async Task SemToken_Retorna401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/game/daily-reward");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public record StatusDto(bool CanClaim, decimal MinAmount, decimal MaxAmount, int CurrentStreak,
        double StreakBonusPercent, double EggChancePercent, DateTime? NextAvailableAtUtc);

    public record ClaimDto(decimal Amount, decimal Wallet, int Streak, bool GotEgg, string? EggItemKey);
}
