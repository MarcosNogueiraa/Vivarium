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
        // Faixas ÷10 (10/08/2026, TEMPORÁRIO pra fase de testes — ver BreedingDefaults.BaseGestationHours).
        // 2 comuns (score ~5 cada, combinado ~10 = ref): quase imediato (Base=0,6h).
        double comuns = BreedingCalculator.GestationHours(5m, 5m);
        Assert.InRange(comuns, 0.55, 0.65);

        // 2 lendários (score ~14 cada): o corte foi assimétrico — o topo continua o mais lento
        // proporcionalmente (~15-19h, era ~150-190h antes do ÷10 temporário).
        double lendarios = BreedingCalculator.GestationHours(14m, 14m);
        Assert.InRange(lendarios, 15, 19);
    }
}
