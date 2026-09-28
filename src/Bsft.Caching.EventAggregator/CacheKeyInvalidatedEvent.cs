namespace Bsft.Caching.EventAggregator;

/// <summary>
/// Announces that a single cache entry has become stale and must be evicted.
/// Publish it through the in-process event aggregator after a write that invalidates one
/// known query result.
/// </summary>
/// <param name="Key">The fully qualified cache key to evict.</param>
public sealed record CacheKeyInvalidatedEvent(string Key);
