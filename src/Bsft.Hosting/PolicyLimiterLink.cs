using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting;

/// <summary>
/// One limiter of a named policy's chain: a partitioned limiter keyed the way its options say, and the
/// <c>Retry-After</c> it falls back to.
/// </summary>
/// <remarks>
/// The partitioned limiter runs a replenishment timer until it is disposed, so the link is disposable and
/// <see cref="ConfiguredRateLimitPolicies"/>, which the container owns, disposes it with the host.
/// </remarks>
internal sealed class PolicyLimiterLink : IDisposable
{
    private readonly PolicyLimiterOptions _options;
    private readonly Func<HttpContext, string?>? _selector;

    /// <summary>Initializes a new instance of the <see cref="PolicyLimiterLink"/> class.</summary>
    /// <param name="options">The limiter's options, already validated.</param>
    /// <param name="rateLimiting">The options the limiter belongs to, for its selector and fallback.</param>
    internal PolicyLimiterLink(PolicyLimiterOptions options, BsftRateLimitingOptions rateLimiting)
    {
        _options = options;
        _selector = options.PartitionBy == RateLimitPartitionKind.Custom
            ? rateLimiting.PartitionKeySelectors[options.PartitionKeySelector!]
            : null;
        RetryAfterFallback = TimeSpan.FromSeconds(options.RetryAfterSeconds ?? rateLimiting.RetryAfterSeconds);
        Limiter = PartitionedRateLimiter.Create<string, string>(key => Partition(key, options));
    }

    /// <summary>Gets the algorithm, which decides the order the chain asks its links in.</summary>
    internal RateLimiterKind Kind => _options.Kind;

    /// <summary>Gets the limiter, one partition per key.</summary>
    internal PartitionedRateLimiter<string> Limiter { get; }

    /// <summary>Gets the <c>Retry-After</c> of a refusal the limiter gave no estimate for.</summary>
    internal TimeSpan RetryAfterFallback { get; }

    /// <summary>The key this link counts <paramref name="context"/> against.</summary>
    /// <param name="context">The request being limited.</param>
    /// <returns>The partition key.</returns>
    internal string KeyFor(HttpContext context) => RateLimitPartitionKeys.For(_options, _selector, context);

    /// <inheritdoc />
    public void Dispose() => Limiter.Dispose();

    /// <summary>Builds the partition of one key.</summary>
    /// <param name="key">The partition key.</param>
    /// <param name="options">The limiter's options.</param>
    /// <returns>The partition.</returns>
    /// <remarks>
    /// The <c>RateLimitPartition</c> factories turn the per-limiter timers off and let the partitioned
    /// limiter replenish every partition from its one timer.
    /// </remarks>
    private static RateLimitPartition<string> Partition(string key, PolicyLimiterOptions options)
    {
        var window = TimeSpan.FromSeconds(options.WindowSeconds);

        return options.Kind switch
        {
            RateLimiterKind.SlidingWindow => RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = options.PermitLimit,
                Window = window,
                SegmentsPerWindow = options.SegmentsPerWindow,
                QueueLimit = options.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }),
            RateLimiterKind.TokenBucket => RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = options.PermitLimit,
                TokensPerPeriod = options.TokensPerPeriod,
                ReplenishmentPeriod = window,
                QueueLimit = options.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }),
            RateLimiterKind.Concurrency => RateLimitPartition.GetConcurrencyLimiter(key, _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = options.PermitLimit,
                QueueLimit = options.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }),
            _ => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.PermitLimit,
                Window = window,
                QueueLimit = options.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            })
        };
    }
}
