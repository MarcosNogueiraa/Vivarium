using Vivarium.Core.Gameplay;

namespace Vivarium.Core.Tests;

public class LevelCalculatorTests
{
    private static readonly LevelConfig Config = LevelConfig.Default;

    [Fact]
    public void XpForLevel_Nivel1_EhZero()
        => Assert.Equal(0, LevelCalculator.XpForLevel(1, Config));

    [Fact]
    public void XpForLevel_CresceComONivel()
    {
        long xp2 = LevelCalculator.XpForLevel(2, Config);
        long xp3 = LevelCalculator.XpForLevel(3, Config);
        long xp10 = LevelCalculator.XpForLevel(10, Config);
        Assert.True(xp2 > 0);
        Assert.True(xp3 > xp2);
        Assert.True(xp10 > xp3);
    }

    [Fact]
    public void LevelForXp_ZeroOuNegativo_EhNivel1()
    {
        Assert.Equal(1, LevelCalculator.LevelForXp(0, Config));
        Assert.Equal(1, LevelCalculator.LevelForXp(-5, Config));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(50)]
    public void LevelForXp_RoundTrip_ComXpForLevel(int level)
    {
        long xp = LevelCalculator.XpForLevel(level, Config);
        Assert.Equal(level, LevelCalculator.LevelForXp(xp, Config));
    }

    [Fact]
    public void LevelForXp_UmXpAntesDoProximoNivel_AindaNoNivelAtual()
    {
        long xpForLevel5 = LevelCalculator.XpForLevel(5, Config);
        Assert.Equal(4, LevelCalculator.LevelForXp(xpForLevel5 - 1, Config));
    }

    [Fact]
    public void LevelForXp_EhMonotonicoCrescente()
    {
        int prevLevel = 1;
        for (long xp = 0; xp <= 50_000; xp += 137)
        {
            int level = LevelCalculator.LevelForXp(xp, Config);
            Assert.True(level >= prevLevel);
            prevLevel = level;
        }
    }

    [Fact]
    public void ProgressOf_NoInicioDoNivel_ProgressoZero()
    {
        long xpForLevel5 = LevelCalculator.XpForLevel(5, Config);
        var (level, currentLevelXp, _, progress) = LevelCalculator.ProgressOf(xpForLevel5, Config);
        Assert.Equal(5, level);
        Assert.Equal(0, currentLevelXp);
        Assert.Equal(0.0, progress, 3);
    }

    [Fact]
    public void ProgressOf_NoFinalDoNivel_ProgressoPertoDeUm()
    {
        long xpForLevel5 = LevelCalculator.XpForLevel(5, Config);
        long xpForLevel6 = LevelCalculator.XpForLevel(6, Config);
        var (level, _, _, progress) = LevelCalculator.ProgressOf(xpForLevel6 - 1, Config);
        Assert.Equal(5, level);
        Assert.True(progress > 0.9);
        Assert.True(progress < 1.0);
    }

    [Fact]
    public void ProgressOf_Progresso01_SempreEntreZeroEUm()
    {
        for (long xp = 0; xp <= 100_000; xp += 331)
        {
            var (_, _, _, progress) = LevelCalculator.ProgressOf(xp, Config);
            Assert.InRange(progress, 0.0, 1.0);
        }
    }
}
