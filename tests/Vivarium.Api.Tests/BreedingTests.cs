using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Tests;

public class BreedingTests : IClassFixture<VivariumApiFactory>
{
    private readonly VivariumApiFactory _factory;

    public BreedingTests(VivariumApiFactory factory) => _factory = factory;

    private record CreatureDto(
        long Id, int SpeciesId, string Seed, int TraitConfigVersion, decimal RarityScore, DateTime CreatedAt,
        bool IsBred, string? ParentASeed, string? ParentBSeed, int BreedCount);
    private record BreedingSlotDto(
        long Id, CreatureDto ParentA, CreatureDto ParentB, DateTime StartedAt, DateTime ReadyAt, bool IsReady, decimal CostPaid,
        decimal ParentADeathChance, decimal ParentBDeathChance, bool InsuranceUsed);
    private record BreedingStatusDto(bool Active, BreedingSlotDto? Slot);
    private record StartResultDto(long SlotId, DateTime ReadyAt, decimal CostPaid, decimal InsuranceCostPaid);
    private record CollectBreedingResponse(CreatureDto Child, bool ParentADied, bool ParentBDied);
    private record BreedingQuoteDto(
        decimal CostSoft, double GestationHours, DateTime EstimatedReadyAt,
        Dictionary<string, double> ChildTierProbabilities,
        int ParentABreedCount, double ParentADeathChance,
        int ParentBBreedCount, double ParentBDeathChance,
        decimal StabilizerCostSoft, double StabilizerReductionFactor, decimal InsuranceCostPremium);
    private record ErrorDto(string Error);

