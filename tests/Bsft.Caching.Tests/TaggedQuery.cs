namespace Bsft.Caching.Tests;

/// <summary>A cacheable request overriding lifetime and tags.</summary>
public sealed record TaggedQuery(string CacheKey, TimeSpan? TimeToLive, IReadOnlyCollection<string> CacheTags)
    : ICacheable;
