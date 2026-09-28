using Bsft.Mediator;

namespace Bsft.Caching.Mediator.Tests;

/// <summary>A cacheable query relying on the interface defaults for lifetime and tags.</summary>
public sealed record DefaultsQuery(string CacheKey) : IQuery<string>, ICacheable;
