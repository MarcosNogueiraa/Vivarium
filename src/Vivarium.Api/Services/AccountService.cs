using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Api.Validation;

namespace Vivarium.Api.Services;

/// <summary>Perfil do jogador (14/08/2026) — trocar email/senha, sempre exigindo a senha
/// atual (mesmo padrão de qualquer ação sensível já usado no jogo, ex: transferência).</summary>
public class AccountService(VivariumDbContext db)
{
    public async Task<ServiceResult> UpdateEmailAsync(long userId, string newEmail, string currentPassword)
    {
        var invalid = AccountValidation.Email(newEmail);
        if (invalid is not null)
            return ServiceResult.Bad(invalid);

        var user = await db.Users.FirstAsync(u => u.Id == userId);
        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
            return ServiceResult.Bad("Senha atual incorreta");
        if (newEmail == user.Email)
            return ServiceResult.Bad("O novo email é igual ao atual");
        if (await db.Users.AnyAsync(u => u.Email == newEmail && u.Id != userId))
            return ServiceResult.Conflict("Esse email já está cadastrado em outra conta");

        user.Email = newEmail;
        await db.SaveChangesAsync();
        return ServiceResult.Success(new MeResponse(user.Id, user.Username, user.Email));
    }

    public async Task<ServiceResult> UpdatePasswordAsync(long userId, string currentPassword, string newPassword)
    {
        var invalid = AccountValidation.Password(newPassword);
        if (invalid is not null)
            return ServiceResult.Bad(invalid);

        var user = await db.Users.FirstAsync(u => u.Id == userId);
        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
            return ServiceResult.Bad("Senha atual incorreta");
        if (currentPassword == newPassword)
            return ServiceResult.Bad("A nova senha precisa ser diferente da atual");

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync();
        return ServiceResult.Success();
    }
}
