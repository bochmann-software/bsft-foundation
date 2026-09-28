namespace Bsft.Caching.Tests;

/// <summary>A cacheable request relying on the interface defaults for lifetime and tags.</summary>
public sealed record DefaultsQuery(string CacheKey) : ICacheable;
