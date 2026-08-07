using Vivarium.Core.Gameplay;

namespace Vivarium.Core.Tests;

public class HabitatTickerTests
{
    private static readonly DateTime T0 = new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TickConfig Config = TickConfig.Default;

    // Intervalo fixo em 15 nos testes (validam a lógica do tick, não o default do jogo).
    private static HabitatTickState Estado(
        DateTime? heartbeat = null,
        decimal maintenance = 100m,
        decimal progress = 0m,
        int pending = 0,
        decimal filterCapacity = 0m,
        decimal fishWeight = 0m,
        decimal bandFactor = 1m)
        => new(
            LastTickAtUtc: T0,
            LastHeartbeatAtUtc: heartbeat,
            MaintenanceLevel: maintenance,
            GenerationProgressMinutes: progress,
            GenerationIntervalMinutes: 15,
            OnlineGenerationRate: HabitatDefaults.OnlineGenerationRate,
            OfflineGenerationRate: HabitatDefaults.OfflineGenerationRate,
            QueueCap: HabitatDefaults.QueueCap,
            PendingQueueCount: pending,
            FilterCapacity: filterCapacity,
            ActiveFishWeight: fishWeight,
            CapacityBandDegradationFactor: bandFactor);

    [Fact]
    public void JanelaInteiraOnline_GeraNaTaxaCheia()
    {
        // 60 min online com intervalo de 15 → 4 itens, sem resto
        var now = T0.AddMinutes(60);
        var outcome = HabitatTicker.ProcessTick(Estado(heartbeat: now.AddMinutes(-1)), now, new Random(1), Config);

        Assert.Equal(4, outcome.SpawnCount);
        Assert.Equal(0m, outcome.GenerationProgressMinutes);
    }

    [Fact]
    public void JanelaDividida_OnlineAteOHeartbeatOfflineDepois()
    {
        // Heartbeat parou aos 30 min: 30×1.0 + 30×0.45 = 43.5 min → 2 itens, resto 13.5
        var now = T0.AddMinutes(60);
        var outcome = HabitatTicker.ProcessTick(Estado(heartbeat: T0.AddMinutes(30)), now, new Random(1), Config);

        Assert.Equal(2, outcome.SpawnCount);
        Assert.Equal(13.5m, outcome.GenerationProgressMinutes);
    }

    [Fact]
    public void SemHeartbeat_JanelaInteiraOffline()
    {
        // 60×0.45 = 27 min → 1 item, resto 12
        var now = T0.AddMinutes(60);
        var outcome = HabitatTicker.ProcessTick(Estado(heartbeat: null), now, new Random(1), Config);

        Assert.Equal(1, outcome.SpawnCount);
        Assert.Equal(12m, outcome.GenerationProgressMinutes);
    }

    [Fact]
    public void HeartbeatAnteriorAoTick_NaoContaOnlineNegativo()
    {
        var now = T0.AddMinutes(30);
        var outcome = HabitatTicker.ProcessTick(Estado(heartbeat: T0.AddMinutes(-10)), now, new Random(1), Config);

        // 30×0.45 = 13.5, nenhum item ainda
        Assert.Equal(0, outcome.SpawnCount);
        Assert.Equal(13.5m, outcome.GenerationProgressMinutes);
    }

    [Fact]
    public void QualidadeDegrada_1PontoACada20Min()
    {
        var now = T0.AddMinutes(60);
        var outcome = HabitatTicker.ProcessTick(Estado(heartbeat: now, maintenance: 100m), now, new Random(1), Config);

        Assert.Equal(97m, outcome.MaintenanceLevel);
    }

    [Fact]
    public void FiltroAutomatico_DegradaMetade()
    {
        var now = T0.AddMinutes(60);
        var outcome = HabitatTicker.ProcessTick(
            Estado(heartbeat: now, maintenance: 100m, filterCapacity: 5m), now, new Random(1), Config);

        Assert.Equal(98.5m, outcome.MaintenanceLevel);
    }

