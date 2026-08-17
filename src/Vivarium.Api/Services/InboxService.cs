using Microsoft.EntityFrameworkCore;
using Vivarium.Api.Contracts;
using Vivarium.Api.Data;
using Vivarium.Api.Http;
using Vivarium.Core.Domain;

namespace Vivarium.Api.Services;

/// <summary>
/// Caixa de Entrada (14/08/2026, CLAUDE.md §8.23/§8.24) — mensagens administrativas (broadcast ou
/// segmentada, com recompensa opcional) e entrega pendente de peixe (compra no mercado ou
/// transferência direta), unificadas na mesma lista. Item resgatado não desaparece — fica
/// marcado (<see cref="InboxEntry.ClaimedAt"/>) até o jogador limpar explicitamente.
/// </summary>
public class InboxService(VivariumDbContext db, GameService game, ILogger<InboxService> logger)
{
    private async Task<bool> IsAdminAsync(long userId)
        => await db.Users.Where(u => u.Id == userId).Select(u => u.IsAdmin).FirstOrDefaultAsync();

    private static InboxEntryDto ToDto(InboxEntry e) => new(
        e.Id, e.Kind.ToString(),
        e.InboxMessage?.Title, e.InboxMessage?.Body,
        e.SenderUser?.Username,
        e.CreatureInstance is not null ? CreatureDto.From(e.CreatureInstance) : null,
        e.InboxMessage?.RewardCurrencyType?.Code, e.InboxMessage?.RewardCurrencyAmount,
        e.ReadAt, e.ClaimedAt, e.CreatedAt);

    public async Task<ServiceResult> ListAsync(long userId)
    {
        var entries = await db.InboxEntries
            .Where(e => e.RecipientId == userId)
            .Include(e => e.InboxMessage).ThenInclude(m => m!.RewardCurrencyType)
            .Include(e => e.SenderUser)
            .Include(e => e.CreatureInstance)
            // Pendentes primeiro (o que precisa de atenção), depois mais recentes primeiro dentro
            // de cada grupo — resgatado não desaparece (pedido do usuário), só fica pra trás.
            .OrderBy(e => e.ClaimedAt != null)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync();

        return ServiceResult.Success(new InboxListResponse(entries.Select(ToDto).ToList()));
    }

    public async Task<ServiceResult> ClaimAsync(long userId, long entryId, DateTime now)
    {
        var entry = await db.InboxEntries
            .Include(e => e.InboxMessage)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.RecipientId == userId && e.ClaimedAt == null);
        if (entry is null)
            return ServiceResult.NotFound("Item não encontrado ou já resgatado");

        var applied = await TryApplyRewardAsync(entry, userId, now);
        if (!applied.Ok)
            return applied;

