namespace Bsft.Testing.Web.Oidc;

/// <summary>
/// The one thing wrong with a deliberately malformed logout token.
/// </summary>
/// <remarks>
/// One defect per value, and never two at once, because a receiver test has to prove <em>which</em>
/// check refused the token. A token that was both unsigned and stale would pass whichever assertion
/// was written and say nothing about the other rule.
/// </remarks>
public enum FakeLogoutTokenDefect
{
    /// <summary>Nothing is wrong with it. The token a conforming authority would send.</summary>
    None = 0,

    /// <summary>Signed with a key this authority never published, so the signature cannot validate.</summary>
    ForeignSigningKey = 1,

    /// <summary>Addressed to a relying party other than the one receiving it.</summary>
    WrongAudience = 2,

    /// <summary>Minted long enough ago to fall outside any sane replay window.</summary>
    StaleIssuedAt = 3,

    /// <summary>Carries a <c>nonce</c>, which Back-Channel Logout 1.0 §2.4 forbids outright.</summary>
    NoncePresent = 4,

    /// <summary>Carries no <c>events</c> claim, so nothing marks it as a logout token.</summary>
    EventsMissing = 5,

    /// <summary>Carries an <c>events</c> claim naming some other event than back-channel logout.</summary>
    EventsWrongKey = 6,

    /// <summary>Names an issuer this relying party is not configured against.</summary>
    UnknownIssuer = 7,

    /// <summary>Names neither a subject nor a session, so there is nothing to revoke.</summary>
    NoSubjectAndNoSessionId = 8,

    /// <summary>Carries no <c>jti</c>, so a receiver has no key to catch a replay of it by.</summary>
    JwtIdMissing = 9,

    /// <summary>Carries no <c>iat</c>, so a receiver cannot judge whether it is fresh.</summary>
    IssuedAtMissing = 10,
}
