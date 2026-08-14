using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace Vivarium.Api.Tests;

/// <summary>"Esqueci minha senha" — sempre responde igual (anti-enumeração), token de uso
/// único com expiração, extraído do corpo do email capturado pelo FakeEmailSender.</summary>
public partial class PasswordResetTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public PasswordResetTests(VivariumApiFactory factory) => _factory = factory;

    [GeneratedRegex(@"resetToken=([0-9A-Fa-f]+)")]
    private static partial Regex TokenRegex();

    private static string ExtractToken(string html) => TokenRegex().Match(html).Groups[1].Value;

    [Fact]
    public async Task Esqueci_EmailExistente_MandaEmailComToken()
    {
        await _factory.RegisterAsync("esqueci1");
        var anon = _factory.CreateClient();

        var response = await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = "esqueci1@teste.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(_factory.Emails.Sent, e => e.To == "esqueci1@teste.com");
        Assert.False(string.IsNullOrEmpty(ExtractToken(sent.Html)));
    }

    [Fact]
    public async Task Esqueci_EmailInexistente_RespondeIgualSemMandarEmail()
    {
        var anon = _factory.CreateClient();
        int before = _factory.Emails.Sent.Count;

        var response = await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = "ninguem-tem-esse@teste.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, _factory.Emails.Sent.Count); // nenhum email novo — mas resposta idêntica
    }

    [Fact]
    public async Task Resetar_ComTokenValido_TrocaSenhaEPermiteLogin()
    {
        await _factory.RegisterAsync("resetar1");
        var anon = _factory.CreateClient();
        await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = "resetar1@teste.com" });
        string token = ExtractToken(_factory.Emails.Sent.Last(e => e.To == "resetar1@teste.com").Html);

        var reset = await anon.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "senha-redefinida-789" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var login = await anon.PostAsJsonAsync("/api/auth/login", new { usernameOrEmail = "resetar1", password = "senha-redefinida-789" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Resetar_TokenUsadoDeNovo_Retorna400()
    {
        await _factory.RegisterAsync("resetar2");
        var anon = _factory.CreateClient();
        await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = "resetar2@teste.com" });
        string token = ExtractToken(_factory.Emails.Sent.Last(e => e.To == "resetar2@teste.com").Html);
        await anon.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "senha-redefinida-789" });

        var second = await anon.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "outra-senha-000" });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Resetar_TokenInvalido_Retorna400()
    {
        var anon = _factory.CreateClient();

        var response = await anon.PostAsJsonAsync("/api/auth/reset-password", new { token = "nao-existe-esse-token", newPassword = "senha-forte-123" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Resetar_TokenExpirado_Retorna400()
    {
        var (_, userId) = await _factory.RegisterAsync("resetar3");
        var anon = _factory.CreateClient();
        await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = "resetar3@teste.com" });
        string token = ExtractToken(_factory.Emails.Sent.Last(e => e.To == "resetar3@teste.com").Html);

        // Empurra ExpiresAt pro passado direto no banco — não dá pra esperar 1h de verdade no teste.
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.PasswordResetTokens.FirstAsync(t => t.UserId == userId);
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        });

        var response = await anon.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "senha-forte-123" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Esqueci_PedidoDeNovo_InvalidaLinkAnterior()
    {
        await _factory.RegisterAsync("resetar4");
        var anon = _factory.CreateClient();
        await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = "resetar4@teste.com" });
        string firstToken = ExtractToken(_factory.Emails.Sent.Last(e => e.To == "resetar4@teste.com").Html);

        await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = "resetar4@teste.com" });

        var response = await anon.PostAsJsonAsync("/api/auth/reset-password", new { token = firstToken, newPassword = "senha-forte-123" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
