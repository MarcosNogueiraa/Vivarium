using Vivarium.Core.Gameplay;

namespace Vivarium.Core.Tests;

public class BreedingCalculatorTests
{
    [Fact]
    public void DeathChance_CrescComOUsoSemNuncaAlcancarOTeto()
    {
        double primeira = BreedingCalculator.DeathChance(0);
        double quinta = BreedingCalculator.DeathChance(5);
        double muitas = BreedingCalculator.DeathChance(50); // grande o bastante pra chegar perto do teto, sem estourar o ponto flutuante

        Assert.Equal(BreedingDefaults.BaseDeathChance, primeira, 4);
        Assert.True(primeira < quinta && quinta < muitas);
        Assert.True(muitas < BreedingDefaults.MaxDeathChance);
    }

    [Fact]
    public void EffectiveBreedCount_SemHistorico_DevolveOBruto()
    {
        var now = DateTime.UtcNow;
        Assert.Equal(0, BreedingCalculator.EffectiveBreedCount(0, null, now));
        Assert.Equal(3, BreedingCalculator.EffectiveBreedCount(3, null, now));
    }

    [Fact]
    public void EffectiveBreedCount_DescansoDecaiPelaMeiaVida()
    {
        var now = DateTime.UtcNow;
        var semDescanso = now;
        var umaMeiaVida = now.AddDays(-BreedingDefaults.RestHalfLifeDays);
        var duasMeiasVidas = now.AddDays(-2 * BreedingDefaults.RestHalfLifeDays);

        double sem = BreedingCalculator.EffectiveBreedCount(4, semDescanso, now);
        double uma = BreedingCalculator.EffectiveBreedCount(4, umaMeiaVida, now);
        double duas = BreedingCalculator.EffectiveBreedCount(4, duasMeiasVidas, now);

        Assert.Equal(4, sem, 3);
        Assert.Equal(2, uma, 3);   // 1 meia-vida: metade do risco acumulado
        Assert.Equal(1, duas, 3);  // 2 meias-vidas: um quarto
        Assert.True(BreedingCalculator.DeathChance(duas) < BreedingCalculator.DeathChance(sem));
    }

    [Fact]
    public void EffectiveBreedCount_NuncaNegativoMesmoComTimestampFuturo()
    {
        var now = DateTime.UtcNow;
        double effective = BreedingCalculator.EffectiveBreedCount(2, now.AddMinutes(5), now); // defensivo
        Assert.Equal(2, effective, 3);
    }

    [Fact]
    public void InsuranceCostPremium_EscalaComORiscoRemovidoEClampa()
    {
        decimal barato = BreedingCalculator.InsuranceCostPremium(0.05, 0.05); // par fresco
        decimal caro = BreedingCalculator.InsuranceCostPremium(0.6, 0.6);     // veteranos

        Assert.True(barato < caro);
        Assert.Equal(BreedingDefaults.InsuranceMinPremium, BreedingCalculator.InsuranceCostPremium(0, 0));
        Assert.Equal(BreedingDefaults.InsuranceMaxPremium, BreedingCalculator.InsuranceCostPremium(0.85, 0.85));
    }
}
