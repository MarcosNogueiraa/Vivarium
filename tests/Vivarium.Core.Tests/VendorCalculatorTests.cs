using Vivarium.Core.Gameplay;

namespace Vivarium.Core.Tests;

public class VendorCalculatorTests
{
    [Fact]
    public void Price_EscalaComARaridade()
    {
        decimal comum = VendorCalculator.Price(5m, TickConfig.Default);
        decimal raro = VendorCalculator.Price(8m, TickConfig.Default);
        decimal lendario = VendorCalculator.Price(15m, TickConfig.Default);

        Assert.True(comum < raro);
        Assert.True(raro < lendario);
    }

    [Fact]
    public void Price_NuncaMenorQueOMinimo()
    {
        Assert.Equal(TickConfig.Default.VendorMinPrice, VendorCalculator.Price(0m, TickConfig.Default));
    }

    [Fact]
    public void Price_FicaBemAbaixoDoCustoDeBreedingParaUmComum()
    {
        // Duas gestações de comuns custam 150 soft (BreedingCalculator) — o vendor não pode
        // competir com o valor real de um peixe, só dar vazão a comuns acumulados.
        decimal comum = VendorCalculator.Price(5m, TickConfig.Default);
        Assert.True(comum < 20m); // menos que 1 filtro básico
    }
}
