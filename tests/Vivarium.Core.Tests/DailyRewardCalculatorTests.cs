using Vivarium.Core.Gameplay;

namespace Vivarium.Core.Tests;

public class DailyRewardCalculatorTests
{
    private static readonly TickConfig Config = TickConfig.Default;

    [Fact]
    public void BaseAmount_TanqueVazio_UsaOPiso()
        => Assert.Equal(25m, DailyRewardCalculator.BaseAmount(0m, Config));

    [Fact]
    public void BaseAmount_RendaAlta_EscalaComAsHorasConfiguradas()
        => Assert.Equal(300m, DailyRewardCalculator.BaseAmount(100m, Config)); // 100 * 3h

    [Fact]
    public void RouletteRange_ProduzFaixaSimetricaAoRedorDoBase()
    {
        var (min, max) = DailyRewardCalculator.RouletteRange(100m, Config);
        Assert.Equal(60m, min);
        Assert.Equal(140m, max);
    }

    [Theory]
    [InlineData(0.0, 60)]   // roll mínimo → piso da faixa (-40%)
    [InlineData(1.0, 140)]  // roll máximo → teto da faixa (+40%)
    [InlineData(0.5, 100)]  // roll médio → sem desvio do base
    public void RouletteAmount_RespeitaAFaixaNosExtremosENoMeio(double roll, decimal expected)
        => Assert.Equal(expected, DailyRewardCalculator.RouletteAmount(100m, roll, Config));

    [Fact]
    public void StreakMultiplier_PrimeiroDia_SemBonus()
        => Assert.Equal(1.0, DailyRewardCalculator.StreakMultiplier(1, Config));

    [Fact]
    public void StreakMultiplier_CrescePorDiaConsecutivo()
    {
        Assert.Equal(1.05, DailyRewardCalculator.StreakMultiplier(2, Config), 6);
        Assert.Equal(1.10, DailyRewardCalculator.StreakMultiplier(3, Config), 6);
    }

    [Fact]
    public void StreakMultiplier_RespeitaOTeto()
        => Assert.Equal(1.50, DailyRewardCalculator.StreakMultiplier(1000, Config), 6);

    [Fact]
    public void NextStreak_SemResgateAnterior_ComecaEm1()
        => Assert.Equal(1, DailyRewardCalculator.NextStreak(0, null, DateTime.UtcNow));

    [Fact]
    public void NextStreak_ResgateOntem_SomaUm()
    {
        var now = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);
        var ontem = new DateTime(2026, 8, 16, 23, 0, 0, DateTimeKind.Utc);
        Assert.Equal(6, DailyRewardCalculator.NextStreak(5, ontem, now));
    }

    [Fact]
    public void NextStreak_LacunaDeDoisDias_ReiniciaEm1()
    {
        var now = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);
        var anteontem = new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);
        // Pedido explícito do usuário (17/08/2026): faltar um dia RESETA de verdade,
        // não é "recomeçar sem perder nada" — perde o bônus acumulado inteiro.
        Assert.Equal(1, DailyRewardCalculator.NextStreak(9, anteontem, now));
    }

    [Fact]
    public void NextStreak_MesmoDia_NaoAvanca()
    {
        var now = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);
        var maisCedoHoje = new DateTime(2026, 8, 17, 1, 0, 0, DateTimeKind.Utc);
        // Não deveria ser chamado nesse caso na prática (CanClaim já bloquearia), mas a função
        // pura não deve "avançar" streak pra um dia que não é consecutivo real.
        Assert.Equal(1, DailyRewardCalculator.NextStreak(3, maisCedoHoje, now));
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.0299, true)]
    [InlineData(0.03, false)]
    [InlineData(0.5, false)]
    public void RollsEgg_RespeitaOLimiarConfigurado(double roll, bool expected)
        => Assert.Equal(expected, DailyRewardCalculator.RollsEgg(roll, Config));

    [Fact]
    public void FinalAmount_CombinaBaseRoletaEStreakNaOrdemCerta()
    {
        // base=100 (renda alta), roll médio (sem desvio de roleta), streak 3 (+10%)
        decimal amount = DailyRewardCalculator.FinalAmount(coinsPerHour: 100m / 3m, streak: 3, rouletteRoll01: 0.5, Config);
        Assert.Equal(110m, amount);
    }

    [Fact]
    public void FinalAmount_NuncaFicaAbaixoDoPisoComStreakEBase()
    {
        decimal amount = DailyRewardCalculator.FinalAmount(coinsPerHour: 0m, streak: 1, rouletteRoll01: 0.0, Config);
        Assert.Equal(15m, amount); // piso 25 * 0.6 (roleta no mínimo)
    }
}
