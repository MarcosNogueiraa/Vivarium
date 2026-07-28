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
    }
}
