namespace Vivarium.Core.Domain;

public class User
{
    public long Id { get; set; }
    public required string Username { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Último resgate da recompensa diária (UTC), nullable = nunca resgatou.</summary>
    public DateTime? LastDailyRewardAt { get; set; }
}

// Separado do User (não um bool IsVip) para permitir histórico e, no futuro,
// diferentes tiers de assinatura sem alterar User.
public class VipSubscription
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User? User { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public SubscriptionStatus Status { get; set; }
}
