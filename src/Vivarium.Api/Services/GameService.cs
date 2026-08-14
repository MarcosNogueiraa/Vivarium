using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Services;

/// <summary>
/// Aplica a lógica pura de Vivarium.Core.Gameplay nas entidades. O tick roda
/// "lazy" dentro das chamadas de API (heartbeat/tanque/coleta) — sem job agendado.
/// Nada aqui chama SaveChanges: o endpoint salva no fim, uma transação por request.
/// </summary>
public class GameService(VivariumDbContext db)
{
    /// <summary>Aquário principal do usuário (não o habitat de reprodução — ver BreedingService).</summary>
    public async Task<Habitat?> FindHabitatAsync(long userId)
        => await db.Habitats.FirstOrDefaultAsync(h => h.UserId == userId && h.HabitatType!.Code == "Aquarium");

    public async Task ApplyTickAsync(Habitat habitat, DateTime nowUtc)
    {
        int pending = await db.GenerationQueueItems
            .CountAsync(q => q.HabitatId == habitat.Id && q.Status == QueueItemStatus.Pending);
        decimal filterCapacity = await FilterCapacityAsync(habitat.UserId);
        // Hook futuro (cascudo, CLAUDE.md §8.15/16): "cascudo" é um PEIXE novo (uma
        // criatura, não um item de loja — diferente do filtro automático/`filterCapacity`
        // acima, que é equipamento comprado). Quando essa espécie existir, o bônus de
        // limpeza passiva dela entraria aqui somado a `filterCapacity` (ou como
        // multiplicador extra no fator de filtro em HabitatTicker) — sem estrutura nova,
        // mais um termo na mesma fórmula, só a origem do bônus é diferente (peixe vivo
        // no tanque, não item comprado).
        decimal bandFactor = CapacityBands.BandFor(habitat.Capacity).DegradationBandFactor;
        var fish = await TankFishAsync(habitat);

        var state = new HabitatTickState(
            LastTickAtUtc: habitat.LastTickAt,
            LastHeartbeatAtUtc: habitat.LastHeartbeatAt,
            MaintenanceLevel: habitat.MaintenanceLevel,
            GenerationProgressMinutes: habitat.GenerationProgressMinutes,
            GenerationIntervalMinutes: habitat.GenerationIntervalMinutes,
            OnlineGenerationRate: habitat.OnlineGenerationRate,
            OfflineGenerationRate: habitat.OfflineGenerationRate,
            QueueCap: habitat.QueueCap,
            PendingQueueCount: pending,
            FilterCapacity: filterCapacity,
            ActiveFishWeight: fish.Sum(f => f.RarityScore) / TickConfig.Default.DegradationRarityRefScore,
            CapacityBandDegradationFactor: bandFactor);

        var outcome = HabitatTicker.ProcessTick(state, nowUtc, Random.Shared, TickConfig.Default);
        habitat.LastTickAt = outcome.LastTickAtUtc;
        habitat.MaintenanceLevel = outcome.MaintenanceLevel;
        habitat.GenerationProgressMinutes = outcome.GenerationProgressMinutes;

        // Farm de moedas: renda passiva por raridade + sinergia (server-side, limitada
        // por tempo real decorrido + teto offline; cliente nunca envia valor).
        await AccrueIncomeAsync(habitat, outcome, fish);

        if (outcome.SpawnCount > 0)
        {
            int speciesId = await db.Species
                .Where(s => s.HabitatTypeId == habitat.HabitatTypeId)
                .Select(s => s.Id)
                .FirstAsync();
            foreach (bool sick in outcome.SpawnSickFlags)
            {
                db.GenerationQueueItems.Add(new GenerationQueueItem
                {
                    HabitatId = habitat.Id,
                    SpeciesId = speciesId,
                    ReadyAt = nowUtc,
                    Status = QueueItemStatus.Pending,
                    IsSick = sick,
                });
            }
        }

        // Diferencial VIP: coleta automática + Limpeza Automática (§8.18), mas só enquanto
        // o tanque está online — hoisting do check pra não consultar HasActiveVipAsync 2x.
        // Cada uma tem opt-out próprio (Habitat.AutoCollectEnabled/AutoCleanEnabled, default
        // true — preserva o comportamento de sempre pra quem nunca mexeu no toggle).
        bool vipOnline = HabitatTicker.IsOnline(habitat.LastHeartbeatAt, nowUtc, TickConfig.Default)
            && await HasActiveVipAsync(habitat.UserId, nowUtc);
        if (vipOnline && habitat.AutoCollectEnabled)
            await CollectAllReadyAsync(habitat, nowUtc);
        if (vipOnline && habitat.AutoCleanEnabled)
            await ApplyAutoCleanAsync(habitat, nowUtc);
    }

