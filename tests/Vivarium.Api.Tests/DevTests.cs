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

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        var item = Assert.Single(tank!.Queue);
        Assert.True(item.IsReady);
        Assert.False(item.IsSick);

        (await client.PostAsync($"/api/game/collect/{item.Id}", null)).EnsureSuccessStatusCode();
        tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Single(tank!.Creatures);
    }

    [Fact]
    public async Task Spawn_FilaCheia_Retorna400()
    {
        var (client, _) = await _factory.RegisterAsync("devspawn2");

        for (int i = 0; i < 5; i++)
            (await client.PostAsync("/api/dev/spawn", null)).EnsureSuccessStatusCode();

        var sixth = await client.PostAsync("/api/dev/spawn", null);
        Assert.Equal(HttpStatusCode.BadRequest, sixth.StatusCode);
    }
}
