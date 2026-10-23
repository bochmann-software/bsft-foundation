using System.Threading.RateLimiting;

namespace Bsft.Hosting;

/// <summary>
/// The lease of a <see cref="ChainedRateLimiter"/>: the leases of every limiter of the chain when it was
/// granted, or the lease of the limiter that refused it.
/// </summary>
/// <remarks>
/// A refused lease always answers <see cref="MetadataName.RetryAfter"/>: with the refusing limiter's own
/// estimate when it has one, and with the configured fallback otherwise, so the rejection writer never has
/// to guess which limiter of a chain spoke.
/// </remarks>
internal sealed class ChainedRateLimitLease : RateLimitLease
{
    private readonly RateLimitLease[] _leases;
    private readonly TimeSpan? _retryAfterFallback;
    private readonly Action? _onRelease;
    private int _isDisposed;

    /// <summary>Initializes a new instance of the <see cref="ChainedRateLimitLease"/> class.</summary>
    /// <param name="isAcquired">Whether the chain granted the request.</param>
    /// <param name="leases">The leases it holds, in acquisition order.</param>
    /// <param name="retryAfterFallback">The <c>Retry-After</c> of a refusal whose limiter gave none.</param>
    /// <param name="onRelease">Runs once when a granted lease is disposed.</param>
    internal ChainedRateLimitLease(
        bool isAcquired, RateLimitLease[] leases, TimeSpan? retryAfterFallback, Action? onRelease)
    {
        IsAcquired = isAcquired;
        _leases = leases;
        _retryAfterFallback = isAcquired ? null : retryAfterFallback;
        _onRelease = onRelease;
    }

    /// <inheritdoc />
    public override bool IsAcquired { get; }

    /// <inheritdoc />
    public override IEnumerable<string> MetadataNames
    {
        get
        {
            var names = _leases.SelectMany(lease => lease.MetadataNames);

            return (_retryAfterFallback is null ? names : names.Append(MetadataName.RetryAfter.Name))
                .Distinct(StringComparer.Ordinal);
        }
    }

    /// <inheritdoc />
    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        foreach (var lease in _leases)
        {
            if (lease.TryGetMetadata(metadataName, out metadata))
                return true;
        }

        if (_retryAfterFallback is { } retryAfter
            && string.Equals(metadataName, MetadataName.RetryAfter.Name, StringComparison.Ordinal))
        {
            metadata = retryAfter;
            return true;
        }

        metadata = null;
        return false;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
            return;

        for (var index = _leases.Length - 1; index >= 0; index--)
            _leases[index].Dispose();

        _onRelease?.Invoke();
    }
}
