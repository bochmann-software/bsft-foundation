namespace Bsft.Caching.Invalidation;

/// <summary>
/// Tracks which cache keys carry which tag, so a tag can be invalidated without the caller
/// knowing the individual keys behind it. Associations carry the expiry of the entry they
/// describe, so the index cannot outlive the entries and grow without bound.
/// </summary>
public interface ICacheTagIndex
{
    /// <summary>
    /// Records that <paramref name="key"/> belongs to each of <paramref name="tags"/> until
    /// <paramref name="expiresAt"/>. Every call stamps the associations it writes with a value
    /// greater than any stamp handed out before, so a later association can be told apart from the
    /// one a caller observed.
    /// </summary>
    /// <param name="key">The fully qualified cache key.</param>
    /// <param name="tags">The tags the entry carries. An empty collection records nothing.</param>
    /// <param name="expiresAt">The instant the described cache entry expires.</param>
    void Associate(string key, IReadOnlyCollection<string> tags, DateTimeOffset expiresAt);

    /// <summary>
    /// Returns the keys currently carrying a tag, excluding associations that have expired.
    /// </summary>
    /// <param name="tag">The tag to resolve.</param>
    /// <returns>The keys carrying the tag; empty when the tag is unknown.</returns>
    IReadOnlyCollection<string> ListKeys(string tag);

    /// <summary>
    /// Returns the newest stamp currently recorded for a key, to be passed to
    /// <see cref="Forget"/> after the entry behind the key has been evicted.
    /// </summary>
    /// <param name="key">The fully qualified cache key.</param>
    /// <returns>The newest stamp across every tag carrying the key, or zero when it is unknown.</returns>
    long GetStamp(string key);

    /// <summary>
    /// Removes a key from every tag whose association is no newer than <paramref name="stamp"/>,
    /// and leaves any newer association in place.
    /// </summary>
    /// <remarks>
    /// The stamp is what makes forgetting safe against a concurrent write. A caller reads it before
    /// evicting and passes it back afterwards; a query that re-cached the key in between recorded a
    /// higher stamp, and its association survives. Forgetting unconditionally would delete that
    /// fresh association while its entry stays in the store, where no tag invalidation could reach
    /// it again for the rest of its lifetime.
    /// </remarks>
    /// <param name="key">The fully qualified cache key to forget.</param>
    /// <param name="stamp">The stamp observed before the eviction, as returned by <see cref="GetStamp"/>.</param>
    void Forget(string key, long stamp);
}
