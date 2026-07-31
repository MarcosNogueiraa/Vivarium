using Vivarium.Api.Services;

namespace Vivarium.Api.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Verify_SenhaCorreta_RetornaTrue()
    {
        var hash = PasswordHasher.Hash("senha-forte-123");

        Assert.True(PasswordHasher.Verify("senha-forte-123", hash));
    }

    [Fact]
    public void Verify_SenhaErrada_RetornaFalse()
    {
        var hash = PasswordHasher.Hash("senha-forte-123");

        Assert.False(PasswordHasher.Verify("outra-senha-456", hash));
    }

    [Fact]
    public void Hash_MesmaSenhaDuasVezes_ProduzHashesDiferentes()
    {
        var a = PasswordHasher.Hash("senha-forte-123");
        var b = PasswordHasher.Hash("senha-forte-123");

        Assert.NotEqual(a, b); // salt aleatório
        Assert.True(PasswordHasher.Verify("senha-forte-123", a));
        Assert.True(PasswordHasher.Verify("senha-forte-123", b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao-tem-pontos")]
    [InlineData("um.dois")]
    [InlineData("nao-e-numero.c2FsdA==.aGFzaA==")]
    [InlineData("100000.!!!nao-base64!!!.aGFzaA==")]
    public void Verify_HashArmazenadoMalformado_RetornaFalseSemLancar(string stored)
    {
        Assert.False(PasswordHasher.Verify("qualquer-senha", stored));
    }
}
