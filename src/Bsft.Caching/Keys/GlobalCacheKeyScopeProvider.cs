namespace Bsft.Caching.Keys;

/// <summary>
/// Default <see cref="ICacheKeyScopeProvider"/> that qualifies keys with no scope at all, so
/// results are cached globally. Applications whose queries are tenant- or user-specific replace it
/// with a provider reading their own ambient context.
/// </summary>
public sealed class GlobalCacheKeyScopeProvider : ICacheKeyScopeProvider
{
    /// <inheritdoc />
    public CacheKeyScope GetScope() => CacheKeyScope.Global;
}
