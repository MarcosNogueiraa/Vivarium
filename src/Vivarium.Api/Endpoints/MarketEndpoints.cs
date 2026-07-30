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

        group.MapGet("/listings", async (MarketService market, int skip = 0, int take = 50) =>
            Results.Ok(await market.ListingsAsync(skip, take)));

        group.MapPost("/listings", async (CreateListingRequest req, ClaimsPrincipal principal, MarketService market) =>
            (await market.CreateListingAsync(TokenService.GetUserId(principal), req)).ToHttp());

        group.MapPost("/listings/{id:long}/cancel", async (long id, ClaimsPrincipal principal, MarketService market) =>
            (await market.CancelAsync(TokenService.GetUserId(principal), id)).ToHttp());

        group.MapPost("/listings/{id:long}/buy", async (long id, ClaimsPrincipal principal, MarketService market) =>
            (await market.BuyAsync(TokenService.GetUserId(principal), id)).ToHttp());
    }
}
