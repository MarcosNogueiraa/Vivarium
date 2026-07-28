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
    public record TransferRequest(string ToUsername);
    // Seed como string: 63 bits não cabem num JSON number (double trunca acima de 2^53)
    public record CreatureDto(long Id, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore, DateTime CreatedAt);
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
            var creatures = (await db.CreatureInstances
                .Where(c => c.HabitatId == habitat.Id)
                .ToListAsync())
                .Select(c => new CreatureDto(c.Id, c.SpeciesId, c.Seed.ToString(), c.TraitConfigVersion, c.RarityScore, c.CreatedAt))
                .ToList();
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

        // Transferência direta entre contas (negociação externa é responsabilidade
        // dos jogadores — o jogo só move o item e audita no TransactionLog)
        group.MapPost("/creatures/{creatureId:long}/transfer", async (
            long creatureId, TransferRequest req, ClaimsPrincipal principal, VivariumDbContext db) =>
        {
            long userId = TokenService.GetUserId(principal);
            var creature = await db.CreatureInstances
                .FirstOrDefaultAsync(c => c.Id == creatureId && c.OwnerId == userId);
            if (creature is null)
                return Results.NotFound(new { error = "Criatura não encontrada" });
            if (creature.HabitatId is null)
                return Results.BadRequest(new { error = "Criatura está no mercado — cancele a listagem antes" });

            var target = await db.Users.FirstOrDefaultAsync(u => u.Username == req.ToUsername);
            if (target is null)
                return Results.NotFound(new { error = "Jogador destinatário não encontrado" });
            if (target.Id == userId)
                return Results.BadRequest(new { error = "Não dá pra transferir pra si mesmo" });

            creature.OwnerId = target.Id;
            var targetHabitat = await db.Habitats.FirstOrDefaultAsync(h => h.UserId == target.Id);
            int active = targetHabitat is null
                ? 0
                : await db.CreatureInstances.CountAsync(c => c.HabitatId == targetHabitat.Id);
            creature.HabitatId = targetHabitat is not null && active < targetHabitat.Capacity
                ? targetHabitat.Id
                : null;

            db.TransactionLogs.Add(new TransactionLog
            {
                Type = TransactionType.DirectTransfer,
                FromUserId = userId,
                ToUserId = target.Id,
                CreatureInstanceId = creature.Id,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return Results.Ok(new { transferredTo = target.Username });
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
                creature.Id, creature.SpeciesId, creature.Seed.ToString(),
                creature.TraitConfigVersion, creature.RarityScore, creature.CreatedAt));
        });
    }
}
