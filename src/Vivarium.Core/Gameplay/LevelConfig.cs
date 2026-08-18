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

    /// <summary>XP acumulado pra alcançar o nível 2 (ver <see cref="LevelCalculator.XpForLevel"/>).</summary>
    public long LevelBaseXp { get; init; } = 100;
    /// <summary>Curva de crescimento — >1 deixa cada nível progressivamente mais caro.</summary>
    public double LevelExponent { get; init; } = 1.6;

    public static readonly LevelConfig Default = new();
}
