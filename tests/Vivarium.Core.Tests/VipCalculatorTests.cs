using Vivarium.Core.Gameplay;

namespace Vivarium.Core.Tests;

public class VipCalculatorTests
{
    [Theory]
    [InlineData(7, 7)]
    [InlineData(15, 10)]
    [InlineData(30, 15)]
    public void TryGetPrice_PacotesValidos(int days, decimal expected)
    {
        Assert.True(VipCalculator.TryGetPrice(days, out decimal price));
        Assert.Equal(expected, price);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(0)]
    [InlineData(-7)]
    public void TryGetPrice_PacoteInvalido_RetornaFalse(int days)
    {
        Assert.False(VipCalculator.TryGetPrice(days, out _));
    }

    [Fact]
    public void PrecoPorDia_CaiQuantoMaiorOPacote()
    {
        VipCalculator.TryGetPrice(7, out decimal p7);
        VipCalculator.TryGetPrice(15, out decimal p15);
        VipCalculator.TryGetPrice(30, out decimal p30);

        Assert.True(p7 / 7 > p15 / 15);
        Assert.True(p15 / 15 > p30 / 30);
    }
}