    /// <summary>
    /// Limpeza Automática (VIP, §8.18): compra sozinha um Filtro quando a água cruza o gatilho
    /// configurado (0% por padrão — grátis pra qualquer VIP; até <c>WaterSensorMaxTriggerPercent</c>
    /// com o Sensor de Qualidade da Água comprado). Roda DEPOIS de <see cref="AccrueIncomeAsync"/>
    /// (chamado em <see cref="ApplyTickAsync"/>) de propósito: a renda do intervalo que acabou de
    /// passar já foi calculada em cima da água real (suja) daquele período — a limpeza só afeta o
    /// tick seguinte, mesmo raciocínio causal já usado pra compra manual de filtro.
    /// </summary>
    private async Task ApplyAutoCleanAsync(Habitat habitat, DateTime nowUtc)
    {
        decimal trigger = habitat.HasWaterSensor ? habitat.AutoCleanTriggerPercent : 0m;
        if (habitat.MaintenanceLevel > trigger)
            return;

        // Preço vem do ItemDefinition (nunca hardcodeado) — se o Filtro for rebalanceado,
        // a limpeza automática acompanha sem precisar de mudança de código.
        decimal price = await db.ItemDefinitions
            .Where(i => i.Key == "filter_basic")
            .Select(i => i.PriceSoft)
            .FirstAsync();
        int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
        var wallet = await db.WalletBalances
            .FirstOrDefaultAsync(w => w.UserId == habitat.UserId && w.CurrencyTypeId == softId);
        if (wallet is null || wallet.Amount < price)
            return; // sem saldo: não compra, sem erro — a água continua baixa até o próximo tick

        wallet.Amount -= price;
        habitat.MaintenanceLevel = 100m;

        db.TransactionLogs.Add(new TransactionLog
        {
            Type = TransactionType.ItemPurchase,
            FromUserId = habitat.UserId,
            CurrencyTypeId = softId,
            Amount = price,
            CreatedAt = nowUtc,
        });
    }

