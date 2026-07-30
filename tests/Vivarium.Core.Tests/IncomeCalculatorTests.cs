using Vivarium.Core.Gameplay;
using Vivarium.Core.Generation;

namespace Vivarium.Core.Tests;

public class IncomeCalculatorTests
{
    private static readonly TickConfig Cfg = TickConfig.Default;

    // cores distintas por padrão → sem sinergia; passe a mesma cor pra ativar sinergia
    private static FishIncome[] Distinct(params decimal[] scores)
    {
        var colors = Enum.GetValues<PartColor>();
        return scores.Select((s, i) => new FishIncome(s, colors[i % colors.Length])).ToArray();
    }
    private static FishIncome[] SameColor(decimal score, int n)
        => Enumerable.Repeat(new FishIncome(score, PartColor.Blue), n).ToArray();

    [Fact]
    public void RendaCrescExponencialComRaridade()
    {
        double comum = IncomeCalculator.CoinsPerHour(4m, Cfg);   // score ref
        double raro = IncomeCalculator.CoinsPerHour(7.5m, Cfg);  // início da faixa Raro (v2)
        double lendario = IncomeCalculator.CoinsPerHour(14m, Cfg); // início do Lendário (v2)

        Assert.True(comum < raro && raro < lendario);
        Assert.InRange(comum, 1.4, 2.0);           // base 1.7/h
        Assert.True(lendario > 150);               // lendário rende muito mais
        Assert.True(lendario / comum > 25);        // gap enorme comum→lendário
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
    public void Sinergia_MesmaCorRendeMaisQueCoresDistintas()
    {
        decimal distintas = IncomeCalculator.TankRatePerHour(Distinct(6m, 6m, 6m, 6m, 6m), 100m, Cfg);
        decimal mesmaCor = IncomeCalculator.TankRatePerHour(SameColor(6m, 5), 100m, Cfg);

        // 5 peixes mesma cor: cada um ×(1 + 0.15×4) = ×1.6
        Assert.True(mesmaCor > distintas);
        Assert.Equal(1.6, (double)(mesmaCor / distintas), 2);
    }

    [Fact]
    public void Sinergia_TemTeto()
    {
        Assert.Equal(1.0, IncomeCalculator.SynergyMultiplier(1, Cfg), 3);
        Assert.Equal(1.15, IncomeCalculator.SynergyMultiplier(2, Cfg), 3);
        // muitos peixes: limitado a 1 + SynergyMaxBonus
        Assert.Equal(1.0 + Cfg.SynergyMaxBonus, IncomeCalculator.SynergyMultiplier(100, Cfg), 3);
    }

    [Fact]
    public void Accrue_OfflineRendeMenosQueOnline()
    {
        var fish = Distinct(5m, 5m);
        decimal online = IncomeCalculator.Accrue(fish, 100m, 60m, 0m, 1.0m, 0.45m, Cfg);
        decimal offline = IncomeCalculator.Accrue(fish, 100m, 0m, 60m, 1.0m, 0.45m, Cfg);

        Assert.True(offline < online);
        Assert.Equal(0.45, (double)(offline / online), 2);
    }

    [Fact]
    public void Accrue_AguaSecaZeraRenda()
    {
        decimal earned = IncomeCalculator.Accrue(Distinct(8m), 0m, 120m, 0m, 1.0m, 0.45m, Cfg);
        Assert.Equal(0m, earned);
    }

    [Fact]
    public void Accrue_TetoOfflineDe8Horas()
    {
        var fish = Distinct(6m);
        decimal tresDias = IncomeCalculator.Accrue(fish, 100m, 0m, 3 * 24 * 60m, 1.0m, 0.45m, Cfg);
        decimal oitoHoras = IncomeCalculator.Accrue(fish, 100m, 0m, 8 * 60m, 1.0m, 0.45m, Cfg);

        Assert.Equal(oitoHoras, tresDias);
    }

    [Fact]
    public void Accrue_SemPeixes_SemJanela_RendeZero()
    {
        Assert.Equal(0m, IncomeCalculator.Accrue([], 100m, 60m, 0m, 1.0m, 0.45m, Cfg));
        Assert.Equal(0m, IncomeCalculator.Accrue(Distinct(5m), 100m, 0m, 0m, 1.0m, 0.45m, Cfg));
    }
}
