using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

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

    [Fact]
    public async Task Login_CincoSenhasErradasSeguidas_TravaAContaMesmoComSenhaCorreta()
    {
        var (_, userId) = await _factory.RegisterAsync("travalogin");
        var client = _factory.CreateClient();

        for (int i = 0; i < Vivarium.Core.Gameplay.SecurityConfig.LoginMaxFailedAttempts; i++)
        {
            var fail = await client.PostAsJsonAsync("/api/auth/login", new
            {
                usernameOrEmail = "travalogin", password = "senha-incorreta-x",
            });
            Assert.Equal(HttpStatusCode.Unauthorized, fail.StatusCode);
        }

        // A conta travou — nem com a senha CERTA entra agora, e a mensagem é idêntica à de
        // senha errada (não pode vazar que a conta existe/está travada).
        var withCorrectPassword = await client.PostAsJsonAsync("/api/auth/login", new
        {
            usernameOrEmail = "travalogin", password = "senha-forte-123",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, withCorrectPassword.StatusCode);

        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            Assert.NotNull(user.LockedUntil);
            Assert.Equal(0, user.FailedLoginCount); // zera junto com o lockout
        });
    }

    [Fact]
    public async Task Login_ContaTravada_DestravaSozinhaDepoisDoLockoutExpirar()
    {
        var (_, userId) = await _factory.RegisterAsync("destravalogin");
        var client = _factory.CreateClient();

        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            user.LockedUntil = DateTime.UtcNow.AddMinutes(-1); // já expirado
        });

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            usernameOrEmail = "destravalogin", password = "senha-forte-123",
        });
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Login_SucessoDepoisDeFalhas_ZeraOContador()
    {
        var (_, userId) = await _factory.RegisterAsync("zeracontador");
        var client = _factory.CreateClient();

        await client.PostAsJsonAsync("/api/auth/login", new { usernameOrEmail = "zeracontador", password = "errada-1" });
        await client.PostAsJsonAsync("/api/auth/login", new { usernameOrEmail = "zeracontador", password = "errada-2" });

        var ok = await client.PostAsJsonAsync("/api/auth/login", new
        {
            usernameOrEmail = "zeracontador", password = "senha-forte-123",
        });
        ok.EnsureSuccessStatusCode();

        await _factory.WithDbAsync(async db =>
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            Assert.Equal(0, user.FailedLoginCount);
        });
    }

    public record TankDto(
        bool Online, decimal MaintenanceLevel, int Capacity, int QueueCap,
        List<QueueItemDto> Queue, List<CreatureDto> Creatures, Dictionary<string, decimal> Wallet);
    public record QueueItemDto(long Id, DateTime ReadyAt, bool IsReady, bool IsSick);
    public record CreatureDto(long Id, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore, DateTime CreatedAt);
}
