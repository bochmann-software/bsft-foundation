namespace Bsft.Core;

/// <summary>The bidirectional classes the RFC 5893 rules tell apart; every other class counts as <see cref="Other"/>.</summary>
internal enum BidiClass
{
    /// <summary>A class the rules do not name individually.</summary>
    Other,

    /// <summary>Left-to-right.</summary>
    L,

    /// <summary>Right-to-left.</summary>
    R,

    /// <summary>Arabic letter.</summary>
    AL,

    /// <summary>European number.</summary>
    EN,

    /// <summary>European separator.</summary>
    ES,

    /// <summary>European terminator.</summary>
    ET,

    /// <summary>Arabic number.</summary>
    AN,

    /// <summary>Common separator.</summary>
    CS,

    /// <summary>Nonspacing mark.</summary>
    NSM,

    /// <summary>Boundary neutral.</summary>
    BN,

    /// <summary>Other neutral.</summary>
    ON,
}
