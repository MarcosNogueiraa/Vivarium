namespace Vivarium.Api.Contracts;

/// <summary>Uma entrada da Caixa de Entrada — cobre mensagem administrativa (Title/Body/reward
/// vêm de InboxMessage) e entrega de peixe pendente (Creature/SenderUsername preenchidos).</summary>
public record InboxEntryDto(
    long Id, string Kind, string? Title, string? Body,
    string? SenderUsername, CreatureDto? Creature,
    string? RewardCurrencyCode, decimal? RewardCurrencyAmount,
    DateTime? ReadAt, DateTime? ClaimedAt, DateTime CreatedAt);

public record InboxListResponse(IReadOnlyList<InboxEntryDto> Entries);

public record ClaimAllResponse(int ClaimedCount, int FailedCount);

public record AdminSendInboxMessageResponse(int RecipientCount, IReadOnlyList<string> NotFoundUsernames);
