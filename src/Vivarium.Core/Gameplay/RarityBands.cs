namespace Vivarium.Core.Gameplay;

public enum RarityBand { Comum, Incomum, Raro, Epico, Lendario }

/// <summary>
/// Classificação de banda de raridade (CLAUDE.md §5) — até 20/08/2026 só existia como método
/// PRIVADO em MarketService (BandNameOf), duplicável por engano em qualquer novo consumidor
/// (ex: o marco de raridade do sistema de Níveis). Fonte única agora; cortes espelham
/// fishRenderer.js BANDS e MarketService.BandNameOf.
/// </summary>
public static class RarityBands
{
    public static RarityBand BandOf(decimal score) => score switch
    {
        < 5.45m => RarityBand.Comum,
        < 12.04m => RarityBand.Incomum,
        < 13.78m => RarityBand.Raro,
        < 16.60m => RarityBand.Epico,
        _ => RarityBand.Lendario,
    };

    public static string NameOf(decimal score) => BandOf(score) switch
    {
        RarityBand.Comum => "Comum",
        RarityBand.Incomum => "Incomum",
        RarityBand.Raro => "Raro",
        RarityBand.Epico => "Épico",
        RarityBand.Lendario => "Lendário",
        _ => "Comum",
    };
}
