namespace Vivarium.Core.Domain;

public enum InboxAudience { All, Selected }

public enum InboxEntryKind { AdminMessage, MarketPurchase, DirectTransfer, MarketSale }

/// <summary>
/// Caixa de Entrada (14/08/2026, CLAUDE.md §8.23/§8.24) — 1 linha por envio administrativo
/// (broadcast ou segmentado) OU por notificação de sistema (ex: venda no Mercado, 16/08/2026);
/// a recompensa (se houver) é a MESMA pra todo mundo que recebeu essa mensagem específica. Os
/// destinatários reais já foram materializados em <see cref="InboxEntry"/> no momento do envio
/// (mesmo princípio do "TransactionLog genérico" — gravar de uma vez, não recalcular quem
/// recebeu depois).
/// </summary>
public class InboxMessage
{
    public long Id { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    /// <summary>Null pra mensagem gerada pelo sistema (ex: notificação de venda no Mercado) —
    /// só preenchido quando um admin de verdade disparou o envio.</summary>
    public long? CreatedByAdminId { get; set; }
    public User? CreatedByAdmin { get; set; }
    public InboxAudience Audience { get; set; }
    public int? RewardCurrencyTypeId { get; set; }
    public CurrencyType? RewardCurrencyType { get; set; }
    public decimal? RewardCurrencyAmount { get; set; }
    /// <summary>Dormente nesta leva — schema genérico pra quando existir loja de itens premium
    /// (CLAUDE.md §8.11), mas nenhuma UI admin ainda seta isso.</summary>
    public int? RewardItemDefinitionId { get; set; }
    public ItemDefinition? RewardItemDefinition { get; set; }
    public int? RewardItemQuantity { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 1 linha por (destinatário, evento) — é isso que a Caixa de Entrada de um jogador lista de
/// verdade. Cobre TANTO mensagem administrativa QUANTO entrega de peixe (compra no mercado ou
/// transferência direta) — unificado numa UI só, a pedido do usuário. Fica visível mesmo depois
/// de resgatada (marcada via <see cref="ClaimedAt"/>); só some da lista via limpeza explícita
/// ("Apagar mensagens lidas").
/// </summary>
public class InboxEntry
{
    public long Id { get; set; }
    public long RecipientId { get; set; }
    public User? Recipient { get; set; }
    public InboxEntryKind Kind { get; set; }
    /// <summary>Preenchido só quando <see cref="Kind"/> == AdminMessage.</summary>
    public long? InboxMessageId { get; set; }
    public InboxMessage? InboxMessage { get; set; }
    /// <summary>Vendedor (compra), remetente (transferência) ou comprador (venda no Mercado);
    /// null pra mensagem administrativa.</summary>
    public long? SenderUserId { get; set; }
    public User? SenderUser { get; set; }
    /// <summary>O peixe entregue, quando aplicável (MarketPurchase/DirectTransfer).</summary>
    public long? CreatureInstanceId { get; set; }
    public CreatureInstance? CreatureInstance { get; set; }
    public DateTime? ReadAt { get; set; }
    /// <summary>Null = ainda pendente de resgate. Resgatar também marca <see cref="ReadAt"/> se
    /// ainda estiver vazio.</summary>
    public DateTime? ClaimedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
