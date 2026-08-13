using Vivarium.Core.Domain;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Services;

/// <summary>
/// Cor da cauda usada pra agrupar sinergia (`SynergyMultiplier`) — precisa bater exatamente com o
/// que a tela exibe, senão `coinsPerHour` (servidor) diverge de `tankPotential()` (cliente), mesmo
/// bug já corrigido 2x antes desta simplificação (08/08 e 12/08/2026, ver histórico em CLAUDE.md
/// §8.19.1). Desde 13/08/2026, isso deixou de ser um problema por construção: os traits já vêm
/// congelados em `TraitsJson` no nascimento — aqui é só uma leitura, sem motor de geração
/// envolvido, então não tem mais como divergir do que é exibido.
/// </summary>
public static class TailColorResolver
{
    public static PartColor Of(CreatureInstance c) =>
        TraitsSerialization.DeserializeTraits(c.TraitsJson!).Tail.Color;
}
