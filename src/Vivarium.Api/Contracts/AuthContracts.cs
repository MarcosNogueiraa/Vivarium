namespace Vivarium.Api.Contracts;

public record RegisterRequest(string Username, string Email, string Password);
public record LoginRequest(string UsernameOrEmail, string Password);
public record AuthResponse(long UserId, string Username, string Token);

/// <summary>
/// Xp/Level/CurrentLevelXp/XpForNextLevel/Progress01 (18/08/2026, BACKLOG.md #7): progressão
/// do jogador, só social/cosmético — sempre derivada ao vivo de <c>User.Xp</c> via
/// <c>LevelCalculator.ProgressOf</c>, nunca armazenada. Avatar é o peixe escolhido manualmente
/// pelo jogador (null = sem avatar, usa ícone padrão).
/// </summary>
public record MeResponse(
    long UserId, string Username, string Email,
    long Xp, int Level, long CurrentLevelXp, long XpForNextLevel, double Progress01,
    CreatureDto? Avatar);

public record UpdateAvatarRequest(long? CreatureInstanceId);

/// <summary>Sempre responde com a mesma mensagem genérica, exista ou não o email — evita
/// que alguém descubra quais emails têm conta (enumeração de usuários).</summary>
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);
public record UpdateEmailRequest(string NewEmail, string CurrentPassword);
public record UpdatePasswordRequest(string CurrentPassword, string NewPassword);
