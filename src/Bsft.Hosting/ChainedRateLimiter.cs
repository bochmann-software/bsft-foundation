using System.Threading.RateLimiting;

namespace Bsft.Hosting;

/// <summary>
/// The limiter of one partition of a named policy: asks every link of the chain for a permit under the
/// key that link computed for the request, and grants the request only when all of them do.
/// </summary>
/// <remarks>
/// <para>
/// <c>RateLimiter.CreateChained</c> does the same on .NET 10 but not on .NET 8, and cannot put a fallback
/// <c>Retry-After</c> on a refusal, so the chain is built here for both frameworks.
/// </para>
/// <para>
/// The limiter holds no permits of its own; the links keep their partitions and replenish them. It
/// reports itself idle while no lease it granted is outstanding, so the middleware's partitioned limiter
/// can drop it, and disposing it does not dispose the links, which other partitions share.
/// </para>
/// </remarks>
internal sealed class ChainedRateLimiter : RateLimiter
{
    private readonly PolicyLimiterLink[] _links;
    private readonly string[] _keys;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private int _inFlight;
    private DateTimeOffset _idleSince;
    private long _successfulLeases;
    private long _failedLeases;

    /// <summary>Initializes a new instance of the <see cref="ChainedRateLimiter"/> class.</summary>
    /// <param name="links">The links, in the order they are asked.</param>
    /// <param name="keys">The key of this partition in each link, by position.</param>
    /// <param name="timeProvider">The clock the idle duration is measured on.</param>
    internal ChainedRateLimiter(PolicyLimiterLink[] links, string[] keys, TimeProvider timeProvider)
    {
        _links = links;
        _keys = keys;
        _timeProvider = timeProvider;
        _idleSince = timeProvider.GetUtcNow();
    }

    /// <inheritdoc />
    public override TimeSpan? IdleDuration
    {
        get
        {
            lock (_gate)
                return _inFlight > 0 ? null : _timeProvider.GetUtcNow() - _idleSince;
        }
    }

    /// <inheritdoc />
    public override RateLimiterStatistics? GetStatistics()
    {
        long available = long.MaxValue;
        long queued = 0;

        for (var index = 0; index < _links.Length; index++)
        {
            var statistics = _links[index].Limiter.GetStatistics(_keys[index]);
            available = Math.Min(available, statistics?.CurrentAvailablePermits ?? 0);
            queued += statistics?.CurrentQueuedCount ?? 0;
        }

        return new RateLimiterStatistics
        {
            CurrentAvailablePermits = available,
            CurrentQueuedCount = queued,
            TotalSuccessfulLeases = Interlocked.Read(ref _successfulLeases),
            TotalFailedLeases = Interlocked.Read(ref _failedLeases)
        };
    }

    /// <inheritdoc />
    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        var acquired = new List<RateLimitLease>(_links.Length);

        for (var index = 0; index < _links.Length; index++)
        {
            var lease = _links[index].Limiter.AttemptAcquire(_keys[index], permitCount);
            if (!lease.IsAcquired)
                return Refuse(acquired, lease, _links[index]);

            acquired.Add(lease);
        }

        return Grant(acquired);
    }

    /// <inheritdoc />
    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        var acquired = new List<RateLimitLease>(_links.Length);

        try
        {
            for (var index = 0; index < _links.Length; index++)
            {
                var lease = await _links[index].Limiter
                    .AcquireAsync(_keys[index], permitCount, cancellationToken)
                    .ConfigureAwait(false);
                if (!lease.IsAcquired)
                    return Refuse(acquired, lease, _links[index]);

                acquired.Add(lease);
            }
        }
        catch
        {
            Release(acquired);
            throw;
        }

        return Grant(acquired);
    }

    /// <summary>Hands back what the earlier links granted and wraps the refusal.</summary>
    /// <param name="acquired">The leases of the links that granted the request.</param>
    /// <param name="refusal">The lease of the link that refused it.</param>
    /// <param name="link">The link that refused it.</param>
    /// <returns>The refused lease.</returns>
    private ChainedRateLimitLease Refuse(List<RateLimitLease> acquired, RateLimitLease refusal, PolicyLimiterLink link)
    {
        Release(acquired);
        Interlocked.Increment(ref _failedLeases);

        return new ChainedRateLimitLease(isAcquired: false, [refusal], link.RetryAfterFallback, onRelease: null);
    }

    /// <summary>Wraps the leases of every link into one granted lease and counts it in flight.</summary>
    /// <param name="acquired">The leases, in acquisition order.</param>
    /// <returns>The granted lease.</returns>
    private ChainedRateLimitLease Grant(List<RateLimitLease> acquired)
    {
        lock (_gate)
            _inFlight++;

        Interlocked.Increment(ref _successfulLeases);

        return new ChainedRateLimitLease(isAcquired: true, [.. acquired], retryAfterFallback: null, OnLeaseReleased);
    }

    /// <summary>Counts a granted lease out of flight and starts the idle clock when it was the last.</summary>
    private void OnLeaseReleased()
    {
        lock (_gate)
        {
            _inFlight--;
            if (_inFlight == 0)
                _idleSince = _timeProvider.GetUtcNow();
        }
    }

    /// <summary>Disposes leases in the reverse order they were acquired.</summary>
    /// <param name="acquired">The leases.</param>
    private static void Release(List<RateLimitLease> acquired)
    {
        for (var index = acquired.Count - 1; index >= 0; index--)
            acquired[index].Dispose();
    }
}
