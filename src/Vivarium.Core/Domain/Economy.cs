namespace Vivarium.Core.Domain;

// Moedas como tabela, não colunas fixas no User: nova moeda (ex: ticket de
// evento) é um insert, não uma migration estrutural.
public class CurrencyType
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
}

public class WalletBalance
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User? User { get; set; }
    public int CurrencyTypeId { get; set; }
    public CurrencyType? CurrencyType { get; set; }
    public decimal Amount { get; set; }
}

public class ItemDefinition
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public ItemCategory Category { get; set; }
    /// <summary>Efeito interpretado pelo motor, não hardcoded no código.</summary>
    public required string EffectJson { get; set; }
    public decimal PriceSoft { get; set; }
    public decimal? PricePremium { get; set; }
}

public class UserInventory
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User? User { get; set; }
    public int ItemDefinitionId { get; set; }
    public ItemDefinition? ItemDefinition { get; set; }
    public int Quantity { get; set; }
}

// Log único e genérico pra tudo que envolve valor ou posse mudando de mãos —
// ferramenta central de auditoria/anti-cheat.
public class TransactionLog
{
    public long Id { get; set; }
    public TransactionType Type { get; set; }
    public long? FromUserId { get; set; }
    public long? ToUserId { get; set; }
    public long? CreatureInstanceId { get; set; }
    public int? CurrencyTypeId { get; set; }
    public decimal? Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}
