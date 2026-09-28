namespace Bsft.Caching.Tests;

/// <summary>
/// A store decorator that runs a callback the moment an eviction has landed, which is the point at
/// which a concurrent query would re-cache the key it just evicted. It makes the otherwise timing
/// dependent window between eviction and forget deterministic.
/// </summary>
internal sealed class RecachingCacheStore : ICacheStore
{
    private readonly ICacheStore _inner;
    private readonly Func<string, Task> _afterRemove;

    public RecachingCacheStore(ICacheStore inner, Func<string, Task> afterRemove)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _afterRemove = afterRemove ?? throw new ArgumentNullException(nameof(afterRemove));
    }

    public Task<CacheLookupResult<T>> GetAsync<T>(string key, CancellationToken ct = default) =>
        _inner.GetAsync<T>(key, ct);

    public Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken ct = default) =>
        _inner.SetAsync(key, value, timeToLive, ct);

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        await _inner.RemoveAsync(key, ct);
        await _afterRemove(key);
    }
}
