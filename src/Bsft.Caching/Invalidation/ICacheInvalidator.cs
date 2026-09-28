
namespace Bsft.Caching.Invalidation;

/// <summary>
/// Evicts cached query results, either by their exact key or by a tag they carry.
/// Command handlers depend on this seam to invalidate what their write made stale.
/// </summary>
public interface ICacheInvalidator
{
    /// <summary>
    /// Evicts a single entry and drops it from the tag index.
    /// </summary>
    /// <param name="key">
    /// The fully qualified cache key, as produced by <see cref="Keys.ICacheKeyBuilder"/>. It must start
    /// with the configured key prefix, so an invalidation cannot reach entries another writer owns
    /// on a shared cache. <see langword="null"/> is a no-op: it is what
    /// <see cref="Keys.ICacheKeyBuilder.Build"/> returns for a <see cref="Keys.CacheKeyScope.NotCacheable"/>
    /// scope, and nothing was ever stored under it, so there is nothing to evict. A best-effort
    /// invalidation may therefore pass the builder's result straight through.
    /// </param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when the entry has been evicted.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="key"/> does not start with the configured key prefix.
    /// </exception>
    Task InvalidateKeyAsync(string? key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evicts every entry carrying the given tag that this process knows about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Which entries a tag reaches is decided by <see cref="ICacheTagIndex"/>, and the implementation
    /// registered by default is in-process and volatile. It therefore resolves only the associations
    /// this process observed itself: with <c>CacheProvider.Distributed</c> an entry another instance
    /// wrote, or one this instance wrote before a restart, stays in the store until its own lifetime
    /// runs out even though its tag was invalidated. Only key invalidation is unaffected, because it
    /// does not consult the index to find the entry.
    /// </para>
    /// <para>
    /// Where fleet-wide tag invalidation matters, either register an <see cref="ICacheTagIndex"/>
    /// backed by shared storage before calling <c>AddBsftCaching</c>, or propagate the invalidation
    /// to every instance and keep the lifetime short enough to bound the staleness.
    /// </para>
    /// </remarks>
    /// <param name="tag">The tag to invalidate.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A task that completes when all entries carrying the tag have been evicted.</returns>
    Task InvalidateTagAsync(string tag, CancellationToken cancellationToken = default);
}
