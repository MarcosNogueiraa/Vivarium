using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Tests;

public class IncomeTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public IncomeTests(VivariumApiFactory factory) => _factory = factory;

    private record TankDto(decimal MaintenanceLevel, Dictionary<string, decimal> Wallet, decimal CoinsPerHour);

    private async Task SetupTank(long userId, decimal rarity, TimeSpan sinceLastTick)
    {
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId);
            habitat.LastHeartbeatAt = DateTime.UtcNow;               // online
            habitat.LastTickAt = DateTime.UtcNow - sinceLastTick;    // janela decorrida
            db.CreatureInstances.Add(new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, HabitatId = habitat.Id,
                Seed = 12345, TraitConfigVersion = 1, RarityScore = rarity,
                CreatedAt = DateTime.UtcNow,
            });
        });
    }

    [Fact]
    public async Task PeixeNoTanque_AcumulaMoedasComOTempo()
    {
        var (client, userId) = await _factory.RegisterAsync("farmer1");
        await SetupTank(userId, rarity: 8m, sinceLastTick: TimeSpan.FromHours(2));

        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank");

        Assert.True(tank!.Wallet["SOFT"] > 100m, "renda deveria ter creditado moedas");
        Assert.True(tank.CoinsPerHour > 0m);
    }

    [Fact]
    public async Task LendarioRendeMuitoMaisQueComum()
    {
        var (c1, u1) = await _factory.RegisterAsync("farmcomum");
        await SetupTank(u1, rarity: 4m, sinceLastTick: TimeSpan.FromHours(1));
        var comum = await c1.GetFromJsonAsync<TankDto>("/api/game/tank");

        var (c2, u2) = await _factory.RegisterAsync("farmlendario");
        await SetupTank(u2, rarity: 12m, sinceLastTick: TimeSpan.FromHours(1));
        var lendario = await c2.GetFromJsonAsync<TankDto>("/api/game/tank");

        Assert.True(lendario!.CoinsPerHour > comum!.CoinsPerHour * 25,
            $"lendário {lendario.CoinsPerHour}/h vs comum {comum.CoinsPerHour}/h");
    }

    [Fact]
    public async Task AguaSuja_ReduzARenda()
    {
        var (client, userId) = await _factory.RegisterAsync("aguasuja");
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId);
            habitat.MaintenanceLevel = 20m; // água ruim
            habitat.LastHeartbeatAt = DateTime.UtcNow;
            db.CreatureInstances.Add(new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, HabitatId = habitat.Id,
                Seed = 999, TraitConfigVersion = 1, RarityScore = 8m,
                CreatedAt = DateTime.UtcNow,
            });
        });

        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank");

        // taxa com água 20 deve ser bem menor que a taxa nominal com água cheia
        decimal nominal = 21m; // ~CoinsPerHour(8) com água 100
        Assert.True(tank!.CoinsPerHour < nominal * 0.6m);
    }
}
