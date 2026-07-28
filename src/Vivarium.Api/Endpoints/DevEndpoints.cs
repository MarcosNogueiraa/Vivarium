using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Data;
using Vivarium.Api.Services;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Endpoints;

/// <summary>
/// Atalhos de desenvolvimento. Só são mapeados em ambiente Development
/// (ver Program.cs) — em produção estas rotas nem existem.
/// </summary>
public static class DevEndpoints
{
    public static void MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dev").RequireAuthorization();

        // Gera um item já pronto na fila, sem esperar o intervalo de geração
        group.MapPost("/spawn", async (ClaimsPrincipal principal, VivariumDbContext db, GameService game) =>
        {
            var habitat = await game.FindHabitatAsync(TokenService.GetUserId(principal));
            if (habitat is null)
                return Results.NotFound();

            int pending = await db.GenerationQueueItems
                .CountAsync(q => q.HabitatId == habitat.Id && q.Status == QueueItemStatus.Pending);
            if (pending >= habitat.QueueCap)
                return Results.BadRequest(new { error = "Fila cheia — colete antes de gerar mais" });

            int speciesId = await db.Species
                .Where(s => s.HabitatTypeId == habitat.HabitatTypeId)
                .Select(s => s.Id)
                .FirstAsync();
            db.GenerationQueueItems.Add(new GenerationQueueItem
            {
                HabitatId = habitat.Id,
                SpeciesId = speciesId,
                ReadyAt = DateTime.UtcNow,
                Status = QueueItemStatus.Pending,
            });
            await db.SaveChangesAsync();
            return Results.Ok(new { pending = pending + 1 });
        });

        // Remove todos os peixes do tanque do jogador (não toca em criaturas
        // listadas no mercado — essas estão fora do tanque, HabitatId null)
        group.MapPost("/clear", async (ClaimsPrincipal principal, VivariumDbContext db, GameService game) =>
        {
            var habitat = await game.FindHabitatAsync(TokenService.GetUserId(principal));
            if (habitat is null)
                return Results.NotFound();

            int removed = await db.CreatureInstances
                .Where(c => c.HabitatId == habitat.Id)
                .ExecuteDeleteAsync();
            return Results.Ok(new { removed });
        });

        // Credita fichas (moeda soft) no jogador logado, pra teste
        group.MapPost("/coins", async (long? amount, ClaimsPrincipal principal, VivariumDbContext db) =>
        {
            long userId = TokenService.GetUserId(principal);
            decimal credit = Math.Clamp(amount ?? 1000, 1, 1_000_000);

            int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
            var wallet = await db.WalletBalances
                .FirstOrDefaultAsync(w => w.UserId == userId && w.CurrencyTypeId == softId);
            if (wallet is null)
                return Results.NotFound();

            wallet.Amount += credit;
            db.TransactionLogs.Add(new TransactionLog
            {
                Type = TransactionType.CurrencyPurchase,
                ToUserId = userId,
                CurrencyTypeId = softId,
                Amount = credit,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return Results.Ok(new { credited = credit, balance = wallet.Amount });
        });
    }
}
