using System.Net;
using System.Net.Http.Json;

namespace Vivarium.Api.Tests;

public class AuthTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public AuthTests(VivariumApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Registro_CriaTanqueECarteiraIniciais()
    {
        var (client, _) = await _factory.RegisterAsync("jogador1");

        var tank = await client.GetFromJsonAsync<TankDto>("/api/game/tank");

        Assert.NotNull(tank);
        Assert.Equal(3, tank.Capacity);
        Assert.Equal(5, tank.QueueCap);
        Assert.Equal(100m, tank.Wallet["SOFT"]);
        Assert.Equal(0m, tank.Wallet["PREMIUM"]);
        Assert.Empty(tank.Creatures);
        Assert.False(tank.Online); // ainda sem heartbeat

        // Peixe inicial já pronto pra coletar, sem esperar o primeiro ciclo de geração.
        Assert.Single(tank.Queue);
        Assert.True(tank.Queue[0].IsReady);
        Assert.False(tank.Queue[0].IsSick);
    }

    [Fact]
    public async Task Registro_UsernameDuplicado_Retorna409()
    {
        await _factory.RegisterAsync("duplicado");

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            username = "duplicado", email = "outro@teste.com", password = "senha-forte-123",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Registro_SenhaCurta_Retorna400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            username = "senhacurta", email = "sc@teste.com", password = "123",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ComEmailESenhaCorretos_RetornaToken()
    {
        await _factory.RegisterAsync("loginok");

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            usernameOrEmail = "loginok@teste.com", password = "senha-forte-123",
        });

        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<VivariumApiFactory.AuthDto>();
        Assert.False(string.IsNullOrEmpty(auth!.Token));
    }

    [Fact]
    public async Task Login_SenhaErrada_Retorna401()
    {
        await _factory.RegisterAsync("loginerrado");

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            usernameOrEmail = "loginerrado", password = "senha-incorreta-x",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tanque_SemToken_Retorna401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/game/tank");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public record TankDto(
        bool Online, decimal MaintenanceLevel, int Capacity, int QueueCap,
        List<QueueItemDto> Queue, List<CreatureDto> Creatures, Dictionary<string, decimal> Wallet);
    public record QueueItemDto(long Id, DateTime ReadyAt, bool IsReady, bool IsSick);
    public record CreatureDto(long Id, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore, DateTime CreatedAt);
}
