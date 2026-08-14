using System.Security.Claims;
using Vivarium.Api.Contracts;
using Vivarium.Api.Http;
using Vivarium.Api.Services;

namespace Vivarium.Api.Endpoints;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account").RequireAuthorization();

        group.MapPut("/email", async (UpdateEmailRequest req, ClaimsPrincipal principal, AccountService accounts) =>
            (await accounts.UpdateEmailAsync(TokenService.GetUserId(principal), req.NewEmail, req.CurrentPassword)).ToHttp());

        group.MapPut("/password", async (UpdatePasswordRequest req, ClaimsPrincipal principal, AccountService accounts) =>
            (await accounts.UpdatePasswordAsync(TokenService.GetUserId(principal), req.CurrentPassword, req.NewPassword)).ToHttp());
    }
}
