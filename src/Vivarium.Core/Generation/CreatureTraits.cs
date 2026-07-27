namespace Vivarium.Core.Generation;

public sealed record PartTraits(
    PartColor Color,
    PatternType Pattern,
    PartColor? PatternColor,
    double? PatternSize,
    double? PatternOpacity);

public sealed record CreatureTraits(
    ShimmerTier ShimmerTier,
    ShimmerColor? ShimmerColor,
    double ShimmerOpacity,
    PartTraits Tail,
    PartTraits Dorsal,
    PartTraits Pectoral,
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
