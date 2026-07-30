using System.Security.Claims;
using Vivarium.Api.Http;
using Vivarium.Api.Services;

namespace Vivarium.Api.Endpoints;

public static class ItemEndpoints
{
    public static void MapItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/items").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, ItemService items) =>
            (await items.ListAsync(TokenService.GetUserId(principal))).ToHttp());

        group.MapPost("/{key}/buy", async (string key, ClaimsPrincipal principal, ItemService items) =>
            (await items.BuyAsync(TokenService.GetUserId(principal), key)).ToHttp());
    }
}
