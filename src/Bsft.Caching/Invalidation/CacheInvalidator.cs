using Microsoft.Extensions.Options;

namespace Bsft.Caching.Invalidation;

/// <summary>
/// Default <see cref="ICacheInvalidator"/>. Evicts entries from the configured store and forgets
/// them in the tag index afterwards, so the index never points at a key the store no longer holds
/// and a failed eviction leaves the association in place to be retried. Forgetting is conditional
/// on the association observed before the eviction, so a query that re-caches a key while the
/// eviction is in flight keeps its fresh association.
/// </summary>
public sealed class CacheInvalidator : ICacheInvalidator
{
    /// <summary>The number of entries evicted concurrently while invalidating a tag.</summary>
    private const int EvictionBatchSize = 32;

    private readonly ICacheStore _store;
    private readonly ICacheTagIndex _tagIndex;
    private readonly BsftCachingOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheInvalidator"/> class.
    /// </summary>
    /// <param name="store">The cache store to evict from.</param>
    /// <param name="tagIndex">The tag index resolving tags to keys.</param>
    /// <param name="options">The caching options supplying the key prefix.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public CacheInvalidator(ICacheStore store, ICacheTagIndex tagIndex, IOptions<BsftCachingOptions> options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tagIndex = tagIndex ?? throw new ArgumentNullException(nameof(tagIndex));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task InvalidateKeyAsync(string? key, CancellationToken cancellationToken = default)
    {
        // No key is what the builder produces for a scope that is not cacheable. Nothing was ever
        // stored under it, so there is nothing to evict and a best-effort invalidation whose ambient
        // scope did not resolve must not fail the write it follows.
        if (key is null)
            return;

        // Refusing foreign keys keeps an invalidation, which may be driven by request data, from
        // deleting entries other writers own on a shared distributed cache.
        if (!key.StartsWith($"{_options.KeyPrefix}:", StringComparison.Ordinal))
            throw new ArgumentException($"Cache key must start with the configured prefix '{_options.KeyPrefix}:'.", nameof(key));

        // The stamp is read before the eviction, so a query that re-caches the key while the
        // eviction is in flight lands on a higher one. Forgetting unconditionally would delete that
        // fresh association while the re-cached entry stays in the store, invisible to every future
        // tag invalidation until it expires on its own.
        var stamp = _tagIndex.GetStamp(key);

        await _store.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        _tagIndex.Forget(key, stamp);
    }

    /// <inheritdoc />
    public async Task InvalidateTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (tag is null)
            throw new ArgumentNullException(nameof(tag));

        // Each key's stamp is captured before its entry is evicted, so a query that re-caches one
        // of them meanwhile records a higher stamp and keeps its fresh association below.
        var stampByKey = _tagIndex.ListKeys(tag).ToDictionary(key => key, _tagIndex.GetStamp, StringComparer.Ordinal);

        // Evicting in parallel keeps a tag covering many entries from serializing one distributed
        // cache round trip per key while the invalidating caller waits, while the chunking bounds
        // the fan-out so a large tag cannot saturate the connection or the thread pool.
        foreach (var chunk in stampByKey.Keys.Chunk(EvictionBatchSize))
            await Task.WhenAll(chunk.Select(key => _store.RemoveAsync(key, cancellationToken))).ConfigureAwait(false);

        // Forgetting after the eviction succeeded drops each key from every tag it carried, not
        // just from the invalidated one.
        foreach (var association in stampByKey)
            _tagIndex.Forget(association.Key, association.Value);
    }
}
