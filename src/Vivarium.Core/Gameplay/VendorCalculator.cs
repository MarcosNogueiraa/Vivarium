namespace Vivarium.Core.Gameplay;

/// <summary>
/// Preço de venda ao NPC (vendor, §8.12) — sink pra duplicatas/comuns acumulados.
/// Deliberadamente baixo (poucas horas do que o peixe já renderia sozinho no tanque),
/// pra nunca competir com o mercado entre jogadores: quem tem um peixe que vale a pena
/// deveria listar, não vender pro NPC. Reaproveita a mesma curva exponencial da renda
/// (IncomeCalculator.CoinsPerHour) em vez de inventar uma fórmula nova.
/// </summary>
public static class VendorCalculator
{
    public static decimal Price(decimal rarityScore, TickConfig config)
    {
        double coinsPerHour = IncomeCalculator.CoinsPerHour(rarityScore, config);
        decimal price = (decimal)(coinsPerHour * config.VendorHoursEquivalent);
        return Math.Max(config.VendorMinPrice, Math.Round(price, 0, MidpointRounding.AwayFromZero));
    }
}
