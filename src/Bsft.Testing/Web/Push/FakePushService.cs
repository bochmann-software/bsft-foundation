using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace Bsft.Testing.Web.Push;

/// <summary>
/// A Web Push service and the browser subscribed to it, for tests of anything that pushes.
/// </summary>
/// <remarks>
/// <para>
/// It holds a real P-256 key pair and a real authentication secret, so <see cref="Subscription"/> can
/// be posted at a registration endpoint unchanged and every push to it is decrypted rather than
/// pattern-matched. A push whose key derivation or record framing is wrong does not produce a
/// mismatched string — it fails to authenticate, which is the assertion worth having.
/// </para>
/// <para>
/// <see cref="Status"/> is what makes the interesting paths reachable: 410 for a browser that has
/// gone, 413 for a payload nobody may send, 429 for a sender being throttled. It applies to every
/// push until it is changed, because a test that needs a sequence of answers is a test that should
/// change it between the calls it is asserting on.
/// </para>
/// </remarks>
public sealed class FakePushService : IFakePushService, IDisposable
{
    /// <summary>The origin the fake serves, which is what a VAPID audience has to name.</summary>
    public const string DefaultOrigin = "https://push.fake.test";

    private readonly ECDiffieHellman _browserKey;
    private readonly byte[] _browserPoint;
    private readonly byte[] _authSecret;
    private readonly ConcurrentQueue<FakePushRequest> _requests = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="FakePushService"/> class.
    /// </summary>
    /// <param name="endpoint">
    /// The endpoint to hand out, or <c>null</c> for one under <see cref="DefaultOrigin"/>. Pass one to
    /// register a second browser, or to give the endpoint a shape a test asserts on.
    /// </param>
    public FakePushService(string? endpoint = null)
    {
        _browserKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        var parameters = _browserKey.ExportParameters(includePrivateParameters: false);

        _browserPoint = EncodePoint(parameters.Q);
        _authSecret = RandomNumberGenerator.GetBytes(AuthSecretLength);

        Subscription = new FakePushSubscription(
            endpoint ?? $"{DefaultOrigin}/send/{Guid.NewGuid():N}",
            WebEncoders.Base64UrlEncode(_browserPoint),
            WebEncoders.Base64UrlEncode(_authSecret));
    }

    /// <summary>The length of a subscription's authentication secret, fixed by RFC 8291.</summary>
    private const int AuthSecretLength = 16;

    /// <summary>The length of one P-256 coordinate.</summary>
    private const int CoordinateLength = 32;

    /// <summary>The SEC 1 marker for an uncompressed point, which is the form a browser publishes.</summary>
    private const byte UncompressedPointPrefix = 0x04;

    /// <inheritdoc />
    public FakePushSubscription Subscription { get; }

    /// <inheritdoc />
    public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;

    /// <inheritdoc />
    public TimeSpan? RetryAfter { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<FakePushRequest> Requests => _requests.ToList();

    /// <inheritdoc />
    public HttpMessageHandler CreateHandler() => new FakePushServiceHandler(this);

    /// <inheritdoc />
    public void Dispose() => _browserKey.Dispose();

    /// <summary>
    /// Records one received push, decrypting its body with the browser's key.
    /// </summary>
    /// <param name="endpoint">The endpoint it was posted to.</param>
    /// <param name="timeToLiveSeconds">The <c>TTL</c> header.</param>
    /// <param name="contentEncoding">The <c>Content-Encoding</c> header.</param>
    /// <param name="authorization">The <c>Authorization</c> header.</param>
    /// <param name="body">The encrypted body.</param>
    internal void Record(
        Uri endpoint,
        int? timeToLiveSeconds,
        string? contentEncoding,
        string? authorization,
        byte[] body) =>
        _requests.Enqueue(
            new FakePushRequest(
                endpoint,
                timeToLiveSeconds,
                contentEncoding,
                VapidTokenReader.Read(authorization),
                WebPushDecryptor.Decrypt(body, _browserKey, _authSecret, _browserPoint)));

    /// <summary>
    /// Renders a public point in the uncompressed form a browser publishes.
    /// </summary>
    /// <param name="point">The point to render.</param>
    /// <returns>The 65 bytes.</returns>
    private static byte[] EncodePoint(ECPoint point)
    {
        var encoded = new byte[1 + CoordinateLength + CoordinateLength];
        encoded[0] = UncompressedPointPrefix;

        point.X!.CopyTo(encoded.AsSpan(1));
        point.Y!.CopyTo(encoded.AsSpan(1 + CoordinateLength));

        return encoded;
    }
}
