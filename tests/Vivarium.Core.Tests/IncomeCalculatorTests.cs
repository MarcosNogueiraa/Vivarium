using Vivarium.Core.Gameplay;

namespace Vivarium.Core.Tests;

public class IncomeCalculatorTests
{
    private static readonly TickConfig Cfg = TickConfig.Default;

    [Fact]
    public void RendaCrescExponencialComRaridade()
    {
        double comum = IncomeCalculator.CoinsPerHour(4m, Cfg);
        double raro = IncomeCalculator.CoinsPerHour(7.5m, Cfg);
        double lendario = IncomeCalculator.CoinsPerHour(11.2m, Cfg);

        Assert.True(comum < raro && raro < lendario);
        Assert.InRange(comum, 2.5, 3.5);         // ~3/h
        Assert.True(lendario > 90);               // lendário rende muito mais
        Assert.True(lendario / comum > 25);       // gap enorme comum→lendário
    }

    [Fact]
    public void FatorAgua_Monotonico_EZeroQuandoSeca()
    {
        Assert.Equal(1.0, IncomeCalculator.WaterFactor(100m, Cfg), 3);
        Assert.Equal(0.0, IncomeCalculator.WaterFactor(0m, Cfg), 3);
        Assert.True(IncomeCalculator.WaterFactor(40m, Cfg) < IncomeCalculator.WaterFactor(100m, Cfg));
        Assert.True(IncomeCalculator.WaterFactor(15m, Cfg) < IncomeCalculator.WaterFactor(40m, Cfg));
    }

    [Fact]
    public void Accrue_OfflineRendeMenosQueOnline()
    {
        var scores = new[] { 5m, 5m };
        decimal online = IncomeCalculator.Accrue(scores, 100m, 60m, 0m, 1.0m, 0.45m, Cfg);
        decimal offline = IncomeCalculator.Accrue(scores, 100m, 0m, 60m, 1.0m, 0.45m, Cfg);

        Assert.True(offline < online);
        Assert.Equal(0.45, (double)(offline / online), 2);
    }

    [Fact]
    public void Accrue_AguaSecaZeraRenda()
    {
        decimal earned = IncomeCalculator.Accrue(new[] { 8m }, 0m, 120m, 0m, 1.0m, 0.45m, Cfg);
        Assert.Equal(0m, earned);
    }

    [Fact]
    public void Accrue_TetoOfflineDe8Horas()
    {
        var scores = new[] { 6m };
        // 3 dias offline creditam no máximo o mesmo que 8h offline
        decimal tresDias = IncomeCalculator.Accrue(scores, 100m, 0m, 3 * 24 * 60m, 1.0m, 0.45m, Cfg);
        decimal oitoHoras = IncomeCalculator.Accrue(scores, 100m, 0m, 8 * 60m, 1.0m, 0.45m, Cfg);

        Assert.Equal(oitoHoras, tresDias);
    }

    [Fact]
    public void Accrue_SemPeixes_SemJanela_RendeZero()
    {
        Assert.Equal(0m, IncomeCalculator.Accrue([], 100m, 60m, 0m, 1.0m, 0.45m, Cfg));
        Assert.Equal(0m, IncomeCalculator.Accrue(new[] { 5m }, 100m, 0m, 0m, 1.0m, 0.45m, Cfg));
    }
}
