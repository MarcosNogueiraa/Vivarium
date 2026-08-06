using Vivarium.Core.Gameplay;

namespace Vivarium.Core.Tests;

public class RushCalculatorTests
{
    [Fact]
    public void QueueRushCost_EscalaComOTempoRestante()
    {
        decimal pouco = RushCalculator.QueueRushCost(5);
        decimal muito = RushCalculator.QueueRushCost(60);

        Assert.True(pouco < muito);
        Assert.Equal(9m, muito); // 0.15 * 60 = 9
    }

    [Fact]
    public void QueueRushCost_NuncaMenorQueOMinimo()
    {
        Assert.Equal(RushConfig.MinRushCostPremium, RushCalculator.QueueRushCost(0));
        Assert.Equal(RushConfig.MinRushCostPremium, RushCalculator.QueueRushCost(-5)); // defensivo
    }

    [Fact]
    public void GestationRushCost_EscalaComAsHorasRestantes()
    {
        decimal umDia = RushCalculator.GestationRushCost(24);
        decimal dezDias = RushCalculator.GestationRushCost(240);

        Assert.Equal(48m, umDia);   // 2.0 * 24
        Assert.Equal(480m, dezDias); // 2.0 * 240
        Assert.True(umDia < dezDias);
    }

    [Fact]
    public void GestationHours_ComunsRapidos_LendariosContinuamLentos()
    {
        // 2 comuns (score ~5 cada, combinado ~10 = ref): quase imediato (06/08/2026, Base=6h).
        double comuns = BreedingCalculator.GestationHours(5m, 5m);
        Assert.InRange(comuns, 5.5, 6.5);

        // 2 lendários (score ~14 cada): o corte foi assimétrico — o topo continua lento (~7 dias).
        double lendarios = BreedingCalculator.GestationHours(14m, 14m);
        Assert.InRange(lendarios, 150, 190);
    }
}
