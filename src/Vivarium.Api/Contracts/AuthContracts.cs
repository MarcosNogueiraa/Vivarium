namespace Vivarium.Api.Contracts;

public record RegisterRequest(string Username, string Email, string Password);
public record LoginRequest(string UsernameOrEmail, string Password);
public record AuthResponse(long UserId, string Username, string Token);
public record MeResponse(long UserId, string Username, string Email);

/// <summary>Sempre responde com a mesma mensagem genérica, exista ou não o email — evita
/// que alguém descubra quais emails têm conta (enumeração de usuários).</summary>
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);
public record UpdateEmailRequest(string NewEmail, string CurrentPassword);
public record UpdatePasswordRequest(string CurrentPassword, string NewPassword);