        entry.ClaimedAt = now;
        entry.ReadAt ??= now;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceResult.Conflict("O peixe mudou de estado — atualize e tente de novo.");
        }
        return ServiceResult.Success();
    }

    /// <summary>Coloca o peixe pendente no tanque/mochila e/ou credita a recompensa da mensagem —
    /// SEM marcar a entrada como resgatada (isso é responsabilidade de quem chama). Devolve
    /// erro (sem persistir nada) se não houver espaço pro peixe.</summary>
    private async Task<ServiceResult> TryApplyRewardAsync(InboxEntry entry, long userId, DateTime now)
    {
        if (entry.CreatureInstanceId is not null)
        {
            var creature = await db.CreatureInstances.FirstAsync(c => c.Id == entry.CreatureInstanceId);
            var habitat = await game.FindHabitatAsync(userId);
            if (habitat is null)
                return ServiceResult.NotFound("Habitat não encontrado");
            if (!await game.TryPlaceAsync(creature, habitat))
                return ServiceResult.Bad("Seu tanque e mochila estão cheios — libere espaço antes de resgatar.");
            creature.PendingInboxClaim = false;
        }

        if (entry.InboxMessage is { RewardCurrencyTypeId: not null, RewardCurrencyAmount: not null } msg)
        {
            var wallet = await db.WalletBalances
                .FirstAsync(w => w.UserId == userId && w.CurrencyTypeId == msg.RewardCurrencyTypeId);
            wallet.Amount += msg.RewardCurrencyAmount!.Value;
            db.TransactionLogs.Add(new TransactionLog
            {
                Type = TransactionType.InboxReward,
                ToUserId = userId,
                CurrencyTypeId = msg.RewardCurrencyTypeId,
                Amount = msg.RewardCurrencyAmount,
                CreatedAt = now,
            });
        }
        // RewardItemDefinitionId fica dormente nesta leva (nenhuma UI admin ainda seta isso) —
        // ver CLAUDE.md §8.24 pro porquê.

        return ServiceResult.Success();
    }

    /// <summary>Resgata todas as entradas pendentes do usuário, uma de cada vez (não em paralelo —
    /// CreatureInstance/Habitat usam concorrência otimista via xmin, mesma razão do bulk-sell
    /// sequencial da Mochila). Uma falha (ex: sem espaço) não impede o resto de seguir.</summary>
    public async Task<ServiceResult> ClaimAllAsync(long userId, DateTime now)
    {
        var pendingIds = await db.InboxEntries
            .Where(e => e.RecipientId == userId && e.ClaimedAt == null)
            .Select(e => e.Id)
            .ToListAsync();

        int claimed = 0, failed = 0;
        foreach (long id in pendingIds)
        {
            var result = await ClaimAsync(userId, id, now);
            if (result.Ok) claimed++; else failed++;
        }
        return ServiceResult.Success(new ClaimAllResponse(claimed, failed));
    }

    public async Task<ServiceResult> MarkAllReadAsync(long userId, DateTime now)
    {
        await db.InboxEntries
            .Where(e => e.RecipientId == userId && e.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReadAt, now));
        return ServiceResult.Success();
    }

    /// <summary>Remove só entradas JÁ resgatadas (ClaimedAt preenchido) — nunca uma com
    /// recompensa/entrega ainda em aberto, mesmo que "lida". Hard delete: o TransactionLog da
    /// recompensa (se houve) já foi gravado separadamente no momento do resgate, então apagar a
    /// InboxEntry depois não perde nenhum rastro de auditoria.</summary>
    public async Task<ServiceResult> ClearClaimedAsync(long userId)
    {
        await db.InboxEntries
            .Where(e => e.RecipientId == userId && e.ClaimedAt != null)
            .ExecuteDeleteAsync();
        return ServiceResult.Success();
    }

    /// <summary>Cria uma mensagem+entrada de sistema pra 1 destinatário — sem admin, sem
    /// recompensa a resgatar (o que ela anuncia, se for dinheiro, já foi creditado por quem
    /// chamou). Não salva (deixa o SaveChangesAsync/transação pra quem chama, mesmo padrão de
    /// <see cref="TryApplyRewardAsync"/>) — assim o disparo entra na MESMA transação do evento
    /// que a originou (ex: venda no Mercado), sem round-trip extra nem risco de a notificação
    /// existir sem o evento (ou vice-versa) se algo falhar no meio.</summary>
    public void QueueSystemMessage(
        long recipientId, InboxEntryKind kind, string title, string body, long? senderUserId, DateTime now)
    {
        var message = new InboxMessage { Title = title, Body = body, CreatedAt = now };
        db.InboxMessages.Add(message);
        db.InboxEntries.Add(new InboxEntry
        {
            RecipientId = recipientId,
            Kind = kind,
            InboxMessage = message,
            SenderUserId = senderUserId,
            CreatedAt = now,
        });
    }

    public async Task<ServiceResult> AdminSendMessageAsync(
        long requestingUserId, string title, string body, InboxAudience audience,
        IReadOnlyList<string>? usernames, string? rewardCurrencyCode, decimal? rewardCurrencyAmount, DateTime now)
    {
        if (!await IsAdminAsync(requestingUserId))
            return ServiceResult.Forbidden("Só administradores podem fazer isso");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
            return ServiceResult.Bad("Título e mensagem não podem ficar vazios");
        if (audience == InboxAudience.Selected && (usernames is null || usernames.Count == 0))
            return ServiceResult.Bad("Informe pelo menos um username pra audiência selecionada");

        int? rewardCurrencyId = null;
        if (!string.IsNullOrWhiteSpace(rewardCurrencyCode))
        {
            string code = rewardCurrencyCode.Trim().ToUpperInvariant();
            if (code is not ("SOFT" or "PREMIUM"))
                return ServiceResult.Bad("Moeda inválida (use SOFT ou PREMIUM)");
            if (rewardCurrencyAmount is null || rewardCurrencyAmount <= 0)
                return ServiceResult.Bad("Quantia da recompensa precisa ser positiva");
            rewardCurrencyId = await db.CurrencyTypes.Where(c => c.Code == code).Select(c => c.Id).FirstAsync();
        }

        List<long> recipientIds;
        var notFoundUsernames = new List<string>();
        if (audience == InboxAudience.All)
        {
            recipientIds = await db.Users.Select(u => u.Id).ToListAsync();
        }
        else
        {
            var trimmed = usernames!.Select(u => u.Trim()).Where(u => u.Length > 0).Distinct().ToList();
            var found = await db.Users.Where(u => trimmed.Contains(u.Username))
                .Select(u => new { u.Id, u.Username }).ToListAsync();
            recipientIds = found.Select(u => u.Id).ToList();
            notFoundUsernames = trimmed.Except(found.Select(u => u.Username)).ToList();
            // Não bloqueia o envio pelos que não foram encontrados (decisão do usuário,
            // 14/08/2026) — manda pra quem existe, loga o resto pra consulta/análise posterior.
            if (notFoundUsernames.Count > 0)
                logger.LogWarning(
                    "Admin {AdminId} mandou mensagem de inbox com username(s) não encontrado(s): {Usernames}",
                    requestingUserId, string.Join(", ", notFoundUsernames));
        }

        var message = new InboxMessage
        {
            Title = title.Trim(),
            Body = body.Trim(),
            CreatedByAdminId = requestingUserId,
            Audience = audience,
            RewardCurrencyTypeId = rewardCurrencyId,
            RewardCurrencyAmount = rewardCurrencyId is not null ? rewardCurrencyAmount : null,
            CreatedAt = now,
        };
        db.InboxMessages.Add(message);

        foreach (long recipientId in recipientIds)
        {
            db.InboxEntries.Add(new InboxEntry
            {
                RecipientId = recipientId,
                Kind = InboxEntryKind.AdminMessage,
                InboxMessage = message,
                CreatedAt = now,
            });
        }

        return ServiceResult.Success(new AdminSendInboxMessageResponse(recipientIds.Count, notFoundUsernames));
    }
}
