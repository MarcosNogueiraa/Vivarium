using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Core.Domain;
using Vivarium.Core.Gameplay;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Services;

/// <summary>
/// Cruzamento de peixes (CLAUDE.md 8.8). O casal vai fisicamente pro habitat de
/// reprodução dedicado do usuário; a gestação escala com a raridade combinada dos
/// pais (BreedingCalculator) — sink de soft (custo dinâmico) + de tempo (o pai fica
/// fora do tanque principal, sem render renda, durante a gestação) + risco
/// crescente do pai não sobreviver a cada gestação completada. Só 1 gestação em
/// andamento por usuário no MVP (capacidade do habitat de reprodução = 2).
/// </summary>
public class BreedingService(VivariumDbContext db, GameService game)
{
    /// <summary>Habitat de reprodução do usuário — criado sob demanda (contas antigas não têm um).</summary>
    public async Task<Habitat> FindOrCreateBreedingHabitatAsync(long userId)
    {
        var habitat = await db.Habitats
            .FirstOrDefaultAsync(h => h.UserId == userId && h.HabitatType!.Code == "Breeding");
        if (habitat is not null)
            return habitat;

        int breedingTypeId = await db.HabitatTypes.Where(h => h.Code == "Breeding").Select(h => h.Id).FirstAsync();
        var now = DateTime.UtcNow;
        habitat = new Habitat
        {
            UserId = userId,
            HabitatTypeId = breedingTypeId,
            Capacity = 2,
            MaintenanceLevel = 100m,
            QueueCap = 0,
            GenerationIntervalMinutes = int.MaxValue, // nunca gera fila; nunca passa por ApplyTickAsync
            OnlineGenerationRate = 0m,
            OfflineGenerationRate = 0m,
            LastTickAt = now,
            CreatedAt = now,
        };
        db.Habitats.Add(habitat);
        await db.SaveChangesAsync();
        return habitat;
    }

    /// <summary>Prévia sem custo/compromisso: custo, gestação, chances do filho e risco de morte dos pais.</summary>
    public async Task<ServiceResult> GetQuoteAsync(long userId, long parentAId, long parentBId)
    {
        if (parentAId == parentBId)
            return ServiceResult.Bad("Escolha dois peixes diferentes");

        var parentA = await db.CreatureInstances.FirstOrDefaultAsync(c => c.Id == parentAId && c.OwnerId == userId && !c.IsDead);
        var parentB = await db.CreatureInstances.FirstOrDefaultAsync(c => c.Id == parentBId && c.OwnerId == userId && !c.IsDead);
        if (parentA is null || parentB is null)
            return ServiceResult.NotFound("Peixe não encontrado");

        var now = DateTime.UtcNow;
        double hours = BreedingCalculator.GestationHours(parentA.RarityScore, parentB.RarityScore);
        decimal cost = BreedingCalculator.CostSoft(parentA.RarityScore, parentB.RarityScore);
        var tierDist = TraitGenerator.ChildTierDistribution(
            parentA.Seed, parentB.Seed, TraitConfigV1.Version, BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength);

        double chanceA = BreedingCalculator.DeathChance(BreedingCalculator.EffectiveBreedCount(parentA.BreedCount, parentA.LastBredAt, now));
        double chanceB = BreedingCalculator.DeathChance(BreedingCalculator.EffectiveBreedCount(parentB.BreedCount, parentB.LastBredAt, now));

        var quote = new BreedingQuoteDto(
            cost, hours, now.AddHours(hours),
            tierDist.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            parentA.BreedCount, chanceA,
            parentB.BreedCount, chanceB,
            BreedingDefaults.StabilizerCostSoft, BreedingDefaults.StabilizerReductionFactor,
            BreedingCalculator.InsuranceCostPremium(chanceA, chanceB));
        return ServiceResult.Success(quote);
    }

