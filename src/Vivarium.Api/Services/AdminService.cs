using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Services;

/// <summary>
/// Ferramentas administrativas — nada aqui é lógica de jogo, só operações manuais
/// pontuais (ex: eventos, correções). Todo endpoint que usa isso exige IsAdmin=true
/// (checado no próprio serviço, não só no endpoint, pra nunca vazar sem a checagem).
/// </summary>
public class AdminService(VivariumDbContext db)
{
    private async Task<bool> IsAdminAsync(long userId)
        => await db.Users.Where(u => u.Id == userId).Select(u => u.IsAdmin).FirstOrDefaultAsync();

    /// <summary>
    /// Dá 1 peixe pronto pra coletar a todo aquário que ainda tem espaço na fila
    /// (respeita QueueCap) — mesma mecânica do peixe inicial no registro (AuthEndpoints),
    /// só que aplicada a todas as contas de uma vez. Idempotente na prática: rodar de
    /// novo só preenche quem não estiver com a fila cheia.
    /// </summary>
    public async Task<ServiceResult> GiveStarterFishToAllAsync(long requestingUserId, DateTime now)
    {
        if (!await IsAdminAsync(requestingUserId))
            return ServiceResult.Forbidden("Só administradores podem fazer isso");

        var aquariumTypeId = await db.HabitatTypes
            .Where(h => h.Code == "Aquarium").Select(h => h.Id).FirstAsync();
        int speciesId = await db.Species
            .Where(s => s.HabitatTypeId == aquariumTypeId).Select(s => s.Id).FirstAsync();

        var eligibleHabitats = await db.Habitats
            .Where(h => h.HabitatTypeId == aquariumTypeId)
            .Where(h => db.GenerationQueueItems.Count(q =>
                q.HabitatId == h.Id && q.Status == QueueItemStatus.Pending) < h.QueueCap)
            .Select(h => h.Id)
            .ToListAsync();

        foreach (long habitatId in eligibleHabitats)
        {
            db.GenerationQueueItems.Add(new GenerationQueueItem
            {
                HabitatId = habitatId,
                SpeciesId = speciesId,
                ReadyAt = now,
                Status = QueueItemStatus.Pending,
                IsSick = false,
            });
        }

        return ServiceResult.Success(new { habitatsAffected = eligibleHabitats.Count });
    }
}
