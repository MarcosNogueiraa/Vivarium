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

    /// <summary>
    /// Credita <paramref name="amount"/> de moeda premium na carteira de todo usuário (evento
    /// pontual — pedido do usuário, 12/08/2026, pra testar uso/VIP em produção com jogadores
    /// reais). Audita 1 TransactionLog.AdminGrant por usuário, mesmo padrão de toda transação
    /// nomeada da economia. Idempotente só na intenção, não no efeito: rodar de novo credita
    /// de novo (mesmo comportamento de "dar peixe a todos" — ferramenta manual, não um cupom).
    /// </summary>
    public async Task<ServiceResult> GrantPremiumToAllAsync(long requestingUserId, decimal amount, DateTime now)
    {
        if (!await IsAdminAsync(requestingUserId))
            return ServiceResult.Forbidden("Só administradores podem fazer isso");
        if (amount <= 0)
            return ServiceResult.Bad("Quantia precisa ser positiva");

        int premiumId = await db.CurrencyTypes.Where(c => c.Code == "PREMIUM").Select(c => c.Id).FirstAsync();

        var wallets = await db.WalletBalances
            .Where(w => w.CurrencyTypeId == premiumId)
            .ToListAsync();

        foreach (var wallet in wallets)
        {
            wallet.Amount += amount;
            db.TransactionLogs.Add(new TransactionLog
            {
                Type = TransactionType.AdminGrant,
                ToUserId = wallet.UserId,
                CurrencyTypeId = premiumId,
                Amount = amount,
                CreatedAt = now,
            });
        }

        return ServiceResult.Success(new { usersAffected = wallets.Count, amount });
    }

    /// <summary>
    /// Ajusta a carteira de UM jogador específico — "add" soma (delta pode ser negativo,
    /// pra remover; resultado nunca fica abaixo de 0) e "set" define o saldo absoluto
    /// (precisa ser ≥ 0). Ferramenta manual pontual (suporte/correção/teste), mesmo
    /// espírito de GrantPremiumToAllAsync, só que mirada e com os dois modos.
    /// </summary>
    public async Task<ServiceResult> AdjustUserWalletAsync(
        long requestingUserId, string username, string currencyCode, string mode, decimal amount, DateTime now)
    {
        if (!await IsAdminAsync(requestingUserId))
            return ServiceResult.Forbidden("Só administradores podem fazer isso");

        currencyCode = currencyCode.Trim().ToUpperInvariant();
        if (currencyCode is not ("SOFT" or "PREMIUM"))
            return ServiceResult.Bad("Moeda inválida (use SOFT ou PREMIUM)");

        mode = mode.Trim().ToLowerInvariant();
        if (mode is not ("add" or "set"))
            return ServiceResult.Bad("Modo inválido (use add ou set)");
        if (mode == "set" && amount < 0)
            return ServiceResult.Bad("Saldo definido não pode ser negativo");

        var target = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (target is null)
            return ServiceResult.NotFound("Jogador não encontrado");

        int currencyId = await db.CurrencyTypes.Where(c => c.Code == currencyCode).Select(c => c.Id).FirstAsync();
        var wallet = await db.WalletBalances
            .FirstOrDefaultAsync(w => w.UserId == target.Id && w.CurrencyTypeId == currencyId);
        if (wallet is null)
        {
            wallet = new WalletBalance { UserId = target.Id, CurrencyTypeId = currencyId, Amount = 0 };
            db.WalletBalances.Add(wallet);
        }

        decimal before = wallet.Amount;
        wallet.Amount = mode == "set" ? amount : Math.Max(0, wallet.Amount + amount);
        decimal delta = wallet.Amount - before;

        if (delta != 0)
        {
            db.TransactionLogs.Add(new TransactionLog
            {
                Type = TransactionType.AdminGrant,
                ToUserId = target.Id,
                CurrencyTypeId = currencyId,
                Amount = delta,
                CreatedAt = now,
            });
        }

        return ServiceResult.Success(new { username = target.Username, currencyCode, balance = wallet.Amount });
    }
}