    private async Task<long> HabitatIdOf(long userId)
    {
        long id = 0;
        await _factory.WithDbAsync(async db =>
            id = (await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium")).Id);
        return id;
    }

    private async Task<long> CreateOwnedCreature(long userId, decimal rarityScore, long seed)
    {
        long habitatId = await HabitatIdOf(userId);
        long creatureId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var c = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, HabitatId = habitatId,
                Seed = seed, TraitConfigVersion = 1, RarityScore = rarityScore,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(seed)),
                CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(c);
            await db.SaveChangesAsync();
            creatureId = c.Id;
        });
        return creatureId;
    }

    private async Task GiveSoft(long userId, decimal amount)
    {
        await _factory.WithDbAsync(async db =>
        {
            int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == softId);
            wallet.Amount += amount;
        });
    }

    private async Task GivePremium(long userId, decimal amount)
    {
        await _factory.WithDbAsync(async db =>
        {
            int premiumId = await db.CurrencyTypes.Where(c => c.Code == "PREMIUM").Select(c => c.Id).FirstAsync();
            var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == premiumId);
            wallet.Amount += amount;
        });
    }

    private async Task<decimal> PremiumBalance(long userId)
    {
        decimal amount = 0m;
        await _factory.WithDbAsync(async db =>
        {
            int premiumId = await db.CurrencyTypes.Where(c => c.Code == "PREMIUM").Select(c => c.Id).FirstAsync();
            amount = (await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == premiumId)).Amount;
        });
        return amount;
    }

    private async Task SetBreedCountAndRest(long creatureId, int breedCount, DateTime? lastBredAt)
    {
        await _factory.WithDbAsync(async db =>
        {
            var c = await db.CreatureInstances.FirstAsync(x => x.Id == creatureId);
            c.BreedCount = breedCount;
            c.LastBredAt = lastBredAt;
        });
    }

    private async Task MakeSlotReadyNow(long userId)
    {
        await _factory.WithDbAsync(async db =>
        {
            var slot = await db.BreedingSlots.FirstAsync(s => s.UserId == userId && s.Status == BreedingStatus.InProgress);
            slot.ReadyAt = DateTime.UtcNow.AddMinutes(-1);
        });
    }

    [Fact]
    public async Task FluxoCompleto_StartECollect()
    {
        var (client, userId) = await _factory.RegisterAsync("breed1");
        await GiveSoft(userId, 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 111);
        long b = await CreateOwnedCreature(userId, 6m, 222);

        var status0 = await client.GetFromJsonAsync<BreedingStatusDto>("/api/breeding");
        Assert.False(status0!.Active);

        var startResp = await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b });
        startResp.EnsureSuccessStatusCode();

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Empty(tank!.Creatures); // pais saíram do tanque principal

        var status1 = await client.GetFromJsonAsync<BreedingStatusDto>("/api/breeding");
        Assert.True(status1!.Active);
        Assert.Equal(a, status1.Slot!.ParentA.Id);
        Assert.Equal(b, status1.Slot.ParentB.Id);
        Assert.False(status1.Slot.IsReady);

        var collectTooSoon = await client.PostAsync("/api/breeding/collect", null);
        Assert.Equal(HttpStatusCode.BadRequest, collectTooSoon.StatusCode);

        await MakeSlotReadyNow(userId);

        var collectResp = await client.PostAsync("/api/breeding/collect", null);
        collectResp.EnsureSuccessStatusCode();
        var result = await collectResp.Content.ReadFromJsonAsync<CollectBreedingResponse>();
        Assert.NotNull(result);
        var child = result!.Child;

        var statusAfter = await client.GetFromJsonAsync<BreedingStatusDto>("/api/breeding");
        Assert.False(statusAfter!.Active);

        // Risco de morte é não-determinístico (chance baixa por pai): o filho sempre
        // volta pro tanque, mas cada pai só volta se sobreviveu à gestação.
        int survivors = (result.ParentADied ? 0 : 1) + (result.ParentBDied ? 0 : 1);
        var tankAfter = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(1 + survivors, tankAfter!.Creatures.Count);

        // Conserta o bug relatado: o filhote precisa reconstruir os traits reais
        // (herdados via BreedTraits), não um peixe aleatório do próprio seed solto —
        // por isso o DTO precisa expor os seeds dos pais.
        Assert.True(child.IsBred);
        Assert.Equal("111", child.ParentASeed);
        Assert.Equal("222", child.ParentBSeed);

        await _factory.WithDbAsync(async db =>
        {
            var childEntity = await db.CreatureInstances.FirstAsync(c => c.Id == child!.Id);
            Assert.Equal(a, childEntity.ParentAId);
            Assert.Equal(b, childEntity.ParentBId);
            Assert.Equal(111, childEntity.ParentASeed);
            Assert.Equal(222, childEntity.ParentBSeed);
        });
    }

    [Fact]
    public async Task Quote_RetornaCustoGestacaoEChancesSemCobrarNada()
    {
        var (client, userId) = await _factory.RegisterAsync("breed8");
        long a = await CreateOwnedCreature(userId, 5m, 2020);
        long b = await CreateOwnedCreature(userId, 6m, 2121);

        var quote = await client.GetFromJsonAsync<BreedingQuoteDto>($"/api/breeding/quote?parentAId={a}&parentBId={b}");
        Assert.NotNull(quote);
        Assert.True(quote!.CostSoft > 0);
        Assert.True(quote.GestationHours > 0);
        Assert.Equal(0, quote.ParentABreedCount);
        Assert.Equal(0, quote.ParentBBreedCount);
        Assert.InRange(quote.ParentADeathChance, 0, 1);
        Assert.True(quote.ChildTierProbabilities.Values.Sum() is > 0.99 and < 1.01);

        // Prévia não cobra nem inicia nada — o par continua livre pra outras coisas.
        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Equal(2, tank!.Creatures.Count);
    }

    [Fact]
    public async Task Start_ComPeixesIguais_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("breed2");
        await GiveSoft(userId, 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 333);

        var resp = await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = a });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Start_SaldoInsuficiente_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("breed3"); // saldo inicial 100 < CostSoft 150
        long a = await CreateOwnedCreature(userId, 5m, 444);
        long b = await CreateOwnedCreature(userId, 6m, 555);

        var resp = await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Start_PeixeJaEmGestacao_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("breed4");
        await GiveSoft(userId, 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 666);
        long b = await CreateOwnedCreature(userId, 6m, 777);
        long c = await CreateOwnedCreature(userId, 7m, 888);

        (await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b })).EnsureSuccessStatusCode();

        // 2ª gestação do mesmo usuário — bloqueada mesmo com peixe C livre
        var resp = await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = c });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Start_PeixeListadoNoMercado_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("breed5");
        await GiveSoft(userId, 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 999);
        long b = await CreateOwnedCreature(userId, 6m, 1010);

        var listResp = await client.PostAsJsonAsync("/api/market/listings", new { creatureInstanceId = a, priceSoft = 10m });
        listResp.EnsureSuccessStatusCode();

        var resp = await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Start_PeixeDeOutroUsuario_Retorna404()
    {
        var (owner, ownerId) = await _factory.RegisterAsync("breed6dono");
        long a = await CreateOwnedCreature(ownerId, 5m, 1111);

        var (attacker, attackerId) = await _factory.RegisterAsync("breed6atacante");
        await GiveSoft(attackerId, 1000m);
        long b = await CreateOwnedCreature(attackerId, 6m, 1212);

        var resp = await attacker.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task FilhoteDeFilhote_DenormalizaOsSeedsDosAvosCorretamente()
    {
        // 31/07/2026: fecha o gap de consistência client/server do bug de traits fantasma —
        // quando um FILHOTE (não só um peixe fresco) é usado como pai num novo cruzamento, o
        // neto precisa denormalizar os seeds dos AVÓS (os pais do filhote-pai), não ficar sem
        // essa informação (o que forçaria reconstruir esse pai com Generate(seed) errado).
        var (client, userId) = await _factory.RegisterAsync("breed9");
        await GiveSoft(userId, 10_000m);

        // Geração 1: avós cruzam, produzindo um filhote.
        long grandparentA = await CreateOwnedCreature(userId, 5m, 5001);
        long grandparentB = await CreateOwnedCreature(userId, 6m, 5002);
        (await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = grandparentA, parentBId = grandparentB })).EnsureSuccessStatusCode();
        await MakeSlotReadyNow(userId);
        var firstCollect = await client.PostAsync("/api/breeding/collect", null);
        firstCollect.EnsureSuccessStatusCode();
        var firstChild = (await firstCollect.Content.ReadFromJsonAsync<CollectBreedingResponse>())!.Child;

        // Geração 2: o filhote da 1ª geração cruza com um peixe fresco.
        long freshMate = await CreateOwnedCreature(userId, 7m, 5003);
        (await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = firstChild.Id, parentBId = freshMate })).EnsureSuccessStatusCode();
        await MakeSlotReadyNow(userId);
        var secondCollect = await client.PostAsync("/api/breeding/collect", null);
        secondCollect.EnsureSuccessStatusCode();
        var grandchild = (await secondCollect.Content.ReadFromJsonAsync<CollectBreedingResponse>())!.Child;

        await _factory.WithDbAsync(async db =>
        {
            var entity = await db.CreatureInstances.FirstAsync(c => c.Id == grandchild.Id);
            // Lado A do neto é o filhote (parentA = firstChild) — os avós denormalizados
            // devem ser os PAIS de firstChild (5001/5002), nunca Generate(firstChild.Seed).
            Assert.Equal(5001, entity.ParentAGrandparentASeed);
            Assert.Equal(5002, entity.ParentAGrandparentBSeed);
            // Lado B é o peixe fresco — sem avós.
            Assert.Null(entity.ParentBGrandparentASeed);
            Assert.Null(entity.ParentBGrandparentBSeed);
        });
    }

    [Fact]
    public async Task Collect_DuasVezesSeguidas_SegundaFalha()
    {
        var (client, userId) = await _factory.RegisterAsync("breed7");
        await GiveSoft(userId, 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 1313);
        long b = await CreateOwnedCreature(userId, 6m, 1414);
        (await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b })).EnsureSuccessStatusCode();
        await MakeSlotReadyNow(userId);

        var first = await client.PostAsync("/api/breeding/collect", null);
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsync("/api/breeding/collect", null);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode); // não há mais gestação em andamento
    }

    [Fact]
    public async Task Collect_SemEspacoPraFilhoteEOsDoisPais_Bloqueia()
    {
        // Regressão (12/08/2026): antes disso, um pai sobrevivente sem vaga no tanque/mochila
        // ficava preso dentro do próprio habitat de reprodução, invisível pro jogador (achado
        // via relato real de usuário). Agora a coleta simplesmente não é permitida até haver
        // espaço pro pior caso (filhote + os 2 pais sobrevivendo) — ninguém fica preso.
        var (client, userId) = await _factory.RegisterAsync("breed14");
        await GiveSoft(userId, 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 5001);
        long b = await CreateOwnedCreature(userId, 6m, 5002);
        (await client.PostAsJsonAsync("/api/breeding/start", new { parentAId = a, parentBId = b })).EnsureSuccessStatusCode();
        await MakeSlotReadyNow(userId);

        // Tanque (capacidade 3) e mochila (capacidade HabitatDefaults.BackpackCapacity) ficam
        // com só 1 vaga livre no total — menos que as 3 necessárias pro pior caso.
        long habitatId = await HabitatIdOf(userId);
        await _factory.WithDbAsync(async db =>
        {
            for (int i = 0; i < 3; i++)
                db.CreatureInstances.Add(new CreatureInstance
                {
                    SpeciesId = 1, OwnerId = userId, HabitatId = habitatId,
                    Seed = 9000 + i, TraitConfigVersion = 1, RarityScore = 4m,
                    TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(9000 + i)),
                    CreatedAt = DateTime.UtcNow,
                });
            for (int i = 0; i < HabitatDefaults.BackpackCapacity - 1; i++)
                db.CreatureInstances.Add(new CreatureInstance
                {
                    SpeciesId = 1, OwnerId = userId, HabitatId = null,
                    Seed = 9100 + i, TraitConfigVersion = 1, RarityScore = 4m,
                    TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(9100 + i)),
                    CreatedAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        });

        var collectResp = await client.PostAsync("/api/breeding/collect", null);
        Assert.Equal(HttpStatusCode.BadRequest, collectResp.StatusCode);

        // Nada foi mutado: a gestação continua em andamento, sem filhote criado.
        var status = await client.GetFromJsonAsync<BreedingStatusDto>("/api/breeding");
        Assert.True(status!.Active);
    }

    [Fact]
    public async Task Tanque_ResgataPeixePresoNoNinhoSemGestacaoAtiva()
    {
        // Regressão (12/08/2026): antes da checagem em Collect existir, um pai sobrevivente
        // sem vaga no tanque/mochila ficava parado dentro do próprio habitat de reprodução
        // pra sempre — nem GET /api/game/tank nem nada mais o resgatava depois que o jogador
        // abria espaço. Simula esse estado (peixe estacionado no Breeding sem slot ativo
        // referenciando ele) e confirma que carregar o tanque move ele de volta sozinho.
        var (client, userId) = await _factory.RegisterAsync("breed15");
        long breedingHabitatId = 0;
        long strandedId = 0;
        await _factory.WithDbAsync(async db =>
        {
            var breedingHabitat = await db.Habitats.FirstAsync(h => h.UserId == userId && h.HabitatType!.Code == "Breeding");
            breedingHabitatId = breedingHabitat.Id;
            var stranded = new CreatureInstance
            {
                SpeciesId = 1, OwnerId = userId, HabitatId = breedingHabitatId,
                Seed = 7001, TraitConfigVersion = 1, RarityScore = 5m,
                TraitsJson = TraitsSerialization.Serialize(TraitGenerator.Generate(7001)),
                CreatedAt = DateTime.UtcNow,
            };
            db.CreatureInstances.Add(stranded);
            await db.SaveChangesAsync();
            strandedId = stranded.Id;
        });

        var tank = await client.GetFromJsonAsync<AuthTests.TankDto>("/api/game/tank");
        Assert.Contains(tank!.Creatures, c => c.Id == strandedId); // saiu do Ninho, voltou pro tanque

        await _factory.WithDbAsync(async db =>
        {
            var c = await db.CreatureInstances.FirstAsync(x => x.Id == strandedId);
            Assert.NotEqual(breedingHabitatId, c.HabitatId);
        });
    }

    [Fact]
    public async Task Seguro_GarantePremiumTravaRiscoZeroECobraPremium()
    {
        var (client, userId) = await _factory.RegisterAsync("breed10");
        await GiveSoft(userId, 1000m);
        await GivePremium(userId, 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 3001);
        long b = await CreateOwnedCreature(userId, 6m, 3002);
        // Veteranos: risco alto o bastante pra garantir que o seguro faz diferença de verdade.
        await SetBreedCountAndRest(a, 5, null);
        await SetBreedCountAndRest(b, 5, null);

        decimal premiumBefore = await PremiumBalance(userId);

        var startResp = await client.PostAsJsonAsync("/api/breeding/start",
            new { parentAId = a, parentBId = b, useInsurance = true });
        startResp.EnsureSuccessStatusCode();
        var startResult = await startResp.Content.ReadFromJsonAsync<StartResultDto>();
        Assert.True(startResult!.InsuranceCostPaid > 0);

        decimal premiumAfter = await PremiumBalance(userId);
        Assert.Equal(premiumBefore - startResult.InsuranceCostPaid, premiumAfter);

        var status = await client.GetFromJsonAsync<BreedingStatusDto>("/api/breeding");
        Assert.True(status!.Slot!.InsuranceUsed);
        Assert.Equal(0m, status.Slot.ParentADeathChance);
        Assert.Equal(0m, status.Slot.ParentBDeathChance);

        await MakeSlotReadyNow(userId);
        var collectResp = await client.PostAsync("/api/breeding/collect", null);
        collectResp.EnsureSuccessStatusCode();
        var result = await collectResp.Content.ReadFromJsonAsync<CollectBreedingResponse>();
        Assert.False(result!.ParentADied);
        Assert.False(result.ParentBDied);
    }

    [Fact]
    public async Task Seguro_SemSaldoPremium_Retorna400()
    {
        var (client, userId) = await _factory.RegisterAsync("breed11");
        await GiveSoft(userId, 1000m); // sem GivePremium — saldo premium fica 0
        long a = await CreateOwnedCreature(userId, 5m, 3003);
        long b = await CreateOwnedCreature(userId, 6m, 3004);

        var resp = await client.PostAsJsonAsync("/api/breeding/start",
            new { parentAId = a, parentBId = b, useInsurance = true });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Estabilizador_ReduzRiscoPelaMetadeECobraSoftExtra()
    {
        var (client, userId) = await _factory.RegisterAsync("breed12");
        await GiveSoft(userId, 1000m);
        long a = await CreateOwnedCreature(userId, 5m, 3005);
        long b = await CreateOwnedCreature(userId, 6m, 3006);
        await SetBreedCountAndRest(a, 5, null);
        await SetBreedCountAndRest(b, 5, null);

        var quote = await client.GetFromJsonAsync<BreedingQuoteDto>($"/api/breeding/quote?parentAId={a}&parentBId={b}");
        double baselineA = quote!.ParentADeathChance;
        double baselineB = quote.ParentBDeathChance;

        var startResp = await client.PostAsJsonAsync("/api/breeding/start",
            new { parentAId = a, parentBId = b, useStabilizer = true });
        startResp.EnsureSuccessStatusCode();
        var startResult = await startResp.Content.ReadFromJsonAsync<StartResultDto>();
        Assert.Equal(0m, startResult!.InsuranceCostPaid);

        var status = await client.GetFromJsonAsync<BreedingStatusDto>("/api/breeding");
        Assert.False(status!.Slot!.InsuranceUsed);
        Assert.Equal(baselineA * 0.5, (double)status.Slot.ParentADeathChance, 3);
        Assert.Equal(baselineB * 0.5, (double)status.Slot.ParentBDeathChance, 3);
        // Custo base + estabilizador (150) — maior que só o custo base da gestação.
        Assert.True(status.Slot.CostPaid > quote.CostSoft);
    }

    [Fact]
    public async Task Descanso_ReduzORiscoMostradoNaPrevia()
    {
        var (client, userId) = await _factory.RegisterAsync("breed13");
        long a = await CreateOwnedCreature(userId, 5m, 3007);
        long b = await CreateOwnedCreature(userId, 6m, 3008);
        // Mesmo BreedCount alto pros dois peixes, mas só um descansou.
        await SetBreedCountAndRest(a, 6, DateTime.UtcNow); // acabou de cruzar — sem descanso
        await SetBreedCountAndRest(b, 6, DateTime.UtcNow.AddDays(-30)); // descansou bastante

        var quote = await client.GetFromJsonAsync<BreedingQuoteDto>($"/api/breeding/quote?parentAId={a}&parentBId={b}");
        Assert.NotNull(quote);
        Assert.True(quote!.ParentBDeathChance < quote.ParentADeathChance);
    }
}
