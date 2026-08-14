using Vivarium.Api.Services;

namespace Vivarium.Api.Tests;

/// <summary>Substitui IEmailSender nos testes (VivariumApiFactory) — captura em memória
/// em vez de chamar o Resend de verdade, pra testes conseguirem extrair o token bruto
/// do link (que só existe no corpo do email, nunca no banco).</summary>
public class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Html)> Sent { get; } = [];

    public Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        Sent.Add((toEmail, subject, htmlBody));
        return Task.CompletedTask;
    }
}
