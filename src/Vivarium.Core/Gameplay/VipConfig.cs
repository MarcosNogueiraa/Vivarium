namespace Vivarium.Core.Gameplay;

/// <summary>
/// VIP (11/08/2026): pacotes de dias fixos pagos em moeda premium, SEM renovação
/// automática — o jogador compra 7/15/30 dias, a assinatura expira sozinha e ele decide
/// se compra de novo (mesmo espírito simples de "seguro"/"estabilizador" no Ninho, sem
/// complexidade de cobrança recorrente). Preço por dia cai quanto maior o pacote —
/// mesmo princípio de desconto por volume já usado na compra de premium via PIX
/// (CLAUDE.md 8.17). Único benefício hoje: coleta automática enquanto o tanque está
/// online (`GameService.HasActiveVipAsync`, consumido em `ApplyTickAsync`).
/// </summary>
public static class VipConfig
{
    public static readonly IReadOnlyDictionary<int, decimal> PackagePricePremium = new Dictionary<int, decimal>
    {
        [7] = 7m,
        [15] = 10m,
        [30] = 15m,
    };
}

public static class VipCalculator
{
    public static bool TryGetPrice(int days, out decimal price) => VipConfig.PackagePricePremium.TryGetValue(days, out price);
}
