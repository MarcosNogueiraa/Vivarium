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
/// pais (BreedingCalculator) — sink de soft (custo imediato) + de tempo (o pai fica
/// fora do tanque principal, sem render renda, durante a gestação). Só 1 gestação
/// em andamento por usuário no MVP (capacidade do habitat de reprodução = 2).
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

    public async Task<ServiceResult> StartAsync(long userId, long parentAId, long parentBId, DateTime now)
    {
        if (parentAId == parentBId)
            return ServiceResult.Bad("Escolha dois peixes diferentes");

        var parentA = await db.CreatureInstances.FirstOrDefaultAsync(c => c.Id == parentAId && c.OwnerId == userId);
        var parentB = await db.CreatureInstances.FirstOrDefaultAsync(c => c.Id == parentBId && c.OwnerId == userId);
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

        int softId = await db.CurrencyTypes.Where(c => c.Code == "SOFT").Select(c => c.Id).FirstAsync();
        var wallet = await db.WalletBalances.FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == softId);
        if (wallet.Amount < BreedingDefaults.CostSoft)
            return ServiceResult.Bad("Saldo insuficiente");

        var breedingHabitat = await FindOrCreateBreedingHabitatAsync(userId);

        await using var transaction = await db.Database.BeginTransactionAsync();

        wallet.Amount -= BreedingDefaults.CostSoft;
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
            Status = BreedingStatus.InProgress,
        };
        db.BreedingSlots.Add(slot);

        db.TransactionLogs.Add(new TransactionLog
        {
            Type = TransactionType.Breeding,
            FromUserId = userId,
            CurrencyTypeId = softId,
            Amount = BreedingDefaults.CostSoft,
            CreatedAt = now,
        });

        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("Um dos peixes mudou de estado — atualize e tente de novo.");
        }

        return ServiceResult.Success(new { slotId = slot.Id, readyAt = slot.ReadyAt });
    }

    public async Task<ServiceResult> GetStatusAsync(long userId)
    {
        var slot = await db.BreedingSlots
            .Include(s => s.ParentA)
            .Include(s => s.ParentB)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == BreedingStatus.InProgress);
        if (slot is null)
            return ServiceResult.Success(new BreedingStatusResponse(false, null));

        var dto = new BreedingSlotDto(
            slot.Id, CreatureDto.From(slot.ParentA!), CreatureDto.From(slot.ParentB!),
            slot.StartedAt, slot.ReadyAt, slot.ReadyAt <= DateTime.UtcNow);
        return ServiceResult.Success(new BreedingStatusResponse(true, dto));
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
        var traits = TraitGenerator.BreedTraits(
            childSeed, parentA.Seed, parentB.Seed, TraitConfigV1.Version,
            BreedingDefaults.MutationChance, BreedingDefaults.RarityBiasStrength);

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
            CreatedAt = now,
        };
        db.CreatureInstances.Add(child);
        if (childToTank) active++; else backpackCount++;

        // Devolve os pais ao tanque/mochila; se nenhum couber, ficam no habitat de
        // reprodução até o jogador abrir espaço (sem perda, só adiado).
        foreach (var parent in new[] { parentA, parentB })
        {
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

        return ServiceResult.Success(CreatureDto.From(child));
    }
}
