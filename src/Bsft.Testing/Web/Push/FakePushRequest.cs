namespace Bsft.Testing.Web.Push;

/// <summary>
/// One push request as the fake push service received it, decrypted.
/// </summary>
/// <param name="Endpoint">The endpoint it was posted to.</param>
/// <param name="TimeToLiveSeconds">The <c>TTL</c> header, or <c>null</c> when the sender omitted it.</param>
/// <param name="ContentEncoding">The <c>Content-Encoding</c> header, which has to be <c>aes128gcm</c>.</param>
/// <param name="Vapid">The VAPID token, or <c>null</c> when the request carried no usable one.</param>
/// <param name="Payload">
/// The decrypted payload as text. Decryption succeeding <em>is</em> the assertion that the sender
/// encrypted correctly: a body that was built wrongly does not decrypt, it fails authentication.
/// </param>
public sealed record FakePushRequest(
    Uri Endpoint,
    int? TimeToLiveSeconds,
    string? ContentEncoding,
    FakeVapidToken? Vapid,
    string Payload);
