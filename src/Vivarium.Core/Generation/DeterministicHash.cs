using System.Security.Cryptography;
using System.Text;

namespace Vivarium.Core.Generation;

/// <summary>
/// Hash determinístico seed+salt → valor. Um hash independente por trait (não um
/// random state sequencial), para que adicionar/reordenar traits no futuro não
/// altere o resultado dos traits já existentes.
/// </summary>
public static class DeterministicHash
{
    public static ulong Hash(long seed, string salt)
    {
        Span<byte> input = stackalloc byte[8 + Encoding.UTF8.GetByteCount(salt)];
        BitConverter.TryWriteBytes(input, seed);
        Encoding.UTF8.GetBytes(salt, input[8..]);

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(input, digest);
        return BitConverter.ToUInt64(digest);
    }

    /// <summary>Uniforme em [0, 1) com 53 bits de precisão.</summary>
    public static double Roll01(long seed, string salt)
        => (Hash(seed, salt) >> 11) * (1.0 / (1UL << 53));
}