    /// <summary>
    /// Configura o gatilho da Limpeza Automática (§8.18) — exige o Sensor de Qualidade da Água já
    /// comprado (<see cref="Habitat.HasWaterSensor"/>). Sem custo: é uma configuração, não compra.
    /// </summary>
    public async Task<ServiceResult> SetAutoCleanTriggerAsync(long userId, decimal percent)
    {
        var habitat = await FindHabitatAsync(userId);
        if (habitat is null)
            return ServiceResult.NotFound("Habitat não encontrado");
        if (!habitat.HasWaterSensor)
            return ServiceResult.Bad("Compre o Sensor de Qualidade da Água antes de configurar o gatilho.");
        if (percent < 0m || percent > TickConfig.Default.WaterSensorMaxTriggerPercent)
            return ServiceResult.Bad($"Gatilho precisa estar entre 0 e {TickConfig.Default.WaterSensorMaxTriggerPercent}.");

        habitat.AutoCleanTriggerPercent = percent;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("Tente de novo.");
        }
        return ServiceResult.Success(new { autoCleanTriggerPercent = habitat.AutoCleanTriggerPercent });
    }

    /// <summary>
    /// Liga/desliga a coleta automática e a Limpeza Automática de VIP (opt-out, default
    /// ambos ligados) — não exige VIP ativo pra configurar, só tem EFEITO com VIP ativo
    /// (mesmo espírito do Sensor de Qualidade da Água: comprável/configurável sem VIP, fica
    /// dormente até assinar).
    /// </summary>
    public async Task<ServiceResult> SetTogglesAsync(long userId, bool autoCollectEnabled, bool autoCleanEnabled)
    {
        var habitat = await FindHabitatAsync(userId);
        if (habitat is null)
            return ServiceResult.NotFound("Habitat não encontrado");

        habitat.AutoCollectEnabled = autoCollectEnabled;
        habitat.AutoCleanEnabled = autoCleanEnabled;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("Tente de novo.");
        }
        return ServiceResult.Success(new { habitat.AutoCollectEnabled, habitat.AutoCleanEnabled });
    }

    /// <summary>
    /// Marca um peixe coletado automaticamente (<see cref="CreatureInstance.IsNew"/>) como visto
    /// — some o selo/silhueta da Mochila. Ownership checado pelo OwnerId, nunca confia no cliente.
    /// </summary>
    public async Task<ServiceResult> MarkSeenAsync(long userId, long creatureId)
    {
        var creature = await db.CreatureInstances
            .FirstOrDefaultAsync(c => c.Id == creatureId && c.OwnerId == userId);
        if (creature is null)
            return ServiceResult.NotFound("Criatura não encontrada");

        creature.IsNew = false;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("Tente de novo.");
        }
        return ServiceResult.Success();
    }

    /// <summary>
    /// Capacidade coberta pelo melhor filtro automático do jogador (08/08/2026) — níveis não
    /// empilham, o maior `filterCapacity` possuído prevalece. 0 = sem filtro.
    /// </summary>
    private async Task<decimal> FilterCapacityAsync(long userId)
    {
        var effectJsons = await db.UserInventories
            .Where(i => i.UserId == userId && i.Quantity > 0 && i.ItemDefinition!.Category == ItemCategory.AutoFilter)
            .Select(i => i.ItemDefinition!.EffectJson)
            .ToListAsync();
        decimal max = 0m;
        foreach (var json in effectJsons)
        {
            decimal capacity = ItemEffect.Parse(json).FilterCapacity ?? 0m;
            if (capacity > max)
                max = capacity;
        }
        return max;
    }

    /// <summary>Peixes ativos do tanque como entrada de renda (raridade + cor das 3 partes pra sinergia).</summary>
    private async Task<List<FishIncome>> TankFishAsync(Habitat habitat)
    {
        var rows = await db.CreatureInstances
            .Where(c => c.HabitatId == habitat.Id)
            .Select(c => new CreatureInstance
            {
                Id = c.Id, Seed = c.Seed, RarityScore = c.RarityScore, TraitConfigVersion = c.TraitConfigVersion,
                TraitsJson = c.TraitsJson, OriginalOwnerId = c.OriginalOwnerId,
            })
            .ToListAsync();
        var list = new List<FishIncome>(rows.Count);
        foreach (var r in rows)
        {
            var (tail, dorsal, pectoral) = PartColorsResolver.Of(r);
            list.Add(new FishIncome(r.RarityScore, tail, dorsal, pectoral));
        }
        return list;
    }

    private async Task AccrueIncomeAsync(Habitat habitat, TickOutcome outcome, IReadOnlyList<FishIncome> fish)
    {
        if (outcome.OnlineMinutes <= 0 && outcome.OfflineMinutes <= 0)
            return;
        if (fish.Count == 0)
            return;

        decimal earned = IncomeCalculator.Accrue(
            fish, outcome.MaintenanceAtStart, outcome.MaintenanceLevel,
            outcome.OnlineMinutes, outcome.OfflineMinutes,
            habitat.OnlineGenerationRate, habitat.OfflineGenerationRate,
            TickConfig.Default);

        habitat.CoinAccrual += earned;
        decimal whole = Math.Floor(habitat.CoinAccrual);
        if (whole < 1)
            return;

        int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
        var wallet = await db.WalletBalances
            .FirstOrDefaultAsync(w => w.UserId == habitat.UserId && w.CurrencyTypeId == softId);
        if (wallet is null)
            return; // sem carteira não credita — não descontar o acúmulo (evita perder moedas)
        wallet.Amount += whole;
        habitat.CoinAccrual -= whole;
        // Renda passiva não vai pro TransactionLog (inundaria a auditoria); mercado/transferência continuam logados.
    }

    /// <summary>Taxa de renda atual do tanque (moedas/hora), já com água + sinergia — pra UI.</summary>
    public async Task<decimal> CoinsPerHourAsync(Habitat habitat)
    {
        var fish = await TankFishAsync(habitat);
        return IncomeCalculator.TankRatePerHour(fish, habitat.MaintenanceLevel, TickConfig.Default);
    }

    public Task<bool> HasActiveVipAsync(long userId, DateTime nowUtc)
        => db.VipSubscriptions.AnyAsync(v =>
            v.UserId == userId
            && v.Status == SubscriptionStatus.Active
            && v.StartAt <= nowUtc
            && v.EndAt > nowUtc);

    private async Task<(CreatureInstance? Creature, string? Error)> CollectInternalAsync(
        Habitat habitat, long queueItemId, DateTime nowUtc)
    {
        var item = await db.GenerationQueueItems
            .FirstOrDefaultAsync(q => q.Id == queueItemId && q.HabitatId == habitat.Id);
        if (item is null || item.Status != QueueItemStatus.Pending)
            return (null, "Item não encontrado ou já coletado");
        if (item.ReadyAt > nowUtc)
            return (null, "Item ainda não está pronto");

        // Vai pro tanque se couber, senão pra mochila; se ambos cheios, bloqueia.
        bool toTank = await CountActiveCreaturesAsync(habitat) < habitat.Capacity;
        if (!toTank && await CountBackpackAsync(habitat.UserId) >= HabitatDefaults.BackpackCapacity)
            return (null, "Tanque e mochila cheios — venda ou solte um peixe antes.");

        return (CollectOne(habitat, item, nowUtc, toTank), null);
    }

    /// <summary>
    /// Bug real corrigido (12/08/2026, relatado pelo usuário): quando o tanque enchia, a
    /// coleta automática VIP simplesmente PARAVA (`break` no primeiro item sem espaço) — ao
    /// contrário da coleta MANUAL (`CollectInternalAsync`), que sempre cai pra mochila se o
    /// tanque estiver cheio. Resultado: um VIP com o tanque cheio ficava com a fila
    /// PERMANENTEMENTE travada (nunca mais coletava sozinho, mesmo com espaço de sobra na
    /// mochila) até abrir espaço manualmente no tanque — justamente o cenário que a coleta
    /// automática deveria evitar. Agora, com o tanque cheio, os itens prontos vão pra
    /// mochila (até ela também encher, aí sim para).
    /// </summary>
    private async Task CollectAllReadyAsync(Habitat habitat, DateTime nowUtc)
    {
        var ready = await db.GenerationQueueItems
            .Where(q => q.HabitatId == habitat.Id && q.Status == QueueItemStatus.Pending && q.ReadyAt <= nowUtc)
            .OrderBy(q => q.ReadyAt)
            .ToListAsync();
        // Itens gerados neste mesmo tick ainda não foram salvos — estão só no change tracker
        ready.AddRange(db.GenerationQueueItems.Local.Where(q =>
            q.Id == 0 && q.HabitatId == habitat.Id && q.Status == QueueItemStatus.Pending));

        int active = await CountActiveCreaturesAsync(habitat);
        int backpackCount = await CountBackpackAsync(habitat.UserId);
        foreach (var item in ready)
        {
            bool toTank = active < habitat.Capacity;
            if (!toTank && backpackCount >= HabitatDefaults.BackpackCapacity)
                break; // tanque e mochila cheios — só aí a coleta automática realmente para

            CollectOne(habitat, item, nowUtc, toTank, isNew: true);
            if (toTank) active++; else backpackCount++;
        }
    }

    /// <summary>
    /// <paramref name="isNew"/>: true só na coleta AUTOMÁTICA (o jogador não clicou em nada,
    /// não viu o momento de revelação — CollectCelebration). Coleta manual sempre passa false:
    /// o clique já É o momento de revelação, mostrado na hora pelo cliente.
    /// </summary>
    private CreatureInstance CollectOne(Habitat habitat, GenerationQueueItem item, DateTime nowUtc, bool toTank, bool isNew = false)
    {
        var collected = CreatureCollector.Collect(item.IsSick, CreatureCollector.NewRandomSeed);
        item.Status = QueueItemStatus.Collected;

        var creature = new CreatureInstance
        {
            SpeciesId = item.SpeciesId,
            OwnerId = habitat.UserId,
            OriginalOwnerId = habitat.UserId,
            HabitatId = toTank ? habitat.Id : null, // mochila quando o tanque está cheio
            Seed = collected.Seed,
            TraitConfigVersion = collected.TraitConfigVersion,
            RarityScore = collected.RarityScore,
            TraitsJson = TraitsSerialization.Serialize(collected.Traits),
            CreatedAt = nowUtc,
            IsNew = isNew,
        };
        db.CreatureInstances.Add(creature);
        return creature;
    }

    private Task<int> CountActiveCreaturesAsync(Habitat habitat)
        => db.CreatureInstances.CountAsync(c => c.HabitatId == habitat.Id);

    // ---------- Mochila (storage de criaturas) ----------
    // Mochila = criatura do jogador com HabitatId null e SEM listagem ativa.

    public IQueryable<CreatureInstance> BackpackQuery(long userId)
        => db.CreatureInstances.Where(c =>
            c.OwnerId == userId
            && c.HabitatId == null
            && !c.IsDead
            && c.SoldAt == null
            && !c.PendingInboxClaim
            && !db.MarketListings.Any(m => m.CreatureInstanceId == c.Id && m.Status == ListingStatus.Active));

    public Task<int> CountBackpackAsync(long userId) => BackpackQuery(userId).CountAsync();

    /// <summary>Coloca no tanque (se couber) ou na mochila (se couber). false = sem espaço em nenhum.</summary>
    public async Task<bool> TryPlaceAsync(CreatureInstance creature, Habitat habitat)
    {
        if (await CountActiveCreaturesAsync(habitat) < habitat.Capacity)
        {
            creature.HabitatId = habitat.Id;
            return true;
        }
        if (await CountBackpackAsync(habitat.UserId) < HabitatDefaults.BackpackCapacity)
        {
            creature.HabitatId = null;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Resgata peixe(s) presos no habitat de reprodução por falta de espaço no momento da
    /// coleta (§8.8/§8.19) — antes da checagem de espaço em BreedingService.CollectAsync
    /// (12/08/2026), um pai sobrevivente sem vaga no tanque/mochila ficava parado ali pra
    /// sempre, sem nenhum mecanismo de recuperação (o Ninho nunca passa pelo tick normal, e
    /// mesmo depois do jogador abrir espaço não existia nada que movesse o peixe de volta —
    /// achado via relato real de usuário). Roda a cada carregamento do tanque (defesa em
    /// profundidade: cobre tanto os casos já presos ANTES desta correção quanto qualquer
    /// futuro edge case que a checagem no Collect não previna): qualquer criatura do usuário
    /// estacionada num habitat tipo Breeding sem estar referenciada por uma gestação em
    /// andamento é candidata a mover pro tanque/mochila assim que houver espaço.
    /// </summary>
    private async Task RescueStrandedBreedingParentsAsync(long userId, Habitat mainHabitat)
    {
        var breedingHabitatIds = await db.Habitats
            .Where(h => h.UserId == userId && h.HabitatType!.Code == "Breeding")
            .Select(h => h.Id)
            .ToListAsync();
        if (breedingHabitatIds.Count == 0)
            return;

        var activeSlots = await db.BreedingSlots
            .Where(s => s.UserId == userId && s.Status == BreedingStatus.InProgress)
            .Select(s => new { s.ParentAId, s.ParentBId })
            .ToListAsync();
        var activeSlotCreatureIds = activeSlots.SelectMany(s => new[] { s.ParentAId, s.ParentBId }).ToList();

        var stranded = await db.CreatureInstances
            .Where(c => c.OwnerId == userId && c.HabitatId != null && breedingHabitatIds.Contains(c.HabitatId!.Value)
                && !activeSlotCreatureIds.Contains(c.Id))
            .ToListAsync();

        foreach (var creature in stranded)
            await TryPlaceAsync(creature, mainHabitat);
    }

    /// <summary>Tanque → mochila.</summary>
    private async Task<string?> StoreCoreAsync(long userId, long creatureId, Habitat habitat)
    {
        var creature = await db.CreatureInstances
            .FirstOrDefaultAsync(c => c.Id == creatureId && c.OwnerId == userId && c.HabitatId == habitat.Id);
        if (creature is null)
            return "Peixe não está no seu tanque";
        if (await CountBackpackAsync(userId) >= HabitatDefaults.BackpackCapacity)
            return "Mochila cheia";
        creature.HabitatId = null;
        return null;
    }

    /// <summary>Mochila → tanque.</summary>
    private async Task<string?> DeployCoreAsync(long userId, long creatureId, Habitat habitat)
    {
        var creature = await BackpackQuery(userId).FirstOrDefaultAsync(c => c.Id == creatureId);
        if (creature is null)
            return "Peixe não está na sua mochila";
        if (await CountActiveCreaturesAsync(habitat) >= habitat.Capacity)
            return "Tanque cheio";
        creature.HabitatId = habitat.Id;
        return null;
    }

    // ---------- Orquestração (endpoints delegam aqui; devolvem ServiceResult) ----------

    public async Task<ServiceResult> HeartbeatAsync(long userId, DateTime now)
    {
        var habitat = await FindHabitatAsync(userId);
        if (habitat is null)
            return ServiceResult.NotFound("Habitat não encontrado");

        // Tick primeiro, com o heartbeat antigo: senão um retorno após dias
        // contaria a ausência inteira como tempo online.
        try
        {
            await ApplyTickAsync(habitat, now);
            habitat.LastHeartbeatAt = now;
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outro request ticou primeiro; o próximo heartbeat regrava. A janela
            // desta chamada foi descartada (LastTickAt não avançou) e será coberta
            // no próximo tick — renda continua exatamente-uma-vez.
        }

        return ServiceResult.Success(new { online = true, maintenanceLevel = habitat.MaintenanceLevel });
    }

    /// <summary>Resgatável 1x por dia (calendário UTC) — sem streak, sem penalidade por ausência (CLAUDE.md 8.10).</summary>
    private static bool CanClaimDailyReward(User user, DateTime now)
        => user.LastDailyRewardAt is not { } last || now.Date > last.Date;

    public async Task<ServiceResult> GetDailyRewardStatusAsync(long userId, DateTime now)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null)
            return ServiceResult.NotFound("Usuário não encontrado");

        bool canClaim = CanClaimDailyReward(user, now);
        DateTime? nextAvailable = canClaim ? null : user.LastDailyRewardAt!.Value.Date.AddDays(1);
        return ServiceResult.Success(new DailyRewardStatusDto(canClaim, EconomyDefaults.DailyRewardSoft, nextAvailable));
    }

    public async Task<ServiceResult> ClaimDailyRewardAsync(long userId, DateTime now)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null)
            return ServiceResult.NotFound("Usuário não encontrado");
        if (!CanClaimDailyReward(user, now))
            return ServiceResult.Bad("Recompensa diária já resgatada hoje.");

        int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
        var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == softId);
        wallet.Amount += EconomyDefaults.DailyRewardSoft;
        user.LastDailyRewardAt = now;

        db.TransactionLogs.Add(new TransactionLog
        {
            Type = TransactionType.DailyReward,
            ToUserId = userId,
            CurrencyTypeId = softId,
            Amount = EconomyDefaults.DailyRewardSoft,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("Resgate concorrente — tente de novo.");
        }
        return ServiceResult.Success(new { amount = EconomyDefaults.DailyRewardSoft, wallet = wallet.Amount });
    }

    /// <summary>
    /// Pula a espera de um item da fila pagando premium (8.11) — a única forma de acelerar
    /// a geração, que por design é lenta. Não muda o resultado (seed sorteado na coleta
    /// continua igual), só a hora em que fica pronto.
    /// </summary>
    public async Task<ServiceResult> RushQueueItemAsync(long userId, long queueItemId, DateTime now)
    {
        var habitat = await FindHabitatAsync(userId);
        if (habitat is null)
            return ServiceResult.NotFound("Habitat não encontrado");

        var item = await db.GenerationQueueItems.FirstOrDefaultAsync(q =>
            q.Id == queueItemId && q.HabitatId == habitat.Id && q.Status == QueueItemStatus.Pending);
        if (item is null)
            return ServiceResult.NotFound("Item não encontrado");
        if (item.ReadyAt <= now)
            return ServiceResult.Bad("Já está pronto — não precisa acelerar");

        decimal cost = RushCalculator.QueueRushCost((decimal)(item.ReadyAt - now).TotalMinutes);
        int premiumId = await db.CurrencyTypes.Where(c => c.Code == "PREMIUM").Select(c => c.Id).FirstAsync();
        var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == premiumId);
        if (wallet.Amount < cost)
            return ServiceResult.Bad("Saldo de moeda premium insuficiente");

        wallet.Amount -= cost;
        item.ReadyAt = now;

        db.TransactionLogs.Add(new TransactionLog
        {
            Type = TransactionType.TimeSkip,
            FromUserId = userId,
            CurrencyTypeId = premiumId,
            Amount = cost,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("Operação concorrente — tente de novo.");
        }
        return ServiceResult.Success(new { paid = cost, readyAt = item.ReadyAt });
    }

    public async Task<ServiceResult> GetTankAsync(long userId, DateTime now)
    {
        var habitat = await FindHabitatAsync(userId);
        if (habitat is null)
            return ServiceResult.NotFound("Habitat não encontrado");

        try
        {
            await ApplyTickAsync(habitat, now);
            await RescueStrandedBreedingParentsAsync(userId, habitat);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outro request ticou primeiro; recarrega o estado atual e segue.
            db.ChangeTracker.Clear();
            habitat = (await FindHabitatAsync(userId))!;
        }

        // Materializa antes de mapear: RushCalculator não traduz pra SQL.
        var queueRaw = await db.GenerationQueueItems
            .Where(q => q.HabitatId == habitat.Id && q.Status == QueueItemStatus.Pending)
            .OrderBy(q => q.ReadyAt)
            .ToListAsync();
        var queue = queueRaw.Select(q => new QueueItemDto(
            q.Id, q.ReadyAt, q.ReadyAt <= now, q.IsSick,
            q.ReadyAt <= now ? 0m : RushCalculator.QueueRushCost((decimal)(q.ReadyAt - now).TotalMinutes)))
            .ToList();
        var creatures = (await db.CreatureInstances
            .Where(c => c.HabitatId == habitat.Id)
            .ToListAsync())
            .Select(CreatureDto.From)
            .ToList();
        var wallet = await db.WalletBalances
            .Where(w => w.UserId == userId)
            .Select(w => new { w.CurrencyType!.Code, w.Amount })
            .ToDictionaryAsync(x => x.Code, x => x.Amount);
        var coinsPerHour = await CoinsPerHourAsync(habitat);
        bool isAdmin = await db.Users.Where(u => u.Id == userId).Select(u => u.IsAdmin).FirstAsync();
        decimal filterCapacity = await FilterCapacityAsync(userId);
        var vipSub = await db.VipSubscriptions
            .Where(v => v.UserId == userId && v.Status == SubscriptionStatus.Active && v.EndAt > now)
            .OrderByDescending(v => v.EndAt)
            .FirstOrDefaultAsync();

        return ServiceResult.Success(new TankResponse(
            HabitatTicker.IsOnline(habitat.LastHeartbeatAt, now, TickConfig.Default),
            habitat.MaintenanceLevel,
            habitat.Capacity,
            habitat.QueueCap,
            queue,
            creatures,
            wallet,
            coinsPerHour,
            habitat.GenerationProgressMinutes,
            habitat.GenerationIntervalMinutes,
            isAdmin,
            CapacityBands.BandFor(habitat.Capacity).Name,
            CapacityBands.MaxCapacity,
            CapacityBands.BandFor(habitat.Capacity).DegradationBandFactor,
            filterCapacity,
            vipSub is not null,
            vipSub?.EndAt,
            habitat.HasWaterSensor,
            habitat.AutoCleanTriggerPercent,
            TickConfig.Default.WaterSensorMaxTriggerPercent,
            habitat.AutoCollectEnabled,
            habitat.AutoCleanEnabled));
    }

    // Transferência direta entre contas (negociação externa é responsabilidade
    // dos jogadores — o jogo só move o item e audita no TransactionLog)
    public async Task<ServiceResult> TransferAsync(long userId, long creatureId, string toUsername)
    {
        var creature = await db.CreatureInstances
            .FirstOrDefaultAsync(c => c.Id == creatureId && c.OwnerId == userId && !c.PendingInboxClaim);
        if (creature is null)
            return ServiceResult.NotFound("Criatura não encontrada");
        if (creature.IsDead)
            return ServiceResult.Bad("Essa criatura não sobreviveu à gestação");
        // Só transfere do tanque ou da mochila; se listada, cancele antes.
        bool listed = await db.MarketListings.AnyAsync(m =>
            m.CreatureInstanceId == creature.Id && m.Status == ListingStatus.Active);
        if (listed)
            return ServiceResult.Bad("Criatura está no mercado — cancele a listagem antes");

        var target = await db.Users.FirstOrDefaultAsync(u => u.Username == toUsername);
        if (target is null)
            return ServiceResult.NotFound("Jogador destinatário não encontrado");
        if (target.Id == userId)
            return ServiceResult.Bad("Não dá pra transferir pra si mesmo");

        var now = DateTime.UtcNow;
        creature.OwnerId = target.Id;
        // Deixa de cair direto no tanque/mochila do destinatário — vai pra Caixa de Entrada
        // dele, resgatado explicitamente (CLAUDE.md §8.23/§8.24). Espaço deixa de ser checado
        // aqui; falta de espaço agora é um erro no momento do resgate, não da transferência.
        creature.HabitatId = null;
        creature.PendingInboxClaim = true;

        db.TransactionLogs.Add(new TransactionLog
        {
            Type = TransactionType.DirectTransfer,
            FromUserId = userId,
            ToUserId = target.Id,
            CreatureInstanceId = creature.Id,
            CreatedAt = now,
        });
        db.InboxEntries.Add(new InboxEntry
        {
            RecipientId = target.Id,
            Kind = InboxEntryKind.DirectTransfer,
            SenderUserId = userId,
            CreatureInstanceId = creature.Id,
            CreatedAt = now,
        });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("O peixe mudou de estado — atualize e tente de novo.");
        }
        return ServiceResult.Success(new { transferredTo = target.Username });
    }

    public async Task<ServiceResult> CollectQueueItemAsync(long userId, long queueItemId, DateTime now)
    {
        var habitat = await FindHabitatAsync(userId);
        if (habitat is null)
            return ServiceResult.NotFound("Habitat não encontrado");

        await ApplyTickAsync(habitat, now);
        var (creature, error) = await CollectInternalAsync(habitat, queueItemId, now);
        if (creature is null)
            return ServiceResult.Bad(error!);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("Operação concorrente — tente de novo.");
        }
        return ServiceResult.Success(CreatureDto.From(creature));
    }

    public async Task<ServiceResult> GetBackpackAsync(long userId)
    {
        var creatures = (await BackpackQuery(userId).ToListAsync())
            .Select(CreatureDto.From)
            .ToList();
        return ServiceResult.Success(new BackpackResponse(HabitatDefaults.BackpackCapacity, creatures));
    }

    public async Task<ServiceResult> StoreAsync(long userId, long creatureId)
    {
        var habitat = await FindHabitatAsync(userId);
        if (habitat is null)
            return ServiceResult.NotFound("Habitat não encontrado");
        var error = await StoreCoreAsync(userId, creatureId, habitat);
        if (error is not null)
            return ServiceResult.Bad(error);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return ServiceResult.Conflict("Tente de novo."); }
        return ServiceResult.Success();
    }

    public async Task<ServiceResult> DeployAsync(long userId, long creatureId)
    {
        var habitat = await FindHabitatAsync(userId);
        if (habitat is null)
            return ServiceResult.NotFound("Habitat não encontrado");
        var error = await DeployCoreAsync(userId, creatureId, habitat);
        if (error is not null)
            return ServiceResult.Bad(error);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return ServiceResult.Conflict("Tente de novo."); }
        return ServiceResult.Success();
    }

    // ---------- Venda ao NPC (vendor, §8.12) ----------
    // Sink pra duplicatas/comuns acumulados: preço baixo (VendorCalculator), mas instantâneo
    // e sem depender de outro jogador comprar. Não apaga a linha (mesmo motivo do IsDead —
    // preserva FK Restrict de linhagem e o histórico do TransactionLog): marca SoldAt e some
    // das queries de tanque/mochila.

    public async Task<ServiceResult> SellToVendorAsync(long userId, long creatureId, DateTime now)
    {
        var creature = await db.CreatureInstances
            .FirstOrDefaultAsync(c => c.Id == creatureId && c.OwnerId == userId);
        if (creature is null)
            return ServiceResult.NotFound("Criatura não encontrada");
        if (creature.IsDead)
            return ServiceResult.Bad("Essa criatura não sobreviveu à gestação");
        if (creature.SoldAt is not null)
            return ServiceResult.Bad("Essa criatura já foi vendida");
        if (creature.PendingInboxClaim)
            return ServiceResult.Bad("Criatura pendente de resgate na Caixa de Entrada");
        bool listed = await db.MarketListings.AnyAsync(m =>
            m.CreatureInstanceId == creature.Id && m.Status == ListingStatus.Active);
        if (listed)
            return ServiceResult.Bad("Criatura está no mercado — cancele a listagem antes");

        decimal price = VendorCalculator.Price(creature.RarityScore, TickConfig.Default);

        int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
        var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == softId);
        wallet.Amount += price;
        creature.SoldAt = now;
        creature.HabitatId = null;

        db.TransactionLogs.Add(new TransactionLog
        {
            Type = TransactionType.VendorSale,
            ToUserId = userId,
            CreatureInstanceId = creature.Id,
            CurrencyTypeId = softId,
            Amount = price,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("Operação concorrente — tente de novo.");
        }
        return ServiceResult.Success(new { price, wallet = wallet.Amount });
    }
}
