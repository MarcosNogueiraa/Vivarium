using Vivarium.Core.Gameplay;

namespace Vivarium.Core.Tests;

public class RarityBandsTests
{
    [Theory]
    [InlineData(0, RarityBand.Comum)]
    [InlineData(5.44, RarityBand.Comum)]
    [InlineData(5.45, RarityBand.Incomum)]
    [InlineData(12.03, RarityBand.Incomum)]
    [InlineData(12.04, RarityBand.Raro)]
    [InlineData(13.77, RarityBand.Raro)]
    [InlineData(13.78, RarityBand.Epico)]
    [InlineData(16.59, RarityBand.Epico)]
    [InlineData(16.60, RarityBand.Lendario)]
    [InlineData(30, RarityBand.Lendario)]
    public void BandOf_CortesBatemComCLAUDEmd(double score, RarityBand expected)
        => Assert.Equal(expected, RarityBands.BandOf((decimal)score));

    [Theory]
    [InlineData(0, "Comum")]
    [InlineData(12.04, "Raro")]
    [InlineData(16.60, "Lendário")]
    public void NameOf_BateComBandOf(double score, string expected)
        => Assert.Equal(expected, RarityBands.NameOf((decimal)score));
}