    public async Task<ServiceResult> StartAsync(long userId, long parentAId, long parentBId, DateTime now, bool useStabilizer = false, bool useInsurance = false)
    {
        if (parentAId == parentBId)
            return ServiceResult.Bad("Escolha dois peixes diferentes");

        var parentA = await db.CreatureInstances.FirstOrDefaultAsync(c => c.Id == parentAId && c.OwnerId == userId && !c.IsDead);
        var parentB = await db.CreatureInstances.FirstOrDefaultAsync(c => c.Id == parentBId && c.OwnerId == userId && !c.IsDead);
        if (parentA is null || parentB is null)
            return ServiceResult.NotFound("Peixe não encontrado");

        bool anyListed = await db.MarketListings.AnyAsync(m =>
            m.Status == ListingStatus.Active && (m.CreatureInstanceId == parentAId || m.CreatureInstanceId == parentBId));
        if (anyListed)
            return ServiceResult.Bad("Um dos peixes está no mercado — cancele a listagem antes");

        bool alreadyBreeding = await db.BreedingSlots.AnyAsync(s =>
            s.Status == BreedingStatus.InProgress
            && (s.ParentAId == parentAId || s.ParentBId == parentAId || s.ParentAId == parentBId || s.ParentBId == parentBId));
        if (alreadyBreeding)
            return ServiceResult.Bad("Um dos peixes já está em gestação");

        bool userAlreadyBreeding = await db.BreedingSlots.AnyAsync(s => s.UserId == userId && s.Status == BreedingStatus.InProgress);
        if (userAlreadyBreeding)
            return ServiceResult.Bad("Você já tem um casal em gestação");

        // Risco travado agora (descanso já aplicado) — o Start é o momento em que o jogador
        // decide se paga pra mitigar. Seguro tem prioridade se os dois forem marcados.
        double chanceA = BreedingCalculator.DeathChance(BreedingCalculator.EffectiveBreedCount(parentA.BreedCount, parentA.LastBredAt, now));
        double chanceB = BreedingCalculator.DeathChance(BreedingCalculator.EffectiveBreedCount(parentB.BreedCount, parentB.LastBredAt, now));
        bool insuranceApplied = useInsurance;
        bool stabilizerApplied = useStabilizer && !useInsurance;

        decimal insuranceCost = 0m;
        if (insuranceApplied)
        {
            insuranceCost = BreedingCalculator.InsuranceCostPremium(chanceA, chanceB);
            chanceA = 0;
            chanceB = 0;
        }
        else if (stabilizerApplied)
        {
            chanceA *= BreedingDefaults.StabilizerReductionFactor;
            chanceB *= BreedingDefaults.StabilizerReductionFactor;
        }

        decimal baseCost = BreedingCalculator.CostSoft(parentA.RarityScore, parentB.RarityScore);
        decimal softCost = baseCost + (stabilizerApplied ? BreedingDefaults.StabilizerCostSoft : 0m);

        int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
        var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == softId);
        if (wallet.Amount < softCost)
            return ServiceResult.Bad("Saldo insuficiente");