    [Fact]
    public void FiltroComCoberturaParcial_BeneficioTaperaSuavemente()
    {
        // Filtro cobre 5, tanque pesa 10 (2x o excedente) — benefício fica entre o pleno
        // (0.5) e nenhum (1.0), nunca corta abruptamente pro sem-filtro.
        var now = T0.AddMinutes(60);
        var semFiltro = HabitatTicker.ProcessTick(
            Estado(heartbeat: now, maintenance: 100m, fishWeight: 10m), now, new Random(1), Config);
        var filtroParcial = HabitatTicker.ProcessTick(
            Estado(heartbeat: now, maintenance: 100m, fishWeight: 10m, filterCapacity: 5m), now, new Random(1), Config);
        var filtroTotal = HabitatTicker.ProcessTick(
            Estado(heartbeat: now, maintenance: 100m, fishWeight: 10m, filterCapacity: 10m), now, new Random(1), Config);

        Assert.True(filtroParcial.MaintenanceLevel > semFiltro.MaintenanceLevel);
        Assert.True(filtroParcial.MaintenanceLevel < filtroTotal.MaintenanceLevel);
    }

    [Fact]
    public void FaixaDeCapacidadeMaior_DegradaMaisRapido()
    {
        var now = T0.AddMinutes(60);
        var faixaBase = HabitatTicker.ProcessTick(
            Estado(heartbeat: now, maintenance: 100m, bandFactor: 1.0m), now, new Random(1), Config);
        var faixaGrande = HabitatTicker.ProcessTick(
            Estado(heartbeat: now, maintenance: 100m, bandFactor: 1.25m), now, new Random(1), Config);

        Assert.True(faixaGrande.MaintenanceLevel < faixaBase.MaintenanceLevel);
    }

    [Fact]
    public void DegradacaoEscalaComOsPeixes()
    {
        // Sem peixes: 60 min → -3 (100→97). Com peso 10 (ex: 10 comuns score~5, peso=score/5=1 cada):
        // fator 1+0.3×10=4 → -12 (100→88).
        var now = T0.AddMinutes(60);
        var vazio = HabitatTicker.ProcessTick(Estado(heartbeat: now, maintenance: 100m), now, new Random(1), Config);
        var cheio = HabitatTicker.ProcessTick(Estado(heartbeat: now, maintenance: 100m, fishWeight: 10m), now, new Random(1), Config);

        Assert.Equal(97m, vazio.MaintenanceLevel);
        Assert.Equal(88m, cheio.MaintenanceLevel);
    }

    [Fact]
    public void QualidadeNaoFicaNegativa()
    {
        var now = T0.AddMinutes(600);
        var outcome = HabitatTicker.ProcessTick(Estado(heartbeat: now, maintenance: 5m), now, new Random(1), Config);

        Assert.Equal(0m, outcome.MaintenanceLevel);
    }

    [Fact]
    public void QualidadeBaixa_GeraNaMetadeDaVelocidade()
    {
        // Abaixo de 40: 60 min online × 0.5 = 30 min efetivos → 2 itens
        var now = T0.AddMinutes(60);
        var outcome = HabitatTicker.ProcessTick(Estado(heartbeat: now, maintenance: 30m), now, new Random(1), Config);

        Assert.Equal(2, outcome.SpawnCount);
    }

    [Fact]
    public void FilaCheia_NaoGeraENaoAcumulaProgressoAlemDeUmIntervalo()
    {
        var now = T0.AddMinutes(120);
        var outcome = HabitatTicker.ProcessTick(Estado(heartbeat: now, pending: 5), now, new Random(1), Config);

        Assert.Equal(0, outcome.SpawnCount);
        Assert.Equal(15m, outcome.GenerationProgressMinutes);
    }

    [Fact]
    public void EspacoParcialNaFila_GeraSoOQueCabe()
    {
        // 60 min renderiam 4, mas só há 2 vagas
        var now = T0.AddMinutes(60);
        var outcome = HabitatTicker.ProcessTick(Estado(heartbeat: now, pending: 3), now, new Random(1), Config);

        Assert.Equal(2, outcome.SpawnCount);
    }

