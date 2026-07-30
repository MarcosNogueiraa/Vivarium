using System.Net.Mail;

namespace Vivarium.Api.Validation;

/// <summary>Regras de validação de conta (registro). Retorna a mensagem de erro, ou null se ok.</summary>
public static class AccountValidation
{
    public static string? Username(string? username) =>
        string.IsNullOrWhiteSpace(username) || username.Length is < 3 or > 32
        || !username.All(c => char.IsLetterOrDigit(c) || c is '_' or '-')
            ? "Username: 3–32 caracteres, só letras, números, _ ou -"
            : null;

    public static string? Email(string? email) =>
        string.IsNullOrWhiteSpace(email) || email.Length > 256 || !MailAddress.TryCreate(email, out _)
            ? "Email inválido"
            : null;

    public static string? Password(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < 8
            ? "Senha deve ter pelo menos 8 caracteres"
            : null;
}
