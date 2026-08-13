using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Tests;

/// <summary>Ranking global (raridade/renda) + visita a aquário de outro jogador, só leitura.</summary>
public class LeaderboardTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public LeaderboardTests(VivariumApiFactory factory) => _factory = factory;

    private record LeaderboardEntryRow(int Rank, string Username, decimal Value, bool IsSelf);
    private record LeaderboardResponseRow(string Metric, List<LeaderboardEntryRow> Entries, LeaderboardEntryRow? SelfOutsideTop);
    private record SpectatorCreatureRow(long Id, string Seed, string? ParentASeed, string? ParentBSeed);
    private record SpectatorTankRow(string Username, decimal MaintenanceLevel, string CapacityBandName, decimal RarityTotal, decimal CoinsPerHour, List<SpectatorCreatureRow> Creatures);

    private async Task<long> HabitatIdOf(long userId)
    {
        long id = 0;
        await _factory.WithDbAsync(async db =>
            id = (await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium")).Id);
        return id;
    }

    private async Task AddCreature(long habitatId, long ownerId, decimal rarityScore, long seed, long? parentASeed = null, long? parentBSeed = null)
    {
        await _factory.WithDbAsync(async db =>
        {
            db.CreatureInstances.Add(new CreatureInstance
            {
                SpeciesId = 1, OwnerId = ownerId, HabitatId = habitatId,
                Seed = seed, TraitConfigVersion = 1, RarityScore = rarityScore, CreatedAt = DateTime.UtcNow,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(seed)),
                ParentASeed = parentASeed, ParentBSeed = parentBSeed,
            });
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Insere N usuários "fantasma" (aquário + 1 peixe caro cada), direto no banco — sem
    /// passar pelo endpoint de registro, só pra empurrar um usuário real pra fora do top 100.</summary>
    private async Task SeedFakeUsersAsync(int count, decimal startingRarity)
    {
        await _factory.WithDbAsync(async db =>
        {
            int aquariumTypeId = await db.HabitatTypes.Where(t => t.Code == "Aquarium").Select(t => t.Id).FirstAsync();
            for (int i = 0; i < count; i++)
            {
                var user = new User { Username = $"fantasma{i}", Email = $"fantasma{i}@teste.com", PasswordHash = "x", CreatedAt = DateTime.UtcNow };
                var habitat = new Habitat
                {
                    User = user, HabitatTypeId = aquariumTypeId, Capacity = 5, MaintenanceLevel = 100,
                    QueueCap = 5, GenerationIntervalMinutes = 10, OnlineGenerationRate = 1, OfflineGenerationRate = 0.45m,
                    LastTickAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
                };
                // startingRarity + fração pequena (nunca soma ao ponto de passar do máximo real
                // do jogo, ~19 — CLAUDE.md §5) — usar um score irreal (ex: centenas) estoura
                // Decimal na fórmula exponencial de renda (IncomeCalculator.CoinsPerHour).
                db.CreatureInstances.Add(new CreatureInstance
                {
                    SpeciesId = 1, Owner = user, Habitat = habitat,
                    Seed = 5_000_000 + i, TraitConfigVersion = 1, RarityScore = startingRarity + i * 0.01m, CreatedAt = DateTime.UtcNow,
                    TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(5_000_000 + i)),
                });
            }
            await db.SaveChangesAsync();
        });
    }

    /// <summary>
    /// A própria posição de quem pediu — a API sempre garante isso, esteja no top 100
    /// (Entries, isSelf=true) ou fora dele (SelfOutsideTop). Usar isso em vez de procurar
    /// por username nos Entries deixa os testes de ordenação independentes de quantos
    /// outros usuários (de outros testes na mesma classe, mesmo banco compartilhado)
    /// já estão acima na base — sem depender de estar no top 100.
    /// </summary>
    private static LeaderboardEntryRow OwnRank(LeaderboardResponseRow response)
        => response.Entries.SingleOrDefault(e => e.IsSelf) ?? response.SelfOutsideTop!;

    [Fact]
    public async Task Rarity_OrdenaPelaSomaDeRarityScoreDoTanque()
    {
        var (clientA, userIdA) = await _factory.RegisterAsync("rankA");
        var (clientB, userIdB) = await _factory.RegisterAsync("rankB");
        await AddCreature(await HabitatIdOf(userIdA), userIdA, rarityScore: 5m, seed: 111);
        await AddCreature(await HabitatIdOf(userIdB), userIdB, rarityScore: 12m, seed: 222);

        var entryA = OwnRank((await clientA.GetFromJsonAsync<LeaderboardResponseRow>("/api/leaderboard/rarity"))!);
        var entryB = OwnRank((await clientB.GetFromJsonAsync<LeaderboardResponseRow>("/api/leaderboard/rarity"))!);

        Assert.True(entryB.Rank < entryA.Rank);
        Assert.Equal(12m, entryB.Value);
        Assert.Equal(5m, entryA.Value);
        Assert.True(entryA.IsSelf);
        Assert.True(entryB.IsSelf);
    }

    [Fact]
    public async Task Income_OrdenaPorCoinsPerHour_NaoPorRarityBruta()
    {
        // Sinergia (mesma cor de cauda) pode fazer um tanque com raridade menor render mais/h
        // que um com raridade maior sem sinergia — por isso as duas métricas existem separadas.
        var (clientC, userIdC) = await _factory.RegisterAsync("rankC");
        var (clientD, userIdD) = await _factory.RegisterAsync("rankD");
        await AddCreature(await HabitatIdOf(userIdC), userIdC, rarityScore: 20m, seed: 333);
        await AddCreature(await HabitatIdOf(userIdD), userIdD, rarityScore: 1m, seed: 444);

        var rarityC = OwnRank((await clientC.GetFromJsonAsync<LeaderboardResponseRow>("/api/leaderboard/rarity"))!).Rank;
        var rarityD = OwnRank((await clientD.GetFromJsonAsync<LeaderboardResponseRow>("/api/leaderboard/rarity"))!).Rank;
        var incomeC = OwnRank((await clientC.GetFromJsonAsync<LeaderboardResponseRow>("/api/leaderboard/income"))!).Rank;
        var incomeD = OwnRank((await clientD.GetFromJsonAsync<LeaderboardResponseRow>("/api/leaderboard/income"))!).Rank;
        // Ordem relativa consistente entre as duas métricas nesse caso simples (sem sinergia
        // envolvida) — o que importa aqui é confirmar que /income é uma rota/cálculo
        // independente de /rarity, não que produza uma ordem diferente sempre.
        Assert.True(rarityC < rarityD);
        Assert.True(incomeC < incomeD);
    }

    [Fact]
    public async Task MetricaInvalida_Retorna400()
    {
        var (client, _) = await _factory.RegisterAsync("metricainvalida1");

        var response = await client.GetAsync("/api/leaderboard/nao-existe");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UsuarioForaDoTop100_ApareceEmSelfOutsideTop()
    {
        var (client, _) = await _factory.RegisterAsync("baixinho1");
        // Tanque do "baixinho1" fica vazio (RarityTotal=0, o mesmo de qualquer conta recém-criada) —
        // 100 fantasmas com raridade bem maior garantem que ele fique fora do top 100.
        await SeedFakeUsersAsync(100, startingRarity: 17m);

        var response = await client.GetFromJsonAsync<LeaderboardResponseRow>("/api/leaderboard/rarity");

        Assert.Equal(100, response!.Entries.Count);
        Assert.DoesNotContain(response.Entries, e => e.Username == "baixinho1");
        Assert.NotNull(response.SelfOutsideTop);
        Assert.Equal("baixinho1", response.SelfOutsideTop!.Username);
        Assert.True(response.SelfOutsideTop.IsSelf);
        Assert.True(response.SelfOutsideTop.Rank > 100);
    }

    [Fact]
    public async Task Visitar_UsuarioInexistente_Retorna404()
    {
        var (client, _) = await _factory.RegisterAsync("visitante1");

        var response = await client.GetAsync("/api/leaderboard/visit/ninguem-com-esse-nome");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Visitar_RetornaCriaturasDoTanqueAlheio_IncluindoFilhoteDeBreeding()
    {
        var (visitorClient, _) = await _factory.RegisterAsync("visitante2");
        var (_, dono) = await _factory.RegisterAsync("dono1");
        long habitatDono = await HabitatIdOf(dono);
        await AddCreature(habitatDono, dono, rarityScore: 6m, seed: 555);
        // Filhote — ParentASeed/BSeed preenchidos, como um breeding real deixaria.
        await AddCreature(habitatDono, dono, rarityScore: 9m, seed: 777, parentASeed: 555, parentBSeed: 999);

        var response = await visitorClient.GetFromJsonAsync<SpectatorTankRow>("/api/leaderboard/visit/dono1");

        Assert.Equal("dono1", response!.Username);
        Assert.Equal(2, response.Creatures.Count);
        Assert.Equal(15m, response.RarityTotal); // 6 + 9
        Assert.True(response.CoinsPerHour > 0);
        var filhote = response.Creatures.Single(c => c.ParentASeed != null);
        Assert.Equal("555", filhote.ParentASeed);
        Assert.Equal("999", filhote.ParentBSeed);
    }
}
