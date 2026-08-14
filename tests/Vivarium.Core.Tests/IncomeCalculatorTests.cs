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
        double lendario = IncomeCalculator.CoinsPerHour((decimal)Cfg.IncomeLegendaryTaperScore, Cfg); // início do Lendário

        Assert.True(comum < raro && raro < lendario);
        Assert.InRange(comum, 1.4, 2.0);           // base 1.5/h
        Assert.True(lendario > 90);                // lendário rende muito mais (growth 0.42, 06/08/2026)
        Assert.True(lendario / comum > 25);        // gap enorme comum→lendário
    }

    [Fact]
    public void Renda_TaperDoLendario_ComprimeVariacaoAcimaDoPiso()
    {
        decimal taperScore = (decimal)Cfg.IncomeLegendaryTaperScore;
        double piso = IncomeCalculator.CoinsPerHour(taperScore, Cfg);       // início do Lendário
        double meio = IncomeCalculator.CoinsPerHour(taperScore + 2.25m, Cfg);
        double topoObservado = IncomeCalculator.CoinsPerHour(taperScore + 6.25m, Cfg); // ~máx observado (100k/1M seeds)

        // Piso do taper subiu de ~100/h pra ~137/h (13/08/2026) — consequência esperada de
        // empurrar IncomeLegendaryTaperScore de 14.0 pra 14.75 (mesma curva exponencial
        // normal, só compõe por mais distância antes do taper entrar): não é regressão, é o
        // Lendário renovado ficar mais raro (0.15% vs 0.2%) render um pouco mais no piso também.
        Assert.InRange(piso, 130, 145);
        Assert.True(meio > piso && topoObservado > meio); // ainda crescente, não achata de vez
        Assert.True(topoObservado < 300);          // teto comprimido (era ~1958/h sem taper)
        Assert.True(topoObservado / piso < 2.5);    // variação interna do Lendário: era 19.6x, agora <2.5x

        // Contínuo no ponto de corte: sem salto ao cruzar o piso do taper.
        double logoAbaixo = IncomeCalculator.CoinsPerHour(taperScore - 0.001m, Cfg);
        double logoAcima = IncomeCalculator.CoinsPerHour(taperScore + 0.001m, Cfg);
        Assert.True(Math.Abs(logoAcima - logoAbaixo) < 0.1);

        // Abaixo do piso, comportamento idêntico ao de antes (não mexe em Épico pra baixo).
        double epico = IncomeCalculator.CoinsPerHour(11m, Cfg);
        double esperadoEpico = Cfg.IncomeBasePerHour * Math.Exp(Cfg.IncomeGrowth * (11.0 - Cfg.IncomeRefScore));
        Assert.Equal(esperadoEpico, epico, 6);
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
    public void FatorAgua_SemPerdaNoPatamar_QuedaSuaveAbaixo()
    {
        // 80-100%: sem perda de renda — água "quase perfeita" não é punida.
        Assert.Equal(1.0, IncomeCalculator.WaterFactor(80m, Cfg), 6);
        Assert.Equal(1.0, IncomeCalculator.WaterFactor(90m, Cfg), 6);
        Assert.Equal(1.0, IncomeCalculator.WaterFactor(100m, Cfg), 6);
        // Logo abaixo do patamar já cai (sem penhasco: contínuo em 80%, não um salto).
        Assert.True(IncomeCalculator.WaterFactor(79m, Cfg) < 1.0);
        Assert.True(IncomeCalculator.WaterFactor(70m, Cfg) < IncomeCalculator.WaterFactor(79m, Cfg));
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
        decimal online = IncomeCalculator.Accrue(fish, 100m, 100m, 60m, 0m, 1.0m, 0.45m, Cfg);
        decimal offline = IncomeCalculator.Accrue(fish, 100m, 100m, 0m, 60m, 1.0m, 0.45m, Cfg);

        Assert.True(offline < online);
        Assert.Equal(0.45, (double)(offline / online), 2);
    }

    [Fact]
    public void Accrue_OfflineComAguaDecaida_UsaMediaDoFator()
    {
        // Ausência longa: água caiu de 100 -> 0 na janela. A renda offline usa a MÉDIA
        // do fator (não a água cheia do início), então rende menos que se a água tivesse
        // ficado cheia, mas mais que zero.
        var fish = Distinct(6m);
        decimal decaiu = IncomeCalculator.Accrue(fish, 100m, 0m, 0m, 8 * 60m, 1.0m, 0.45m, Cfg);
        decimal aguaCheia = IncomeCalculator.Accrue(fish, 100m, 100m, 0m, 8 * 60m, 1.0m, 0.45m, Cfg);

        Assert.True(decaiu > 0m);
        Assert.True(decaiu < aguaCheia);
        // fator médio = (WaterFactor(100) + WaterFactor(0))/2 = (1 + 0)/2 = 0.5
        Assert.Equal(0.5, (double)(decaiu / aguaCheia), 3);
    }

    [Fact]
    public void Accrue_AguaSecaZeraRenda()
    {
        decimal earned = IncomeCalculator.Accrue(Distinct(8m), 0m, 0m, 120m, 0m, 1.0m, 0.45m, Cfg);
        Assert.Equal(0m, earned);
    }

    [Fact]
    public void Accrue_TetoOfflineDe8Horas()
    {
        var fish = Distinct(6m);
        decimal tresDias = IncomeCalculator.Accrue(fish, 100m, 100m, 0m, 3 * 24 * 60m, 1.0m, 0.45m, Cfg);
        decimal oitoHoras = IncomeCalculator.Accrue(fish, 100m, 100m, 0m, 8 * 60m, 1.0m, 0.45m, Cfg);

        Assert.Equal(oitoHoras, tresDias);
    }

    [Fact]
    public void Accrue_SemPeixes_SemJanela_RendeZero()
    {
        Assert.Equal(0m, IncomeCalculator.Accrue([], 100m, 100m, 60m, 0m, 1.0m, 0.45m, Cfg));
        Assert.Equal(0m, IncomeCalculator.Accrue(Distinct(5m), 100m, 100m, 0m, 0m, 1.0m, 0.45m, Cfg));
    }
}
