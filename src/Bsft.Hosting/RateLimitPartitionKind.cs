namespace Bsft.Hosting;

/// <summary>
/// What one limiter of a named rate limiting policy keeps a budget per.
/// </summary>
public enum RateLimitPartitionKind
{
    /// <summary>
    /// The remote address of the connection, as the default policy does. Requests without one share a single
    /// partition. Behind a reverse proxy this is the proxy's address unless the host configures the
    /// forwarded-headers middleware.
    /// </summary>
    ClientAddress = 0,

    /// <summary>
    /// The value of <see cref="PolicyLimiterOptions.ClaimType"/> of the authenticated principal. An anonymous
    /// request, or one whose principal lacks the claim, falls back to its client address. Authentication has
    /// to run before the rate limiting middleware for the principal to be known.
    /// </summary>
    Principal = 1,

    /// <summary>
    /// The key a selector of <see cref="BsftRateLimitingOptions.PartitionKeySelectors"/> returns, named by
    /// <see cref="PolicyLimiterOptions.PartitionKeySelector"/>. A null or empty key falls back to the client
    /// address.
    /// </summary>
    Custom = 2,

    /// <summary>One partition for every request: a limit on the endpoint group as a whole.</summary>
    Global = 3
}
