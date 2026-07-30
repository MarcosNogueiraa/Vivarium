using System.Security.Claims;
using Vivarium.Api.Contracts;
using Vivarium.Api.Http;
using Vivarium.Api.Services;

namespace Vivarium.Api.Endpoints;

public static class BreedingEndpoints
{
    public static void MapBreedingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/breeding").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, BreedingService breeding) =>
            (await breeding.GetStatusAsync(TokenService.GetUserId(principal))).ToHttp());

        group.MapGet("/quote", async (long parentAId, long parentBId, ClaimsPrincipal principal, BreedingService breeding) =>
            (await breeding.GetQuoteAsync(TokenService.GetUserId(principal), parentAId, parentBId)).ToHttp());

        group.MapPost("/start", async (StartBreedingRequest req, ClaimsPrincipal principal, BreedingService breeding) =>
            (await breeding.StartAsync(TokenService.GetUserId(principal), req.ParentAId, req.ParentBId, DateTime.UtcNow)).ToHttp());

        group.MapPost("/collect", async (ClaimsPrincipal principal, BreedingService breeding) =>
            (await breeding.CollectAsync(TokenService.GetUserId(principal), DateTime.UtcNow)).ToHttp());
    }
}
