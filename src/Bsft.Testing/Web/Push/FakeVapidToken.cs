namespace Bsft.Testing.Web.Push;

/// <summary>
/// The VAPID token a push request carried, read the way a push service reads it.
/// </summary>
/// <remarks>
/// <see cref="IsSignatureValid"/> is checked against the public key the request itself declared, which
/// is exactly what a push service can do and no more: it proves the token was signed by the holder of
/// the key the browser subscribed with, and it is the one assertion worth making about a VAPID header
/// that a hand-written expectation cannot fake.
/// </remarks>
/// <param name="Audience">The <c>aud</c> claim, which has to be the push endpoint's origin.</param>
/// <param name="Subject">The <c>sub</c> claim: how the push service reaches the sender's operator.</param>
/// <param name="ExpiresAt">The <c>exp</c> claim.</param>
/// <param name="PublicKey">The <c>k</c> parameter of the header: the application server's key.</param>
/// <param name="IsSignatureValid">Whether the signature verifies against <paramref name="PublicKey"/>.</param>
public sealed record FakeVapidToken(
    string Audience,
    string Subject,
    DateTimeOffset ExpiresAt,
    string PublicKey,
    bool IsSignatureValid);
