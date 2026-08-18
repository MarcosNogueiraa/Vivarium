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
        var (_, userId) = await _factory.RegisterAsync("resetar4");
        var anon = _factory.CreateClient();
        await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = "resetar4@teste.com" });
        string firstToken = ExtractToken(_factory.Emails.Sent.Last(e => e.To == "resetar4@teste.com").Html);

        // Simula o intervalo mínimo (BACKLOG.md #5) já ter passado — sem isso o 2º pedido,
        // feito na hora, seria silenciosamente ignorado pelo freio anti-cota.
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.PasswordResetTokens.FirstAsync(t => t.UserId == userId);
            row.CreatedAt = DateTime.UtcNow.AddMinutes(-10);
        });

        await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = "resetar4@teste.com" });

        var response = await anon.PostAsJsonAsync("/api/auth/reset-password", new { token = firstToken, newPassword = "senha-forte-123" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Esqueci_PedidoRepetidoRapido_NaoMandaEmailNovoAindaAssimRespondeIgual()
    {
        await _factory.RegisterAsync("intervalomin");
        var anon = _factory.CreateClient();
        await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = "intervalomin@teste.com" });
        int afterFirst = _factory.Emails.Sent.Count;

        var second = await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = "intervalomin@teste.com" });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(afterFirst, _factory.Emails.Sent.Count); // segundo pedido rápido demais — nenhum email novo
    }

    [Fact]
    public async Task Esqueci_TetoDiarioGlobalAtingido_BloqueiaOutraContaSemMandarEmail()
    {
        var (_, enchedorId) = await _factory.RegisterAsync("enchedorteto");
        await _factory.RegisterAsync("tetoglobal");
        var anon = _factory.CreateClient();

        // Enche a cota GLOBAL do dia com tokens de outra conta — prova que o teto é do
        // sistema inteiro, não por usuário (o freio de intervalo mínimo já cobre isso).
        await _factory.WithDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            for (int i = 0; i < Vivarium.Core.Gameplay.SecurityConfig.ForgotPasswordDailyGlobalCap; i++)
            {
                db.PasswordResetTokens.Add(new Vivarium.Core.Domain.PasswordResetToken
                {
                    UserId = enchedorId, TokenHash = $"fake-cap-{i}", ExpiresAt = now.AddHours(1), CreatedAt = now,
                });
            }
        });

        int before = _factory.Emails.Sent.Count;
        var response = await anon.PostAsJsonAsync("/api/auth/forgot-password", new { email = "tetoglobal@teste.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // resposta continua igual (anti-enumeração)
        Assert.Equal(before, _factory.Emails.Sent.Count); // mas nenhum email novo saiu
    }
}
