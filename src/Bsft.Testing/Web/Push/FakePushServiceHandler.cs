using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace Bsft.Testing.Web.Push;

/// <summary>
/// The transport half of <see cref="FakePushService"/>: it answers a push the way a push service
/// does, and hands the request to the service to be decrypted and recorded.
/// </summary>
internal sealed class FakePushServiceHandler : HttpMessageHandler
{
    private readonly FakePushService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakePushServiceHandler"/> class.
    /// </summary>
    /// <param name="service">The service whose keys decrypt the bodies and whose answer is returned.</param>
    internal FakePushServiceHandler(FakePushService service) => _service = service;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        // Recorded before the status is consulted, so a test that made the service answer 410 can
        // still assert on what was sent. A real push service reads the request either way.
        _service.Record(
            request.RequestUri!,
            TimeToLive(request),
            request.Content?.Headers.ContentEncoding.FirstOrDefault(),
            request.Headers.TryGetValues("Authorization", out var authorization)
                ? authorization.FirstOrDefault()
                : null,
            body);

        var response = new HttpResponseMessage(_service.Status);

        if (_service.Status == HttpStatusCode.TooManyRequests && _service.RetryAfter is { } retryAfter)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);

        return response;
    }

    /// <summary>
    /// Reads the <c>TTL</c> header a push carries.
    /// </summary>
    /// <param name="request">The push request.</param>
    /// <returns>The value, or <c>null</c> when it is absent or not a number.</returns>
    private static int? TimeToLive(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues("TTL", out var values))
            return null;

        return int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ttl)
            ? ttl
            : null;
    }
}
