using Microsoft.AspNetCore.Http;

namespace Bsft.Http;

/// <summary>
/// Reads whether a request a browser sent with its cookies comes from the application's own origin: the
/// cross-site check of every state-changing endpoint a browser application calls on its host.
/// </summary>
/// <remarks>
/// <para>
/// Fetch metadata decides first: when the request carries <c>Sec-Fetch-Site</c>, it is same-origin exactly
/// when that header is <c>same-origin</c>, and any other value (<c>same-site</c>, <c>cross-site</c>,
/// <c>none</c>) is refused. The browser sets the header and a page cannot forge it, and it needs no
/// knowledge of the public origin, so it keeps working behind a TLS-terminating proxy that forwards no
/// scheme. Only a request without fetch metadata (an older engine) falls back to <c>Origin</c>: exactly
/// one value equal to the expected origin. <c>Origin: null</c> (a sandboxed frame, a redirect chain across
/// origins) and a request with neither header are refused.
/// </para>
/// <para>
/// The expected origin is the configured public origin, or the scheme and host the request arrived at.
/// Behind a proxy, either configure the public origin or run the forwarded-headers middleware first.
/// Origins are compared case-insensitively, as scheme and host are, and a trailing <c>/</c> of the
/// configured origin is ignored.
/// </para>
/// <para>
/// This is one layer. A caller that wants a second adds a custom request header (which forces a CORS
/// preflight and which a form post cannot set) and a <c>SameSite</c> cookie on top.
/// </para>
/// </remarks>
public static class SameOriginRequest
{
    /// <summary>The fetch metadata header naming the relation of the initiator's origin to the target's.</summary>
    public const string FetchSiteHeaderName = "Sec-Fetch-Site";

    /// <summary>The <see cref="FetchSiteHeaderName"/> value of a request from the target's own origin.</summary>
    private const string SameOrigin = "same-origin";

    /// <summary>Reports whether a request comes from the application's own origin.</summary>
    /// <param name="request">The request to read.</param>
    /// <param name="publicOrigin">
    /// The origin the browser loads the application from, such as <c>https://app.example</c>, or
    /// <see langword="null"/> or empty to take the request's own scheme and host.
    /// </param>
    /// <returns><see langword="true"/> when the request is same-origin.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    public static bool IsSameOrigin(this HttpRequest request, string? publicOrigin)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fetchSite = request.Headers[FetchSiteHeaderName];

        if (fetchSite.Count > 0)
            return fetchSite.Count == 1 && string.Equals(fetchSite[0], SameOrigin, StringComparison.OrdinalIgnoreCase);

        var origin = request.Headers.Origin;

        if (origin.Count == 0)
            return false;

        var expected = string.IsNullOrEmpty(publicOrigin) ? $"{request.Scheme}://{request.Host.Value}" : publicOrigin.TrimEnd('/');

        return origin.Count == 1 && string.Equals(origin[0], expected, StringComparison.OrdinalIgnoreCase);
    }
}
