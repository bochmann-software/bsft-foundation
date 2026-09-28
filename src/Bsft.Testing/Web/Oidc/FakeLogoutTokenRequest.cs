namespace Bsft.Testing.Web.Oidc;

/// <summary>
/// What one back-channel logout token this authority mints should assert, and what — if anything —
/// should be wrong with it.
/// </summary>
/// <param name="Audience">The relying party the token is addressed to, written as <c>aud</c>.</param>
/// <param name="Subject">The subject to name, written as <c>sub</c>, or <see langword="null"/> to name none.</param>
/// <param name="SessionId">The session to name, written as <c>sid</c>, or <see langword="null"/> to name none.</param>
/// <param name="Defect">The one way this token departs from the specification, or <see cref="FakeLogoutTokenDefect.None"/>.</param>
/// <param name="TokenId">
/// The <c>jti</c> to stamp, or <see langword="null"/> for a fresh one. Naming it is how a replay test
/// sends the same identifier twice.
/// </param>
/// <remarks>
/// A record rather than a parameter list, because the negative cases need five values and a test
/// reading <c>new FakeLogoutTokenRequest(client, SessionId: "s1", Defect: NoncePresent)</c> can see
/// which case it is exercising without counting positions.
/// </remarks>
public sealed record FakeLogoutTokenRequest(
    string Audience,
    string? Subject = null,
    string? SessionId = null,
    FakeLogoutTokenDefect Defect = FakeLogoutTokenDefect.None,
    string? TokenId = null);
