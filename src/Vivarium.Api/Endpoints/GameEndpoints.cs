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
        Dictionary<string, decimal> Wallet,
        decimal CoinsPerHour);

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
            try
            {
                await game.ApplyTickAsync(habitat, now);
                habitat.LastHeartbeatAt = now;
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Outro request ticou primeiro; o próximo heartbeat regrava. A janela
                // desta chamada foi descartada (LastTickAt não avançou) e será coberta
                // no próximo tick — renda continua exatamente-uma-vez.
            }

            return Results.Ok(new { online = true, maintenanceLevel = habitat.MaintenanceLevel });
        });

        group.MapGet("/tank", async (ClaimsPrincipal principal, VivariumDbContext db, GameService game) =>
        {
            var now = DateTime.UtcNow;
            long userId = TokenService.GetUserId(principal);
            var habitat = await game.FindHabitatAsync(userId);
            if (habitat is null)
                return Results.NotFound();

            try
            {
                await game.ApplyTickAsync(habitat, now);
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Outro request ticou primeiro; recarrega o estado atual e segue.
                db.ChangeTracker.Clear();
                habitat = (await game.FindHabitatAsync(userId))!;
            }

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
            var coinsPerHour = await game.CoinsPerHourAsync(habitat);

            return Results.Ok(new TankResponse(
                HabitatTicker.IsOnline(habitat.LastHeartbeatAt, now, TickConfig.Default),
                habitat.MaintenanceLevel,
                habitat.Capacity,
                habitat.QueueCap,
                queue,
                creatures,
                wallet,
                coinsPerHour));
        });

        // Transferência direta entre contas (negociação externa é responsabilidade
        // dos jogadores — o jogo só move o item e audita no TransactionLog)
        group.MapPost("/creatures/{creatureId:long}/transfer", async (
            long creatureId, TransferRequest req, ClaimsPrincipal principal, VivariumDbContext db, GameService game) =>
        {
            long userId = TokenService.GetUserId(principal);
            var creature = await db.CreatureInstances
                .FirstOrDefaultAsync(c => c.Id == creatureId && c.OwnerId == userId);
            if (creature is null)
                return Results.NotFound(new { error = "Criatura não encontrada" });
            // Só transfere do tanque ou da mochila; se listada, cancele antes.
            bool listed = await db.MarketListings.AnyAsync(m =>
                m.CreatureInstanceId == creature.Id && m.Status == ListingStatus.Active);
            if (listed)
                return Results.BadRequest(new { error = "Criatura está no mercado — cancele a listagem antes" });

            var target = await db.Users.FirstOrDefaultAsync(u => u.Username == req.ToUsername);
            if (target is null)
                return Results.NotFound(new { error = "Jogador destinatário não encontrado" });
            if (target.Id == userId)
                return Results.BadRequest(new { error = "Não dá pra transferir pra si mesmo" });

            var targetHabitat = await game.FindHabitatAsync(target.Id);
            if (targetHabitat is null)
                return Results.BadRequest(new { error = "Destinatário não tem habitat" });

            creature.OwnerId = target.Id;
            if (!await game.TryPlaceAsync(creature, targetHabitat))
                return Results.BadRequest(new { error = "O tanque e a mochila do destinatário estão cheios." });

            db.TransactionLogs.Add(new TransactionLog
            {
                Type = TransactionType.DirectTransfer,
                FromUserId = userId,
                ToUserId = target.Id,
                CreatureInstanceId = creature.Id,
                CreatedAt = DateTime.UtcNow,
            });
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "O peixe mudou de estado — atualize e tente de novo." });
            }
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

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "Operação concorrente — tente de novo." });
            }
            return Results.Ok(new CreatureDto(
                creature.Id, creature.SpeciesId, creature.Seed.ToString(),
                creature.TraitConfigVersion, creature.RarityScore, creature.CreatedAt));
        });

        // ---------- Mochila (storage) ----------

        group.MapGet("/backpack", async (ClaimsPrincipal principal, GameService game) =>
        {
            long userId = TokenService.GetUserId(principal);
            var creatures = (await game.BackpackQuery(userId).ToListAsync())
                .Select(c => new CreatureDto(c.Id, c.SpeciesId, c.Seed.ToString(),
                    c.TraitConfigVersion, c.RarityScore, c.CreatedAt))
                .ToList();
            return Results.Ok(new { capacity = HabitatDefaults.BackpackCapacity, creatures });
        });

        // Tanque → mochila
        group.MapPost("/creatures/{creatureId:long}/store", async (
            long creatureId, ClaimsPrincipal principal, VivariumDbContext db, GameService game) =>
        {
            var habitat = await game.FindHabitatAsync(TokenService.GetUserId(principal));
            if (habitat is null)
                return Results.NotFound();
            var error = await game.StoreAsync(habitat.UserId, creatureId, habitat);
            if (error is not null)
                return Results.BadRequest(new { error });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Tente de novo." }); }
            return Results.Ok();
        });

        // Mochila → tanque
        group.MapPost("/creatures/{creatureId:long}/deploy", async (
            long creatureId, ClaimsPrincipal principal, VivariumDbContext db, GameService game) =>
        {
            var habitat = await game.FindHabitatAsync(TokenService.GetUserId(principal));
            if (habitat is null)
                return Results.NotFound();
            var error = await game.DeployAsync(habitat.UserId, creatureId, habitat);
            if (error is not null)
                return Results.BadRequest(new { error });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Tente de novo." }); }
            return Results.Ok();
        });
    }
}
