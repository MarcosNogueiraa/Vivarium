using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Data;
using Vivarium.Api.Services;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;

namespace Vivarium.Api.Endpoints;

public static class GameEndpoints
{
    public record QueueItemDto(long Id, DateTime ReadyAt, bool IsReady, bool IsSick);
    public record CreatureDto(long Id, int SpeciesId, long Seed, int TraitConfigVersion, decimal RarityScore, DateTime CreatedAt);
    public record TankResponse(
        bool Online,
        decimal MaintenanceLevel,
        int Capacity,
        int QueueCap,
        IReadOnlyList<QueueItemDto> Queue,
        IReadOnlyList<CreatureDto> Creatures,
        Dictionary<string, decimal> Wallet);

    public static void MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/game").RequireAuthorization();

        group.MapPost("/heartbeat", async (ClaimsPrincipal principal, VivariumDbContext db, GameService game) =>
        {
            var now = DateTime.UtcNow;
            var habitat = await game.FindHabitatAsync(TokenService.GetUserId(principal));
            if (habitat is null)
                return Results.NotFound();

            // Tick primeiro, com o heartbeat antigo: senão um retorno após dias
            // contaria a ausência inteira como tempo online.
            await game.ApplyTickAsync(habitat, now);
            habitat.LastHeartbeatAt = now;
            await db.SaveChangesAsync();

            return Results.Ok(new { online = true, maintenanceLevel = habitat.MaintenanceLevel });
        });

        group.MapGet("/tank", async (ClaimsPrincipal principal, VivariumDbContext db, GameService game) =>
        {
            var now = DateTime.UtcNow;
            long userId = TokenService.GetUserId(principal);
            var habitat = await game.FindHabitatAsync(userId);
            if (habitat is null)
                return Results.NotFound();

            await game.ApplyTickAsync(habitat, now);
            await db.SaveChangesAsync();

            var queue = await db.GenerationQueueItems
                .Where(q => q.HabitatId == habitat.Id && q.Status == QueueItemStatus.Pending)
                .OrderBy(q => q.ReadyAt)
                .Select(q => new QueueItemDto(q.Id, q.ReadyAt, q.ReadyAt <= now, q.IsSick))
                .ToListAsync();
            var creatures = await db.CreatureInstances
                .Where(c => c.HabitatId == habitat.Id)
                .Select(c => new CreatureDto(c.Id, c.SpeciesId, c.Seed, c.TraitConfigVersion, c.RarityScore, c.CreatedAt))
                .ToListAsync();
            var wallet = await db.WalletBalances
                .Where(w => w.UserId == userId)
                .Select(w => new { w.CurrencyType!.Code, w.Amount })
                .ToDictionaryAsync(x => x.Code, x => x.Amount);

            return Results.Ok(new TankResponse(
                HabitatTicker.IsOnline(habitat.LastHeartbeatAt, now, TickConfig.Default),
                habitat.MaintenanceLevel,
                habitat.Capacity,
                habitat.QueueCap,
                queue,
                creatures,
                wallet));
        });

        group.MapPost("/collect/{queueItemId:long}", async (
            long queueItemId, ClaimsPrincipal principal, VivariumDbContext db, GameService game) =>
        {
            var now = DateTime.UtcNow;
            var habitat = await game.FindHabitatAsync(TokenService.GetUserId(principal));
            if (habitat is null)
                return Results.NotFound();

            await game.ApplyTickAsync(habitat, now);
            var (creature, error) = await game.CollectAsync(habitat, queueItemId, now);
            if (creature is null)
                return Results.BadRequest(new { error });

            await db.SaveChangesAsync();
            return Results.Ok(new CreatureDto(
                creature.Id, creature.SpeciesId, creature.Seed,
                creature.TraitConfigVersion, creature.RarityScore, creature.CreatedAt));
        });
    }
}
