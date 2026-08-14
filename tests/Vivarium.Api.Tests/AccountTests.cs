using System.Net;
using System.Net.Http.Json;

namespace Vivarium.Api.Tests;

/// <summary>Trocar email/senha do próprio perfil — sempre exige a senha atual.</summary>
public class AccountTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public AccountTests(VivariumApiFactory factory) => _factory = factory;

    [Fact]
    public async Task TrocarEmail_ComSenhaCorreta_Funciona()
    {
        var (client, _) = await _factory.RegisterAsync("trocaemail1");

        var response = await client.PutAsJsonAsync("/api/account/email", new
        {
            newEmail = "novo-email@teste.com", currentPassword = "senha-forte-123",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.Equal("novo-email@teste.com", me!.Email);
    }

    [Fact]
    public async Task TrocarEmail_SenhaErrada_Retorna400ENaoMuda()
    {
        var (client, _) = await _factory.RegisterAsync("trocaemail2");

        var response = await client.PutAsJsonAsync("/api/account/email", new
        {
            newEmail = "outro@teste.com", currentPassword = "senha-errada-xyz",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var me = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.Equal("trocaemail2@teste.com", me!.Email);
    }

    [Fact]
    public async Task TrocarEmail_JaUsadoPorOutraConta_Retorna409()
    {
        await _factory.RegisterAsync("dono-do-email");
        var (client, _) = await _factory.RegisterAsync("trocaemail3");

        var response = await client.PutAsJsonAsync("/api/account/email", new
        {
            newEmail = "dono-do-email@teste.com", currentPassword = "senha-forte-123",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task TrocarEmail_InvalidoFormato_Retorna400()
    {
        var (client, _) = await _factory.RegisterAsync("trocaemail4");

        var response = await client.PutAsJsonAsync("/api/account/email", new
        {
            newEmail = "nao-e-email", currentPassword = "senha-forte-123",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TrocarSenha_ComSenhaAtualCorreta_PermiteLoginComANova()
    {
        var (client, _) = await _factory.RegisterAsync("trocasenha1");

        var response = await client.PutAsJsonAsync("/api/account/password", new
        {
            currentPassword = "senha-forte-123", newPassword = "senha-nova-456",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var anon = _factory.CreateClient();
        var login = await anon.PostAsJsonAsync("/api/auth/login", new
        {
            usernameOrEmail = "trocasenha1", password = "senha-nova-456",
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task TrocarSenha_SenhaAtualErrada_Retorna400()
    {
        var (client, _) = await _factory.RegisterAsync("trocasenha2");

        var response = await client.PutAsJsonAsync("/api/account/password", new
        {
            currentPassword = "senha-errada-xyz", newPassword = "senha-nova-456",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TrocarSenha_NovaSenhaCurta_Retorna400()
    {
        var (client, _) = await _factory.RegisterAsync("trocasenha3");

        var response = await client.PutAsJsonAsync("/api/account/password", new
        {
            currentPassword = "senha-forte-123", newPassword = "123",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TrocarEmailESenha_SemToken_Retorna401()
    {
        var anon = _factory.CreateClient();

        var r1 = await anon.PutAsJsonAsync("/api/account/email", new { newEmail = "x@teste.com", currentPassword = "y" });
        var r2 = await anon.PutAsJsonAsync("/api/account/password", new { currentPassword = "x", newPassword = "senha-forte-123" });

        Assert.Equal(HttpStatusCode.Unauthorized, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);
    }

    private record MeDto(long UserId, string Username, string Email);
}
