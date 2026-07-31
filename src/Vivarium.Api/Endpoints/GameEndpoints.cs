using System.Security.Claims;
using Vivarium.Api.Contracts;
using Vivarium.Api.Http;
using Vivarium.Api.Services;

namespace Vivarium.Api.Endpoints;

public static class GameEndpoints
{
    public static void MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/game").RequireAuthorization();

        group.MapPost("/heartbeat", async (ClaimsPrincipal principal, GameService game) =>
            (await game.HeartbeatAsync(TokenService.GetUserId(principal), DateTime.UtcNow)).ToHttp());

        group.MapGet("/tank", async (ClaimsPrincipal principal, GameService game) =>
            (await game.GetTankAsync(TokenService.GetUserId(principal), DateTime.UtcNow)).ToHttp());

        // Transferência direta entre contas (negociação externa é responsabilidade
        // dos jogadores — o jogo só move o item e audita no TransactionLog)
        group.MapPost("/creatures/{creatureId:long}/transfer", async (
            long creatureId, TransferRequest req, ClaimsPrincipal principal, GameService game) =>
            (await game.TransferAsync(TokenService.GetUserId(principal), creatureId, req.ToUsername)).ToHttp());

        group.MapPost("/collect/{queueItemId:long}", async (
            long queueItemId, ClaimsPrincipal principal, GameService game) =>
            (await game.CollectQueueItemAsync(TokenService.GetUserId(principal), queueItemId, DateTime.UtcNow)).ToHttp());

        // ---------- Recompensa diária (8.10) ----------

        group.MapGet("/daily-reward", async (ClaimsPrincipal principal, GameService game) =>
            (await game.GetDailyRewardStatusAsync(TokenService.GetUserId(principal), DateTime.UtcNow)).ToHttp());

        group.MapPost("/daily-reward/claim", async (ClaimsPrincipal principal, GameService game) =>
            (await game.ClaimDailyRewardAsync(TokenService.GetUserId(principal), DateTime.UtcNow)).ToHttp());

        // ---------- Acelerar com moeda premium (8.11) ----------

        group.MapPost("/queue/{queueItemId:long}/rush", async (
            long queueItemId, ClaimsPrincipal principal, GameService game) =>
            (await game.RushQueueItemAsync(TokenService.GetUserId(principal), queueItemId, DateTime.UtcNow)).ToHttp());

        // ---------- Mochila (storage) ----------

        group.MapGet("/backpack", async (ClaimsPrincipal principal, GameService game) =>
            (await game.GetBackpackAsync(TokenService.GetUserId(principal))).ToHttp());

        // Tanque → mochila
        group.MapPost("/creatures/{creatureId:long}/store", async (
            long creatureId, ClaimsPrincipal principal, GameService game) =>
            (await game.StoreAsync(TokenService.GetUserId(principal), creatureId)).ToHttp());

        // Mochila → tanque
        group.MapPost("/creatures/{creatureId:long}/deploy", async (
            long creatureId, ClaimsPrincipal principal, GameService game) =>
            (await game.DeployAsync(TokenService.GetUserId(principal), creatureId)).ToHttp());

        // ---------- Venda ao NPC (vendor, §8.12) ----------

        group.MapPost("/creatures/{creatureId:long}/sell-vendor", async (
            long creatureId, ClaimsPrincipal principal, GameService game) =>
            (await game.SellToVendorAsync(TokenService.GetUserId(principal), creatureId, DateTime.UtcNow)).ToHttp());
    }
}
