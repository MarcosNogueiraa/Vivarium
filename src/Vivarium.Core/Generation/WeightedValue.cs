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

    /// <summary>
    /// Probabilidade de herdar o valor do pai A dado um viés de raridade: bias=0 é
    /// 50/50 puro (ignora raridade); bias=1 pesa pelo inverso da probabilidade,
    /// favorecendo fortemente o valor mais raro entre os dois pais. Quando os pais
    /// têm o mesmo valor (probA == probB), sempre devolve 0.5 — o viés só entra em
    /// jogo quando os pais diferem nesse trait.
    /// </summary>
    public static double BiasedInheritProbability(double probA, double probB, double rarityBias)
    {
        if (rarityBias <= 0) return 0.5;
        double wA = Math.Pow(probA, -rarityBias);
        double wB = Math.Pow(probB, -rarityBias);
        return wA / (wA + wB);
    }
}
