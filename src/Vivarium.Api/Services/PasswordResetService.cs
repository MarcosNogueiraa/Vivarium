using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Api.Validation;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;

namespace Vivarium.Api.Services;

/// <summary>
/// "Esqueci minha senha" (14/08/2026) — token aleatório de alta entropia (32 bytes), só o
/// hash SHA256 fica no banco (PasswordResetToken), o valor bruto só existe no link do email
/// e nunca é persistido. Expira em 1h; pedir de novo invalida qualquer link anterior ainda
/// válido (só 1 ativo por vez).
/// </summary>
public class PasswordResetService(VivariumDbContext db, IEmailSender emailSender, IConfiguration config)
{
    private const int TokenBytes = 32;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    /// <summary>Nunca revela se o email existe — quem chama sempre trata como sucesso,
    /// exista ou não a conta (anti-enumeração de usuários). Dois freios silenciosos
    /// (BACKLOG.md #5, cota diária do Resend free tier): teto GLOBAL de envios do dia e
    /// intervalo mínimo por conta — nenhum dos dois muda a resposta, só não manda o email.</summary>
    public async Task RequestAsync(string email, DateTime now)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null) return;

        int sentToday = await db.PasswordResetTokens.CountAsync(t => t.CreatedAt >= now.Date);
        if (sentToday >= SecurityConfig.ForgotPasswordDailyGlobalCap) return;

        var lastRequestedAt = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => (DateTime?)t.CreatedAt)
            .FirstOrDefaultAsync();
        if (lastRequestedAt is { } last && now - last < TimeSpan.FromMinutes(SecurityConfig.ForgotPasswordMinIntervalMinutes))
            return;

        var stillValid = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null && t.ExpiresAt > now)
            .ToListAsync();
        foreach (var old in stillValid) old.UsedAt = now;

        string rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenBytes));
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id, TokenHash = Hash(rawToken), ExpiresAt = now.Add(TokenLifetime), CreatedAt = now,
        });
        await db.SaveChangesAsync();

        string frontendUrl = config["Frontend:BaseUrl"] ?? "https://vivarium.marcospdnnogueira.workers.dev";
        string link = $"{frontendUrl}/?resetToken={rawToken}";
        string html = $"""
            <p>Alguém (esperamos que você) pediu pra redefinir a senha da sua conta no Vivarium.</p>
            <p><a href="{link}">Clique aqui pra escolher uma senha nova</a> — esse link expira em 1 hora.</p>
            <p>Se não foi você, pode ignorar este email — sua senha continua a mesma.</p>
            """;
        await emailSender.SendAsync(user.Email, "Redefinir sua senha — Vivarium", html);
    }

    public async Task<ServiceResult> ResetAsync(string rawToken, string newPassword, DateTime now)
    {
        var invalid = AccountValidation.Password(newPassword);
        if (invalid is not null)
            return ServiceResult.Bad(invalid);

        var token = await db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == Hash(rawToken));
        if (token is null || token.UsedAt is not null || token.ExpiresAt <= now)
            return ServiceResult.Bad("Link inválido ou expirado — peça uma nova redefinição de senha.");

        token.User!.PasswordHash = PasswordHasher.Hash(newPassword);
        token.UsedAt = now;
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
