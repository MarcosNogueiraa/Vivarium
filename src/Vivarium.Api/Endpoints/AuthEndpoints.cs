using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Data;
using Vivarium.Api.Services;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;

namespace Vivarium.Api.Endpoints;

public static class AuthEndpoints
{
    public record RegisterRequest(string Username, string Email, string Password);
    public record LoginRequest(string UsernameOrEmail, string Password);
    public record AuthResponse(long UserId, string Username, string Token);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/register", async (RegisterRequest req, VivariumDbContext db, TokenService tokens) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length is < 3 or > 32)
                return Results.BadRequest(new { error = "Username deve ter entre 3 e 32 caracteres" });
            if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
                return Results.BadRequest(new { error = "Email inválido" });
            if (string.IsNullOrEmpty(req.Password) || req.Password.Length < 8)
                return Results.BadRequest(new { error = "Senha deve ter pelo menos 8 caracteres" });

            if (await db.Users.AnyAsync(u => u.Username == req.Username))
                return Results.Conflict(new { error = "Username já em uso" });
            if (await db.Users.AnyAsync(u => u.Email == req.Email))
                return Results.Conflict(new { error = "Email já cadastrado" });

            var now = DateTime.UtcNow;
            var user = new User
            {
                Username = req.Username,
                Email = req.Email,
                PasswordHash = PasswordHasher.Hash(req.Password),
                CreatedAt = now,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(); // gera o Id pro resto do setup

            var currencies = await db.CurrencyTypes.ToDictionaryAsync(c => c.Code, c => c.Id);
            db.WalletBalances.Add(new WalletBalance
            {
                UserId = user.Id, CurrencyTypeId = currencies["SOFT"], Amount = EconomyDefaults.StartingSoftBalance,
            });
            db.WalletBalances.Add(new WalletBalance
            {
                UserId = user.Id, CurrencyTypeId = currencies["PREMIUM"], Amount = EconomyDefaults.StartingPremiumBalance,
            });

            var aquariumTypeId = await db.HabitatTypes
                .Where(h => h.Code == "Aquarium").Select(h => h.Id).FirstAsync();
            db.Habitats.Add(new Habitat
            {
                UserId = user.Id,
                HabitatTypeId = aquariumTypeId,
                Capacity = HabitatDefaults.Capacity,
                MaintenanceLevel = HabitatDefaults.MaintenanceLevel,
                QueueCap = HabitatDefaults.QueueCap,
                GenerationIntervalMinutes = HabitatDefaults.GenerationIntervalMinutes,
                OnlineGenerationRate = HabitatDefaults.OnlineGenerationRate,
                OfflineGenerationRate = HabitatDefaults.OfflineGenerationRate,
                LastTickAt = now,
                CreatedAt = now,
            });
            await db.SaveChangesAsync();

            return Results.Ok(new AuthResponse(user.Id, user.Username, tokens.CreateToken(user)));
        });

        group.MapPost("/login", async (LoginRequest req, VivariumDbContext db, TokenService tokens) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u =>
                u.Username == req.UsernameOrEmail || u.Email == req.UsernameOrEmail);

            if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
                return Results.Unauthorized();

            return Results.Ok(new AuthResponse(user.Id, user.Username, tokens.CreateToken(user)));
        });
    }
}
