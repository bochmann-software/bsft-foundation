using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting;

/// <summary>
/// Works out which partition a request counts against.
/// </summary>
/// <remarks>
/// <para>
/// The default is the remote address, so that one caller exhausting its permits costs that caller and
/// nobody else. A single limiter shared by a whole policy would be the opposite of a rate limit: the first
/// client to spend the budget would lock every other client out of the endpoint for the rest of the
/// window, which is a denial of service anyone can trigger. <see cref="RateLimitPartitionKind.Global"/> is
/// that shared limiter on purpose, and belongs in a chain behind a per-caller one.
/// </para>
/// <para>
/// Behind a reverse proxy the remote address is the proxy's, and every caller lands in one partition
/// again. A host deployed that way configures the forwarded-headers middleware so that the address
/// reaching here is the client's; this package does not enable it, because trusting a forwarded header
/// without knowing which hop set it is how a caller gets to choose its own partition.
/// </para>
/// <para>
/// The keys of a named policy carry a prefix per kind, so that a principal whose identifier happens to
/// read like an address, or a custom key, never shares a partition with that address.
/// </para>
/// </remarks>
internal static class RateLimitPartitionKeys
{
    /// <summary>
    /// The partition of a request that carries no remote address, as one over a pipe or an in-memory test
    /// server does. They share it rather than each getting a budget of their own.
    /// </summary>
    internal const string UnattributableClientKey = "unattributable";

    /// <summary>The one partition of <see cref="RateLimitPartitionKind.Global"/>.</summary>
    internal const string GlobalKey = "global";

    /// <summary>The key of the default policy: the remote address, without a prefix.</summary>
    /// <param name="context">The request being limited.</param>
    /// <returns>The partition key.</returns>
    internal static string ClientAddress(HttpContext context)
        => context.Connection.RemoteIpAddress?.ToString() ?? UnattributableClientKey;

    /// <summary>The key one limiter of a named policy counts a request against.</summary>
    /// <param name="limiter">The limiter's options.</param>
    /// <param name="selector">The custom selector, when the limiter partitions by one.</param>
    /// <param name="context">The request being limited.</param>
    /// <returns>The partition key.</returns>
    internal static string For(PolicyLimiterOptions limiter, Func<HttpContext, string?>? selector, HttpContext context)
        => limiter.PartitionBy switch
        {
            RateLimitPartitionKind.Global => GlobalKey,
            RateLimitPartitionKind.Principal when PrincipalKey(context, limiter.ClaimType) is { } principal
                => "principal:" + principal,
            RateLimitPartitionKind.Custom when selector?.Invoke(context) is { Length: > 0 } custom => "custom:" + custom,
            _ => "address:" + ClientAddress(context)
        };

    /// <summary>The value of the claim that identifies an authenticated principal.</summary>
    /// <param name="context">The request being limited.</param>
    /// <param name="claimType">The claim that identifies the principal.</param>
    /// <returns>The value, or <see langword="null"/> for an anonymous request or a principal without the claim.</returns>
    private static string? PrincipalKey(HttpContext context, string claimType)
        => context.User.Identity?.IsAuthenticated == true
            && context.User.FindFirst(claimType)?.Value is { Length: > 0 } value
                ? value
                : null;
}
