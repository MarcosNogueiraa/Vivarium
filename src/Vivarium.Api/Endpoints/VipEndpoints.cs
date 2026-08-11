using System.Security.Claims;
using Vivarium.Api.Contracts;
using Vivarium.Api.Http;
using Vivarium.Api.Services;

namespace Vivarium.Api.Endpoints;

public static class VipEndpoints
{
    public static void MapVipEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vip").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, VipService vip) =>
            (await vip.StatusAsync(TokenService.GetUserId(principal), DateTime.UtcNow)).ToHttp());

        group.MapPost("/subscribe", async (VipSubscribeRequest req, ClaimsPrincipal principal, VipService vip) =>
            (await vip.SubscribeAsync(TokenService.GetUserId(principal), req.Days, DateTime.UtcNow)).ToHttp());
    }
}
