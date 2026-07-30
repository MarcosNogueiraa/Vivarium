namespace Vivarium.Api.Contracts;

public record RegisterRequest(string Username, string Email, string Password);
public record LoginRequest(string UsernameOrEmail, string Password);
public record AuthResponse(long UserId, string Username, string Token);
