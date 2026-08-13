using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Tests;

public class GameTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public GameTests(VivariumApiFactory factory) => _factory = factory;

    private async Task<long> HabitatIdOf(long userId)
    {
        long id = 0;
        await _factory.WithDbAsync(async db =>
            id = (await db.Habitats.FirstAsync(h => h.UserId == userId)).Id);
        return id;
    }

    private async Task<long> InserirItemProntoNaFila(long habitatId, bool sick = false)
    {
        long itemId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var item = new GenerationQueueItem
            {
                HabitatId = habitatId,
                SpeciesId = 1,
                ReadyAt = DateTime.UtcNow.AddMinutes(-1),
                Status = QueueItemStatus.Pending,
                IsSick = sick,
            };
            db.GenerationQueueItems.Add(item);
            await db.SaveChangesAsync();
            itemId = item.Id;
        });
        return itemId;
    }

    [Fact]
    public async Task Heartbeat_MarcaTanqueComoOnline()
    {
        var (client, _) = await _factory.RegisterAsync("online1");

        var response = await client.PostAsync("/api/game/heartbeat", null);
        response.EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.True(tank!.Online);
    }

    [Fact]
    public async Task Coleta_ItemPronto_CriaCriaturaComSeedERaridade()
    {
        var (client, userId) = await _factory.RegisterAsync("coletor1");
        long habitatId = await HabitatIdOf(userId);
        long itemId = await InserirItemProntoNaFila(habitatId);

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        var item = tank!.Queue.Single(q => q.Id == itemId);
        Assert.True(item.IsReady);

        var response = await client.PostAsync($"/api/game/collect/{item.Id}", null);
        response.EnsureSuccessStatusCode();
        var creature = await response.Content.ReadFromJsonAsync<AuthTests.CreatureDto>();

        Assert.True(long.Parse(creature!.Seed) >= 0);
        Assert.True(creature.RarityScore > 0);
        Assert.Equal(Vivarium.Core.Generation.TraitConfigV1.Version, creature.TraitConfigVersion);

        tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Single(tank!.Creatures);
        Assert.Single(tank.Queue); // sobra o peixe inicial do registro, ainda não coletado
    }

    [Fact]
    public async Task Coleta_TanqueCheio_VaiParaMochila()
    {
        var (client, userId) = await _factory.RegisterAsync("cheio1");
        long habitatId = await HabitatIdOf(userId);

        await _factory.WithDbAsync(db =>
        {
            for (int i = 0; i < 3; i++)
            {
                db.CreatureInstances.Add(new CreatureInstance
                {
                    SpeciesId = 1, OwnerId = userId, HabitatId = habitatId,
                    Seed = 1000 + i, TraitConfigVersion = 1, RarityScore = 3m,
                    CreatedAt = DateTime.UtcNow,
                });
            }
            return Task.CompletedTask;
        });
        long itemId = await InserirItemProntoNaFila(habitatId);

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        var item = tank!.Queue.Single(q => q.Id == itemId);

        // Tanque cheio: coleta vai pra mochila, não dá erro
        var response = await client.PostAsync($"/api/game/collect/{item.Id}", null);
        response.EnsureSuccessStatusCode();

        tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(3, tank!.Creatures.Count); // tanque não estourou
    }

    [Fact]
    public async Task Coleta_ItemJaColetado_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("coletor2");
        long habitatId = await HabitatIdOf(userId);
        long itemId = await InserirItemProntoNaFila(habitatId);

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        var item = tank!.Queue.Single(q => q.Id == itemId);

        (await client.PostAsync($"/api/game/collect/{item.Id}", null)).EnsureSuccessStatusCode();
        var second = await client.PostAsync($"/api/game/collect/{item.Id}", null);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Vip_Online_ColetaAutomaticamenteNoTick()
    {
        var (client, userId) = await _factory.RegisterAsync("vip1");
        long habitatId = await HabitatIdOf(userId);

        await _factory.WithDbAsync(async db =>
        {
            db.VipSubscriptions.Add(new VipSubscription
            {
                UserId = userId,
                StartAt = DateTime.UtcNow.AddDays(-1),
                EndAt = DateTime.UtcNow.AddDays(30),
                Status = SubscriptionStatus.Active,
            });
            var habitat = await db.Habitats.FirstAsync(h => h.Id == habitatId);
            habitat.LastHeartbeatAt = DateTime.UtcNow; // online
        });
        await InserirItemProntoNaFila(habitatId);

        // O GET do tanque roda o tick lazy; VIP online deve coletar sozinho —
        // inclusive o peixe inicial do registro, que também estava pendente.
        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");

        Assert.Empty(tank!.Queue);
        Assert.Equal(2, tank.Creatures.Count);
    }

    [Fact]
    public async Task Vip_Online_TanqueCheio_ColetaAutomaticaCaiParaMochilaEmVezDeTravar()
    {
        // Bug real corrigido 12/08/2026 (relatado pelo usuário): a coleta automática VIP
        // (CollectAllReadyAsync) parava assim que o tanque enchia (break), diferente da coleta
        // MANUAL, que sempre cai pra mochila — resultado: fila travada pra sempre com o tanque
        // cheio, mesmo com espaço de sobra na mochila e o jogador online o tempo todo.
        var (client, userId) = await _factory.RegisterAsync("vip2");
        long habitatId = await HabitatIdOf(userId);

        await _factory.WithDbAsync(async db =>
        {
            db.VipSubscriptions.Add(new VipSubscription
            {
                UserId = userId,
                StartAt = DateTime.UtcNow.AddDays(-1),
                EndAt = DateTime.UtcNow.AddDays(30),
                Status = SubscriptionStatus.Active,
            });
            var habitat = await db.Habitats.FirstAsync(h => h.Id == habitatId);
            habitat.LastHeartbeatAt = DateTime.UtcNow; // online
        });
        // Capacidade padrão do tanque é 3; o registro já deixa 1 item pronto na fila (8.13) —
        // insere mais 4 pra garantir itens sobrando depois de encher o tanque.
        for (int i = 0; i < 4; i++)
            await InserirItemProntoNaFila(habitatId);

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");

        Assert.Empty(tank!.Queue); // nada preso na fila
        Assert.Equal(3, tank.Creatures.Count); // tanque cheio (capacidade 3)

        var backpack = await client.GetFromJsonAsync<Vivarium.Api.Contracts.BackpackResponse>("/api/game/backpack");
        Assert.Equal(2, backpack!.Creatures.Count); // os 2 excedentes foram pra mochila, não travaram
    }

    [Fact]
    public async Task FreeOnline_NaoColetaAutomaticamente()
    {
        var (client, userId) = await _factory.RegisterAsync("free1");
        long habitatId = await HabitatIdOf(userId);

        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.Id == habitatId);
            habitat.LastHeartbeatAt = DateTime.UtcNow;
        });
        await InserirItemProntoNaFila(habitatId);

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");

        // Sem VIP, nada é auto-coletado — nem o item inserido, nem o peixe inicial do registro.
        Assert.Equal(2, tank!.Queue.Count);
        Assert.Empty(tank.Creatures);
    }
}
