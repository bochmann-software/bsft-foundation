using Bsft.Mediator;

namespace Bsft.Caching.Mediator.Tests;

/// <summary>A cacheable query overriding lifetime and tags.</summary>
public sealed record TaggedQuery(string CacheKey, TimeSpan? TimeToLive, IReadOnlyCollection<string> CacheTags)
    : IQuery<string>, ICacheable;
