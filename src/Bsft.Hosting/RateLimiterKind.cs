namespace Bsft.Hosting;

/// <summary>
/// The algorithm of one limiter in a named rate limiting policy, each the one of
/// <c>System.Threading.RateLimiting</c> it is named after.
/// </summary>
public enum RateLimiterKind
{
    /// <summary>A fixed number of requests per window; the window restarts when it ends.</summary>
    FixedWindow = 0,

    /// <summary>A fixed number of requests per window that slides forward segment by segment.</summary>
    SlidingWindow = 1,

    /// <summary>A bucket of permits that refills by a fixed number of tokens per period.</summary>
    TokenBucket = 2,

    /// <summary>
    /// A number of requests in flight at once. The permit is held until the response is complete, so a
    /// caller with many slow requests uses up its own budget instead of everybody's connections.
    /// </summary>
    Concurrency = 3
}
