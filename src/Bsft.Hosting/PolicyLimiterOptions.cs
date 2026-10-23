using System.Security.Claims;

namespace Bsft.Hosting;

/// <summary>
/// One limiter in the chain of a <see cref="RateLimitPolicyOptions"/>: which algorithm, which partition and
/// how many permits.
/// </summary>
public sealed class PolicyLimiterOptions
{
    /// <summary>Gets or sets the algorithm. Defaults to <see cref="RateLimiterKind.FixedWindow"/>.</summary>
    public RateLimiterKind Kind { get; set; } = RateLimiterKind.FixedWindow;

    /// <summary>
    /// Gets or sets what the limiter keeps one budget per. Defaults to
    /// <see cref="RateLimitPartitionKind.ClientAddress"/>.
    /// </summary>
    public RateLimitPartitionKind PartitionBy { get; set; } = RateLimitPartitionKind.ClientAddress;

    /// <summary>
    /// Gets or sets the claim whose value identifies the principal for
    /// <see cref="RateLimitPartitionKind.Principal"/>. Defaults to <see cref="ClaimTypes.NameIdentifier"/>;
    /// must not be blank when the limiter partitions by principal.
    /// </summary>
    public string ClaimType { get; set; } = ClaimTypes.NameIdentifier;

    /// <summary>
    /// Gets or sets the name of the selector in <see cref="BsftRateLimitingOptions.PartitionKeySelectors"/>
    /// for <see cref="RateLimitPartitionKind.Custom"/>. Required, and must be registered, when the limiter
    /// partitions by a custom key.
    /// </summary>
    public string? PartitionKeySelector { get; set; }

    /// <summary>
    /// Gets or sets the permits per partition: requests per window for a fixed or sliding window, the bucket
    /// size for a token bucket, and requests in flight at once for a concurrency limiter. Defaults to 100;
    /// must be at least one.
    /// </summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>
    /// Gets or sets the window of a fixed or sliding window, and the replenishment period of a token bucket,
    /// in seconds. Defaults to 60; must be at least one. A concurrency limiter ignores it.
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the segments a sliding window is divided into. Defaults to 4; must be at least one. Only
    /// a sliding window reads it.
    /// </summary>
    public int SegmentsPerWindow { get; set; } = 4;

    /// <summary>
    /// Gets or sets the tokens a token bucket gains per replenishment period. Defaults to 1; must be at least
    /// one. Only a token bucket reads it.
    /// </summary>
    public int TokensPerPeriod { get; set; } = 1;

    /// <summary>
    /// Gets or sets how many requests may wait for a permit, oldest first, instead of being rejected at once.
    /// Defaults to 0, no queue; must not be negative. A queued request holds its connection while it waits.
    /// </summary>
    public int QueueLimit { get; set; }

    /// <summary>
    /// Gets or sets the <c>Retry-After</c> in seconds of a rejection by this limiter when the limiter cannot
    /// estimate it itself, as a concurrency limiter cannot. <see langword="null"/>, the default, uses
    /// <see cref="BsftRateLimitingOptions.RetryAfterSeconds"/>; a value must be at least one.
    /// </summary>
    public int? RetryAfterSeconds { get; set; }
}
