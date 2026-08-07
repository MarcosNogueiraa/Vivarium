using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Api.Services;
using Vivarium.Api.Validation;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;

namespace Vivarium.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        group.MapPost("/register", async (RegisterRequest req, VivariumDbContext db, TokenService tokens) =>
        {
            var invalid = AccountValidation.Username(req.Username)
                ?? AccountValidation.Email(req.Email)
                ?? AccountValidation.Password(req.Password);
            if (invalid is not null)
                return ApiError.BadRequest(invalid);

            if (await db.Users.AnyAsync(u => u.Username == req.Username))
                return ApiError.Conflict("Username já em uso");
            if (await db.Users.AnyAsync(u => u.Email == req.Email))
                return ApiError.Conflict("Email já cadastrado");

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
            var aquarium = new Habitat
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
            };
            db.Habitats.Add(aquarium);

            // Peixe inicial já pronto pra coletar — sem esperar o primeiro ciclo de geração,
            // o jogador novo tem algo pra fazer no primeiro clique.
            int aquariumSpeciesId = await db.Species
                .Where(s => s.HabitatTypeId == aquariumTypeId).Select(s => s.Id).FirstAsync();
            db.GenerationQueueItems.Add(new GenerationQueueItem
            {
                Habitat = aquarium,
                SpeciesId = aquariumSpeciesId,
                ReadyAt = now,
                Status = QueueItemStatus.Pending,
                IsSick = false,
            });

            var breedingTypeId = await db.HabitatTypes
                .Where(h => h.Code == "Breeding").Select(h => h.Id).FirstAsync();
            db.Habitats.Add(new Habitat
            {
                UserId = user.Id,
                HabitatTypeId = breedingTypeId,
                Capacity = 2,
                MaintenanceLevel = 100m,
                QueueCap = 0,
                GenerationIntervalMinutes = int.MaxValue, // nunca gera fila; nunca passa por ApplyTickAsync
                OnlineGenerationRate = 0m,
                OfflineGenerationRate = 0m,
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
                return ApiError.Unauthorized("Usuário ou senha incorretos");

            return Results.Ok(new AuthResponse(user.Id, user.Username, tokens.CreateToken(user)));
        });

        group.MapGet("/me", async (ClaimsPrincipal principal, VivariumDbContext db) =>
        {
            long userId = TokenService.GetUserId(principal);
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            return Results.Ok(new MeResponse(user.Id, user.Username, user.Email));
        }).RequireAuthorization();
    }
}
