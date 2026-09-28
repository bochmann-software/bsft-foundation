using Bsft.Testing.Web.Push;

namespace Bsft.Testing.Tests.Support;

/// <summary>
/// Posts a push at a <see cref="FakePushService"/> the way a sender does.
/// </summary>
/// <remarks>
/// It drives the handler through an <see cref="HttpMessageInvoker"/> rather than an
/// <see cref="HttpClient"/> so that a body the fake refuses to decrypt surfaces as the exception the
/// fake threw, instead of whatever the client would have wrapped it in.
/// </remarks>
internal static class FakePushDriver
{
    /// <summary>The content encoding every Web Push body carries.</summary>
    internal const string Aes128Gcm = "aes128gcm";

    /// <summary>
    /// Sends one push.
    /// </summary>
    /// <param name="service">The service to push at.</param>
    /// <param name="body">The encrypted body, or <c>null</c> to send no content at all.</param>
    /// <param name="contentEncoding">The <c>Content-Encoding</c> to declare, or <c>null</c> for none.</param>
    /// <param name="timeToLive">The <c>TTL</c> header verbatim, or <c>null</c> to omit it.</param>
    /// <param name="authorization">The <c>Authorization</c> header verbatim, or <c>null</c> to omit it.</param>
    /// <param name="endpoint">The endpoint to post to, or <c>null</c> for the subscription's own.</param>
    /// <returns>The service's answer.</returns>
    internal static async Task<HttpResponseMessage> SendAsync(
        FakePushService service,
        byte[]? body,
        string? contentEncoding = Aes128Gcm,
        string? timeToLive = "60",
        string? authorization = null,
        string? endpoint = null)
    {
        using var invoker = new HttpMessageInvoker(service.CreateHandler());
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(endpoint ?? service.Subscription.Endpoint));

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);

            if (contentEncoding is not null)
                request.Content.Headers.ContentEncoding.Add(contentEncoding);
        }

        if (timeToLive is not null)
            request.Headers.TryAddWithoutValidation("TTL", timeToLive);

        if (authorization is not null)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);

        return await invoker.SendAsync(request, CancellationToken.None);
    }
}
