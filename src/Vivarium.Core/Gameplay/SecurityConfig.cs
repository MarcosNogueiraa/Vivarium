namespace Vivarium.Core.Gameplay;

/// <summary>
/// Rate limiting orientado a segurança (18/08/2026, BACKLOG.md #5) — complementa o rate
/// limit genérico por IP (Program.cs, policy "auth") com dois controles POR CONTA que ele
/// sozinho não cobre: força bruta de senha (um IP paciente ainda tem 10 tentativas/min) e
/// esgotamento da cota diária do Resend (plano grátis) via "esqueci minha senha".
/// </summary>
public static class SecurityConfig
{
    /// <summary>Tentativas de senha errada seguidas até travar a CONTA (não o IP).</summary>
    public const int LoginMaxFailedAttempts = 5;
    /// <summary>Duração do travamento — expira sozinho, sem precisar de suporte.</summary>
    public const int LoginLockoutMinutes = 15;

    /// <summary>Intervalo mínimo entre pedidos de redefinição pro MESMO email — evita
    /// esgotar a cota diária global só sendo paciente (o rate limit de IP genérico não
    /// impede isso: 10/min já é folgado o bastante pra passar batido aqui).</summary>
    public const int ForgotPasswordMinIntervalMinutes = 5;
    /// <summary>Teto de emails de redefinição enviados pelo sistema INTEIRO por dia
    /// calendário UTC — sem visibilidade direta da cota real do Resend (free tier), esse é
    /// um piso conservador pra nunca estourar e derrubar o envio de outros jogadores no
    /// mesmo dia. Ajustar pra cima se a cota real do plano comportar.</summary>
    public const int ForgotPasswordDailyGlobalCap = 80;
}
