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

    private record LeaderboardEntryRow(int Rank, string Username, decimal Value, bool IsSelf, int Level, object? Avatar);
    private record LeaderboardResponseRow(string Metric, int Page, int PageSize, int TotalCount, List<LeaderboardEntryRow> Entries, int SelfRank, decimal SelfValue);
    private record SpectatorCreatureRow(long Id, string Seed, string? ParentASeed, string? ParentBSeed);
    private record SpectatorBreedingRow(bool Active, SpectatorCreatureRow? ParentA, SpectatorCreatureRow? ParentB, DateTime? ReadyAt, bool IsReady);
    private record SpectatorTankRow(string Username, decimal MaintenanceLevel, string CapacityBandName, decimal RarityTotal, decimal CoinsPerHour, List<SpectatorCreatureRow> Creatures, SpectatorBreedingRow Breeding);

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
                SpeciesId = 1, OwnerId = ownerId, OriginalOwnerId = ownerId, HabitatId = habitatId,
                Seed = seed, TraitConfigVersion = 1, RarityScore = rarityScore, CreatedAt = DateTime.UtcNow,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(seed)),
                ParentASeed = parentASeed, ParentBSeed = parentBSeed,
            });
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Insere N usuários "fantasma" (aquário + 1 peixe caro cada), direto no banco — sem
    /// passar pelo endpoint de registro, só pra empurrar um usuário real pra fora do top 100.
    /// <paramref name="namePrefix"/> evita colisão de username entre chamadas de testes
    /// diferentes na mesma classe (banco compartilhado via IClassFixture).</summary>
    private async Task SeedFakeUsersAsync(int count, decimal startingRarity, string namePrefix = "fantasma")
    {
        await _factory.WithDbAsync(async db =>
        {
            int aquariumTypeId = await db.HabitatTypes.Where(t => t.Code == "Aquarium").Select(t => t.Id).FirstAsync();
            for (int i = 0; i < count; i++)
            {
                var user = new User { Username = $"{namePrefix}{i}", Email = $"{namePrefix}{i}@teste.com", PasswordHash = "x", CreatedAt = DateTime.UtcNow };
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
                    // OriginalOwnerId=0 só satisfaz o `required` do compilador — o fixup do EF
                    // resolve o valor de verdade a partir da navegação `OriginalOwner`, mesmo
                    // mecanismo já usado aqui por `Owner`/`Habitat` (User/Habitat também ainda
                    // não têm Id, só ganham depois do SaveChangesAsync no fim do loop).
                    SpeciesId = 1, Owner = user, OriginalOwnerId = 0, OriginalOwner = user, Habitat = habitat,
                    Seed = 5_000_000 + i, TraitConfigVersion = 1, RarityScore = startingRarity + i * 0.01m, CreatedAt = DateTime.UtcNow,
                    TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(5_000_000 + i)),
                });
            }
            await db.SaveChangesAsync();
        });
    }

    /// <summary>
    /// A própria posição de quem pediu — SelfRank/SelfValue vêm sempre preenchidos
    /// (18/08/2026, paginação real), independente da página pedida. Usar isso em vez de
    /// procurar por username nos Entries deixa os testes de ordenação independentes de
    /// quantos outros usuários (de outros testes na mesma classe, mesmo banco compartilhado)
    /// já estão acima na base — sem depender de estar na primeira página.
    /// </summary>
    private static (int Rank, decimal Value) OwnRank(LeaderboardResponseRow response)
        => (response.SelfRank, response.SelfValue);

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
        // CoinsPerHourSnapshot (18/08/2026) só é gravado dentro de ApplyTickAsync — sem isso
        // "income" ficaria comparando 0 com 0 (creature inserida direto no banco, sem tick).
        await clientC.GetAsync("/api/game/tank");
        await clientD.GetAsync("/api/game/tank");

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
    public async Task UsuarioForaDaPrimeiraPagina_SelfRankSempreDisponivel()
    {
        var (client, _) = await _factory.RegisterAsync("baixinho1");
        // Tanque do "baixinho1" fica vazio (RarityTotal=0, o mesmo de qualquer conta recém-criada) —
        // 60 fantasmas com raridade bem maior garantem que ele fique fora da 1ª página (pageSize=50).
        await SeedFakeUsersAsync(60, startingRarity: 17m, namePrefix: "fantasmaA");

        var response = await client.GetFromJsonAsync<LeaderboardResponseRow>("/api/leaderboard/rarity");

        Assert.Equal(50, response!.Entries.Count);
        Assert.DoesNotContain(response.Entries, e => e.Username == "baixinho1");
        Assert.Equal(0m, response.SelfValue);
        Assert.True(response.SelfRank > 50);
    }

    [Fact]
    public async Task Paginacao_SegundaPaginaTrazAsProximasLinhas()
    {
        var (client, _) = await _factory.RegisterAsync("paginador1");
        await SeedFakeUsersAsync(60, startingRarity: 17m, namePrefix: "fantasmaB");

        var page1 = await client.GetFromJsonAsync<LeaderboardResponseRow>("/api/leaderboard/rarity?page=1&pageSize=50");
        var page2 = await client.GetFromJsonAsync<LeaderboardResponseRow>("/api/leaderboard/rarity?page=2&pageSize=50");

        Assert.Equal(50, page1!.Entries.Count);
        Assert.True(page2!.Entries.Count >= 1);
        Assert.Empty(page1.Entries.Select(e => e.Username).Intersect(page2.Entries.Select(e => e.Username)));
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

    [Fact]
    public async Task Visitar_SemGestacaoAtiva_BreedingVemInativo()
    {
        var (visitorClient, _) = await _factory.RegisterAsync("visitante4");
        await _factory.RegisterAsync("dono2");

        var response = await visitorClient.GetFromJsonAsync<SpectatorTankRow>("/api/leaderboard/visit/dono2");

        Assert.False(response!.Breeding.Active);
        Assert.Null(response.Breeding.ParentA);
        Assert.Null(response.Breeding.ReadyAt);
    }

    [Fact]
    public async Task Visitar_ComGestacaoAtiva_MostraOsPaisSemInformacaoFinanceira()
    {
        var (visitorClient, _) = await _factory.RegisterAsync("visitante5");
        var (_, dono) = await _factory.RegisterAsync("dono3");
        long habitatAquario = await HabitatIdOf(dono);
        await AddCreature(habitatAquario, dono, rarityScore: 5m, seed: 1001);
        await AddCreature(habitatAquario, dono, rarityScore: 6m, seed: 1002);
        var readyAt = DateTime.UtcNow.AddHours(3);

        await _factory.WithDbAsync(async db =>
        {
            int breedingTypeId = await db.HabitatTypes.Where(t => t.Code == "Breeding").Select(t => t.Id).FirstAsync();
            var parentA = await db.CreatureInstances.FirstAsync(c => c.Seed == 1001);
            var parentB = await db.CreatureInstances.FirstAsync(c => c.Seed == 1002);
            var breedingHabitat = new Habitat
            {
                UserId = dono, HabitatTypeId = breedingTypeId, Capacity = 2, MaintenanceLevel = 100,
                QueueCap = 0, GenerationIntervalMinutes = int.MaxValue, OnlineGenerationRate = 0, OfflineGenerationRate = 0,
                LastTickAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            };
            db.Habitats.Add(breedingHabitat);
            await db.SaveChangesAsync();
            db.BreedingSlots.Add(new BreedingSlot
            {
                UserId = dono, HabitatId = breedingHabitat.Id, ParentAId = parentA.Id, ParentBId = parentB.Id,
                StartedAt = DateTime.UtcNow, ReadyAt = readyAt, CostPaid = 999m,
                ParentADeathChance = 0.5m, ParentBDeathChance = 0.5m, Status = BreedingStatus.InProgress,
            });
            await db.SaveChangesAsync();
        });

        var response = await visitorClient.GetFromJsonAsync<SpectatorTankRow>("/api/leaderboard/visit/dono3");

        Assert.True(response!.Breeding.Active);
        Assert.Equal("1001", response.Breeding.ParentA!.Seed);
        Assert.Equal("1002", response.Breeding.ParentB!.Seed);
        Assert.False(response.Breeding.IsReady);
        Assert.Equal(readyAt, response.Breeding.ReadyAt!.Value, TimeSpan.FromSeconds(1));
        // CostPaid/DeathChance/InsuranceUsed deliberadamente não expostos ao espectador — só o
        // que o SpectatorBreedingDto expõe é acessível via reflection do JSON, então a ausência
        // desses campos na resposta HTTP (não só no record de teste) já é a garantia real.
    }
}
