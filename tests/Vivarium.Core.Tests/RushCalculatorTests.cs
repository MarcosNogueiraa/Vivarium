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
    public void GestationHours_FlatDeUmaHoraParaTodoCasal()
    {
        // TEMPORÁRIO (11/08/2026, pedido do usuário — ver BreedingDefaults.BaseGestationHours):
        // Min=Max=1h, então TODO casal gesta em exatamente 1h, independente da raridade —
        // mais volume de cruzamento pra achar bugs antes do reset dos aquários no lançamento.
        Assert.Equal(1.0, BreedingCalculator.GestationHours(5m, 5m));
        Assert.Equal(1.0, BreedingCalculator.GestationHours(14m, 14m));
    }
}
