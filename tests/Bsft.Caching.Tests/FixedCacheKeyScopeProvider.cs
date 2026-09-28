using Bsft.Caching.Keys;

namespace Bsft.Caching.Tests;

/// <summary>A scope provider returning a fixed scope.</summary>
public sealed class FixedCacheKeyScopeProvider : ICacheKeyScopeProvider
{
    private readonly CacheKeyScope _scope;

    public FixedCacheKeyScopeProvider(string? scope)
    {
        _scope = scope is null ? CacheKeyScope.Global : CacheKeyScope.For(scope);
    }

    public FixedCacheKeyScopeProvider(CacheKeyScope scope)
    {
        _scope = scope;
    }

    public CacheKeyScope GetScope() => _scope;
}