        WalletBalance? premiumWallet = null;
        int premiumId = 0;
        if (insuranceApplied)
        {
            premiumId = await db.CurrencyTypes.Where(c => c.Code == "PREMIUM").Select(c => c.Id).FirstAsync();
            premiumWallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == premiumId);
            if (premiumWallet.Amount < insuranceCost)
                return ServiceResult.Bad("Saldo de moeda premium insuficiente pro seguro");
        }

        var breedingHabitat = await FindOrCreateBreedingHabitatAsync(userId);

        await using var transaction = await db.Database.BeginTransactionAsync();

        wallet.Amount -= softCost;
        if (insuranceApplied && premiumWallet is not null)
            premiumWallet.Amount -= insuranceCost;
        parentA.HabitatId = breedingHabitat.Id;
        parentB.HabitatId = breedingHabitat.Id;

        double hours = BreedingCalculator.GestationHours(parentA.RarityScore, parentB.RarityScore);
        var slot = new BreedingSlot
        {
            UserId = userId,
            HabitatId = breedingHabitat.Id,
            ParentAId = parentA.Id,
            ParentBId = parentB.Id,
            StartedAt = now,
            ReadyAt = now.AddHours(hours),
            CostPaid = softCost,
            ParentADeathChance = (decimal)chanceA,
            ParentBDeathChance = (decimal)chanceB,
            InsuranceUsed = insuranceApplied,
            Status = BreedingStatus.InProgress,
        };
        db.BreedingSlots.Add(slot);

        db.TransactionLogs.Add(new TransactionLog
        {
            Type = TransactionType.Breeding,
            FromUserId = userId,
            CurrencyTypeId = softId,
            Amount = softCost,
            CreatedAt = now,
        });
        if (insuranceApplied)
        {
            db.TransactionLogs.Add(new TransactionLog
            {
                Type = TransactionType.BreedingInsurance,
                FromUserId = userId,
                CurrencyTypeId = premiumId,
                Amount = insuranceCost,
                CreatedAt = now,
            });
        }

        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("Um dos peixes mudou de estado — atualize e tente de novo.");
        }

        return ServiceResult.Success(new { slotId = slot.Id, readyAt = slot.ReadyAt, costPaid = softCost, insuranceCostPaid = insuranceCost });
    }

    public async Task<ServiceResult> GetStatusAsync(long userId)
    {
        var slot = await db.BreedingSlots
            .Include(s => s.ParentA)
            .Include(s => s.ParentB)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == BreedingStatus.InProgress);
        if (slot is null)
            return ServiceResult.Success(new BreedingStatusResponse(false, null));

        var now = DateTime.UtcNow;
        bool ready = slot.ReadyAt <= now;
        decimal rushCost = ready ? 0m : RushCalculator.GestationRushCost((decimal)(slot.ReadyAt - now).TotalHours);
        var dto = new BreedingSlotDto(
            slot.Id, CreatureDto.From(slot.ParentA!), CreatureDto.From(slot.ParentB!),
            slot.StartedAt, slot.ReadyAt, ready, slot.CostPaid, rushCost,
            slot.ParentADeathChance, slot.ParentBDeathChance, slot.InsuranceUsed);
        return ServiceResult.Success(new BreedingStatusResponse(true, dto));
    }

    /// <summary>Pula o tempo restante de gestação pagando premium (8.11) — mesma ideia do rush da fila.</summary>
    public async Task<ServiceResult> RushAsync(long userId, DateTime now)
    {
        var slot = await db.BreedingSlots.FirstOrDefaultAsync(s => s.UserId == userId && s.Status == BreedingStatus.InProgress);
        if (slot is null)
            return ServiceResult.NotFound("Nenhuma gestação em andamento");
        if (slot.ReadyAt <= now)
            return ServiceResult.Bad("Já está pronta — não precisa acelerar");

        decimal cost = RushCalculator.GestationRushCost((decimal)(slot.ReadyAt - now).TotalHours);
        int premiumId = await db.CurrencyTypes.Where(c => c.Code == "PREMIUM").Select(c => c.Id).FirstAsync();
        var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == premiumId);
        if (wallet.Amount < cost)
            return ServiceResult.Bad("Saldo de moeda premium insuficiente");

        wallet.Amount -= cost;
        slot.ReadyAt = now;

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
        return ServiceResult.Success(new { paid = cost, readyAt = slot.ReadyAt });
    }

    public async Task<ServiceResult> CollectAsync(long userId, DateTime now)
    {
        var slot = await db.BreedingSlots.FirstOrDefaultAsync(s => s.UserId == userId && s.Status == BreedingStatus.InProgress);
        if (slot is null)
            return ServiceResult.NotFound("Nenhuma gestação em andamento");
        if (slot.ReadyAt > now)
            return ServiceResult.Bad("Ainda não está pronto");

        var parentA = await db.CreatureInstances.FirstAsync(c => c.Id == slot.ParentAId);
        var parentB = await db.CreatureInstances.FirstAsync(c => c.Id == slot.ParentBId);

        var mainHabitat = await game.FindHabitatAsync(userId);
        if (mainHabitat is null)
            return ServiceResult.NotFound("Habitat não encontrado");

        // Espaço checado com um contador local (não re-consultar o banco a cada
        // posicionamento — 3 criaturas se movem numa única chamada, e re-consultar
        // não veria as anteriores ainda não salvas).
        int active = await db.CreatureInstances.CountAsync(c => c.HabitatId == mainHabitat.Id);
        int backpackCount = await game.CountBackpackAsync(userId);

        bool childToTank = active < mainHabitat.Capacity;
        if (!childToTank && backpackCount >= HabitatDefaults.BackpackCapacity)
            return ServiceResult.Bad("Tanque e mochila cheios — abra espaço antes de coletar.");

        long childSeed = CreatureCollector.NewRandomSeed();
        // Ancestralidade de cada pai: se ele mesmo é filhote, ParentASeed/BSeed (já carregados
        // nesta entidade, zero query extra) SÃO os seeds dos avós do lado dele — habilita a
        // chance de herdar um traço de um avô em vez do pai direto (8.8, 31/07/2026) e evita
        // reconstruir esse pai com Generate(seed) quando na verdade ele é um filhote.
        var ancestryA = new TraitGenerator.ParentAncestry(parentA.Seed, parentA.ParentASeed, parentA.ParentBSeed);
        var ancestryB = new TraitGenerator.ParentAncestry(parentB.Seed, parentB.ParentASeed, parentB.ParentBSeed);
        var traits = TraitGenerator.BreedTraits(
            childSeed, ancestryA, ancestryB, TraitConfigV1.Version,
            BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength, BreedingDefaults.GrandparentReachChance);

        var child = new CreatureInstance
        {
            SpeciesId = parentA.SpeciesId,
            OwnerId = userId,
            HabitatId = childToTank ? mainHabitat.Id : null,
            Seed = childSeed,
            TraitConfigVersion = TraitConfigV1.Version,
            RarityScore = (decimal)traits.RarityScore,
            ParentAId = parentA.Id,
            ParentBId = parentB.Id,
            // Denormalizado no filho pra reconstruir os traits herdados (BreedTraits)
            // sem precisar de join — os seeds dos pais nunca mudam.
            ParentASeed = parentA.Seed,
            ParentBSeed = parentB.Seed,
            // Idem, uma geração mais fundo: se ESTE filhote virar pai de outro cruzamento no
            // futuro, esses seeds são os avós daquele filho — sem eles, `BreedTraits` teria que
            // reconstruir este filhote com Generate(seed), o mesmo bug corrigido aqui.
            ParentAGrandparentASeed = parentA.ParentASeed,
            ParentAGrandparentBSeed = parentA.ParentBSeed,
            ParentBGrandparentASeed = parentB.ParentASeed,
            ParentBGrandparentBSeed = parentB.ParentBSeed,
            CreatedAt = now,
        };
        db.CreatureInstances.Add(child);
        if (childToTank) active++; else backpackCount++;

        // Risco travado no Start (já considerando descanso e estabilizador/seguro, se usados) —
        // não recalcula aqui, pra não deixar a gestação em si (o pai "ocupado") contar como
        // descanso. Roll não-determinístico (não faz parte do motor de traits reproduzível).
        bool parentADied = Random.Shared.NextDouble() < (double)slot.ParentADeathChance;
        bool parentBDied = Random.Shared.NextDouble() < (double)slot.ParentBDeathChance;

        // Devolve os pais ao tanque/mochila (se sobreviveram); se nenhum lugar
        // couber, ficam no habitat de reprodução até o jogador abrir espaço.
        foreach (var (parent, died) in new[] { (parentA, parentADied), (parentB, parentBDied) })
        {
            parent.BreedCount++;
            parent.LastBredAt = now;
            if (died)
            {
                parent.IsDead = true;
                parent.DiedAt = now;
                parent.HabitatId = null;
                db.TransactionLogs.Add(new TransactionLog
                {
                    Type = TransactionType.BreedingLoss,
                    FromUserId = userId,
                    CreatureInstanceId = parent.Id,
                    CreatedAt = now,
                });
                continue;
            }

            if (active < mainHabitat.Capacity)
            {
                parent.HabitatId = mainHabitat.Id;
                active++;
            }
            else if (backpackCount < HabitatDefaults.BackpackCapacity)
            {
                parent.HabitatId = null;
                backpackCount++;
            }
        }

        slot.Status = BreedingStatus.Collected;
        slot.ChildCreature = child; // navegação, não o Id cru: child ainda não tem Id gerado nesta transação

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("Operação concorrente — tente de novo.");
        }

        return ServiceResult.Success(new CollectBreedingResponse(CreatureDto.From(child), parentADied, parentBDied));
    }
}
