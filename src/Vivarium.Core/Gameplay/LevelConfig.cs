namespace Vivarium.Core.Gameplay;

/// <summary>
/// Progressão do JOGADOR (18/08/2026, BACKLOG.md #7) — só social/cosmético, sem vantagem de
/// gameplay. XP por contagem de ações (não raridade acumulada, não tempo de conta). Separado
/// de <see cref="TickConfig"/> (100% economia) de propósito. Valores são ponto de partida pra
/// calibrar com uso real, mesmo espírito de todo resto do jogo.
/// </summary>
public sealed record LevelConfig
{
    public long FishCollectXp { get; init; } = 5;
    public long BreedingCollectXp { get; init; } = 25;

    /// <summary>
    /// Novas fontes de XP (20/08/2026, pedido do usuário: "quero mais formas" de subir de
    /// nível) — mesma filosofia das duas de cima: por CONTAGEM de ação, não por valor
    /// (preço do item, valor da venda), pra não virar disfarce de "quem gasta mais sobe
    /// mais rápido". Valores iniciais, a calibrar com uso real.
    /// </summary>
    public long DailyRewardClaimXp { get; init; } = 15;
    public long VendorSaleXp { get; init; } = 3;
    public long MarketSaleXp { get; init; } = 10;
    public long ItemPurchaseXp { get; init; } = 5;

    /// <summary>
    /// Bônus ÚNICO (não repetível) na primeira vez que o jogador COLETA um peixe de cada
    /// banda Raro+ (Comum/Incomum de fora — universais demais pra marcar "marco"). Rastreado
    /// em <see cref="Vivarium.Core.Domain.User.RarityBandMilestoneFlags"/> (bitmask, ver
    /// <see cref="RarityBands"/>). Índice: [Raro, Épico, Lendário].
    /// </summary>
    public IReadOnlyDictionary<RarityBand, long> RarityMilestoneXp { get; init; } = new Dictionary<RarityBand, long>
    {
        [RarityBand.Raro] = 50,
        [RarityBand.Epico] = 150,
        [RarityBand.Lendario] = 400,
    };

    /// <summary>XP acumulado pra alcançar o nível 2 (ver <see cref="LevelCalculator.XpForLevel"/>).</summary>
    public long LevelBaseXp { get; init; } = 100;
    /// <summary>Curva de crescimento — >1 deixa cada nível progressivamente mais caro.</summary>
    public double LevelExponent { get; init; } = 1.6;

    public static readonly LevelConfig Default = new();
}
