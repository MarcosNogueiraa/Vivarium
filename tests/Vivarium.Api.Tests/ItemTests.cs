using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Tests;

public class ItemTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public ItemTests(VivariumApiFactory factory) => _factory = factory;

    private async Task GiveCurrency(long userId, string code, decimal amount)
    {
        await _factory.WithDbAsync(async db =>
        {
            int currencyId = await db.CurrencyTypes.Where(c => c.Code == code).Select(c => c.Id).FirstAsync();
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == currencyId);
            wallet.Amount += amount;
        });
    }

    private async Task FillTankAndBackpack(long userId, int tankCount, int backpackCount)
    {
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            for (int i = 0; i < tankCount; i++)
                db.CreatureInstances.Add(new CreatureInstance
                {
                    SpeciesId = 1, OwnerId = userId, OriginalOwnerId = userId, HabitatId = habitat.Id,
                    Seed = 71000 + i, TraitConfigVersion = 1, RarityScore = 3m,
                    TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(71000 + i)),
                    CreatedAt = DateTime.UtcNow,
                });
            for (int i = 0; i < backpackCount; i++)
                db.CreatureInstances.Add(new CreatureInstance
                {
                    SpeciesId = 1, OwnerId = userId, OriginalOwnerId = userId, HabitatId = null,
                    Seed = 72000 + i, TraitConfigVersion = 1, RarityScore = 3m,
                    TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(72000 + i)),
                    CreatedAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Catalogo_ListaOsOnzeItensDoMvp()
    {
        var (client, _) = await _factory.RegisterAsync("lojista1");

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");

        Assert.Equal(11, items!.Count);
        Assert.Contains(items, i => i.Key == "filter_basic" && i.Price == 20m);
        Assert.Contains(items, i => i.Key == "auto_filter" && i.Price == 500m && !i.Owned);
        Assert.Contains(items, i => i.Key == "auto_filter_2" && i.Price == 1200m && !i.Owned);
        Assert.Contains(items, i => i.Key == "auto_filter_3" && i.Price == 2500m && !i.Owned);
        Assert.Contains(items, i => i.Key == "tank_upgrade" && i.Price == 50m && !i.Locked);
        // Aquário Grande/Master: preço fixo (TransitionCost), bloqueados até a capacidade
        // atual (3, recém-registrado) chegar no respectivo teto.
        Assert.Contains(items, i => i.Key == "aquario_grande" && i.Price == 4000m && i.Locked && !i.Owned);
        Assert.Contains(items, i => i.Key == "aquario_master" && i.Price == 12000m && i.Locked && !i.Owned);
        // Sensor de Qualidade da Água (§8.18): preço por faixa (Aquário = 800), nunca bloqueado.
        Assert.Contains(items, i => i.Key == "water_sensor" && i.Price == 800m && !i.Locked && !i.Owned);
        // Ovo de peixe (BACKLOG.md #3): preço em PREMIUM, não SOFT.
        Assert.Contains(items, i => i.Key == "egg_common" && i.Price == 8m && i.Currency == "PREMIUM" && !i.Owned);
        Assert.Contains(items, i => i.Key == "egg_rare" && i.Price == 30m && i.Currency == "PREMIUM" && !i.Owned);
        Assert.Contains(items, i => i.Key == "egg_legendary" && i.Price == 90m && i.Currency == "PREMIUM" && !i.Owned);
    }

    [Fact]
    public async Task ComprarFiltro_RestauraQualidadeECobra()
    {
        var (client, userId) = await _factory.RegisterAsync("lojista2");
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            habitat.MaintenanceLevel = 30m;
        });

        var response = await client.PostAsync("/api/items/filter_basic/buy", null);
        response.EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.True(tank!.MaintenanceLevel >= 99m);
        Assert.Equal(80m, tank.Wallet["SOFT"]); // 100 - 20
    }

    [Fact]
    public async Task Filtro_PrecoEscalaComAFaixaDoTanque()
    {
        // 18/08/2026, pedido do usuário: "o valor da limpeza de 20 pode crescer com o tanque
        // maior" — 20 soft ficava irrisório num Aquário Master, onde a renda é bem maior.
        var (client, userId) = await _factory.RegisterAsync("lojista12");
        await GiveCurrency(userId, "SOFT", 200m);
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            habitat.Capacity = 8; // Aquário Grande (5-10) — FilterBasicPrice = 50
            habitat.MaintenanceLevel = 30m;
        });

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.Contains(items!, i => i.Key == "filter_basic" && i.Price == 50m);

        var walletBefore = (await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank"))!.Wallet["SOFT"];
        (await client.PostAsync("/api/items/filter_basic/buy", null)).EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(walletBefore - 50m, tank!.Wallet["SOFT"]);
    }

    [Fact]
    public async Task ComprarUpgrade_AumentaCapacidadeEPrecoSobe50PorCento()
    {
        var (client, _) = await _factory.RegisterAsync("lojista3");

        var response = await client.PostAsync("/api/items/tank_upgrade/buy", null);
        response.EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(4, tank!.Capacity);
        Assert.Equal(50m, tank.Wallet["SOFT"]); // 100 - 50

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.Equal(75m, items!.First(i => i.Key == "tank_upgrade").Price); // 50 × 1.5
    }

    [Fact]
    public async Task NoTetoDaFaixa_TankUpgradeFicaBloqueadoEAquarioGrandeVirouComprável()
    {
        var (client, userId) = await _factory.RegisterAsync("lojista9");
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            habitat.Capacity = 5; // teto do Aquário
        });

        // "Upgrade de tanque" (curva suave) não serve mais pra cruzar de faixa — bloqueado.
        var blocked = await client.PostAsync("/api/items/tank_upgrade/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.True(items!.First(i => i.Key == "tank_upgrade").Locked);
        Assert.False(items!.First(i => i.Key == "aquario_grande").Locked);
    }

    [Fact]
    public async Task ComprarAquarioGrande_ProdutoSeparado_CobraOCustoDeTransicao()
    {
        var (client, userId) = await _factory.RegisterAsync("lojista10");
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            habitat.Capacity = 5; // teto do Aquário — só agora "Aquário Grande" fica comprável
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == 1);
            wallet.Amount = 10000m;
        });

        var response = await client.PostAsync("/api/items/aquario_grande/buy", null);
        response.EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(6, tank!.Capacity);
        Assert.Equal(10000m - CapacityBands.AquarioGrande.TransitionCost, tank.Wallet["SOFT"]);

        // Depois de trocar, o item vira "owned" (não compra de novo) e "Upgrade de tanque"
        // volta a valer, já dentro do Aquário Grande.
        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.True(items!.First(i => i.Key == "aquario_grande").Owned);
        Assert.False(items!.First(i => i.Key == "tank_upgrade").Locked);
    }

    [Fact]
    public async Task ComprarAquarioGrande_AntesDoTeto_Retorna400()
    {
        var (client, _) = await _factory.RegisterAsync("lojista11");
        // Capacidade inicial é 3, ainda longe do teto do Aquário (5) — "Aquário Grande"
        // deveria estar bloqueado (Locked), não comprável.
        var response = await client.PostAsync("/api/items/aquario_grande/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AutoFilter_SaldoInsuficiente_Retorna400()
    {
        var (client, _) = await _factory.RegisterAsync("lojista4");

        // Custa 500, saldo inicial é 100
        var response = await client.PostAsync("/api/items/auto_filter/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AutoFilter_CompraDuplicada_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("lojista5");
        await _factory.WithDbAsync(async db =>
        {
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == 1);
            wallet.Amount = 2000m;
        });

        (await client.PostAsync("/api/items/auto_filter/buy", null)).EnsureSuccessStatusCode();

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.True(items!.First(i => i.Key == "auto_filter").Owned);

        var second = await client.PostAsync("/api/items/auto_filter/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task ComprarFiltroNivel2_NaoBloqueiaMesmoJaTendoNivel1()
    {
        var (client, userId) = await _factory.RegisterAsync("lojista7");
        await _factory.WithDbAsync(async db =>
        {
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == 1);
            wallet.Amount = 5000m;
        });

        (await client.PostAsync("/api/items/auto_filter/buy", null)).EnsureSuccessStatusCode();
        var second = await client.PostAsync("/api/items/auto_filter_2/buy", null);
        second.EnsureSuccessStatusCode();

        var items = await client.GetFromJsonAsync<List<ItemDto>>("/api/items/");
        Assert.True(items!.First(i => i.Key == "auto_filter").Owned);
        Assert.True(items!.First(i => i.Key == "auto_filter_2").Owned);
    }

    [Fact]
    public async Task UpgradeNoTetoDaCapacidade_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("lojista8");
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            habitat.Capacity = 15; // teto absoluto (CapacityBands.MaxCapacity)
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == 1);
            wallet.Amount = 100000m;
        });

        var response = await client.PostAsync("/api/items/tank_upgrade/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ItemInexistente_Retorna404()
    {
        var (client, _) = await _factory.RegisterAsync("lojista6");

        var response = await client.PostAsync("/api/items/nao_existe/buy", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private record BuyEggResponseDto(decimal Paid, CreatureRow? Creature);
    private record CreatureRow(long Id, decimal RarityScore);

    [Fact]
    public async Task ComprarOvo_GeraPeixeNoTanqueEDebitaPremium_NaoSoft()
    {
        var (client, userId) = await _factory.RegisterAsync("ovo1");
        await GiveCurrency(userId, "PREMIUM", 100m);

        var response = await client.PostAsync("/api/items/egg_common/buy", null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<BuyEggResponseDto>();
        Assert.Equal(8m, body!.Paid);
        Assert.NotNull(body.Creature);

        await _factory.WithDbAsync(async db =>
        {
            int premiumId = await db.CurrencyTypes.Where(c => c.Code == "PREMIUM").Select(c => c.Id).FirstAsync();
            int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
            var premiumWallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == premiumId);
            var softWallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == softId);
            Assert.Equal(92m, premiumWallet.Amount); // 100 - 8
            Assert.Equal(100m, softWallet.Amount); // saldo inicial intocado — ovo não usa soft

            var creature = await db.CreatureInstances.SingleAsync(c => c.Id == body.Creature!.Id);
            Assert.Equal(userId, creature.OwnerId);
            Assert.NotNull(creature.HabitatId); // tanque começa vazio (capacidade 3) — vai direto, sem passar por Inbox
        });
    }

    [Fact]
    public async Task ComprarOvo_SemSaldoPremium_Retorna400_NaoGeraPeixe()
    {
        var (client, userId) = await _factory.RegisterAsync("ovo2"); // premium inicial = 0

        var response = await client.PostAsync("/api/items/egg_common/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await _factory.WithDbAsync(async db =>
        {
            // O peixe inicial do registro fica na FILA (GenerationQueueItem), não vira
            // CreatureInstance até ser coletado — então 0 aqui é o estado limpo esperado.
            int count = await db.CreatureInstances.CountAsync(c => c.OwnerId == userId);
            Assert.Equal(0, count);
        });
    }

    [Fact]
    public async Task ComprarOvo_TanqueEMochilaCheios_Bloqueia400_NaoDebitaPremium()
    {
        var (client, userId) = await _factory.RegisterAsync("ovo3");
        await GiveCurrency(userId, "PREMIUM", 100m);
        // O peixe inicial do registro fica na FILA (GenerationQueueItem), não no tanque — o
        // tanque em si começa vazio. Tanque (capacidade 3) + mochila cheios.
        await FillTankAndBackpack(userId, tankCount: 3, backpackCount: HabitatDefaults.BackpackCapacity);

        var response = await client.PostAsync("/api/items/egg_legendary/buy", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await _factory.WithDbAsync(async db =>
        {
            int premiumId = await db.CurrencyTypes.Where(c => c.Code == "PREMIUM").Select(c => c.Id).FirstAsync();
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == premiumId);
            Assert.Equal(100m, wallet.Amount); // nada debitado — bloqueou antes de mexer na carteira
        });
    }

    [Fact]
    public async Task OvoLendario_RendeMaisRaroEmMediaQueOvoComum()
    {
        // Estatístico, não determinístico (seed aleatório) — compra várias unidades de cada
        // tier e compara a média. Amostra pequena o bastante pra rodar rápido nos testes, mas já
        // suficiente pra confirmar a ordem esperada (mesma coisa já validada com rigor estatístico
        // em Vivarium.Core.Tests/TraitGeneratorTests.GenerateBiased_AumentaRarityScoreMedio...).
        var (client, userId) = await _factory.RegisterAsync("ovo4");
        await GiveCurrency(userId, "PREMIUM", 5000m);
        await _factory.WithDbAsync(async db =>
        {
            var habitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");
            habitat.Capacity = 200; // espaço de sobra pra não esbarrar em tanque cheio no meio da amostra
            await db.SaveChangesAsync();
        });

        const int n = 30;
        decimal comumTotal = 0, lendarioTotal = 0;
        for (int i = 0; i < n; i++)
        {
            var r1 = await client.PostAsync("/api/items/egg_common/buy", null);
            r1.EnsureSuccessStatusCode();
            comumTotal += (await r1.Content.ReadFromJsonAsync<BuyEggResponseDto>())!.Creature!.RarityScore;

            var r2 = await client.PostAsync("/api/items/egg_legendary/buy", null);
            r2.EnsureSuccessStatusCode();
            lendarioTotal += (await r2.Content.ReadFromJsonAsync<BuyEggResponseDto>())!.Creature!.RarityScore;
        }

        Assert.True(lendarioTotal / n > comumTotal / n,
            $"Ovo Lendário (média {lendarioTotal / n}) deveria render mais que Ovo Comum (média {comumTotal / n})");
    }

    public record ItemDto(
        string Key, string Name, string Category, decimal Price, bool Owned,
        bool Locked = false, string? LockedReason = null, string Currency = "SOFT");
}
