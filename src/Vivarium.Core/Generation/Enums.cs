namespace Vivarium.Core.Generation;

public enum ShimmerTier
{
    None = 0,
    Subtle = 1,
    Vibrant = 2,
    Rare = 3,
    Legendary = 4,
}

public enum ShimmerColor
{
    Gold,
    Silver,
    Bluish,
    Emerald,
    Purple,
    Pink,
    Rainbow,
    AbsoluteBlack,
    Iridescent,
}

public enum PartColor
{
    Orange,
    Blue,
    Red,
    Yellow,
    Green,
    Purple,
    Black,
    PureWhite,
}

public enum PatternType
{
    None,
    Stripe,
    Dot,
    Gradient,
    Mottled,
    Scales,     // escamas
    Chevron,    // ziguezague
    Net,        // rede/reticulado
    Rays,       // raios de nadadeira
    Ocellus,    // ocelo / olho-falso (raro)
    Marble,     // mármore / veios (raro)
}

public enum PartType
{
    Tail,
    Dorsal,
    Pectoral,
}

/// <summary>
/// Proporção de mistura entre a cor de base e a cor do padrão, só pra Pattern == Gradient.
/// Nomenclatura semântica (não "25/50/75") pra deixar explícito qual cor domina cada valor.
/// </summary>
public enum GradientMix
{
    BaseDominant,
    Even,
    PatternDominant,
}
