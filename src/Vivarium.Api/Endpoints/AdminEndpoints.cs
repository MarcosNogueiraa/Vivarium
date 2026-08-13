using System.Security.Claims;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Api.Services;

namespace Vivarium.Api.Endpoints;

/// <summary>Ferramentas administrativas — protegidas por User.IsAdmin (checado em AdminService).</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization();

        group.MapPost("/give-starter-fish-all", async (
            ClaimsPrincipal principal, AdminService admin, VivariumDbContext db) =>
        {
            var result = await admin.GiveStarterFishToAllAsync(TokenService.GetUserId(principal), DateTime.UtcNow);
            if (result.Ok)
                await db.SaveChangesAsync();
            return result.ToHttp();
        });

        group.MapPost("/grant-premium-all", async (
            GrantPremiumRequest body, ClaimsPrincipal principal, AdminService admin, VivariumDbContext db) =>
        {
            var result = await admin.GrantPremiumToAllAsync(TokenService.GetUserId(principal), body.Amount, DateTime.UtcNow);
            if (result.Ok)
                await db.SaveChangesAsync();
            return result.ToHttp();
        });

        group.MapPost("/wallet", async (
            AdjustWalletRequest body, ClaimsPrincipal principal, AdminService admin, VivariumDbContext db) =>
        {
            var result = await admin.AdjustUserWalletAsync(
                TokenService.GetUserId(principal), body.Username, body.CurrencyCode, body.Mode, body.Amount, DateTime.UtcNow);
            if (result.Ok)
                await db.SaveChangesAsync();
            return result.ToHttp();
        });
    }
}
