namespace Vivarium.Core.Generation;

/// <summary>Par valor + peso percentual (a tabela inteira soma 100).</summary>
public sealed record WeightedValue<T>(T Value, double Weight);

public static class WeightedTable
{
    /// <summary>
    /// Sorteia um valor da tabela a partir de um roll uniforme em [0,1),
    /// retornando também a probabilidade do valor sorteado (usada no rarity score).
    /// </summary>
    public static (T Value, double Probability) Pick<T>(IReadOnlyList<WeightedValue<T>> table, double roll01)
    {
        double total = 0;
        foreach (var entry in table)
            total += entry.Weight;

        double target = roll01 * total;
        double cumulative = 0;
        foreach (var entry in table)
        {
            cumulative += entry.Weight;
            if (target < cumulative)
                return (entry.Value, entry.Weight / total);
        }

        // roll01 ~1.0 com erro de ponto flutuante: cai no último item
        var last = table[^1];
        return (last.Value, last.Weight / total);
    }

    /// <summary>
    /// Probabilidade de um valor já conhecido (peso/total), sem sortear — usado no
    /// breeding quando o filho herda um valor de um pai em vez de sortear um novo.
    /// </summary>
    public static double ProbabilityOf<T>(IReadOnlyList<WeightedValue<T>> table, T value)
    {
        double total = 0, match = 0;
        foreach (var entry in table)
        {
            total += entry.Weight;
            if (EqualityComparer<T>.Default.Equals(entry.Value, value))
                match = entry.Weight;
        }
        return match / total;
    }
}