    [Fact]
    public void RelogioNaoAvancou_NadaMuda()
    {
        var estado = Estado(heartbeat: T0, maintenance: 80m, progress: 7m);
        var outcome = HabitatTicker.ProcessTick(estado, T0, new Random(1), Config);

        Assert.Equal(0, outcome.SpawnCount);
        Assert.Equal(80m, outcome.MaintenanceLevel);
        Assert.Equal(7m, outcome.GenerationProgressMinutes);
    }

    [Fact]
    public void QualidadeCritica_ParteDosItensNasceDoente()
    {
        // Abaixo de 15 com 10% de chance: em muitos spawns, alguns doentes, não todos
        int sick = 0, total = 0;
        var rng = new Random(42);
        for (int i = 0; i < 300; i++)
        {
            var now = T0.AddMinutes(120);
            var outcome = HabitatTicker.ProcessTick(Estado(heartbeat: now, maintenance: 10m), now, rng, Config);
            sick += outcome.SpawnSickFlags.Count(f => f);
            total += outcome.SpawnCount;
        }

        Assert.True(total > 0);
        Assert.InRange(sick / (double)total, 0.05, 0.16);
    }

    [Fact]
    public void QualidadeSaudavel_NenhumItemDoente()
    {
        var now = T0.AddMinutes(120);
        var outcome = HabitatTicker.ProcessTick(Estado(heartbeat: now, maintenance: 50m), now, new Random(1), Config);

        Assert.True(outcome.SpawnCount > 0);
        Assert.All(outcome.SpawnSickFlags, f => Assert.False(f));
    }

    [Fact]
    public void IsOnline_RespeitaOTimeout()
    {
        var now = T0;
        Assert.True(HabitatTicker.IsOnline(now.AddMinutes(-2), now, Config));
        Assert.False(HabitatTicker.IsOnline(now.AddMinutes(-4), now, Config));
        Assert.False(HabitatTicker.IsOnline(null, now, Config));
    }
}

public class CapacityBandsTests
{
    [Theory]
    [InlineData(3, "Aquário")]
    [InlineData(5, "Aquário")]
    [InlineData(6, "Aquário Grande")]
    [InlineData(10, "Aquário Grande")]
    [InlineData(11, "Aquário Master")]
    [InlineData(15, "Aquário Master")]
    public void BandFor_ResolveAFaixaCorreta(int capacity, string expectedName)
        => Assert.Equal(expectedName, CapacityBands.BandFor(capacity).Name);

    [Fact]
    public void MaxCapacity_EhOTetoDaUltimaFaixa()
        => Assert.Equal(15, CapacityBands.MaxCapacity);

    [Fact]
    public void BandFor_AcimaDoTeto_RetornaAUltimaFaixa()
        => Assert.Equal(CapacityBands.AquarioMaster, CapacityBands.BandFor(999));
}

public class CreatureCollectorTests
{
    [Fact]
    public void ColetaSaudavel_UsaOPrimeiroSeed()
    {
        var seeds = new Queue<long>([111L, 222L]);
        var creature = CreatureCollector.Collect(isSick: false, seeds.Dequeue);

        Assert.Equal(111L, creature.Seed);
        Assert.Single(seeds); // segundo seed não foi consumido
    }

    [Fact]
    public void ColetaDoente_FicaComOMenorRarityDosDoisSeeds()
    {
        // Com desvantagem, o resultado nunca supera o melhor dos dois isolados
        for (long baseSeed = 1; baseSeed <= 50; baseSeed++)
        {
            long a = baseSeed * 7919, b = baseSeed * 104729;
            var seeds = new Queue<long>([a, b]);
            var creature = CreatureCollector.Collect(isSick: true, seeds.Dequeue);

            var scoreA = CreatureCollector.Collect(false, () => a).RarityScore;
            var scoreB = CreatureCollector.Collect(false, () => b).RarityScore;
            Assert.Equal(Math.Min(scoreA, scoreB), creature.RarityScore);
        }
    }

    [Fact]
    public void SeedAleatorio_SempreNaoNegativo()
    {
        for (int i = 0; i < 1000; i++)
            Assert.True(CreatureCollector.NewRandomSeed() >= 0);
    }
}
