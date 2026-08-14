using System.Security.Claims;
using Vivarium.Api.Http;
using Vivarium.Api.Services;

namespace Vivarium.Api.Endpoints;

public static class InboxEndpoints
{
    public static void MapInboxEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/inbox").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, InboxService inbox) =>
            (await inbox.ListAsync(TokenService.GetUserId(principal))).ToHttp());

        group.MapPost("/{entryId:long}/claim", async (long entryId, ClaimsPrincipal principal, InboxService inbox) =>
            (await inbox.ClaimAsync(TokenService.GetUserId(principal), entryId, DateTime.UtcNow)).ToHttp());

        group.MapPost("/claim-all", async (ClaimsPrincipal principal, InboxService inbox) =>
            (await inbox.ClaimAllAsync(TokenService.GetUserId(principal), DateTime.UtcNow)).ToHttp());

        group.MapPost("/mark-all-read", async (ClaimsPrincipal principal, InboxService inbox) =>
            (await inbox.MarkAllReadAsync(TokenService.GetUserId(principal), DateTime.UtcNow)).ToHttp());

        group.MapPost("/clear-claimed", async (ClaimsPrincipal principal, InboxService inbox) =>
            (await inbox.ClearClaimedAsync(TokenService.GetUserId(principal))).ToHttp());
    }
}
