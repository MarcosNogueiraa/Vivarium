using System.Net;
using System.Net.Http.Json;

namespace Vivarium.Api.Tests;

public class DevTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public DevTests(VivariumApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Spawn_CriaItemProntoQuePodeSerColetado()
    {
        var (client, _) = await _factory.RegisterAsync("devspawn1");

        (await client.PostAsync("/api/dev/spawn", null)).EnsureSuccessStatusCode();

        // Fila já tinha o peixe inicial do registro — spawn soma mais 1.
        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(2, tank!.Queue.Count);
        var item = tank.Queue[^1];
        Assert.True(item.IsReady);
        Assert.False(item.IsSick);

        (await client.PostAsync($"/api/game/collect/{item.Id}", null)).EnsureSuccessStatusCode();
        tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Single(tank!.Creatures); // só coletamos o spawnado; o inicial ainda está na fila
        Assert.Single(tank.Queue);
    }

    [Fact]
    public async Task Spawn_FilaCheia_Retorna400()
    {
        var (client, _) = await _factory.RegisterAsync("devspawn2");

        // Fila já começa com 1 (peixe inicial do registro) — só precisa de +4 pra bater no cap de 5.
        for (int i = 0; i < 4; i++)
            (await client.PostAsync("/api/dev/spawn", null)).EnsureSuccessStatusCode();

        var sixth = await client.PostAsync("/api/dev/spawn", null);
        Assert.Equal(HttpStatusCode.BadRequest, sixth.StatusCode);
    }

    [Fact]
    public async Task Coins_CreditaFichasNoJogador()
    {
        var (client, _) = await _factory.RegisterAsync("devcoins1");

        var response = await client.PostAsync("/api/dev/coins?amount=2500", null);
        response.EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(2600m, tank!.Wallet["SOFT"]); // 100 inicial + 2500
    }

    [Fact]
    public async Task Clear_RemoveTodosOsPeixesDoTanque()
    {
        var (client, _) = await _factory.RegisterAsync("devclear1");

        // gera e coleta 2 peixes
        for (int i = 0; i < 2; i++)
        {
            (await client.PostAsync("/api/dev/spawn", null)).EnsureSuccessStatusCode();
            var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
            var ready = tank!.Queue.First(q => q.IsReady);
            (await client.PostAsync($"/api/game/collect/{ready.Id}", null)).EnsureSuccessStatusCode();
        }

        var before = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(2, before!.Creatures.Count);

        var response = await client.PostAsync("/api/dev/clear", null);
        response.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Empty(after!.Creatures);
    }
}
