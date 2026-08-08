using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Api.Services;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;

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
                return ApiError.BadRequest("Fila cheia — colete antes de gerar mais");

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

        // Credita moeda (soft por padrão; ?currency=PREMIUM pra testar o rush — 8.11)
        // no jogador logado, pra teste. Sem isso não tem como testar o rush sem um
        // processador de pagamento real, que este projeto ainda não integra.
        group.MapPost("/coins", async (long? amount, string? currency, ClaimsPrincipal principal, VivariumDbContext db) =>
        {
            long userId = TokenService.GetUserId(principal);
            decimal credit = Math.Clamp(amount ?? 1000, 1, 1_000_000);
            string code = string.Equals(currency, "PREMIUM", StringComparison.OrdinalIgnoreCase) ? "PREMIUM" : "SOFT";

            int currencyId = await db.CurrencyTypes.Where(c => c.Code == code).Select(c => c.Id).FirstAsync();
            var wallet = await db.WalletBalances
                .FirstOrDefaultAsync(w => w.UserId == userId && w.CurrencyTypeId == currencyId);
            if (wallet is null)
                return Results.NotFound();

            wallet.Amount += credit;
            db.TransactionLogs.Add(new TransactionLog
            {
                Type = TransactionType.CurrencyPurchase,
                ToUserId = userId,
                CurrencyTypeId = currencyId,
                Amount = credit,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return Results.Ok(new { credited = credit, currency = code, balance = wallet.Amount });
        });

        // Volta o tanque do jogador logado pro estado inicial: capacidade 3, água
        // 100%, remove os filtros automáticos comprados (pra ver a progressão de
        // níveis de novo), manda os peixes do tanque pra mochila (não apaga nada)
        // e credita um saldo generoso de soft pra testar as compras sem esperar a
        // renda passiva. Só pra contas de teste — nunca existe em produção.
        group.MapPost("/reset-tank", async (ClaimsPrincipal principal, VivariumDbContext db, GameService game) =>
        {
            long userId = TokenService.GetUserId(principal);
            var habitat = await game.FindHabitatAsync(userId);
            if (habitat is null)
                return Results.NotFound();

            habitat.Capacity = HabitatDefaults.Capacity;
            habitat.MaintenanceLevel = HabitatDefaults.MaintenanceLevel;

            int movedToBackpack = await db.CreatureInstances
                .Where(c => c.HabitatId == habitat.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.HabitatId, (long?)null));

            int filtersRemoved = await db.UserInventories
                .Where(i => i.UserId == userId && i.ItemDefinition!.Category == ItemCategory.AutoFilter)
                .ExecuteDeleteAsync();

            int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == softId);
            wallet.Amount = 5000m;

            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                capacity = habitat.Capacity,
                maintenanceLevel = habitat.MaintenanceLevel,
                movedToBackpack,
                filtersRemoved,
                wallet = wallet.Amount,
            });
        });

        // Zera o tempo de gestação restante (não muda o resultado, só a hora) — pra
        // testar o "coletar" sem esperar as horas/dias reais da fórmula.
        group.MapPost("/breeding/finish", async (ClaimsPrincipal principal, VivariumDbContext db) =>
        {
            long userId = TokenService.GetUserId(principal);
            var slot = await db.BreedingSlots
                .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == BreedingStatus.InProgress);
            if (slot is null)
                return Results.NotFound();

            slot.ReadyAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { readyAt = slot.ReadyAt });
        });
    }
}
