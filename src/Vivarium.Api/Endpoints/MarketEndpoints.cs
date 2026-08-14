using System.Security.Claims;
using Vivarium.Api.Contracts;
using Vivarium.Api.Http;
using Vivarium.Api.Services;

namespace Vivarium.Api.Endpoints;

public static class MarketEndpoints
{
    public static void MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/market").RequireAuthorization();

        group.MapGet("/listings", async (
            ClaimsPrincipal principal, MarketService market,
            int skip = 0, int take = 24, string sort = "newest", string? band = null,
            string? tailColor = null, string? tailPattern = null,
            string? dorsalColor = null, string? dorsalPattern = null,
            string? pectoralColor = null, string? pectoralPattern = null) =>
            Results.Ok(await market.ListingsAsync(TokenService.GetUserId(principal), skip, take, sort,
                band, tailColor, tailPattern, dorsalColor, dorsalPattern, pectoralColor, pectoralPattern)));

        group.MapPost("/listings", async (CreateListingRequest req, ClaimsPrincipal principal, MarketService market) =>
            (await market.CreateListingAsync(TokenService.GetUserId(principal), req)).ToHttp());

        group.MapPost("/listings/{id:long}/cancel", async (long id, ClaimsPrincipal principal, MarketService market) =>
            (await market.CancelAsync(TokenService.GetUserId(principal), id)).ToHttp());

        group.MapPost("/listings/{id:long}/buy", async (long id, ClaimsPrincipal principal, MarketService market) =>
            (await market.BuyAsync(TokenService.GetUserId(principal), id)).ToHttp());
    }
}
