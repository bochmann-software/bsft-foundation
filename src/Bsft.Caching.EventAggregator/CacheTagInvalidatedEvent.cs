namespace Bsft.Caching.EventAggregator;

/// <summary>
/// Announces that every cache entry carrying a tag has become stale and must be evicted.
/// Publish it through the in-process event aggregator after a write that invalidates a whole
/// family of query results, without needing to know their individual keys.
/// </summary>
/// <param name="Tag">The tag whose entries to evict.</param>
public sealed record CacheTagInvalidatedEvent(string Tag);
