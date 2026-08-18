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
    /// <summary>Dias consecutivos resgatando (17/08/2026, redesenho §8.10) — reseta pra 1 (não
    /// pra 0) a cada resgate que não seguiu direto o dia anterior. Só afeta o BÔNUS de streak,
    /// nunca o valor base da recompensa.</summary>
    public int DailyRewardStreak { get; set; }
    /// <summary>Acesso a ferramentas administrativas (ex: painel /api/admin/*). Não é papel de jogo.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>Tentativas de senha errada seguidas (18/08/2026, BACKLOG.md #5) — zera no
    /// login bem-sucedido; ao atingir SecurityConfig.LoginMaxFailedAttempts, vira
    /// LockedUntil e zera de novo (não fica contando pra sempre depois de travar).</summary>
    public int FailedLoginCount { get; set; }
    /// <summary>Conta travada até este instante (UTC), nullable = nunca travou. Login
    /// durante o travamento responde a MESMA mensagem genérica de senha errada — revelar
    /// "conta travada" separado de "senha errada" vazaria que a conta existe.</summary>
    public DateTime? LockedUntil { get; set; }
}

/// <summary>
/// Token de "esqueci minha senha" (14/08/2026) — o token bruto só existe no link do email
/// (nunca gravado); só o hash (SHA256, não PBKDF2 — já é alta entropia, não senha escolhida
/// por humano, então não precisa de hash lento) fica no banco, pra permitir lookup O(1) sem
/// expor o valor em caso de vazamento do banco. Usado uma vez (UsedAt) e expira sozinho.
/// </summary>
public class PasswordResetToken
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User? User { get; set; }
    public required string TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; }
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
