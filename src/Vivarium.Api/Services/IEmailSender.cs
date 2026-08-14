namespace Vivarium.Api.Services;

/// <summary>Abstração de envio de email — deliberadamente genérica (não sabe nada de "reset de senha"),
/// pra qualquer feature futura que precise mandar email (ex: aviso de venda no mercado) reusar.</summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody);
}
