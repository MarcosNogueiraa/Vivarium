using Vivarium.Core.Domain;
using Vivarium.Core.Generation;

namespace Vivarium.Api.Services;

/// <summary>
/// Cores das 3 partes (cauda/dorsal/peitoral) usadas pra agrupar sinergia
/// (`IncomeCalculator.SynergyMultiplier`, 14/08/2026 — cada parte conta separado) — precisa bater
/// exatamente com o que a tela exibe, senão `coinsPerHour` (servidor) diverge de `tankPotential()`
/// (cliente), mesmo bug já corrigido 2x antes desta simplificação (08/08 e 12/08/2026, ver
/// histórico em CLAUDE.md §8.19.1). Desde 13/08/2026, isso deixou de ser um problema por
/// construção: os traits já vêm congelados em `TraitsJson` no nascimento — aqui é só uma leitura,
/// sem motor de geração envolvido, então não tem mais como divergir do que é exibido.
/// </summary>
public static class PartColorsResolver
{
    public static (PartColor Tail, PartColor Dorsal, PartColor Pectoral) Of(CreatureInstance c)
    {
        var traits = TraitsSerialization.DeserializeTraits(c.TraitsJson!);
        return (traits.Tail.Color, traits.Dorsal.Color, traits.Pectoral.Color);
    }
}
