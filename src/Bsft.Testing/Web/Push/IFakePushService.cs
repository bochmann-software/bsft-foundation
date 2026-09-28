using System.Net;

namespace Bsft.Testing.Web.Push;

/// <summary>
/// A push service that behaves as a test tells it to, and a browser that can read what was sent.
/// </summary>
/// <remarks>
/// It plays both halves of the conversation on purpose. Asserting on a push means asserting on
/// something encrypted to a browser's key, so the only thing that can check it is something holding
/// that key — and a test double that owns the subscription is also the thing that can hand a
/// registration endpoint a subscription which is real rather than plausible.
/// </remarks>
public interface IFakePushService
{
    /// <summary>
    /// Gets the subscription to register, exactly as a browser would have produced it.
    /// </summary>
    FakePushSubscription Subscription { get; }

    /// <summary>
    /// Gets or sets what the service answers the next push with. Defaults to 201.
    /// </summary>
    /// <remarks>
    /// Setting it to 410 is how a test reaches the case the whole subscription lifecycle turns on;
    /// 413 and 429 reach the other two the channel treats specially.
    /// </remarks>
    HttpStatusCode Status { get; set; }

    /// <summary>
    /// Gets or sets the <c>Retry-After</c> the service sends with a 429, or <c>null</c> for none.
    /// </summary>
    TimeSpan? RetryAfter { get; set; }

    /// <summary>
    /// Gets every push received so far, decrypted, oldest first.
    /// </summary>
    IReadOnlyList<FakePushRequest> Requests { get; }

    /// <summary>
    /// Creates the message handler an <see cref="HttpClient"/> talks to this service through.
    /// </summary>
    /// <returns>The handler. The caller owns it, as an <see cref="HttpClient"/> disposes the handler it was given.</returns>
    HttpMessageHandler CreateHandler();
}
