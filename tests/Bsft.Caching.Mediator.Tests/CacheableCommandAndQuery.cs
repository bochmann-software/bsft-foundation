using Bsft.Mediator;

namespace Bsft.Caching.Mediator.Tests;

/// <summary>A request implementing both contracts, which must be treated as a command.</summary>
public sealed record CacheableCommandAndQuery(string CacheKey) : ICommand<string>, IQuery<string>, ICacheable;
