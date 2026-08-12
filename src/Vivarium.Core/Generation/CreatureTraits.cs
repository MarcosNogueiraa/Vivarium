namespace Vivarium.Core.Generation;

// Mix só é não-nulo quando Pattern == Gradient (mais restrito que os outros 4
// subtraits, que valem pra qualquer padrão != None) — proporção de mistura
// entre a cor de base e a cor do padrão (ver GradientMix/TraitGenerator).
public sealed record PartTraits(
    PartColor Color,
    PatternType Pattern,
    PartColor? PatternColor,
    double? PatternSize,
    double? PatternOpacity,
    GradientMix? Mix = null);

/// <summary>Velocidades em 0-100 (normal 50/20); amplitudes em radianos.</summary>
public sealed record MovementTraits(
    double TailSpeed,
    double TailAmplitude,
    double FinSpeed,
    double FinAmplitude);

public sealed record CreatureTraits(
    ShimmerTier ShimmerTier,
    ShimmerColor? ShimmerColor,
    double ShimmerOpacity,
    PartTraits Tail,
    PartTraits Dorsal,
    PartTraits Pectoral,
    MovementTraits Movement,
    double RarityScore)
{
    public PartTraits Part(PartType part) => part switch
    {
        PartType.Tail => Tail,
        PartType.Dorsal => Dorsal,
        PartType.Pectoral => Pectoral,
        _ => throw new ArgumentOutOfRangeException(nameof(part)),
    };
}
