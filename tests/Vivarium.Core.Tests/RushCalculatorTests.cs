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
    public void GestationHours_BaseMaisLentaAposORebalanceamentoAntiRush()
    {
        // 2 comuns (score ~5 cada, combinado ~10 = ref): agora leva o dia inteiro, não só horas.
        double hours = BreedingCalculator.GestationHours(5m, 5m);
        Assert.True(hours >= 20); // era ~8h antes do rebalanceamento de 30/07
    }
}
