using Vivarium.Core.Generation;

namespace Vivarium.Core.Gameplay;

public sealed record CollectedCreature(long Seed, int TraitConfigVersion, decimal RarityScore);

/// <summary>
/// Coleta de um item da fila: o seed é sorteado AQUI (momento da coleta, não da
/// entrada na fila — CLAUDE.md 8.1). Item doente coleta com "desvantagem":
/// dois seeds são sorteados e fica o de menor rarity score — reduz o potencial
/// sem matar o peixe e sem quebrar a derivação determinística seed→traits.
/// </summary>
public static class CreatureCollector
{
    public static CollectedCreature Collect(bool isSick, Func<long> seedSource, int configVersion = TraitConfigV1.Version)
    {
        long seed = seedSource();
        var traits = TraitGenerator.Generate(seed, configVersion);

        if (isSick)
        {
            long altSeed = seedSource();
            var altTraits = TraitGenerator.Generate(altSeed, configVersion);
            if (altTraits.RarityScore < traits.RarityScore)
            {
                seed = altSeed;
                traits = altTraits;
            }
        }

        return new CollectedCreature(seed, configVersion, (decimal)traits.RarityScore);
    }

    /// <summary>Seed aleatório criptográfico, positivo (63 bits), pra uso em produção.</summary>
    public static long NewRandomSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return (long)(BitConverter.ToUInt64(bytes) >> 1);
    }
}
