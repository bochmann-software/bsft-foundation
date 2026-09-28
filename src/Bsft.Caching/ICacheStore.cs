namespace Bsft.Caching;

/// <summary>
/// Storage abstraction behind the caching behavior. Implemented once over
/// <c>IMemoryCache</c> and once over <c>IDistributedCache</c>, selected by configuration, so
/// callers never depend on which one an application runs.
/// </summary>
public interface ICacheStore
{
    /// <summary>
    /// Reads an entry.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The fully qualified cache key.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A hit carrying the value, or a miss.</returns>
    Task<CacheLookupResult<T>> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes an entry, replacing any existing one under the same key.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The fully qualified cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="timeToLive">How long the entry stays valid.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the entry has been written.</returns>
    Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an entry. Removing an absent key is not an error.
    /// </summary>
    /// <param name="key">The fully qualified cache key.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the entry has been removed.</returns>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
