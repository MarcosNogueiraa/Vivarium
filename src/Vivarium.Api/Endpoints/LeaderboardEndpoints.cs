using System.Security.Claims;
using Vivarium.Api.Http;
using Vivarium.Api.Services;

namespace Vivarium.Api.Endpoints;

public static class LeaderboardEndpoints
{
    public static void MapLeaderboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/leaderboard").RequireAuthorization();

        group.MapGet("/visit/{username}", async (string username, LeaderboardService svc) =>
            (await svc.GetSpectatorTankAsync(username)).ToHttp());

        group.MapGet("/{metric}", async (string metric, int? page, int? pageSize, ClaimsPrincipal principal, LeaderboardService svc) =>
            (await svc.GetLeaderboardAsync(TokenService.GetUserId(principal), metric, page ?? 1, pageSize ?? 50)).ToHttp());
    }
}
