using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Api.Validation;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;

namespace Vivarium.Api.Services;

/// <summary>Perfil do jogador (14/08/2026) — trocar email/senha, sempre exigindo a senha
/// atual (mesmo padrão de qualquer ação sensível já usado no jogo, ex: transferência).
/// Também dono do avatar/progressão (18/08/2026, BACKLOG.md #7).</summary>
public class AccountService(VivariumDbContext db)
{
    /// <summary>Monta o MeResponse completo (nível derivado ao vivo + avatar) — reusado pelo
    /// endpoint /me e por toda ação de conta que devolve o perfil atualizado.</summary>
    public async Task<MeResponse> BuildMeResponseAsync(User user)
    {
        var (level, currentLevelXp, xpForNextLevel, progress) = LevelCalculator.ProgressOf(user.Xp, LevelConfig.Default);

        CreatureDto? avatar = null;
        if (user.AvatarCreatureInstanceId is { } avatarId)
        {
            var creature = await db.CreatureInstances.FirstOrDefaultAsync(c => c.Id == avatarId);
            if (creature is not null)
                avatar = CreatureDto.From(creature);
        }

        return new MeResponse(user.Id, user.Username, user.Email, user.Xp, level, currentLevelXp, xpForNextLevel, progress, avatar);
    }

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
        return ServiceResult.Success(await BuildMeResponseAsync(user));
    }

    /// <summary>Avatar = peixe escolhido pelo jogador (18/08/2026, BACKLOG.md #7). Escolha manual,
    /// não upload nem auto-atualização — só exige posse (não precisa estar no tanque).</summary>
    public async Task<ServiceResult> SetAvatarAsync(long userId, long? creatureInstanceId)
    {
        var user = await db.Users.FirstAsync(u => u.Id == userId);

        if (creatureInstanceId is null)
        {
            user.AvatarCreatureInstanceId = null;
        }
        else
        {
            bool owns = await db.CreatureInstances.AnyAsync(c => c.Id == creatureInstanceId && c.OwnerId == userId);
            if (!owns)
                return ServiceResult.Bad("Esse peixe não é seu");
            user.AvatarCreatureInstanceId = creatureInstanceId;
        }

        await db.SaveChangesAsync();
        return ServiceResult.Success(await BuildMeResponseAsync(user));
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
