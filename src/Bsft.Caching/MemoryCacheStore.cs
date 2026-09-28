using Microsoft.Extensions.Caching.Memory;

namespace Bsft.Caching;

/// <summary>
/// <see cref="ICacheStore"/> over the in-process <see cref="IMemoryCache"/>. Values are stored as
/// live object references, so only immutable results should be cached.
/// </summary>
/// <remarks>
/// Every method here completes synchronously and hands the answer back through a finished
/// <see cref="Task"/>, which costs one small allocation per read. Turning the whole
/// <see cref="ICacheStore"/> contract into <see cref="ValueTask"/> to save it is not worth what it
/// costs: it is a breaking change to a public seam every host and every substitute store implements,
/// for one allocation next to the key string the same read already built and the entry it returns.
/// </remarks>
public sealed class MemoryCacheStore : ICacheStore
{
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryCacheStore"/> class.
    /// </summary>
    /// <param name="cache">The underlying memory cache.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cache"/> is null.</exception>
    public MemoryCacheStore(IMemoryCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <inheritdoc />
    public Task<CacheLookupResult<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        // The type check also guards against a key reused for a different response type, which
        // is reported as a miss rather than an invalid cast at the call site.
        var isHit = _cache.TryGetValue(key, out var cached) && cached is T;

        return Task.FromResult(isHit
            ? new CacheLookupResult<T>(true, (T)cached!)
            : new CacheLookupResult<T>(false, default));
    }

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        // An explicit size keeps the store working on a host that configured MemoryCacheOptions
        // with a SizeLimit, where an entry without one is rejected outright.
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = timeToLive,
            Size = 1,
        };

        _cache.Set(key, value, entryOptions);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        _cache.Remove(key);

        return Task.CompletedTask;
    }
}
