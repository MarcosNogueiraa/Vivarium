namespace Vivarium.Api.Contracts;

/// <summary>Uma entrada da Caixa de Entrada — cobre mensagem administrativa (Title/Body/reward
/// vêm de InboxMessage) e entrega de peixe pendente (Creature/SenderUsername preenchidos).</summary>
public record InboxEntryDto(
    long Id, string Kind, string? Title, string? Body,
    string? SenderUsername, CreatureDto? Creature,
    string? RewardCurrencyCode, decimal? RewardCurrencyAmount,
    // Ovo de recompensa (§7.21, 17/08/2026): key do ItemDefinition (ex: "egg_legendary") — só
    // preenchido quando o item de recompensa da mensagem é da categoria Egg. O peixe em si só
    // existe depois do resgate (gerado na hora, como comprar um ovo) — CreatureDto acima continua
    // reservado pra entrega de peixe JÁ existente (Mercado/Transferência).
    string? RewardEggKey,
    DateTime? ReadAt, DateTime? ClaimedAt, DateTime CreatedAt);

public record InboxListResponse(IReadOnlyList<InboxEntryDto> Entries);

public record ClaimAllResponse(int ClaimedCount, int FailedCount);

public record AdminSendInboxMessageResponse(int RecipientCount, IReadOnlyList<string> NotFoundUsernames);

/// <summary>Resposta de <c>POST /api/inbox/{id}/claim</c> — <paramref name="Creature"/> só vem
/// preenchido quando o resgate gerou um peixe NOVO (recompensa de ovo).</summary>
public record ClaimInboxEntryResponse(CreatureDto? Creature);
