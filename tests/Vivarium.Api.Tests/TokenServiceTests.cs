using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Vivarium.Api.Services;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Tests;

public class TokenServiceTests
{
    private static TokenService BuildService() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "chave-de-teste-nao-usar-em-producao-vivarium-0123456789",
            ["Jwt:Issuer"] = "vivarium-tests",
            ["Jwt:Audience"] = "vivarium-tests",
        })
        .Build());

    private static User BuildUser(long id = 42) => new()
    {
        Id = id,
        Username = "jogador",
        Email = "jogador@teste.com",
        PasswordHash = "hash-irrelevante",
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public void CreateToken_ClaimSubBateComIdDoUsuario()
    {
        var token = BuildService().CreateToken(BuildUser(42));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("42", jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("vivarium-tests", jwt.Issuer);
    }

    [Fact]
    public void GetUserId_ExtraiIdDoClaimSub()
    {
        var identity = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, "7")]);
        var principal = new ClaimsPrincipal(identity);

        Assert.Equal(7, TokenService.GetUserId(principal));
    }

    [Fact]
    public void GetUserId_SemClaimDeUsuario_Lanca()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Throws<InvalidOperationException>(() => TokenService.GetUserId(principal));
    }
}
