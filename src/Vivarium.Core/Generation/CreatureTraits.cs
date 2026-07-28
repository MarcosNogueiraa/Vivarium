namespace Vivarium.Core.Generation;

public sealed record PartTraits(
    PartColor Color,
    PatternType Pattern,
    PartColor? PatternColor,
    double? PatternSize,
    double? PatternOpacity);

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
