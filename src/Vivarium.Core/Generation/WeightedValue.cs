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
    /// Acima desta discrepância de log10(razão de probabilidade) entre os 2 pais, o viés de
    /// raridade continua na força cheia (comportamento de sempre) — calibrado pra cobrir o par
    /// mais extremo que já existia ANTES do Legendary encolher pra 0,02% (14/08/2026): Legendary
    /// (0,2%) x None (78%), razão ~390x, log10≈2,59.
    /// </summary>
    private const double RarityBiasFullLogRatio = 2.6;

    /// <summary>
    /// Acima desta discrepância, o viés vira 0 (herança pura 50/50, raridade ignorada) —
    /// discrepâncias EXTREMAS não devem tornar a "lavagem" de um trait raríssimo mais fácil.
    /// </summary>
    private const double RarityBiasZeroLogRatio = 4.5;

    /// <summary>
    /// Probabilidade de herdar o valor do pai A dado um viés de raridade: bias=0 é
    /// 50/50 puro (ignora raridade); bias=1 pesa pelo inverso da probabilidade,
    /// favorecendo fortemente o valor mais raro entre os dois pais. Quando os pais
    /// têm o mesmo valor (probA == probB), sempre devolve 0.5 — o viés só entra em
    /// jogo quando os pais diferem nesse trait.
    ///
    /// **Amortecimento por discrepância extrema (14/08/2026):** a força do viés escala com a
    /// RAZÃO de raridade entre os dois pais (quanto mais raro um é em relação ao outro, mais
    /// fortemente ele é favorecido) — então encolher o peso de um valor (ex: ShimmerTier.Legendary
    /// 0,2%→0,02%) sem ajustar nada mais na verdade FACILITA "lavar" esse valor raro cruzando com
    /// um parceiro comum qualquer (achado real: passou de <70% pra 74,2% de retenção nesse
    /// cenário). Acima de <see cref="RarityBiasFullLogRatio"/> de discrepância, o viés começa a
    /// amortecer linearmente até virar 0 em <see cref="RarityBiasZeroLogRatio"/> — pares dentro
    /// da faixa já testada/calibrada (qualquer PartColor, PatternType, o Legendary antigo) não
    /// mudam de comportamento; só discrepâncias mais extremas que isso perdem o viés.
    /// </summary>
    public static double BiasedInheritProbability(double probA, double probB, double rarityBias)
    {
        if (rarityBias <= 0 || probA == probB) return 0.5;
        double logRatio = Math.Log10(Math.Max(probA, probB) / Math.Min(probA, probB));
        double effectiveBias = rarityBias;
        if (logRatio > RarityBiasFullLogRatio)
        {
            double t = Math.Min(1.0, (logRatio - RarityBiasFullLogRatio) / (RarityBiasZeroLogRatio - RarityBiasFullLogRatio));
            effectiveBias = rarityBias * (1 - t);
        }
        if (effectiveBias <= 0) return 0.5;
        double wA = Math.Pow(probA, -effectiveBias);
        double wB = Math.Pow(probB, -effectiveBias);
        return wA / (wA + wB);
    }

    /// <summary>Peso bruto (não normalizado) de um valor já conhecido na tabela; 0 se ausente.</summary>
    public static double WeightOf<T>(IReadOnlyList<WeightedValue<T>> table, T value)
    {
        foreach (var entry in table)
            if (EqualityComparer<T>.Default.Equals(entry.Value, value))
                return entry.Weight;
        return 0;
    }

    /// <summary>
    /// Restringe a tabela só aos valores tão raros quanto ou mais raros que
    /// <paramref name="floorWeight"/> (peso bruto menor ou igual) — usado no piso de mutação do
    /// breeding (CLAUDE.md 8.8, 13/08/2026): mutação nunca pode sair mais comum que o pai mais
    /// fraco. Se a restrição esvaziar a tabela (não deveria — o próprio peso de referência sempre
    /// vem de um valor real da tabela), cai pra tabela inteira como rede de segurança.
    /// </summary>
    public static IReadOnlyList<WeightedValue<T>> Restrict<T>(IReadOnlyList<WeightedValue<T>> table, double floorWeight)
    {
        var restricted = new List<WeightedValue<T>>(table.Count);
        foreach (var entry in table)
            if (entry.Weight <= floorWeight)
                restricted.Add(entry);
        return restricted.Count > 0 ? restricted : table;
    }

    /// <summary>
    /// Sorteia da tabela com um leve viés a favor de valores raros: cada peso é reescalado por
    /// <c>weight^(1-biasStrength)</c> antes de sortear — <paramref name="biasStrength"/>=0 é o
    /// sorteio uniforme-por-peso de sempre (<see cref="Pick{T}"/>); quanto mais perto de 1, mais a
    /// tabela se aproxima de uma escolha uniforme POR VALOR (ignora o peso original, favorece bem
    /// mais os raros). Mesma família de fórmula de <see cref="BiasedInheritProbability"/>,
    /// generalizada de 2 candidatos pra tabela inteira. A probabilidade devolvida é a REAL
    /// (peso/total desta tabela) do valor sorteado — não a reescalada — pra manter o rarity score
    /// coerente com a probabilidade de população real.
    /// </summary>
    public static (T Value, double Probability) PickBiasedTowardRare<T>(
        IReadOnlyList<WeightedValue<T>> table, double roll01, double biasStrength)
    {
        if (biasStrength <= 0) return Pick(table, roll01);

        double total = 0;
        var rescaled = new double[table.Count];
        double rescaledTotal = 0;
        for (int i = 0; i < table.Count; i++)
        {
            total += table[i].Weight;
            rescaled[i] = Math.Pow(table[i].Weight, 1 - biasStrength);
            rescaledTotal += rescaled[i];
        }

        double target = roll01 * rescaledTotal;
        double cumulative = 0;
        for (int i = 0; i < table.Count; i++)
        {
            cumulative += rescaled[i];
            if (target < cumulative)
                return (table[i].Value, table[i].Weight / total);
        }

        var last = table[^1];
        return (last.Value, last.Weight / total);
    }
}
