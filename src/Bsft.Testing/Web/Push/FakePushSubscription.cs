namespace Bsft.Testing.Web.Push;

/// <summary>
/// A browser's push subscription, as <see cref="Push.FakePushService"/> would have handed it to a
/// service worker.
/// </summary>
/// <remarks>
/// The field names are the browser's, not this framework's, so a test can post this object at the
/// registration endpoint exactly as a real client posts what <c>pushManager.subscribe</c> returned.
/// </remarks>
/// <param name="Endpoint">The push endpoint of the fake service.</param>
/// <param name="P256dh">The browser's public key, base64url of the uncompressed P-256 point.</param>
/// <param name="Auth">The browser's authentication secret, base64url of 16 bytes.</param>
public sealed record FakePushSubscription(string Endpoint, string P256dh, string Auth);
