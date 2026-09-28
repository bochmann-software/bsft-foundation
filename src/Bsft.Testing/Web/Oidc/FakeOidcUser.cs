namespace Bsft.Testing.Web.Oidc;

/// <summary>
/// The person <see cref="Bsft.Testing.Web.Oidc.FakeOidcAuthority"/> issues an identity token for.
/// </summary>
/// <param name="Subject">The subject identifier the authority asserts.</param>
/// <param name="Email">The address to publish, or <see langword="null"/> to publish none.</param>
/// <param name="IsEmailVerified">Whether the authority claims to have verified the address.</param>
/// <param name="Name">The display name to publish.</param>
/// <param name="AuthenticationMethods">The <c>amr</c> values, written as a JSON array as a real authority would.</param>
/// <param name="AuthenticationContextClass">The <c>acr</c> value.</param>
/// <param name="TenantId">The tenant claim to publish.</param>
/// <param name="OrganizationKeys">The group claims to publish.</param>
/// <param name="AuthenticatedAt">
/// When the authority says the authentication happened, published as <c>auth_time</c>, or
/// <see langword="null"/> to publish none.
/// </param>
/// <param name="SessionId">
/// The authority session this login belongs to, published as <c>sid</c>, or <see langword="null"/> to
/// publish none.
/// </param>
/// <remarks>
/// <para>
/// <see cref="AuthenticatedAt"/> is what lets a test drive a maximum-age policy: a user stamped an hour
/// ago fails a five-minute rule and the same user stamped now passes it, without waiting or moving a
/// clock. It defaults to publishing nothing, which is also worth exercising — an authority that omits
/// the claim cannot satisfy any freshness rule, and a policy layer that quietly accepted it would be
/// enforcing nothing.
/// </para>
/// <para>
/// <see cref="SessionId"/> is the other half of the back-channel logout story. A logout token names a
/// session, and a relying party can only act on that precisely if the identity token told it which
/// session the login came from. Publishing none is the honest default and exercises the subject-wide
/// fallback: an authority that omits <c>sid</c> forces a logout to end every session it holds for that
/// person.
/// </para>
/// </remarks>
public sealed record FakeOidcUser(
    string Subject,
    string? Email = null,
    bool IsEmailVerified = false,
    string? Name = null,
    IReadOnlyList<string>? AuthenticationMethods = null,
    string? AuthenticationContextClass = null,
    string? TenantId = null,
    IReadOnlyList<string>? OrganizationKeys = null,
    DateTimeOffset? AuthenticatedAt = null,
    string? SessionId = null);
