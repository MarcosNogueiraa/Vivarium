using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Vivarium.Api.Services;

/// <summary>
/// Envio via API HTTP do Resend (https://resend.com) — escolhido em vez de SMTP porque a VM Oracle
/// só libera saída em 22/80/443 hoje (deploy/README.md); uma chamada HTTPS não esbarra nisso.
/// Registrado só quando `Resend:ApiKey` está configurada (Program.cs); sem a chave, `NullEmailSender`
/// entra no lugar — o app continua funcionando sem email, mesmo gap já documentado pro processador
/// de pagamento (CLAUDE.md §8.11).
/// </summary>
public class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _http;
    private readonly string _from;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(HttpClient http, IConfiguration config, ILogger<ResendEmailSender> logger)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://api.resend.com/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config["Resend:ApiKey"]);
        // Sem domínio próprio verificado no Resend ainda (CLAUDE.md §11 — domínio próprio é gap
        // conhecido), o remetente de teste só entrega pro email da conta Resend, não pra qualquer
        // jogador. Configurável via `Resend:FromAddress` assim que um domínio for verificado.
        _from = config["Resend:FromAddress"] ?? "Vivarium <onboarding@resend.dev>";
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        var payload = new { from = _from, to = new[] { toEmail }, subject, html = htmlBody };
        var response = await _http.PostAsJsonAsync("emails", payload);
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Resend falhou ao enviar email pra {ToEmail} ({Status}): {Body}", toEmail, response.StatusCode, body);
        }
    }
}

/// <summary>Sem `Resend:ApiKey` configurada (dev local sem user-secrets, ou antes de decidir um
/// provedor) — só loga o conteúdo em vez de falhar. Nunca lança: pedir redefinição de senha sem
/// email configurado não deveria quebrar a request pro usuário.</summary>
public class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody)
    {
        logger.LogWarning(
            "Resend:ApiKey não configurada — email NÃO enviado de verdade. Pra: {ToEmail} | Assunto: {Subject}\n{Body}",
            toEmail, subject, htmlBody);
        return Task.CompletedTask;
    }
}
