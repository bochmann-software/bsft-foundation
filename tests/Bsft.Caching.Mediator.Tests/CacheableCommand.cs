using Bsft.Mediator;

namespace Bsft.Caching.Mediator.Tests;

/// <summary>A command carrying the cacheable marker, which must never be served from the cache.</summary>
public sealed record CacheableCommand(string CacheKey) : ICommand<string>, ICacheable;
